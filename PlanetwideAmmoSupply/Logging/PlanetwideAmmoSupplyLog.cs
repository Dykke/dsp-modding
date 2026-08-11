using BepInEx.Configuration;
using BepInEx.Logging;

namespace PlanetwideAmmoSupply
{
    /// <summary>
    /// Single static log helper (workspace Rule 2). All verbose lines go
    /// through <see cref="Info"/> and are gated behind the DebugLog config
    /// flag, re-read live so toggling the config takes effect without a
    /// restart. Warnings and errors always print. All lines are tagged
    /// <c>[PlanetwideAmmoSupply] ...</c>.
    /// </summary>
    internal static class PlanetwideAmmoSupplyLog
    {
        private const string Tag = "[PlanetwideAmmoSupply] ";

        private static ManualLogSource logger;
        private static ConfigEntry<bool> debugGate;

        public static void Init(ManualLogSource src, ConfigEntry<bool> debug)
        {
            logger = src;
            debugGate = debug;
        }

        public static bool IsDebugEnabled()
        {
            return debugGate != null && debugGate.Value;
        }

        public static void Info(string msg)
        {
            if (logger != null && IsDebugEnabled()) logger.LogInfo(Tag + msg);
        }

        public static void Warn(string msg)
        {
            if (logger != null) logger.LogWarning(Tag + msg);
        }

        public static void Error(string msg)
        {
            if (logger != null) logger.LogError(Tag + msg);
        }
    }
}
