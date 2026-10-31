using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace RemotePlayEnhanced
{
    /// <summary>
    /// Standalone mod entry point. Doorstop creates a GameObject with this
    /// component; no mod-loader framework is involved.
    ///
    /// Each feature installs independently. A missing reflection target logs a
    /// warning and disables only that feature.
    /// </summary>
    public class Plugin : MonoBehaviour
    {
        public const string Guid = "com.l1lith.cotl.remoteplayenhanced";
        public const string Name = "Remote Play Enhanced";
        public const string Version = "0.4.0";
        internal const string KnownUnityVersion = "2022.3.62f2";

        internal static ModLog Log;
        internal static ModConfig Settings;
        internal static bool CotlmpPresent;
        internal static string UnityVersion = "unknown";
        internal static string AssemblyVersion = "unknown";
        internal static bool KnownBuild;
        internal static string ModDirectory = ".";

        private static bool _tickFailureLogged;
        private static bool _awakeCompleted;

        private void Awake()
        {
            if (_awakeCompleted)
                return;
            _awakeCompleted = true;

            ModDirectory = ResolveModDirectory();
            Log = new ModLog(Path.Combine(ModDirectory, "RemotePlayEnhanced.log"));
            Log.LogInfo(Name + " " + Version + " loading from " + ModDirectory);

            string cotlmpPath;
            CotlmpPresent = DetectCotlmp(out cotlmpPath);
            if (CotlmpPresent)
            {
                Log.LogWarning("COTLMP detected at " + cotlmpPath);
                Log.LogWarning("Co-op patches will stay disabled while COTLMP is installed.");
            }

            try
            {
                Settings = ModConfig.Load(Path.Combine(ModDirectory, "config.cfg"));
            }
            catch (Exception e)
            {
                Log.LogError("Could not load config.cfg; using defaults: " + e);
                Settings = ModConfig.Defaults();
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

        private static string ResolveModDirectory()
        {
            try
            {
                string location = Assembly.GetExecutingAssembly().Location;
                string directory = Path.GetDirectoryName(location);
                if (!string.IsNullOrEmpty(directory))
                    return directory;
            }
            catch (Exception)
            {
                // Fall through to the process directory.
            }
            return Directory.GetCurrentDirectory();
        }

        private static void WriteCapabilityReport()
        {
            try
            {
                Directory.CreateDirectory(ModDirectory);
                string path = Path.Combine(ModDirectory, "capabilities.json");
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
            string gameRoot = Path.GetFullPath(Path.Combine(ModDirectory, ".."));
            string bepinexPlugin = Path.Combine(
                gameRoot, "BepInEx", "plugins", "COTLMP");
            if (Directory.Exists(bepinexPlugin))
            {
                path = bepinexPlugin;
                return true;
            }

            path = Path.Combine(gameRoot, "COTLMP");
            return Directory.Exists(path);
        }
    }
}
