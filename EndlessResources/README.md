# EndlessResources

> All veins stay full. All logistics buffers stay full. A modern Harmony
> replacement for the DSP Infinite Resource Nodes IL-patcher mod, with
> added ILS / PLS coverage.

**Author:** `zicarius`
**Version:** 1.0.0
**Game:** Dyson Sphere Program 0.9.27+ and 0.10.33+
**Loader:** BepInEx 5.4.17+

EndlessResources makes planet resources non-depleting regardless of
who consumes them. Every miner, oil extractor, Icarus hand-mine, and
ILS / PLS vein collector or source station leaves the source's amount
unchanged. It is a standalone replacement for the
[DSP Infinite Resource Nodes](https://dsp.thunderstore.io/package/GreyHak/DSP_Infinite_Resource_Nodes/)
mod (which uses a fragile Mono.Cecil IL patcher that has needed 6
version-fix updates) and adds the ILS / PLS coverage that mod lacks.

---

## What it does

When installed, every source of items - miner, oil extractor, Icarus
hand-mine, ILS / PLS vein collector, ILS / PLS source station -
stops consuming the source's resources. The planet's veins stay full
forever. The source ILS / PLS storage stays full forever. The player
can ship items without re-balancing or replacing the source.

The mod does not change extraction rates, mining speed, or stack
sizes. The vein amount is restored after each consumer's call - the
planet still "produces" at the same rate, the consumer still pulls
the same amount per tick, but the source never depletes.

## Features

- **Miner / ore** - all vein amounts restored after miner extract.
- **Crude oil** - all oil vein amounts restored after oil extractor.
- **Icarus hand-mine** - vein amount restored after each `F` press.
- **ILS / PLS vein collection** - vein amount restored after the
  station pulls from a planet vein.
- **ILS / PLS source storage** - source station's storage buffer
  restored after a dispatch.
- **Per-source toggles** - enable or disable each source type
  independently. Default: all on.
- **Gated debug log** - verbose diagnostic logging via the
  `Diagnostics.DebugLog` config toggle.

## Installation

1. Install [BepInEx 5 for Dyson Sphere Program](https://thunderstore.io/c/dyson-sphere-program/p/xiaoye97/BepInEx/).
2. Download the latest `EndlessResources-<version>.zip` from the
   [Releases page](https://github.com/Dykke/dsp-modding/releases).
3. Extract the ZIP into your `Dyson Sphere Program` folder so the
   `plugins\` path lands inside `BepInEx\plugins\`.
4. Launch the game once. BepInEx will create
   `BepInEx\config\com.zicarius.EndlessResources.cfg` with the
   defaults.
5. (Optional) Edit that .cfg to taste.

## Configuration

| Setting | Default | Description |
|---|---|---|
| `General > EnableMinerPatchFlag` | `true` | Restore vein amount after miner extract (ores). |
| `General > EnableOilPatchFlag` | `true` | Restore vein amount after oil extractor. |
| `General > EnableIcarusPatchFlag` | `true` | Restore vein amount after Icarus hand-mine. |
| `General > EnableILSVeinCollectionFlag` | `true` | Restore vein amount after ILS / PLS vein collection. |
| `General > EnableILSSourceFlag` | `true` | Restore source ILS / PLS storage after dispatch. |
| `Diagnostics > DebugLog` | `false` | Verbose logging, off by default. |

(Full reference: see the .cfg file BepInEx writes on first run.)

## Compatibility

- **Game version:** DSP 0.9.27+ and 0.10.33+ (multi-version with
  defensive reflection).
- **DSP Infinite Resource Nodes:** incompatible by design. This mod
  replaces it. Both installed = redundant but not crashing.
- **PlanetMinerFast:** compatible via a dedicated compat layer
  (`Patches\PlanetMinerFastCompat.cs`, gated by
  `EnablePlanetMinerFastCompatFlag`, default `true`). PlanetMinerFast
  depletes vein amounts directly from its own slow-tick handler,
  bypassing `MinerComponent.InternalUpdate` entirely - without the
  compat layer the vein would still slowly drain even with this mod
  installed. Confirmed working in-game as of 2026-08-11.
- **StacksizeMultiplier:** compatible (orthogonal).
- **MaxLVLIncrease:** compatible (orthogonal).
- **CommonAPI:** not used in v1. Will be required for v1.1's
  in-game window.
- **Multiplayer:** untested in MP. Vein / storage state is per-planet
  and the patches are per-tick - should work but is not confirmed.

## Screenshots

(TODO before publish. Warm-palette Forge / Vault / Botanical theme
per the user's branding preference. Avoid "AI slop" bright cyan.)

## License

MIT. See `LICENSE` for the full text.

## Credits

- `EndlessResources` by `zicarius`.
- Built on the workspace template at `cursor-stuff\templates\mod-template\`.
- Replaces and extends
  [DSP Infinite Resource Nodes](https://dsp.thunderstore.io/package/GreyHak/DSP_Infinite_Resource_Nodes/)
  by GreyHak (BSD 3 clause).
