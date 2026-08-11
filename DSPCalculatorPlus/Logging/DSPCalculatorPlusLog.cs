using BepInEx.Configuration;
using BepInEx.Logging;

namespace DSPCalculatorPlus
{
    /// <summary>
    /// The single static logging path for this mod (standing Rule 2). All
    /// verbose lines are tagged <c>[DSPCalculatorPlus] [category] message</c>
    /// and gated behind the one <c>Diagnostics/DebugLog</c> config entry,
    /// which is re-read live (via SettingChanged) so toggling it takes effect
    /// without a restart. <see cref="Warn"/> / <see cref="Error"/> always
    /// print regardless of the gate.
    /// </summary>
    internal static class DSPCalculatorPlusLog
    {
        private const string Tag = "[DSPCalculatorPlus] ";
        private static ManualLogSource _logger;
        private static bool _debugEnabled;

        public static void Init(ManualLogSource src, ConfigEntry<bool> debug)
        {
            _logger = src;
            _debugEnabled = debug.Value;
            // Re-read on change so toggling the config is live, not cached.
            debug.SettingChanged += (_, __) => _debugEnabled = debug.Value;
        }

        public static void Info(string msg)
        {
            if (_debugEnabled && _logger != null) _logger.LogInfo(Tag + msg);
        }

        public static void Warn(string msg)
        {
            _logger?.LogWarning(Tag + msg);
        }

        public static void Error(string msg)
        {
            _logger?.LogError(Tag + msg);
        }
    }
}
