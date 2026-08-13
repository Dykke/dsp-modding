using BepInEx.Configuration;

namespace EndlessResources
{
    /// <summary>
    /// BepInEx config surface for EndlessResources. All settings live
    /// here as typed ConfigEntry bindings; the entry point reads them
    /// once on Awake and passes the values to the patches.
    ///
    /// Per the workspace standing rule (Rule 1: Settings Placement),
    /// no in-game window is exposed for these toggles. The .cfg file
    /// BepInEx auto-creates at first run is the source of truth.
    ///
    /// Per Rule 2: Diagnostics.DebugLog is always present.
    /// </summary>
    internal sealed class PluginConfig
    {
        // -- General -------------------------------------------------------
        public readonly ConfigEntry<bool> EnableMinerPatchFlag;
        public readonly ConfigEntry<bool> EnableOilPatchFlag;
        public readonly ConfigEntry<bool> EnableIcarusPatchFlag;
        public readonly ConfigEntry<bool> EnableILSVeinCollectionFlag;
        public readonly ConfigEntry<bool> EnableILSSourceFlag;
        public readonly ConfigEntry<bool> EnablePlanetMinerFastCompatFlag;

        // -- Diagnostics ---------------------------------------------------
        public readonly ConfigEntry<bool> DebugLog;

        public PluginConfig(ConfigFile config)
        {
            EnableMinerPatchFlag = config.Bind(
                "General",
                "EnableMinerPatchFlag",
                true,
                "Restore vein amount after miner extract (ore). " +
                "When true, regular miners cannot deplete a vein. " +
                "The ILS / PLS vein collector (which uses the same MinerComponent) is also covered.");

            EnableOilPatchFlag = config.Bind(
                "General",
                "EnableOilPatchFlag",
                true,
                "Restore vein amount after oil extractor. " +
                "When true, oil extractors cannot deplete an oil seep.");

            EnableIcarusPatchFlag = config.Bind(
                "General",
                "EnableIcarusPatchFlag",
                true,
                "Restore vein amount after Icarus hand-mining (right-click a resource node). " +
                "When true, the player can hand-mine indefinitely.");

            EnableILSVeinCollectionFlag = config.Bind(
                "General",
                "EnableILSVeinCollectionFlag",
                true,
                "Keep the ILS / PLS collector miner's productCount full. " +
                "When true, the ILS / PLS vein collector can pull from the miner every tick " +
                "without waiting for the next InternalUpdate cycle. " +
                "Note: the vein amount itself is covered by EnableMinerPatchFlag " +
                "(the ILS / PLS uses a regular MinerComponent under the hood).");

            EnableILSSourceFlag = config.Bind(
                "General",
                "EnableILSSourceFlag",
                true,
                "Restore the source ILS / PLS storage after each dispatch. " +
                "When true, the source station's storage buffer is restored to its pre-dispatch " +
                "value, so the station can ship indefinitely.");

            EnablePlanetMinerFastCompatFlag = config.Bind(
                "General",
                "EnablePlanetMinerFastCompatFlag",
                true,
                "Compatibility layer for PlanetMinerFast (DysonSphereMods). " +
                "PlanetMinerFast bypasses MinerComponent.InternalUpdate and does its own vein " +
                "mining in its own slow-tick handler, so Patch A does not cover it. " +
                "When this flag is true, EndlessResources detects PlanetMinerFast via reflection " +
                "and patches its OnSlowTick method with a snapshot (prefix) + restore (postfix) " +
                "pair, so the station vein is also restored to its pre-call amount. " +
                "If PlanetMinerFast is not installed, this flag is a no-op. " +
                "Set to false to disable the compat layer (the station will then deplete the vein " +
                "via PlanetMinerFast as normal).");

            // Rule 2: always present, off by default, gated + detailed.
            DebugLog = config.Bind(
                "Diagnostics",
                "DebugLog",
                false,
                "Enable verbose diagnostic logging. " +
                "Off by default; toggle on for first-run verification. " +
                "Prints category-tagged lines ([config], [patch], [error]) to the BepInEx console.");
        }
    }
}
