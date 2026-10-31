using BepInEx.Configuration;

namespace RemotePlayEnhanced
{
    /// <summary>
    /// User-facing settings. Written by the Tauri app, read here at startup.
    /// Keys map 1:1 to the app's feature toggles.
    /// </summary>
    public class ModConfig
    {
        public ConfigEntry<int> PlayerCount;
        public ConfigEntry<bool> EnableAutoHotJoin;
        public ConfigEntry<bool> EnableSplitScreen;
        public ConfigEntry<bool> EnableDialogueFree;
        public ConfigEntry<bool> EnableBulkTithes;

        public ConfigEntry<float> MergeDistance;
        public ConfigEntry<float> SplitDistance;
        public ConfigEntry<float> DialogueExitRadius;
        public ConfigEntry<float> TitheRadius;

        public static ModConfig Bind(ConfigFile cfg)
        {
            var c = new ModConfig();

            c.PlayerCount = cfg.Bind(
                "General", "PlayerCount", 4,
                new ConfigDescription("Maximum local players. Steam Remote Play Together supports 4.",
                    new AcceptableValueRange<int>(2, 4)));

            c.EnableAutoHotJoin = cfg.Bind(
                "Features", "AutoHotJoin", true,
                "Fill free player slots when extra gamepads connect during an active co-op session.");

            c.EnableSplitScreen = cfg.Bind(
                "Features", "DynamicSplitScreen", true,
                "Split the view when players separate, merge when they regroup.");

            c.EnableDialogueFree = cfg.Bind(
                "Features", "DialogueNoFreeze", true,
                "Players keep moving during conversations; walking away ends them.");

            c.EnableBulkTithes = cfg.Bind(
                "Features", "BulkTithes", true,
                "Collect tithes from every follower in range at once.");

            c.MergeDistance = cfg.Bind(
                "Camera", "MergeDistance", 12f,
                "Player spread below which the view merges into one screen.");

            c.SplitDistance = cfg.Bind(
                "Camera", "SplitDistance", 18f,
                "Player spread above which the view splits. Must exceed MergeDistance.");

            c.DialogueExitRadius = cfg.Bind(
                "Dialogue", "ExitRadius", 6f,
                "Distance the talking player may move before the conversation closes.");

            c.TitheRadius = cfg.Bind(
                "Tithes", "Radius", 8f,
                "Radius in which followers pay tithes.");

            return c;
        }
    }
}
