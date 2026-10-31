using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace RemotePlayEnhanced
{
    /// <summary>
    /// One user-facing setting. Kept deliberately small; the app and the mod
    /// share the cfg file, not a framework type.
    /// </summary>
    internal sealed class Setting<T>
    {
        internal T Value;

        internal Setting(T value)
        {
            Value = value;
        }
    }

    /// <summary>
    /// Keyboard shortcut parsed from the cfg, e.g. "T" or "LeftControl + T".
    /// </summary>
    internal sealed class KeyBinding
    {
        internal KeyCode Key;
        internal KeyCode[] Modifiers;

        internal bool IsDown()
        {
            if (!Input.GetKeyDown(Key))
                return false;

            for (int i = 0; i < Modifiers.Length; i++)
            {
                if (!Input.GetKey(Modifiers[i]))
                    return false;
            }
            return true;
        }

        public override string ToString()
        {
            var parts = new string[Modifiers.Length + 1];
            for (int i = 0; i < Modifiers.Length; i++)
                parts[i] = Modifiers[i].ToString();
            parts[parts.Length - 1] = Key.ToString();
            return string.Join(" + ", parts);
        }

        internal static KeyBinding Parse(string text, KeyBinding fallback)
        {
            if (string.IsNullOrEmpty(text))
                return fallback;

            string[] raw = text.Split('+');
            var parsed = new List<KeyCode>();
            for (int i = 0; i < raw.Length; i++)
            {
                string token = NormalizeToken(raw[i].Trim());
                if (token.Length == 0)
                    continue;

                KeyCode key;
                if (!Enum.TryParse(token, true, out key))
                    return fallback;
                parsed.Add(key);
            }

            if (parsed.Count == 0)
                return fallback;

            var modifiers = new KeyCode[parsed.Count - 1];
            for (int i = 0; i < modifiers.Length; i++)
                modifiers[i] = parsed[i];

            return new KeyBinding
            {
                Key = parsed[parsed.Count - 1],
                Modifiers = modifiers,
            };
        }

        private static string NormalizeToken(string token)
        {
            switch (token.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    return "LeftControl";
                case "shift":
                    return "LeftShift";
                case "alt":
                    return "LeftAlt";
                case "win":
                case "meta":
                    return "LeftWindows";
                default:
                    return token;
            }
        }
    }

    /// <summary>
    /// The mod's cfg model. Uses the same sections and keys the desktop app
    /// writes. No external configuration framework is involved.
    /// </summary>
    internal sealed class ModConfig
    {
        internal Setting<int> PlayerCount;
        internal Setting<bool> EnableAutoHotJoin;
        internal Setting<bool> EnableSplitScreen;
        internal Setting<bool> EnableDialogueFree;
        internal Setting<bool> EnableBulkTithes;

        internal Setting<float> MergeDistance;
        internal Setting<float> SplitDistance;
        internal Setting<int> CameraTransitionMs;
        internal Setting<int> CameraStateDwellMs;
        internal Setting<float> CameraPadding;
        internal Setting<float> MaxSplitSize;
        internal Setting<float> DialogueExitRadius;
        internal Setting<float> TitheRadius;
        internal Setting<KeyBinding> BulkTitheKey;
        internal Setting<float> TitheCooldownSeconds;
        internal bool SplitDistanceClamped;

        internal static ModConfig Defaults()
        {
            return FromIni(new IniConfig());
        }

        internal static ModConfig Load(string path)
        {
            var ini = IniConfig.Load(path);
            var config = FromIni(ini);

            if (!File.Exists(path))
            {
                try
                {
                    config.Save(path);
                }
                catch (Exception)
                {
                    // The cfg is a convenience; defaults still work if it cannot be written.
                }
            }

            return config;
        }

        private static ModConfig FromIni(IniConfig ini)
        {
            var config = new ModConfig();

            config.PlayerCount = new Setting<int>(
                ini.GetInt("General", "PlayerCount", 4, 2, 4));
            config.EnableAutoHotJoin = new Setting<bool>(
                ini.GetBool("Features", "AutoHotJoin", true));
            config.EnableSplitScreen = new Setting<bool>(
                ini.GetBool("Features", "DynamicSplitScreen", true));
            config.EnableDialogueFree = new Setting<bool>(
                ini.GetBool("Features", "DialogueNoFreeze", true));
            config.EnableBulkTithes = new Setting<bool>(
                ini.GetBool("Features", "BulkTithes", true));

            config.MergeDistance = new Setting<float>(
                ini.GetFloat("Camera", "MergeDistance", 12f, 1f, 200f));
            config.SplitDistance = new Setting<float>(
                ini.GetFloat("Camera", "SplitDistance", 18f, 1f, 200f));
            if (config.SplitDistance.Value <= config.MergeDistance.Value)
            {
                config.SplitDistance.Value = config.MergeDistance.Value + 1f;
                config.SplitDistanceClamped = true;
            }

            config.CameraTransitionMs = new Setting<int>(
                ini.GetInt("Camera", "TransitionMs", 250, 50, 1500));
            config.CameraStateDwellMs = new Setting<int>(
                ini.GetInt("Camera", "StateDwellMs", 500, 0, 5000));
            config.CameraPadding = new Setting<float>(
                ini.GetFloat("Camera", "Padding", 1.5f, 0f, 20f));
            config.MaxSplitSize = new Setting<float>(
                ini.GetFloat("Camera", "MaxSplitSize", 16f, 1f, 100f));

            config.DialogueExitRadius = new Setting<float>(
                ini.GetFloat("Dialogue", "ExitRadius", 6f, 1f, 50f));
            config.TitheRadius = new Setting<float>(
                ini.GetFloat("Tithes", "Radius", 8f, 1f, 50f));
            config.BulkTitheKey = new Setting<KeyBinding>(
                KeyBinding.Parse(
                    ini.GetString("Tithes", "Key", "T"),
                    new KeyBinding { Key = KeyCode.T, Modifiers = new KeyCode[0] }));
            config.TitheCooldownSeconds = new Setting<float>(
                ini.GetFloat("Tithes", "Cooldown", 2f, 0.5f, 30f));

            return config;
        }

        internal void Save(string path)
        {
            var ini = new IniConfig();
            ini.Set("General", "PlayerCount", PlayerCount.Value.ToString(CultureInfo.InvariantCulture));
            ini.Set("Features", "AutoHotJoin", Bool(EnableAutoHotJoin.Value));
            ini.Set("Features", "DynamicSplitScreen", Bool(EnableSplitScreen.Value));
            ini.Set("Features", "DialogueNoFreeze", Bool(EnableDialogueFree.Value));
            ini.Set("Features", "BulkTithes", Bool(EnableBulkTithes.Value));
            ini.Set("Camera", "MergeDistance", Float(MergeDistance.Value));
            ini.Set("Camera", "SplitDistance", Float(SplitDistance.Value));
            ini.Set("Camera", "TransitionMs", CameraTransitionMs.Value.ToString(CultureInfo.InvariantCulture));
            ini.Set("Camera", "StateDwellMs", CameraStateDwellMs.Value.ToString(CultureInfo.InvariantCulture));
            ini.Set("Camera", "Padding", Float(CameraPadding.Value));
            ini.Set("Camera", "MaxSplitSize", Float(MaxSplitSize.Value));
            ini.Set("Dialogue", "ExitRadius", Float(DialogueExitRadius.Value));
            ini.Set("Tithes", "Radius", Float(TitheRadius.Value));
            ini.Set("Tithes", "Key", BulkTitheKey.Value.ToString());
            ini.Set("Tithes", "Cooldown", Float(TitheCooldownSeconds.Value));
            ini.Save(path);
        }

        private static string Bool(bool value)
        {
            return value ? "true" : "false";
        }

        private static string Float(float value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Minimal INI reader/writer for the shared cfg format:
    /// sections in square brackets, key = value, # and ; comments.
    /// </summary>
    internal sealed class IniConfig
    {
        private static readonly string[] SectionOrder =
        {
            "General", "Features", "Camera", "Dialogue", "Tithes",
        };

        private readonly Dictionary<string, string> _values =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal static IniConfig Load(string path)
        {
            var ini = new IniConfig();
            if (!File.Exists(path))
                return ini;

            string section = string.Empty;
            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch (Exception)
            {
                return ini;
            }

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                    continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                int equals = line.IndexOf('=');
                if (equals < 0)
                    continue;

                string key = line.Substring(0, equals).Trim();
                string value = line.Substring(equals + 1).Trim();
                if (section.Length > 0 && key.Length > 0)
                    ini._values[section + "." + key] = value;
            }

            return ini;
        }

        internal void Set(string section, string key, string value)
        {
            _values[section + "." + key] = value;
        }

        internal string GetString(string section, string key, string fallback)
        {
            string value;
            return _values.TryGetValue(section + "." + key, out value) && value.Length > 0
                ? value
                : fallback;
        }

        internal bool GetBool(string section, string key, bool fallback)
        {
            string value;
            if (!_values.TryGetValue(section + "." + key, out value))
                return fallback;

            switch (value.Trim().ToLowerInvariant())
            {
                case "true":
                case "1":
                case "yes":
                case "on":
                    return true;
                case "false":
                case "0":
                case "no":
                case "off":
                    return false;
                default:
                    return fallback;
            }
        }

        internal int GetInt(string section, string key, int fallback, int min, int max)
        {
            string value;
            int parsed;
            if (!_values.TryGetValue(section + "." + key, out value)
                || !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                return fallback;
            }
            return Mathf.Clamp(parsed, min, max);
        }

        internal float GetFloat(string section, string key, float fallback, float min, float max)
        {
            string value;
            float parsed;
            if (!_values.TryGetValue(section + "." + key, out value)
                || !float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                return fallback;
            }
            return Mathf.Clamp(parsed, min, max);
        }

        internal void Save(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("## Remote Play Enhanced");
            sb.AppendLine("## Generated by the desktop app. Restart the game after saving.");
            sb.AppendLine();

            for (int s = 0; s < SectionOrder.Length; s++)
            {
                string section = SectionOrder[s];
                sb.Append('[').Append(section).AppendLine("]");

                foreach (string key in KeysInOrder(section))
                {
                    string value;
                    if (_values.TryGetValue(section + "." + key, out value))
                        sb.Append(key).Append(" = ").AppendLine(value);
                }

                if (s < SectionOrder.Length - 1)
                    sb.AppendLine();
            }

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static IEnumerable<string> KeysInOrder(string section)
        {
            switch (section)
            {
                case "General":
                    return new[] { "PlayerCount" };
                case "Features":
                    return new[]
                    {
                        "AutoHotJoin", "DynamicSplitScreen", "DialogueNoFreeze", "BulkTithes",
                    };
                case "Camera":
                    return new[]
                    {
                        "MergeDistance", "SplitDistance", "TransitionMs", "StateDwellMs",
                        "Padding", "MaxSplitSize",
                    };
                case "Dialogue":
                    return new[] { "ExitRadius" };
                case "Tithes":
                    return new[] { "Radius", "Key", "Cooldown" };
                default:
                    return new string[0];
            }
        }
    }
}
