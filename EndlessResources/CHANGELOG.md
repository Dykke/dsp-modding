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
- Vein amount restored after Icarus hand-mining (Patch B on
  `PlayerAction_Mine.GameTick`).
- ILS / PLS vein collector's miner buffer kept full (Patch C on
  `StationComponent.UpdateVeinCollection`).
- Source ILS / PLS storage restored after dispatch (Patch D on
  `StationComponent.DetermineDispatch`).
- PlanetMinerFast compatibility layer (snapshot/restore on its
  `OnSlowTick`, applied via reflection; no-op if not installed).
- 6 General config toggles + 1 Diagnostics `DebugLog` toggle.
- Gated verbose logging through the `EndlessResourcesLog` helper.
- Works on DSP 0.9.27+ and 0.10.33+ (stable vein/station field
  names across both lines).
- 8 documented in-game test scenarios.

### Notes
- ILS / PLS coverage is the primary feature: both the vein-collector
  path and the source-station dispatch path stay non-depleting.
- No in-game UI in v1; all configuration is via the BepInEx .cfg
  file. UI planned for v1.1 via CommonAPI.
