using System;
using System.IO;
using UnityEngine;

namespace RemotePlayEnhanced
{
    /// <summary>
    /// Standalone logger: writes next to the mod and mirrors to the Unity log.
    /// The method names deliberately match the old log surface so feature code
    /// did not need to change.
    /// </summary>
    internal sealed class ModLog
    {
        private readonly string _path;

        internal ModLog(string path)
        {
            _path = path;
        }

        internal void LogInfo(string message)
        {
            Write("INFO", message);
        }

        internal void LogWarning(string message)
        {
            Write("WARN", message);
        }

        internal void LogError(string message)
        {
            Write("ERROR", message);
        }

        private void Write(string level, string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                          + " [" + level + "] " + message;

            try
            {
                File.AppendAllText(_path, line + Environment.NewLine);
            }
            catch (Exception)
            {
                // Logging must never break the mod.
            }

            try
            {
                Debug.Log("[RemotePlayEnhanced] " + message);
            }
            catch (Exception)
            {
                // Unity may not be ready during early bootstrap.
            }
        }
    }
}
