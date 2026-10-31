namespace RemotePlayEnhanced
{
    /// <summary>
    /// Pure bulk-tithe policy, kept free of Unity and reflection so the
    /// eligibility and cooldown rules can be unit-tested.
    /// </summary>
    internal static class BulkTitheRules
    {
        internal static bool InRange(bool active, bool paid, float distance, float radius)
        {
            return active
                   && !paid
                   && distance <= radius;
        }

        /// <summary>
        /// Handles Environment.TickCount wrap-around. A negative elapsed value
        /// means the counter wrapped, which is treated as ready.
        /// </summary>
        internal static bool CooldownReady(int lastTick, int nowTick, int cooldownMs)
        {
            if (cooldownMs <= 0)
                return true;

            int elapsed = unchecked(nowTick - lastTick);
            return elapsed < 0 || elapsed >= cooldownMs;
        }
    }
}
