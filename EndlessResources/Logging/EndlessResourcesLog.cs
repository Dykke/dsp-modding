using BepInEx.Configuration;
using BepInEx.Logging;

namespace EndlessResources
{
    /// <summary>
    /// Single static log helper. All verbose lines go through here and
    /// are gated behind the "Debug log" config flag. Warnings and errors
    /// always print regardless of the gate.
    ///
    /// The gate re-reads the ConfigEntry on every call (via SettingChanged
    /// plus a direct .Value read), so toggling the config takes effect
    /// immediately without a restart.
    /// </summary>
    internal static class EndlessResourcesLog
    {
        private static ManualLogSource logger;
        private static ConfigEntry<bool> debugEntry;

        public static void Init(ManualLogSource src, ConfigEntry<bool> debug)
        {
            logger = src;
            debugEntry = debug;
            // Re-read on every call so toggling the config takes effect
            // immediately. SettingChanged is also wired (the field is read
            // fresh on every Info() call, so this is belt-and-suspenders).
            debug.SettingChanged += (_, __) => { /* no-op; field is re-read each call */ };
        }

        /// <summary>Verbose info. Gated by Debug log. Tag with a category in the message.</summary>
        public static void Info(string msg)
        {
            if (debugEntry != null && debugEntry.Value && logger != null)
                logger.LogInfo("[EndlessResources] " + msg);
        }

        /// <summary>Warning. Always prints regardless of the gate.</summary>
        public static void Warn(string msg)
        {
            if (logger != null) logger.LogWarning("[EndlessResources] " + msg);
        }

        /// <summary>Error. Always prints regardless of the gate.</summary>
        public static void Error(string msg)
        {
            if (logger != null) logger.LogError("[EndlessResources] " + msg);
        }

        /// <summary>
        /// Return the current debug state. Useful for short-circuiting
        /// expensive snapshot work in patches.
        /// </summary>
        public static bool IsDebugEnabled()
        {
            return debugEntry != null && debugEntry.Value;
        }
    }
}
