namespace RemotePlayEnhanced
{
    /// <summary>
    /// Pure dialogue policy, kept separate from reflection and Unity types so
    /// the decisions can be unit-tested.
    /// </summary>
    internal static class DialogueRules
    {
        /// <summary>
        /// A conversation is treated as scripted (lock preserved) when the
        /// letterbox is already up or any line asks for a camera zoom. Those are
        /// the cutscene/mandatory paths; ordinary follower chats have neither.
        /// </summary>
        internal static bool IsScripted(bool letterboxPlaying, bool zoomEntry)
        {
            return letterboxPlaying || zoomEntry;
        }

        internal static bool SuppressFreeze(bool enabled, bool scripted)
        {
            return enabled && !scripted;
        }

        internal static bool ShouldExit(
            bool isPlaying, bool scripted, bool isBark, float distance, float radius)
        {
            return isPlaying
                   && !scripted
                   && !isBark
                   && distance > radius;
        }
    }
}
