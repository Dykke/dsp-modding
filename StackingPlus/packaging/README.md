# StackingPlus

> Raise Dyson Sphere Program's cargo stacking caps beyond vanilla 4x, with an
> optional belt-speed boost.

**Author:** `zicarius`
**Version:** 0.1.0
**Game:** Dyson Sphere Program (current Steam release)
**Loader:** BepInEx 5.4.17+

For players whose factories outgrow vanilla's stacking ceiling - especially
alongside DSPCalculator / DSPCalculatorPlus, where a high-throughput
blueprint's byproduct (hydrogen at scale is the classic case) can exceed even
a fully-stacked Mk.III belt.

---

## What it does

Vanilla DSP caps cargo stacking - how many items a Pile Sorter piles onto one
belt cargo slot, how much a logistics station piler stacks, how much a
delivery package carries - at 4x. StackingPlus raises that ceiling (default
8x, configurable up to the game's hard byte limit of 255x) across four
independent dimensions, and optionally speeds up belts themselves. By
default it's tech-gated: your factory plays exactly like vanilla until
you've researched the vanilla-max stacking tech, then the higher cap kicks
in automatically.

## Features

- **Four independent stacking caps**, each toggleable and separately
  configurable: sorter output (the belt cargo cap - the core throughput
  fix), sorter input, station piler, and delivery package.
- **Tech-gated by default** - early game is untouched vanilla; the boost
  applies once you've researched the vanilla-max stacking tech for that
  dimension. A config toggle makes it apply immediately instead.
- **Raise-only** - never lowers a value you already have above the
  configured cap, even if you tune the cap down later.
- **Optional belt-speed multiplier** (experimental, default OFF) that
  genuinely speeds up belts, including a full fix so a boosted belt still
  visually renders as its real tier instead of looking like a faster one.
- **Works with DSPCalculatorPlus out of the box** - when both are
  installed, DSPCalculatorPlus automatically plans blueprints against your
  raised cap (no extra config on either side), letting its overflow fix push
  a stuck high-throughput byproduct past the vanilla belt wall.

<details>
<summary><strong>Installation</strong></summary>

**Via r2modman / Thunderstore Mod Manager (recommended):**
1. Install [r2modman](https://thunderstore.io/package/ebkr/r2modman/) or the Thunderstore Mod Manager.
2. Search "Dyson Sphere Program" and set up a profile.
3. Search "StackingPlus" in the Online tab and install.
4. Launch the game through the mod manager.

**Manual install:**
1. Install [BepInEx 5 for Dyson Sphere Program](https://thunderstore.io/c/dyson-sphere-program/p/xiaoye97/BepInEx/).
2. Download the latest `StackingPlus-<version>.zip` from the
   [Releases page](https://github.com/Dykke/dsp-modding/releases).
3. Extract the ZIP into your `Dyson Sphere Program` folder so the
   `plugins\` path lands inside `BepInEx\plugins\`.
4. Launch the game once. BepInEx creates
   `BepInEx\config\com.zicarius.StackingPlus.cfg` with the defaults.
5. (Optional) Edit that .cfg to taste.

</details>

## Before you install

This is a BepInEx plugin - it patches the game's code at runtime using
Harmony, targeting the game's own save-load, tech-research, and belt
rendering methods.

- **Only download from sources you trust.** Only this mod's Thunderstore
  page or the [GitHub repo](https://github.com/Dykke/dsp-modding) - treat
  re-uploads elsewhere as unsafe.
- **Game updates can break it.** A DSP update may stop this mod from
  working until a compatibility update is released. Every patched method
  is resolved defensively (via reflection) - a rename disables just that
  one feature with a log warning instead of crashing.
- **Save impact - be aware, not alarmed.** The raised stacking values (and
  boosted belt speeds, if enabled) are stored using the game's own existing
  save fields - nothing new is added to the save format. Once a cap is
  raised in your save, it stays raised even if you later uninstall the mod
  or lower the cap in config (the mod only ever raises, never lowers) - a
  harmless lingering buff, not corruption. Still, back up your save before
  trying a new mod, as always.

## Configuration

All settings live in the BepInEx config file - there is no in-game settings
window.

| Section | Setting | Default | Description |
|---|---|---|---|
| Stacking | `EnableSorterOutput` | `true` | Raise Pile Sorter -> belt output stacking (the belt cargo cap). Core throughput fix. |
| Stacking | `SorterOutputCap` | `8` | Target sorter output stack size (2-255; vanilla 4). |
| Stacking | `EnableSorterInput` | `true` | Raise Pile Sorter pickup (input) stacking. |
| Stacking | `SorterInputCap` | `8` | Target sorter input stack size (2-255; vanilla 4). |
| Stacking | `EnableStationPiler` | `true` | Raise the logistics station output piler level. |
| Stacking | `StationPilerCap` | `8` | Target station piler level (2-255; vanilla 4). |
| Stacking | `EnableDeliveryPackage` | `true` | Raise the delivery package stack-size multiplier. |
| Stacking | `DeliveryPackageCap` | `8` | Target delivery package multiplier (2-255; vanilla 4). |
| Stacking | `TechGated` | `true` | If true, each boost applies only once its vanilla ceiling is researched. If false, caps are forced immediately from the start. |
| Advanced | `VanillaCeilingOutput` / `Input` / `Piler` / `Package` | `4` each | The vanilla max used for tech-gating each dimension. Override only if a DSP update changes these. |
| BeltSpeed | `Enable` | `false` | EXPERIMENTAL: multiply belt speed. Also fixes boosted belts rendering as the wrong tier. Needs a game restart to apply/change. |
| BeltSpeed | `Multiplier` | `2.0` | Belt speed multiplier (1.0-10.0), applied as belts are created/rebuilt and to existing belts on save load. |
| Diagnostics | `DebugLog` | `false` | Verbose logging to the BepInEx console, off by default. |

Every cap is clamped to `[2, 255]` - `Cargo.stack` is a byte in-engine, so
255x is the game's true hard ceiling.

## Compatibility

- **Game version:** current DSP Steam release, BepInEx 5.4.17+.
- **DSPCalculatorPlus:** designed to pair with it. If both are installed,
  DSPCalculatorPlus automatically syncs its blueprint-planning cap to
  StackingPlus's live in-game value - no config needed on either side.
- **Other mods:** no known conflicts. If another mod also modifies belt
  speed or stacking caps, last-writer-wins applies (StackingPlus re-asserts
  its values on save load and tech-research events).
- **Multiplayer (Nebula):** not tested in an actual Nebula session. From a
  code review: the belt-visual fixes are purely local, client-side
  rendering and carry no multiplayer risk. The stacking-cap and belt-speed
  patches modify shared game state (tech/history data, factory/belt data)
  via hooks on the game's own save-load and tech-research methods - the
  same category of change most DSP stat-boosting mods make - so it should
  behave consistently as long as the host and every client run the same
  mod version and config, which is the standard requirement for any
  state-modifying mod under Nebula. One specific, unverified risk: if
  Nebula reuses the game's per-factory load method for its live network
  sync (not just the initial join), the belt-speed feature would re-scan
  the whole belt pool on every such sync - a possible performance cost on
  large factories, not a correctness risk. If you hit issues in multiplayer
  specifically, try `BeltSpeed.Enable=false` first to isolate it.

<details>
<summary><strong>Troubleshooting</strong></summary>

**Mod doesn't load:**
- Ensure the file is in `BepInEx\plugins\StackingPlus\` (not `Mods\`).
- Check `BepInEx\LogOutput.log` for errors near `Loading [StackingPlus]`.

**Stacking isn't increasing:**
- With `TechGated=true` (default), a dimension only boosts once you've
  researched its vanilla-max stacking tech. Either research it, or set
  `TechGated=false` (game closed, then relaunch) to force it immediately.

**Belt speed / belt-visual changes aren't showing up, or a belt looks
wrong-tier:**
- `BeltSpeed` settings are read at game launch - close the game, edit the
  .cfg, and relaunch; a live edit while playing has no effect.
- To fully revert a belt's speed to vanilla, keep `BeltSpeed.Enable=true`
  but set `Multiplier=1` and relaunch - this normalizes every belt back to
  vanilla speed. Setting `Enable=false` instead leaves belts exactly as the
  save last recorded them (it stops the mod from touching belts at all).

</details>

## License

MIT - see [`LICENSE`](LICENSE).

## Credits

- `StackingPlus` by `zicarius`.

### Support

All my mods are free and always will be. If this one made your playthrough
better and you feel like buying me a coffee, that keeps me motivated to
maintain these mods and build new ones:
[https://ko-fi.com/zicarius](https://ko-fi.com/zicarius)

---

**FEWER BELTS, MORE THROUGHPUT.**
