using BepInEx.Configuration;

namespace DSPCalculatorPlus
{
    /// <summary>
    /// Belt tier the generator is forced to use in blueprints.
    /// <see cref="Auto"/> defers entirely to DSPCalculator's own
    /// bpBeltHighest / tech-limit behaviour (no override). The Mk tiers
    /// resolve against the game's live belt list at generation time, so a
    /// higher tier that isn't unlocked yet simply won't be selectable.
    /// (v1 exposes the three vanilla tiers; mod-added extra tiers are a
    /// noted v2 consideration - see the plan.)
    /// </summary>
    public enum BeltTier
    {
        Auto = 0,
        Mk1 = 1,
        Mk2 = 2,
        Mk3 = 3,
    }

    /// <summary>
    /// Sorter tier the generator is forced to use. <see cref="Auto"/> defers
    /// to DSPCalculator's own behaviour. Forcing a tier below Mk4 will bypass
    /// DSPCalculator's pile-sorter (Mk4) output-stacking optimisation for
    /// non-resource cargo - a deliberate override, documented in
    /// TROUBLESHOOTING.
    /// </summary>
    public enum SorterTier
    {
        Auto = 0,
        Mk1 = 1,
        Mk2 = 2,
        Mk3 = 3,
        Mk4 = 4,
    }

    /// <summary>
    /// Typed wrapper over the BepInEx <see cref="ConfigFile"/>. All settings
    /// are bound once, here, in the constructor (called from
    /// <c>Plugin.Awake</c>). Never bind a key twice, and never add settings
    /// through an in-game window (standing Rule 1).
    /// </summary>
    internal sealed class PluginConfig
    {
        public readonly ConfigEntry<BeltTier> BeltTierOverride;
        public readonly ConfigEntry<SorterTier> SorterTierOverride;
        public readonly ConfigEntry<bool> EnableMultiLaneOverflowFix;
        public readonly ConfigEntry<bool> DebugLog;

        public PluginConfig(ConfigFile config)
        {
            BeltTierOverride = config.Bind(
                "General",
                "BeltTierOverride",
                BeltTier.Auto,
                "Force a specific belt tier in generated blueprints (applies to every item). "
                + "Auto = defer to DSPCalculator's own 'highest belt' / tech-limit setting.");

            SorterTierOverride = config.Bind(
                "General",
                "SorterTierOverride",
                SorterTier.Auto,
                "Force a specific sorter tier in generated blueprints (applies to every item). "
                + "Auto = defer to DSPCalculator's own setting. Forcing below Mk4 bypasses "
                + "pile-sorter output stacking.");

            EnableMultiLaneOverflowFix = config.Bind(
                "General",
                "EnableMultiLaneOverflowFix",
                true,
                "When an item's throughput exceeds one belt of the fastest tier (which stock "
                + "DSPCalculator refuses to generate), supply that item as an EXTERNAL logistics "
                + "input instead of failing. The item is no longer produced inside the blueprint - "
                + "you feed it from your ILS/PLS network - which is how the blackbox blueprint model "
                + "is meant to scale. Target outputs cannot be externalized and still fail cleanly.");

            DebugLog = config.Bind(
                "Diagnostics",
                "DebugLog",
                false,
                "Enable verbose diagnostic logging to the BepInEx console.");
        }
    }
}
