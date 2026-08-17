# EndlessResources

> Planet veins, oil, and ILS / PLS station stock never run dry - the
> sources you build on never need re-balancing or replacing.

**Author:** `zicarius`
**Version:** 1.0.0
**Game:** Dyson Sphere Program 0.9.27+ and 0.10.33+
**Loader:** BepInEx 5.4.17+

For players who never want to worry about a vein running dry.

---

## What it does

When installed, every source of items - miner, oil extractor, Icarus
hand-mining, ILS / PLS vein collector, ILS / PLS source station -
stops consuming the source's resources. The planet's veins stay full
forever. The source ILS / PLS storage stays full forever. You can
ship items without re-balancing or replacing the source.

The mod does not change extraction rates, mining speed, or stack
sizes. The vein amount is restored after each consumer's call - the
planet still "produces" at the same rate, the consumer still pulls
the same amount per tick, but the source never depletes.

## Features

- **Miner / ore** - all vein amounts restored after miner extract.
- **Crude oil** - all oil vein amounts restored after oil extractor.
- **Icarus hand-mining** - vein amount restored after Icarus mines a
  resource node (right-click).
- **ILS / PLS vein collection** - vein amount restored after an
  advanced mining station pulls from a planet vein.
- **ILS / PLS source storage** - source station's storage buffer
  restored after a dispatch.
- **PlanetMinerFast support** - a dedicated compatibility layer keeps
  veins full even when PlanetMinerFast (by valoneu) is installed,
  which otherwise depletes them from its own slow-tick handler outside
  this mod's reach.
- **Per-source toggles** - enable or disable each source type
  independently. Default: all on.
- **Gated debug log** - verbose diagnostic logging via the
  `Diagnostics.DebugLog` config toggle.

<details>
<summary><strong>Installation</strong></summary>

**Via r2modman / Thunderstore Mod Manager (recommended):**
1. Install [r2modman](https://thunderstore.io/package/ebkr/r2modman/) or the Thunderstore Mod Manager.
2. Search "Dyson Sphere Program" and set up a profile.
3. Search "EndlessResources" in the Online tab and install.
4. Launch the game through the mod manager.

**Manual install:**
1. Install [BepInEx 5 for Dyson Sphere Program](https://thunderstore.io/c/dyson-sphere-program/p/xiaoye97/BepInEx/).
2. Download the latest `EndlessResources-<version>.zip` from the
   [Releases page](https://github.com/Dykke/dsp-modding/releases).
3. Extract the ZIP into your `Dyson Sphere Program` folder so the
   `plugins\` path lands inside `BepInEx\plugins\`.
4. Launch the game once. BepInEx will create
   `BepInEx\config\com.zicarius.EndlessResources.cfg` with the
   defaults.
5. (Optional) Edit that .cfg to taste.

</details>

## Before you install

This is a BepInEx plugin - it patches the game's code at runtime
using Harmony postfixes (a snapshot-and-restore pattern, not IL
rewriting).

- **Only download from sources you trust.** Only this mod's
  Thunderstore page or the
  [GitHub repo](https://github.com/Dykke/dsp-modding) - treat
  re-uploads elsewhere as unsafe.
- **Game updates can break it.** A DSP update may stop this mod from
  working until a compatibility update is released. If the game
  misbehaves after an update, disable this mod first and check back.
- **Back up your save first.** This mod does not write custom data
  to your save - it only restores in-memory vein/storage values each
  tick - but a backup before enabling any new mod is good practice
  regardless.

## Configuration

All settings live in the BepInEx config file - there is no in-game
settings window.

| Setting | Default | Description |
|---|---|---|
| `General > EnableMinerPatchFlag` | `true` | Restore vein amount after miner extract (ores). |
| `General > EnableOilPatchFlag` | `true` | Restore vein amount after oil extractor. |
| `General > EnableIcarusPatchFlag` | `true` | Restore vein amount after Icarus hand-mining. |
| `General > EnableILSVeinCollectionFlag` | `true` | Restore vein amount after ILS / PLS vein collection. |
| `General > EnableILSSourceFlag` | `true` | Restore source ILS / PLS storage after dispatch. |
| `General > EnablePlanetMinerFastCompatFlag` | `true` | Also restore veins that PlanetMinerFast depletes from its own slow-tick handler. No-op if PlanetMinerFast isn't installed. |
| `Diagnostics > DebugLog` | `false` | Verbose logging, off by default. |

(Full reference: see the .cfg file BepInEx writes on first run.)

## Compatibility

- **Game version:** DSP 0.9.27+ and 0.10.33+ (multi-version with
  defensive reflection).
- **PlanetMinerFast (by valoneu):** supported via a dedicated compat
  layer (`Patches\PlanetMinerFastCompat.cs`, gated by
  `EnablePlanetMinerFastCompatFlag`, default `true`). PlanetMinerFast
  depletes vein amounts directly from its own slow-tick handler,
  bypassing `MinerComponent.InternalUpdate` entirely - without the
  compat layer the vein would still slowly drain even with this mod
  installed. Confirmed working in-game.
- **StacksizeMultiplier:** compatible (orthogonal).
- **MaxLVLIncrease:** compatible (orthogonal).
- **CommonAPI:** not used in v1. Will be required for v1.1's
  in-game window.
- **Multiplayer:** untested in Nebula multiplayer. Vein / storage
  state is per-planet and the patches are per-tick - should work but
  is not confirmed.

<details>
<summary><strong>Troubleshooting</strong></summary>

**Mod doesn't load:**
- Ensure the file is in `BepInEx\plugins\EndlessResources\` (not `Mods\`).
- Check `BepInEx\LogOutput.log` for errors near `Loading [EndlessResources]`.

**Vein or ILS/PLS storage still depletes:**
- Turn on `Diagnostics > DebugLog` and check `BepInEx\LogOutput.log`
  for `[patch] Patch A fired` (or the relevant patch letter) - if it
  never appears, that patch isn't applying.
- If PlanetMinerFast is installed, confirm the log shows
  `[compat] PlanetMinerFast detected; applied snapshot/restore
  patch...` rather than `not detected` - if it says "not detected"
  after the game has fully loaded, PlanetMinerFast likely isn't
  actually running, or its internal method signature changed.

</details>

## License

MIT. See `LICENSE` for the full text.

## Credits

- `EndlessResources` by `zicarius`.
- Interoperates with PlanetMinerFast by valoneu via an optional
  compatibility layer (that mod is not required and not bundled).

### Support

All my mods are free and always will be. If this one made your
playthrough better and you feel like buying me a coffee, that keeps
me motivated to maintain these mods and build new ones:
[https://ko-fi.com/zicarius](https://ko-fi.com/zicarius)

---

**NEVER RUN DRY!**
