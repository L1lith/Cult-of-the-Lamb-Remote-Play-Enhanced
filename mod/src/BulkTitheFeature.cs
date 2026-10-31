using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RemotePlayEnhanced
{
    /// <summary>
    /// Milestone 7: collect tithes from every eligible follower in radius.
    ///
    /// Vanilla pays tithes through interaction_FollowerInteraction's Extort
    /// command: it checks FollowerBrainStats.PaidTithes, sets it true, then
    /// spawns 3–6 pickups whose callback calls Inventory.AddItem(BLACK_GOLD, 1, 0).
    ///
    /// This feature does not depend on the interaction menu or on the
    /// Possessions_ExtortTithes doctrine. A configurable key collects from all
    /// active followers within TitheRadius, marks each as paid and pays the
    /// same 3–6 black gold, with one pickup visual per follower when the game's
    /// ResourceCustomTarget helper is available.
    /// </summary>
    internal static class BulkTitheFeature
    {
        internal const string Id = "bulk-tithes";

        internal static bool Active { get; private set; }
        internal static string Reason { get; private set; }

        private static FieldInfo _followersField;
        private static MethodInfo _brainGetter;
        private static FieldInfo _statsField;
        private static MethodInfo _paidTithesGetter;
        private static MethodInfo _paidTithesSetter;
        private static MethodInfo _addItem;
        private static MethodInfo _resourceCreate;
        private static Type _itemType;
        private static FieldInfo _playerInstance;

        private static FieldInfo _inMenuField;
        private static FieldInfo _conversationPlayingField;
        private static FieldInfo _letterBoxPlayingField;

        private static int _lastActivationTick;
        private static bool _loggedFailure;

        private const int BlackGold = 20;

        private static readonly Action NoopAction = Noop;

        internal static void Install(bool cotlmpPresent)
        {
            Active = false;
            Reason = null;

            if (cotlmpPresent)
            {
                Reason = "COTLMP installed; bulk tithes disabled";
                return;
            }

            if (Plugin.Settings != null && !Plugin.Settings.EnableBulkTithes.Value)
            {
                Reason = "disabled in config";
                return;
            }

            try
            {
                Type followerManager = AccessTools.TypeByName("FollowerManager");
                if (followerManager == null)
                {
                    Reason = "FollowerManager type not found";
                    return;
                }

                _followersField = AccessTools.Field(followerManager, "Followers");
                if (_followersField == null)
                {
                    Reason = "FollowerManager.Followers not found";
                    return;
                }

                Type follower = AccessTools.TypeByName("Follower");
                if (follower == null)
                {
                    Reason = "Follower type not found";
                    return;
                }
                _brainGetter = AccessTools.PropertyGetter(follower, "Brain");

                Type brain = AccessTools.TypeByName("FollowerBrain");
                if (brain != null)
                    _statsField = AccessTools.Field(brain, "Stats");

                Type stats = AccessTools.TypeByName("FollowerBrainStats");
                if (stats != null)
                {
                    _paidTithesGetter = AccessTools.PropertyGetter(stats, "PaidTithes");
                    _paidTithesSetter = AccessTools.PropertySetter(stats, "PaidTithes");
                }

                Type inventory = AccessTools.TypeByName("Inventory");
                if (inventory != null)
                {
                    _addItem = AccessTools.Method(
                        inventory, "AddItem",
                        new[] { typeof(int), typeof(int), typeof(bool) });
                }

                // ITEM_TYPE exists twice as a nested enum. The real item
                // vocabulary is InventoryItem.ITEM_TYPE; the other one belongs
                // to InventoryWeapon and has only five values.
                Type inventoryItem = AccessTools.TypeByName("InventoryItem");
                if (inventoryItem != null)
                {
                    _itemType = inventoryItem.GetNestedType(
                        "ITEM_TYPE", BindingFlags.Public | BindingFlags.NonPublic);
                }

                Type resourceTarget = AccessTools.TypeByName("ResourceCustomTarget");
                if (resourceTarget != null && _itemType != null)
                {
                    _resourceCreate = AccessTools.Method(
                        resourceTarget, "Create",
                        new[]
                        {
                            typeof(GameObject), typeof(Vector3), _itemType,
                            typeof(Action), typeof(bool)
                        });
                }

                Type playerFarming = AccessTools.TypeByName("PlayerFarming");
                if (playerFarming != null)
                    _playerInstance = AccessTools.Field(playerFarming, "Instance");

                Type gameManager = AccessTools.TypeByName("GameManager");
                if (gameManager != null)
                    _inMenuField = AccessTools.Field(gameManager, "InMenu");

                Type conversation = FindType("MMTools.MMConversation", "MMConversation");
                if (conversation != null)
                    _conversationPlayingField = AccessTools.Field(conversation, "isPlaying");

                Type letterBox = AccessTools.TypeByName("LetterBox");
                if (letterBox != null)
                    _letterBoxPlayingField = AccessTools.Field(letterBox, "IsPlaying");

                if (_brainGetter == null || _statsField == null
                    || _paidTithesGetter == null || _paidTithesSetter == null
                    || _addItem == null || _playerInstance == null)
                {
                    Reason = "tithe members incomplete";
                    return;
                }

                Active = true;
                Reason = "bulk collection ready"
                         + (_resourceCreate == null ? " (without coin visuals)" : string.Empty);
                Plugin.Log.LogInfo("Tithes: " + Reason);
            }
            catch (Exception e)
            {
                Active = false;
                Reason = "reflection failed: " + e.GetType().Name + ": " + e.Message;
                Plugin.Log.LogWarning("Tithes: " + Reason);
            }
        }

        internal static void Activate(ModConfig settings)
        {
            if (!Active || settings == null || !settings.EnableBulkTithes.Value)
                return;

            if (IsTrue(_inMenuField) || IsTrue(_conversationPlayingField) || IsTrue(_letterBoxPlayingField))
                return;

            Component player = Player();
            if (player == null)
                return;

            int cooldownMs = Mathf.RoundToInt(
                Mathf.Max(0f, settings.TitheCooldownSeconds.Value) * 1000f);
            int now = Environment.TickCount;
            if (!BulkTitheRules.CooldownReady(_lastActivationTick, now, cooldownMs))
                return;
            _lastActivationTick = now;

            int collected = CollectAround(player, settings.TitheRadius.Value);
            if (collected > 0)
                Plugin.Log.LogInfo("Tithes: collected from " + collected + " followers.");
        }

        internal static void Shutdown()
        {
            Active = false;
            _lastActivationTick = 0;
        }

        private static int CollectAround(Component player, float radius)
        {
            IList followers = _followersField.GetValue(null) as IList;
            if (followers == null)
                return 0;

            float effectiveRadius = Mathf.Max(0.1f, radius);
            Vector3 centre = player.transform.position;
            int collected = 0;

            for (int i = 0; i < followers.Count; i++)
            {
                try
                {
                    Component follower = followers[i] as Component;
                    if (follower == null || !follower.gameObject.activeInHierarchy)
                        continue;

                    float distance = Vector3.Distance(centre, follower.transform.position);
                    if (distance > effectiveRadius)
                        continue;

                    object brain = _brainGetter.Invoke(follower, null);
                    if (brain == null)
                        continue;

                    object stats = _statsField.GetValue(brain);
                    if (stats == null)
                        continue;

                    object paidValue = _paidTithesGetter.Invoke(stats, null);
                    bool paid = paidValue is bool && (bool)paidValue;

                    if (!BulkTitheRules.InRange(true, paid, distance, effectiveRadius))
                        continue;

                    // Pay first: if the inventory call fails, do not mark the
                    // follower as paid for the day.
                    int amount = UnityEngine.Random.Range(3, 7);
                    if (!Pay(amount, player, follower.transform.position))
                        continue;

                    _paidTithesSetter.Invoke(stats, new object[] { true });
                    collected++;
                }
                catch (Exception e)
                {
                    LogOnce(e);
                }
            }

            return collected;
        }

        private static bool Pay(int amount, Component player, Vector3 position)
        {
            try
            {
                _addItem.Invoke(null, new object[] { BlackGold, amount, false });
            }
            catch (Exception e)
            {
                LogOnce(e);
                return false;
            }

            if (_resourceCreate == null || _itemType == null)
                return true;

            try
            {
                object item = Enum.ToObject(_itemType, BlackGold);
                _resourceCreate.Invoke(
                    null,
                    new object[] { player.gameObject, position, item, NoopAction, true });
            }
            catch (Exception e)
            {
                // The payment succeeded; the visual is cosmetic.
                LogOnce(e);
            }

            return true;
        }

        private static Component Player()
        {
            return _playerInstance == null
                ? null
                : _playerInstance.GetValue(null) as Component;
        }

        private static bool IsTrue(FieldInfo field)
        {
            if (field == null)
                return false;
            object value = field.GetValue(null);
            return value is bool && (bool)value;
        }

        private static Type FindType(string fullName, string simpleName)
        {
            return AccessTools.TypeByName(fullName) ?? AccessTools.TypeByName(simpleName);
        }

        private static void Noop()
        {
        }

        private static void LogOnce(Exception e)
        {
            if (_loggedFailure)
                return;

            _loggedFailure = true;
            Plugin.Log.LogWarning("Tithes failed: " + e);
        }
    }
}
