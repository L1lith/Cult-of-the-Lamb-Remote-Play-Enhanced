using System;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace RemotePlayEnhanced
{
    /// <summary>
    /// Plugin entry point. Loads config, installs each feature independently,
    /// then writes a capability report the desktop app can display.
    ///
    /// A feature that cannot resolve its targets logs a warning and stays
    /// inactive; it never aborts startup.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.l1lith.cotl.remoteplayenhanced";
        public const string Name = "Remote Play Enhanced";
        public const string Version = "0.3.0";
        internal const string KnownUnityVersion = "2022.3.62f2";

        internal static ManualLogSource Log;
        internal static ModConfig Settings;
        internal static bool CotlmpPresent;
        internal static string UnityVersion = "unknown";
        internal static string AssemblyVersion = "unknown";
        internal static bool KnownBuild;

        private static bool _tickFailureLogged;

        private void Awake()
        {
            Log = Logger;

            Log.LogInfo(Name + " " + Version + " loading");

            string cotlmpPath;
            CotlmpPresent = DetectCotlmp(out cotlmpPath);
            if (CotlmpPresent)
            {
                // COTLMP also patches CoopManager; running both corrupts state.
                Log.LogWarning("COTLMP detected at " + cotlmpPath);
                Log.LogWarning("Co-op patches will stay disabled while COTLMP is installed.");
            }

            // "Config" is BaseUnityPlugin's ConfigFile property. The plugin's
            // own settings object is named Settings so it does not shadow it.
            Settings = ModConfig.Bind(Config);

            if (Settings.SplitDistance.Value <= Settings.MergeDistance.Value)
            {
                Log.LogWarning("SplitDistance must exceed MergeDistance; the camera will clamp it.");
            }

            UnityVersion = Application.unityVersion ?? "unknown";
            AssemblyVersion = ResolveGameAssemblyVersion();
            KnownBuild = UnityVersion == KnownUnityVersion;
            if (!KnownBuild)
            {
                Log.LogWarning(
                    "Unknown Unity build " + UnityVersion + " (known good: "
                    + KnownUnityVersion + "); trying every target best-effort.");
            }

            FourPlayerFeature.Install(Settings.PlayerCount.Value, CotlmpPresent);
            SplitScreenFeature.Install(CotlmpPresent);
            DialogueNoFreezeFeature.Install(CotlmpPresent);
            BulkTitheFeature.Install(CotlmpPresent);

            WriteCapabilityReport();
        }

        private void Update()
        {
            if (Settings == null)
                return;

            try
            {
                if (Settings.EnableAutoHotJoin.Value)
                    FourPlayerFeature.Tick();

                DialogueNoFreezeFeature.Tick(Settings);

                if (Settings.EnableBulkTithes.Value && Settings.BulkTitheKey.Value.IsDown())
                    BulkTitheFeature.Activate(Settings);
            }
            catch (Exception e)
            {
                LogTickFailure(e);
            }
        }

        private void LateUpdate()
        {
            if (Settings == null)
                return;

            try
            {
                SplitScreenFeature.Tick(Settings, Time.deltaTime);
            }
            catch (Exception e)
            {
                LogTickFailure(e);
            }
        }

        private static void LogTickFailure(Exception e)
        {
            if (_tickFailureLogged)
                return;

            _tickFailureLogged = true;
            Log.LogError("Tick failed; the remaining features keep running: " + e);
        }

        private void OnDestroy()
        {
            SplitScreenFeature.Shutdown();
            DialogueNoFreezeFeature.Shutdown();
            BulkTitheFeature.Shutdown();
        }

        private static void WriteCapabilityReport()
        {
            try
            {
                string dir = Path.Combine(Paths.PluginPath, "RemotePlayEnhanced");
                Directory.CreateDirectory(dir);

                string path = Path.Combine(dir, "capabilities.json");
                string report = CapabilityReport.Build(
                    Settings.PlayerCount.Value,
                    CotlmpPresent,
                    BuildFeatureStatuses(),
                    UnityVersion,
                    AssemblyVersion,
                    KnownBuild);

                File.WriteAllText(path, report, new UTF8Encoding(false));
                Log.LogInfo("Capability report written to " + path);
            }
            catch (Exception e)
            {
                Log.LogWarning("Could not write the capability report: " + e.Message);
            }
        }

        private static string ResolveGameAssemblyVersion()
        {
            try
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.GetName().Name != "Assembly-CSharp")
                        continue;

                    Version version = assembly.GetName().Version;
                    return version != null ? version.ToString() : "unknown";
                }
            }
            catch (Exception)
            {
                // Never let diagnostics break startup.
            }

            return "unknown";
        }

        private static FeatureStatus[] BuildFeatureStatuses()
        {
            string fourPlayerState;
            if (CotlmpPresent)
                fourPlayerState = FeatureStatus.Unavailable;
            else if (FourPlayerFeature.Resized && FourPlayerFeature.HotJoinAvailable)
                fourPlayerState = FeatureStatus.Active;
            else if (FourPlayerFeature.Resized)
                fourPlayerState = FeatureStatus.Degraded;
            else
                fourPlayerState = FeatureStatus.Unavailable;

            bool twoPlayerHudIsEnough = Settings.PlayerCount.Value <= 2;

            string splitState;
            string splitReason;
            if (!Settings.EnableSplitScreen.Value)
            {
                splitState = FeatureStatus.Unavailable;
                splitReason = "disabled in config";
            }
            else if (SplitScreenFeature.Active)
            {
                splitState = FeatureStatus.Active;
                splitReason = SplitScreenFeature.Reason;
            }
            else
            {
                splitState = FeatureStatus.Unavailable;
                splitReason = SplitScreenFeature.Reason ?? "camera director unavailable";
            }

            string dialogueState;
            string dialogueReason;
            if (!Settings.EnableDialogueFree.Value)
            {
                dialogueState = FeatureStatus.Unavailable;
                dialogueReason = "disabled in config";
            }
            else if (DialogueNoFreezeFeature.Active)
            {
                dialogueState = FeatureStatus.Active;
                dialogueReason = DialogueNoFreezeFeature.Reason;
            }
            else
            {
                dialogueState = FeatureStatus.Unavailable;
                dialogueReason = DialogueNoFreezeFeature.Reason ?? "conversation patches unavailable";
            }

            return new[]
            {
                new FeatureStatus(FourPlayerFeature.Id, fourPlayerState, FourPlayerFeature.Reason),
                new FeatureStatus(
                    "per-player-hud",
                    twoPlayerHudIsEnough ? FeatureStatus.Active : FeatureStatus.Unavailable,
                    twoPlayerHudIsEnough
                        ? "vanilla two-player HUD is sufficient"
                        : "vanilla HUD has two heart groups; guests 3-4 share the second"),
                new FeatureStatus(SplitScreenFeature.Id, splitState, splitReason),
                new FeatureStatus(DialogueNoFreezeFeature.Id, dialogueState, dialogueReason),
                new FeatureStatus(
                    BulkTitheFeature.Id,
                    !Settings.EnableBulkTithes.Value
                        ? FeatureStatus.Unavailable
                        : (BulkTitheFeature.Active ? FeatureStatus.Active : FeatureStatus.Unavailable),
                    !Settings.EnableBulkTithes.Value
                        ? "disabled in config"
                        : (BulkTitheFeature.Reason ?? "tithe members unavailable"))
            };
        }

        private static bool DetectCotlmp(out string path)
        {
            path = Path.Combine(Paths.PluginPath, "COTLMP");
            return Directory.Exists(path);
        }
    }
}
