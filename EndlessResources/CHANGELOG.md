# Changelog

All notable changes to this mod are documented here. The format is
based on [Keep a Changelog](https://keepachangelog.com/) and the
project follows [Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-08-11 - Initial release

### Added
- First public release.
- Vein amount restored after miner extract (Patch A on
  `MinerComponent.InternalUpdate`).
- Vein amount restored after oil extractor (folded into Patch A;
  gated by `EnableOilPatchFlag`).
- Vein amount restored after Icarus hand-mine (Patch B on
  `PlayerAction_Mine.GameTick`).
- Vein amount restored after ILS / PLS vein collection (Patch C on
  `StationComponent.UpdateVeinCollection`).
- Source ILS / PLS storage restored after dispatch (Patch D on
  `StationComponent.DetermineDispatch`).
- Orbit collection amount restored (Patch E on
  `StationComponent.UpdateCollection`).
- 5 General config toggles + 1 Diagnostics `DebugLog` toggle.
- Gated verbose logging through the `EndlessResourcesLog` helper.
- Multi-version support via defensive `AccessTools.Method` lookups.
- 8 documented in-game test scenarios.

### Notes
- This release replaces the
  [DSP Infinite Resource Nodes](https://dsp.thunderstore.io/package/GreyHak/DSP_Infinite_Resource_Nodes/)
  IL-patcher mod with a runtime Harmony plugin. The two mods are
  not designed to co-exist.
- ILS / PLS coverage is the primary new feature.
- No in-game UI in v1; all configuration is via the BepInEx .cfg
  file. UI planned for v1.1 via CommonAPI.
