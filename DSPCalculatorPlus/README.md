# DSPCalculatorPlus

> A companion for the **DSPCalculator** mod: force a specific belt/sorter
> tier in generated blueprints, and generate blueprints for
> high-throughput items that stock DSPCalculator refuses to build.

**Author:** `zicarius`
**Status:** 🚧 In development (v1.0.0) — scaffold + config are in place and
compile against DSPCalculator 0.5.25; the two Harmony features below are
being implemented next.
**Game:** Dyson Sphere Program
**Loader:** BepInEx 5.4.17+
**Requires:** [DSPCalculator](https://thunderstore.io/c/dyson-sphere-program/p/jinxOAO/DSPCalculator/) (`com.GniMaerd.DSPCalculator`, tested 0.5.25)

---

## What it will do

1. **Force a belt / sorter tier.** DSPCalculator only offers a
   "highest vs cheapest-that-fits" toggle; this lets you pin an exact
   tier for generated blueprints (`Auto` defers to DSPCalculator).
2. **Multi-lane overflow fix.** When one belt of the chosen tier can't
   carry an item's full throughput, DSPCalculator aborts generation.
   This splits the flow across multiple parallel belts/sorters instead,
   up to a configurable cap.

This mod **patches DSPCalculator's compiled DLL at runtime via
reflection** — it never copies or forks DSPCalculator's source.

## Configuration

All settings live in `BepInEx\config\com.zicarius.DSPCalculatorPlus.cfg`
— there is no in-game settings window.

| Setting | Default | Description |
|---|---|---|
| `General > BeltTierOverride` | `Auto` | Force belt tier (Auto/Mk1/Mk2/Mk3). |
| `General > SorterTierOverride` | `Auto` | Force sorter tier (Auto/Mk1–Mk4). |
| `General > EnableMultiLaneOverflowFix` | `true` | Split flows instead of failing. |
| `General > MaxLaneCount` | `4` | Cap on parallel lanes per item (1–16). |
| `Diagnostics > DebugLog` | `false` | Verbose logging + target-signature dump. |

## Compatibility

- **DSPCalculator** — hard dependency (BepInEx GUID
  `com.GniMaerd.DSPCalculator`, Thunderstore `jinxOAO-DSPCalculator`),
  tested against **0.5.25**. The mod won't load without it.
- Forcing a sorter tier below Mk4 intentionally bypasses DSPCalculator's
  pile-sorter output-stacking.

## License

MIT (see `LICENSE` before first release).

## Credits

- `DSPCalculatorPlus` by `zicarius`. Built on DSPCalculator by its authors.
- Support (optional): [ko-fi.com/zicarius](https://ko-fi.com/zicarius)
