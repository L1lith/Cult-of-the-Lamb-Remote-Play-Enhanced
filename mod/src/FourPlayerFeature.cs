using System;
using System.Reflection;
using HarmonyLib;

namespace RemotePlayEnhanced
{
    /// <summary>
    /// Milestone 3: lift the two-player cap.
    ///
    /// The cap is not part of the co-op logic. It is one allocation in
    /// <c>CoopManager</c>'s static constructor:
    /// <c>AllPlayerGameObjects = new GameObject[2]</c>. Everything downstream
    /// (spawn, lookup, room transitions) indexes that array, so replacing it
    /// with a larger one is what actually allows more players.
    ///
    /// This build still contains <c>HandleCoopCreation</c>, a private static
    /// routine that walks the unclaimed slots, asks Rewired for the matching
    /// player's joysticks and spawns a guest for each one. It has no callers in
    /// the shipped assembly (the pause-menu flow replaced it), so we call it
    /// ourselves once vanilla co-op is running.
    ///
    /// Everything here is resolved by name and never throws out of
    /// <see cref="Install"/>: a game update that renames something degrades the
    /// feature instead of breaking startup.
    /// </summary>
    internal static class FourPlayerFeature
    {
        internal const string Id = "four-players";

        internal static bool Resized { get; private set; }
        internal static bool HotJoinAvailable { get; private set; }
        internal static string Reason { get; private set; }

        private static Type _coopManager;
        private static FieldInfo _allPlayers;
        private static FieldInfo _coopActive;
        private static FieldInfo _instance;
        private static FieldInfo _inMenu;
        private static MethodInfo _handleCreation;
        private static MethodInfo _isSpawning;
        private static int _target;
        private static int _lastTick;
        private static bool _loggedFailure;

        internal static void Install(int playerCount, bool cotlmpPresent)
        {
            Resized = false;
            HotJoinAvailable = false;
            Reason = null;
            _loggedFailure = false;

            try
            {
                InstallUnsafe(playerCount, cotlmpPresent);
            }
            catch (Exception e)
            {
                Resized = false;
                HotJoinAvailable = false;
                Reason = "reflection failed: " + e.GetType().Name + ": " + e.Message;
                Plugin.Log.LogWarning("FourPlayer: " + Reason);
            }
        }

        private static void InstallUnsafe(int playerCount, bool cotlmpPresent)
        {
            if (cotlmpPresent)
            {
                Reason = "COTLMP installed; co-op patches disabled";
                return;
            }

            if (playerCount < 2 || playerCount > 4)
            {
                Reason = "player count out of range: " + playerCount;
                return;
            }

            _target = playerCount;
            _coopManager = AccessTools.TypeByName("CoopManager");
            if (_coopManager == null)
            {
                Reason = "CoopManager type not found";
                return;
            }

            _allPlayers = AccessTools.Field(_coopManager, "AllPlayerGameObjects");
            if (_allPlayers == null)
            {
                Reason = "CoopManager.AllPlayerGameObjects not found";
                return;
            }

            _coopActive = AccessTools.Field(_coopManager, "CoopActive");
            _instance = AccessTools.Field(_coopManager, "Instance");
            _handleCreation = AccessTools.Method(_coopManager, "HandleCoopCreation");
            _isSpawning = AccessTools.PropertyGetter(_coopManager, "IsSpawningOrRemovingPlayer");

            Type gameManager = AccessTools.TypeByName("GameManager");
            if (gameManager != null)
                _inMenu = AccessTools.Field(gameManager, "InMenu");

            if (!Resize())
            {
                Reason = "could not replace AllPlayerGameObjects";
                return;
            }

            Resized = true;
            HotJoinAvailable = _handleCreation != null;

            Reason = HotJoinAvailable
                ? "raised player slots to " + playerCount + "; hot-join enabled"
                : "raised player slots to " + playerCount + "; hot-join routine missing";

            Plugin.Log.LogInfo("FourPlayer: " + Reason);
        }

        /// <summary>
        /// Replace the static array with one of the configured length, keeping
        /// whatever the game has already stored in it. Reading the field runs
        /// CoopManager's static constructor if it has not run yet.
        /// </summary>
        private static bool Resize()
        {
            Array current = _allPlayers.GetValue(null) as Array;
            if (current == null)
                return false;

            if (current.Length == _target)
                return true;

            Type element = current.GetType().GetElementType();
            if (element == null)
                return false;

            Array replacement = Array.CreateInstance(element, _target);
            Array.Copy(current, replacement, Math.Min(current.Length, _target));
            _allPlayers.SetValue(null, replacement);
            return true;
        }

        /// <summary>
        /// Called once per frame by the plugin. Throttled to one attempt per
        /// second because spawning is driven by controller presence, not by a
        /// frame-accurate event.
        /// </summary>
        internal static void Tick()
        {
            if (!Resized || !HotJoinAvailable)
                return;

            int now = Environment.TickCount;
            int elapsed = unchecked(now - _lastTick);
            if (elapsed < 1000 && elapsed > -1000)
                return;

            try
            {
                // Only fill slots once vanilla co-op exists. The first guest is
                // still added through the pause menu, which keeps the tutorial
                // and the assign-input flow intact.
                if (!IsTrue(_coopActive))
                    return;

                if (IsTrue(_inMenu))
                    return;

                // IsSpawningOrRemovingPlayer is an instance property, so it
                // needs the CoopManager singleton as its target.
                object manager = _instance == null ? null : _instance.GetValue(null);
                if (manager == null)
                    return;

                if (_isSpawning != null && IsTrue(_isSpawning.Invoke(manager, null)))
                    return;

                Array slots = _allPlayers.GetValue(null) as Array;
                if (slots == null)
                    return;
                if (slots.Length != _target && !Resize())
                    return;

                _lastTick = now;
                _handleCreation.Invoke(null, null);
            }
            catch (TargetInvocationException e)
            {
                LogOnce(e.InnerException ?? e);
            }
            catch (Exception e)
            {
                LogOnce(e);
            }
        }

        private static bool IsTrue(FieldInfo field)
        {
            return field != null && IsTrue(field.GetValue(null));
        }

        private static bool IsTrue(object value)
        {
            return value is bool && (bool)value;
        }

        private static void LogOnce(Exception e)
        {
            if (_loggedFailure)
                return;

            _loggedFailure = true;
            Plugin.Log.LogWarning("FourPlayer hot-join failed: " + e);
        }
    }
}
