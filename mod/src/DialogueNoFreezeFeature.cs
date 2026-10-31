using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RemotePlayEnhanced
{
    /// <summary>
    /// Milestone 6: conversations no longer freeze every player, and walking
    /// away closes an ordinary conversation.
    ///
    /// Vanilla freezes through two paths:
    ///   GameManager.OnConversationNew  -> PlayerFarming.SetInactive on every player
    ///   CoopManager.OnConversationNew  -> SetStateForAllPlayers(Inactive, ...)
    /// Both receive the same "shouldPlayerBeInactive" flag as their first
    /// argument, so a prefix can clear it for ordinary conversations.
    ///
    /// Scripted conversations are detected from the conversation itself: a
    /// letterbox already on screen, or any line that requests a camera zoom.
    /// Those keep their original lock and never auto-exit.
    ///
    /// Auto-exit anchors on the first non-player speaker seen (the follower or
    /// NPC), falling back to the player's position when the conversation began.
    /// </summary>
    internal static class DialogueNoFreezeFeature
    {
        internal const string Id = "dialogue-no-freeze";

        internal static bool Active { get; private set; }
        internal static string Reason { get; private set; }

        private static Harmony _harmony;

        private static Type _playerFarmingType;
        private static FieldInfo _playerInstance;

        private static Type _conversationType;
        private static FieldInfo _conversationIsPlaying;
        private static FieldInfo _conversationIsBark;
        private static FieldInfo _conversationInstance;
        private static FieldInfo _conversationSpeaker;
        private static FieldInfo _currentConversation;
        private static FieldInfo _entriesField;
        private static FieldInfo _setZoomField;
        private static FieldInfo _letterBoxIsPlaying;
        private static MethodInfo _close;

        private static bool _scripted;
        private static bool _wasPlaying;
        private static bool _exitRequested;
        private static bool _loggedFailure;
        private static Vector3 _fallbackAnchor;
        private static Vector3? _anchor;
        private static int _lastCheck;

        internal static void Install(bool cotlmpPresent)
        {
            Active = false;
            Reason = null;
            _scripted = false;
            _wasPlaying = false;
            _exitRequested = false;

            if (cotlmpPresent)
            {
                Reason = "COTLMP installed; dialogue patches disabled";
                return;
            }

            try
            {
                _playerFarmingType = AccessTools.TypeByName("PlayerFarming");
                if (_playerFarmingType == null)
                {
                    Reason = "PlayerFarming type not found";
                    return;
                }
                _playerInstance = AccessTools.Field(_playerFarmingType, "Instance");

                _conversationType = FindType("MMTools.MMConversation", "MMConversation");
                if (_conversationType == null)
                {
                    Reason = "MMConversation type not found";
                    return;
                }

                _conversationIsPlaying = AccessTools.Field(_conversationType, "isPlaying");
                _conversationIsBark = AccessTools.Field(_conversationType, "isBark");
                _conversationInstance = AccessTools.Field(_conversationType, "mmConversation");
                _conversationSpeaker = AccessTools.Field(_conversationType, "SpeakerSpine");
                _currentConversation = AccessTools.Field(_conversationType, "CURRENT_CONVERSATION");
                _close = AccessTools.Method(
                    _conversationType, "Close",
                    new[] { typeof(bool), typeof(Action), typeof(bool) });

                Type conversationObject = FindType("MMTools.ConversationObject", "ConversationObject");
                if (conversationObject != null)
                    _entriesField = AccessTools.Field(conversationObject, "Entries");

                Type conversationEntry = FindType("MMTools.ConversationEntry", "ConversationEntry");
                if (conversationEntry != null)
                    _setZoomField = AccessTools.Field(conversationEntry, "SetZoom");

                Type letterBox = AccessTools.TypeByName("LetterBox");
                if (letterBox != null)
                    _letterBoxIsPlaying = AccessTools.Field(letterBox, "IsPlaying");

                _harmony = new Harmony(Plugin.Guid + ".dialogue");

                int newPatches = 0;
                newPatches += PatchConversationNew("GameManager");
                newPatches += PatchConversationNew("CoopManager");

                int endPatches = 0;
                endPatches += PatchConversationEnd("GameManager", "OnConversationEnd");
                endPatches += PatchConversationEnd("CoopManager", "ResetMainPlayerOnConversationEnd");

                if (newPatches == 0)
                {
                    Reason = "conversation freeze methods not found";
                    Unpatch();
                    return;
                }

                bool autoExit = _close != null
                                && _conversationIsPlaying != null
                                && _conversationInstance != null;

                if (newPatches < 2)
                {
                    Active = false;
                    Reason = "only " + newPatches + "/2 freeze paths patched";
                    return;
                }

                Active = true;
                Reason = "movement unlocked; auto-exit "
                         + (autoExit ? "enabled" : "unavailable")
                         + " (" + endPatches + "/2 end patches)";
                Plugin.Log.LogInfo("Dialogue: " + Reason);
            }
            catch (Exception e)
            {
                Active = false;
                Reason = "reflection failed: " + e.GetType().Name + ": " + e.Message;
                Plugin.Log.LogWarning("Dialogue: " + Reason);
            }
        }

        private static Type FindType(string fullName, string simpleName)
        {
            return AccessTools.TypeByName(fullName) ?? AccessTools.TypeByName(simpleName);
        }

        private static int PatchConversationNew(string typeName)
        {
            Type type = AccessTools.TypeByName(typeName);
            if (type == null)
                return 0;

            MethodInfo target = AccessTools.Method(
                type, "OnConversationNew",
                new[] { typeof(bool), typeof(bool), typeof(bool), _playerFarmingType });
            return Patch(target, "ConversationNewPrefix");
        }

        private static int PatchConversationEnd(string typeName, string methodName)
        {
            Type type = AccessTools.TypeByName(typeName);
            if (type == null)
                return 0;

            MethodInfo target = AccessTools.Method(
                type, methodName, new[] { typeof(bool), typeof(bool) });
            return Patch(target, "ConversationEndPrefix");
        }

        private static int Patch(MethodInfo target, string prefixName)
        {
            if (target == null)
                return 0;

            MethodInfo prefix = typeof(DialogueNoFreezeFeature).GetMethod(
                prefixName, BindingFlags.Static | BindingFlags.NonPublic);
            if (prefix == null)
                return 0;

            try
            {
                _harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                return 1;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    "Dialogue: could not patch " + target.DeclaringType.Name + "." + target.Name
                    + ": " + e.Message);
                return 0;
            }
        }

        /// <summary>
        /// Harmony passes <c>__0</c> as the first original argument:
        /// the game's "should the player be inactive" flag.
        /// </summary>
        private static void ConversationNewPrefix(ref bool __0)
        {
            if (!Active || !Enabled())
                return;

            if (IsBark())
                return;

            bool scripted = IsScripted();
            _scripted = scripted;

            if (DialogueRules.SuppressFreeze(true, scripted))
                __0 = false;
        }

        private static void ConversationEndPrefix(ref bool __0)
        {
            if (!Active || !Enabled())
                return;

            if (!_scripted)
                __0 = false;
        }

        internal static void Tick(ModConfig settings)
        {
            if (!Active || settings == null || !settings.EnableDialogueFree.Value)
                return;

            if (_conversationIsPlaying == null)
                return;

            bool playing = IsTrue(_conversationIsPlaying.GetValue(null));
            if (playing && !_wasPlaying)
            {
                _exitRequested = false;
                _anchor = null;
                _fallbackAnchor = PlayerPosition();
                _lastCheck = 0;
            }
            _wasPlaying = playing;

            if (!playing)
            {
                _exitRequested = false;
                return;
            }

            if (_exitRequested || _scripted || IsBark())
                return;

            int now = Environment.TickCount;
            int elapsed = unchecked(now - _lastCheck);
            if (elapsed < 250 && elapsed > -250)
                return;
            _lastCheck = now;

            if (_close == null || _conversationInstance == null)
                return;

            object conversation = _conversationInstance.GetValue(null);
            if (conversation == null)
                return;

            if (_anchor == null)
            {
                Component speaker = _conversationSpeaker == null
                    ? null
                    : _conversationSpeaker.GetValue(conversation) as Component;
                if (speaker != null)
                {
                    Component player = Player();
                    if (player != null && speaker.gameObject != player.gameObject)
                        _anchor = speaker.transform.position;
                }
            }

            Vector3 anchor = _anchor.HasValue ? _anchor.Value : _fallbackAnchor;
            Vector3 position = PlayerPosition();
            float distance = Vector3.Distance(position, anchor);

            if (!DialogueRules.ShouldExit(
                    true, _scripted, false, distance, settings.DialogueExitRadius.Value))
                return;

            try
            {
                _close.Invoke(conversation, new object[] { false, null, false });
                _exitRequested = true;
                Plugin.Log.LogInfo("Dialogue: player walked away; conversation closed.");
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

        internal static void Shutdown()
        {
            Unpatch();
            Active = false;
            _scripted = false;
            _wasPlaying = false;
            _exitRequested = false;
        }

        private static void Unpatch()
        {
            if (_harmony == null)
                return;

            try
            {
                _harmony.UnpatchSelf();
            }
            catch (Exception)
            {
                // Unpatching during shutdown is best-effort.
            }
            _harmony = null;
        }

        private static bool Enabled()
        {
            return Plugin.Settings != null && Plugin.Settings.EnableDialogueFree.Value;
        }

        private static bool IsBark()
        {
            return _conversationIsBark != null
                   && IsTrue(_conversationIsBark.GetValue(null));
        }

        private static bool IsScripted()
        {
            if (_letterBoxIsPlaying != null && IsTrue(_letterBoxIsPlaying.GetValue(null)))
                return true;

            if (_currentConversation == null || _entriesField == null || _setZoomField == null)
                return false;

            object conversation = _currentConversation.GetValue(null);
            if (conversation == null)
                return false;

            IList entries = _entriesField.GetValue(conversation) as IList;
            if (entries == null)
                return false;

            for (int i = 0; i < entries.Count; i++)
            {
                object entry = entries[i];
                if (entry != null && IsTrue(_setZoomField.GetValue(entry)))
                    return true;
            }

            return false;
        }

        private static Component Player()
        {
            return _playerInstance == null
                ? null
                : _playerInstance.GetValue(null) as Component;
        }

        private static Vector3 PlayerPosition()
        {
            Component player = Player();
            return player == null ? Vector3.zero : player.transform.position;
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
            Plugin.Log.LogWarning("Dialogue auto-exit failed: " + e);
        }
    }
}
