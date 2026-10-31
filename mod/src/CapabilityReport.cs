using System.Text;

namespace RemotePlayEnhanced
{
    /// <summary>
    /// One feature's advertised state, as read by the desktop app.
    /// </summary>
    internal struct FeatureStatus
    {
        internal const string Active = "active";
        internal const string Degraded = "degraded";
        internal const string Unavailable = "unavailable";

        internal readonly string Id;
        internal readonly string State;
        internal readonly string Reason;

        internal FeatureStatus(string id, string state, string reason)
        {
            Id = id;
            State = state;
            Reason = reason;
        }
    }

    /// <summary>
    /// Emits capabilities.json. The Tauri app reads this to show which features
    /// are active, degraded or unavailable, and why.
    ///
    /// Written by hand (no JSON dependency) because the payload is small and
    /// fixed and the mod should not pull in a serialiser.
    /// </summary>
    internal static class CapabilityReport
    {
        internal static string Build(int playerCount, bool cotlmpPresent, FeatureStatus[] features)
        {
            return Build(playerCount, cotlmpPresent, features, string.Empty, string.Empty, false);
        }

        internal static string Build(
            int playerCount,
            bool cotlmpPresent,
            FeatureStatus[] features,
            string unityVersion,
            string assemblyVersion,
            bool knownBuild)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"modVersion\": \"").Append(Escape(Plugin.Version)).Append("\",\n");
            sb.Append("  \"playerCount\": ").Append(playerCount).Append(",\n");
            sb.Append("  \"cotlmpPresent\": ").Append(cotlmpPresent ? "true" : "false").Append(",\n");
            sb.Append("  \"unityVersion\": \"").Append(Escape(unityVersion)).Append("\",\n");
            sb.Append("  \"assemblyVersion\": \"").Append(Escape(assemblyVersion)).Append("\",\n");
            sb.Append("  \"knownBuild\": ").Append(knownBuild ? "true" : "false").Append(",\n");
            sb.Append("  \"features\": [\n");

            for (int i = 0; i < features.Length; i++)
            {
                sb.Append("    { \"id\": \"").Append(Escape(features[i].Id))
                  .Append("\", \"state\": \"").Append(Escape(features[i].State))
                  .Append("\", \"reason\": \"").Append(Escape(features[i].Reason)).Append("\" }");
                sb.Append(i < features.Length - 1 ? ",\n" : "\n");
            }

            sb.Append("  ]\n}\n");
            return sb.ToString();
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;

            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
