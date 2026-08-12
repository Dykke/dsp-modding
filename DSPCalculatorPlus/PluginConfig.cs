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
    /// Which electric pole (if any) DSPCalculatorPlus auto-places over a
    /// generated blueprint to power its machines. DSPCalculator itself never
    /// adds power infrastructure, so the user normally hand-places poles.
    /// <see cref="Off"/> keeps that manual behaviour. <see cref="TeslaTower"/>
    /// (default) fills the whole blueprint with cheap 1x1 Tesla Towers -
    /// guaranteed full coverage that fits any layout.
    /// <see cref="SatelliteSubstation"/> additionally drops wide-coverage
    /// Satellite Substations wherever their 7x7 footprint fits, then still
    /// fills the rest with Tesla Towers (substations alone can't cover a dense
    /// layout, and cost far more). Poles only land on tiles no machine/belt
    /// occupies and never closer than the game's minimum pole spacing, so the
    /// addition never breaks the paste.
    /// </summary>
    public enum PowerPoleType
    {
        Off = 0,
        TeslaTower = 1,
        SatelliteSubstation = 2,
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
        public readonly ConfigEntry<bool> PushBeltStackingOnOverflow;
        public readonly ConfigEntry<PowerPoleType> AutoPowerPoles;
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

            PushBeltStackingOnOverflow = config.Bind(
                "General",
                "PushBeltStackingOnOverflow",
                true,
                "Last resort when a blueprint still fails because a single block's output belt can't "
                + "carry its rate (typically an un-externalizable BYPRODUCT like hydrogen at high quantity). "
                + "Only activates on an otherwise-failing generation: it raises DSPCalculator's belt-stacking "
                + "to the vanilla max (4x cargo) and regenerates, which ~4x's the throughput ceiling. "
                + "The resulting blueprint ASSUMES 4x cargo stacking, so you need the pile/proliferator "
                + "stacking tech to run it at full rate; without it, belts under-carry. Set false to keep "
                + "failing cleanly instead. Cannot exceed vanilla 4x - past that, reduce quantity or split the blueprint.");

            AutoPowerPoles = config.Bind(
                "General",
                "AutoPowerPoles",
                PowerPoleType.TeslaTower,
                "Auto-place electric poles over generated blueprints so the machines get power "
                + "(DSPCalculator never adds any). Poles land only on empty tiles - never on a "
                + "machine/belt and never closer than the game's minimum pole spacing - so they "
                + "can't break the paste. TeslaTower (default) = fill with cheap 1x1 Tesla Towers "
                + "(full coverage, fits any layout). SatelliteSubstation = also drop wide Satellite "
                + "Substations where their 7x7 footprint fits, filling the rest with Tesla Towers. "
                + "Off = leave power to you, as stock DSPCalculator does.");

            DebugLog = config.Bind(
                "Diagnostics",
                "DebugLog",
                false,
                "Enable verbose diagnostic logging to the BepInEx console.");
        }
    }
}
