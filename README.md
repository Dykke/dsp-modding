# DSP Mods

Source repository for a set of **Dyson Sphere Program** mods built on
**BepInEx 5 + Harmony 2.x**. Each mod is a self-contained folder; the
published release ZIPs (Thunderstore) are the consumer-facing artifact.

---

## Installation (for players)

1. Install [BepInEx 5 for DSP](https://thunderstore.io/c/dyson-sphere-program/p/xiaoye97/BepInEx/), or use [r2modman](https://thunderstore.io/package/ebkr/r2modman/) / the Thunderstore Mod Manager, which installs it for you.
2. Search for the mod you want on Thunderstore, or download its ZIP from this repo's [Releases page](https://github.com/Dykke/dsp-modding/releases).
3. If installing manually: extract the ZIP into your `Dyson Sphere Program` folder. The ZIP is laid out so files land in the right places (`BepInEx\plugins\`, etc.).
4. Launch the game. Each mod writes its default config to `BepInEx\config\` on first run.

## Mods in this repository

| Mod | Description | Status |
|---|---|---|
| [DSPCalculatorPlus](DSPCalculatorPlus/) | Companion for the DSPCalculator mod: force a belt/sorter tier, fix blueprint generation failing on high-throughput items (multi-lane overflow fix), and auto-place power poles over every generated blueprint. Requires DSPCalculator. | v1.0.0 |
| [EndlessResources](EndlessResources/) | Planet veins, oil, and ILS/PLS station stock never run dry - covers miners, oil extractors, Icarus hand-mining, and ILS/PLS vein collection, plus PlanetMinerFast support. | v1.0.0 |
| [PlanetwideAmmoSupply](PlanetwideAmmoSupply/) | Auto-restocks turrets and battle bases with ammo and fighters pulled from the planet's logistics network (ILS/PLS), consuming real station stock. Planetwide by default, configurable reach. | v1.0.0 |
| [StackingPlus](StackingPlus/) | Raises DSP's cargo stacking caps beyond vanilla 4x (sorter output/input, station piler, delivery package), tech-gated and raise-only, plus an optional belt-speed multiplier. Pairs with DSPCalculatorPlus for high-throughput blueprints. | v0.1.0 |

Each mod folder contains its own `README.md` (full feature list,
configuration reference, compatibility notes, troubleshooting) and
`LICENSE`. Open a mod's folder for everything specific to it.

### Mod family

DSPCalculatorPlus and StackingPlus are designed to pair together:
StackingPlus raises the game's cargo-stacking ceiling, and
DSPCalculatorPlus automatically plans generated blueprints against
whatever cap is live - install both, no extra configuration needed on
either side. EndlessResources and PlanetwideAmmoSupply are independent
of that pair and of each other.

## Requirements

- Dyson Sphere Program (latest stable)
- BepInEx 5 (5.4.17 or newer)
- DSPCalculator (only for DSPCalculatorPlus - see that mod's README)

## License

Each mod folder ships with its own MIT `LICENSE` file. The repository
as a whole is private working material; only the mod source code and
its published releases are public.
