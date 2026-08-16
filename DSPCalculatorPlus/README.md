# DSPCalculatorPlus

> Force a belt/sorter tier, fix blueprint generation failing on
> high-throughput items, and auto-wire every generated blueprint with
> power poles - DSPCalculator, hands-off.

**Author:** `zicarius`
**Version:** 1.0.0
**Game:** Dyson Sphere Program 0.10.x (built against 0.10.34)
**Loader:** BepInEx 5.4.17+
**Requires:** [DSPCalculator](https://thunderstore.io/c/dyson-sphere-program/p/jinxOAO/DSPCalculator/) 0.5.25+ (hard dependency - this mod won't load without it)
**Pairs well with:** [StackingPlus](https://github.com/Dykke/dsp-modding) (same author, optional - auto-detected, no setup needed)

For anyone who uses DSPCalculator to generate blueprints and is tired of
hand-placing hundreds of power poles afterward, or hitting "Generation
Failed" on a high-throughput item DSPCalculator refuses to build.

---

## What it does

DSPCalculatorPlus patches DSPCalculator at runtime (Harmony, no source
copied) to add three things DSPCalculator doesn't do on its own: pin an
exact belt/sorter tier instead of "highest vs cheapest", keep generation
from failing outright on items too fast for one belt, and auto-place
power poles over the finished blueprint so it's ready to paste and power
on immediately.

## Features

- **Force a belt/sorter tier.** DSPCalculator only offers a "highest vs
  cheapest-that-fits" toggle; pin an exact tier for every generated
  blueprint instead (`Auto` defers to DSPCalculator).
- **Multi-lane overflow fix.** When an item's throughput exceeds one belt
  of the fastest tier - which stock DSPCalculator aborts generation for -
  this supplies that item as an external logistics input instead, so
  generation succeeds.
- **Belt-stacking push (last resort).** If a block's output still can't
  carry an un-externalizable byproduct (e.g. high-quantity hydrogen),
  raises DSPCalculator's belt-stacking cap and regenerates. The resulting
  blueprint assumes that stacking - you need matching pile/proliferator
  tech to run it at full rate.
- **Auto power poles.** Every generated blueprint gets Tesla Towers placed
  automatically - one pole per machine line, the way a player lays them by
  hand - so you paste and wire in once instead of hand-placing hundreds of
  poles.
- **StackingPlus sync (optional).** When StackingPlus is also installed,
  detected automatically - the overflow fix can then push belt-stacking
  past vanilla 4x to StackingPlus's raised cap, letting more
  high-throughput blueprints succeed without externalizing.

<details>
<summary><strong>Installation</strong></summary>

**Via r2modman / Thunderstore Mod Manager (recommended):**
1. Install [r2modman](https://thunderstore.io/package/ebkr/r2modman/) or the Thunderstore Mod Manager.
2. Search "Dyson Sphere Program" and set up a profile.
3. Search "DSPCalculatorPlus" in the Online tab and install (this pulls in DSPCalculator as a dependency automatically).
4. Launch the game through the mod manager.

**Manual install:**
1. Install [BepInEx 5 for Dyson Sphere Program](https://thunderstore.io/c/dyson-sphere-program/p/xiaoye97/BepInEx/) and [DSPCalculator](https://thunderstore.io/c/dyson-sphere-program/p/jinxOAO/DSPCalculator/).
2. Download the latest `DSPCalculatorPlus-<version>.zip` from the
   [Releases page](https://github.com/Dykke/dsp-modding/releases).
3. Extract the ZIP into your `Dyson Sphere Program` folder so the
   `plugins\` path lands inside `BepInEx\plugins\`.
4. Launch the game once. BepInEx creates
   `BepInEx\config\com.zicarius.DSPCalculatorPlus.cfg` with the defaults.
5. (Optional) Edit that .cfg to taste - **with the game closed**, then relaunch.

</details>

## Before you install

This is a BepInEx plugin - it patches DSPCalculator's compiled code at
runtime via Harmony, reaching every target through reflection. No
DSPCalculator source is copied, forked, or redistributed.

- **Only download from sources you trust.** Only this mod's Thunderstore
  page or the [GitHub repo](https://github.com/Dykke/dsp-modding) - treat
  re-uploads elsewhere as unsafe.
- **Game or DSPCalculator updates can break it.** A DSP or DSPCalculator
  update may stop this mod from working until a compatibility update is
  released; a renamed target is skipped with a warning rather than
  crashing.
- **Save-safe by design.** This mod stores **nothing** in your save. It
  only modifies blueprints DSPCalculator generates, in memory, before you
  paste them. Remove it any time - buildings you already pasted stay
  exactly as pasted (fully vanilla); you just lose the tier-override,
  overflow-fix, and auto-pole features for future generations.

## Configuration

All settings live in the BepInEx config file - there is no in-game
settings window. Edit it with the game closed, then relaunch (config is
read at launch).

| Setting | Default | Description |
|---|---|---|
| `General > BeltTierOverride` | `Auto` | Force belt tier (Auto/Mk1/Mk2/Mk3) in generated blueprints. |
| `General > SorterTierOverride` | `Auto` | Force sorter tier (Auto/Mk1-Mk4). Below Mk4 bypasses pile-sorter output stacking. |
| `General > EnableMultiLaneOverflowFix` | `true` | Externalize an over-throughput item as a logistics input instead of failing generation. |
| `General > PushBeltStackingOnOverflow` | `true` | Last-resort: raise belt-stacking and regenerate when a block's output still can't carry a byproduct. |
| `General > AutoPowerPoles` | `TeslaTower` | `Off` / `TeslaTower` - auto-place Tesla Tower power poles over generated blueprints. |
| `General > PolesUnderRaisedBelts` | `true` | Allow poles on ground tiles only occupied by a belt raised above the pole's height (belt crossings). |
| `Compatibility > EnableStackingPlusCompat` | `true` | Sync DSPCalculator's stacking cap to StackingPlus's raised cap when it's installed. No effect otherwise. |
| `Diagnostics > DebugLog` | `false` | Verbose logging, including exact coordinates of the worst-covered machines for the auto-pole feature. |

(Full reference: the .cfg file BepInEx writes on first run.)

## Compatibility

- **Game version:** DSP 0.10.x (built against 0.10.34).
- **DSPCalculator:** hard dependency (BepInEx GUID
  `com.GniMaerd.DSPCalculator`, Thunderstore `jinxOAO-DSPCalculator`),
  tested against **0.5.25**. This mod won't load without it.
- **StackingPlus:** fully optional. Same author, auto-detected at
  runtime - install it if you want the overflow fix able to push past
  vanilla 4x belt-stacking; DSPCalculatorPlus works completely fine
  without it.
- **Other mods:** this mod only ever touches blueprints DSPCalculator
  itself generates (it detects DSPCalculator's own temp blueprint
  internally) - a normal manual blueprint paste is never modified, so it
  should be safe alongside any other blueprint/building mod.
- **Suggested pairing:** [GalacticScale](https://thunderstore.io/c/dyson-sphere-program/p/Galactic_Scale/GalacticScale/)
  (unrelated third-party mod) raises planet radius well past vanilla's
  ~200 - this mod's auto-pole and overflow-fix features earn their keep
  most on exactly that kind of oversized, high-density production line.
  Not required, just a natural fit if you're building big.
- **Multiplayer:** built and tested for singleplayer; untested in Nebula
  multiplayer.

## Good to know

- **Clustered poles aren't a bug.** DSPCalculator packs machines with
  almost no spare tiles, so in dense blueprints Tesla Towers squeeze into
  whatever aisle tiles are free and can end up close together. A tight
  cluster that looks redundant is usually each pole covering a different
  machine the layout leaves no other way to reach.
- **A rare machine can still come up unpowered.** Poles are added to the
  blueprint's own data before you choose where to paste it, so the mod
  has no way to see the actual ground you'll paste onto. Across extensive
  testing (dozens of generations from ~300 to ~73,000 buildings),
  coverage is consistently at or effectively 100%; on rare occasion a
  single machine may still come up unpowered because the real paste
  location rejected a specific pole for a reason that can't be predicted
  ahead of time. If that happens, just drop one pole by hand - turn on
  `DebugLog` and check the log for `[poles][diag] worst consumer` lines,
  which give the exact local coordinates of the hardest-to-cover
  machines.

<details>
<summary><strong>Troubleshooting</strong></summary>

**Mod doesn't load:**
- Ensure the file is in `BepInEx\plugins\DSPCalculatorPlus\` (not `Mods\`).
- Make sure DSPCalculator itself is installed and loads without errors -
  this mod requires it and won't start otherwise.
- Check `BepInEx\LogOutput.log` for errors near `Loading [DSPCalculatorPlus]`.

**Generated blueprint has no power poles after pasting:**
- Check `AutoPowerPoles` isn't set to `Off`.
- Make sure you're pasting a blueprint DSPCalculator itself just
  generated, not a re-saved or re-exported one - detection keys off
  DSPCalculator's own temp blueprint.

**A few machines are still unpowered after paste:**
- See "Good to know" above. Turn on `DebugLog`, regenerate, and check the
  log for the exact coordinates of the worst-covered machines.

**Overflow fix isn't kicking in / generation still fails:**
- Check `EnableMultiLaneOverflowFix` and `PushBeltStackingOnOverflow` are
  both `true`.
- The blueprint's final target output can't be externalized and will
  still fail generation cleanly by design - reduce its demand or change
  its recipe instead.

</details>

## License

MIT. See `LICENSE` for the full text.

## Credits

- `DSPCalculatorPlus` by `zicarius`. Built on DSPCalculator by its
  author (`jinxOAO`) via Harmony patches and reflection only - no
  DSPCalculator source is copied, forked, or redistributed.

### Support

All my mods are free and always will be. If this one made your
playthrough better and you feel like buying me a coffee, that keeps me
motivated to maintain these mods and build new ones:
[https://ko-fi.com/zicarius](https://ko-fi.com/zicarius)

---

**POWER HANDLED. GO BUILD.**
