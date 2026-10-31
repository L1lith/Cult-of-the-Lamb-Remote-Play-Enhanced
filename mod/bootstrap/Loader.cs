using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Doorstop
{
    /// <summary>
    /// Doorstop 3 entry point. Doorstop loads this assembly before Unity starts
    /// and calls <see cref="Main"/>; Unity then calls the runtime-initialize
    /// methods once the engine is ready.
    /// </summary>
    public static class Loader
    {
        private static string _modDirectory;
        private static string _logPath;
        private static bool _componentAdded;

        public static void Main(string[] args)
        {
            _modDirectory = ResolveModDirectory();
            _logPath = Path.Combine(_modDirectory, "bootstrap.log");
            Log("Doorstop entered; mod directory " + _modDirectory);

            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;

            // If Unity has already loaded the game assembly by the time
            // Doorstop runs (unusual, but possible), start immediately.
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name == "Assembly-CSharp")
                {
                    StartMod();
                    break;
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BeforeSceneLoad()
        {
            // Load the mod assembly early so its references are available when
            // the component is created. Do not touch game types yet.
            try
            {
                Assembly.LoadFrom(Path.Combine(_modDirectory, "RemotePlayEnhanced.dll"));
                Log("Mod assembly preloaded");
            }
            catch (Exception e)
            {
                Log("Mod preload failed: " + e);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AfterSceneLoad()
        {
            StartMod();
        }

        private static void StartMod()
        {
            if (_componentAdded)
                return;

            try
            {
                Assembly mod = Assembly.LoadFrom(
                    Path.Combine(_modDirectory, "RemotePlayEnhanced.dll"));
                Type plugin = mod.GetType("RemotePlayEnhanced.Plugin", true);

                GameObject host = new GameObject("RemotePlayEnhanced");
                UnityEngine.Object.DontDestroyOnLoad(host);
                host.AddComponent(plugin);
                _componentAdded = true;
                Log("Mod component added");
            }
            catch (Exception e)
            {
                Log("Mod start failed: " + e);
            }
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            if (_componentAdded)
                return;

            try
            {
                if (args.LoadedAssembly.GetName().Name == "Assembly-CSharp")
                {
                    Log("Assembly-CSharp loaded; starting mod");
                    StartMod();
                }
            }
            catch (Exception e)
            {
                Log("Assembly load handler failed: " + e);
            }
        }

        private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            try
            {
                string name = new AssemblyName(args.Name).Name;
                if (string.IsNullOrEmpty(name))
                    return null;

                string candidate = Path.Combine(_modDirectory, name + ".dll");
                if (File.Exists(candidate))
                    return Assembly.LoadFrom(candidate);
            }
            catch (Exception e)
            {
                Log("Assembly resolve failed for " + args.Name + ": " + e.Message);
            }

            return null;
        }

        private static string ResolveModDirectory()
        {
            try
            {
                string invoked = Environment.GetEnvironmentVariable("DOORSTOP_INVOKE_DLL_PATH");
                if (!string.IsNullOrEmpty(invoked))
                {
                    string full = Path.GetFullPath(invoked);
                    if (File.Exists(full))
                        return Path.GetDirectoryName(full);
                }

                string process = Environment.GetEnvironmentVariable("DOORSTOP_PROCESS_PATH");
                if (!string.IsNullOrEmpty(process))
                    return Path.Combine(Path.GetDirectoryName(process), "RemotePlayEnhanced");

                string location = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(location))
                    return Path.GetDirectoryName(location);
            }
            catch (Exception)
            {
                // Fall through.
            }

            return Directory.GetCurrentDirectory();
        }

        private static void Log(string message)
        {
            try
            {
                File.AppendAllText(
                    _logPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message
                    + Environment.NewLine);
            }
            catch (Exception)
            {
                // Bootstrap logging must never throw.
            }
        }
    }

    /// <summary>
    /// Doorstop 4 entry point, kept alongside the Doorstop 3 Loader.Main.
    /// </summary>
    public static class Entrypoint
    {
        public static void Start()
        {
            Loader.Main(new string[0]);
        }
    }
}
