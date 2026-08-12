# PlanetwideAmmoSupply

> Your turrets never run dry - the whole planet restocks them for you.

**Author:** `zicarius`
**Version:** 1.0.0
**Game:** Dyson Sphere Program 0.10.x (built against 0.10.34)
**Loader:** BepInEx 5.4.17+

For anyone tired of belting ammo to every single turret. This mod feeds
your planetary defenses straight from your logistics network.

---

## What it does

Every turret and battle base on a planet is topped up with ammo (and
fighters) pulled straight from that planet's logistics stations (ILS/PLS)
- no belts, no manual runs. The ammo is drawn from **real station stock**,
so it isn't free: a station has to actually hold the ammo for it to be
used. When you're not looking, your defenses stay loaded.

## Features

- **Auto-restock turrets** with ammo pulled from the planet's logistics network.
- **Auto-restock battle bases** with ammo and fighters from logistics.
- **Consume model** - ammo comes from real station stock, not thin air.
- **Planetwide by default** - or set a limited radius if you want turrets to pull only from nearby stations.
- **Highest-tier ammo preference** - empty turrets fill with the best ammo you have in stock (toggleable).
- **Nearest-station-first** sourcing so the closest depot is drained first.
- **Cheap** - a throttled scan (not every tick), so it stays light on UPS.
- **Config-only** - no in-game UI; every setting lives in the BepInEx config file.

## Installation

**Via r2modman / Thunderstore Mod Manager (recommended):**
1. Install [r2modman](https://thunderstore.io/package/ebkr/r2modman/) or the Thunderstore Mod Manager.
2. Search "Dyson Sphere Program" and set up a profile.
3. Search "PlanetwideAmmoSupply" in the Online tab and install.
4. Launch the game through the mod manager.

**Manual install:**
1. Install [BepInEx 5 for Dyson Sphere Program](https://thunderstore.io/c/dyson-sphere-program/p/xiaoye97/BepInEx/).
2. Download the latest `PlanetwideAmmoSupply-<version>.zip` from the
   [Releases page](https://github.com/Dykke/dsp-modding/releases).
3. Extract the ZIP into your `Dyson Sphere Program` folder so the
   `plugins\` path lands inside `BepInEx\plugins\`.
4. Launch the game once. BepInEx creates
   `BepInEx\config\com.zicarius.PlanetwideAmmoSupply.cfg` with the defaults.
5. (Optional) Edit that .cfg to taste - **with the game closed**, then relaunch.

## Before you install

This is a BepInEx plugin - it patches the game's code at runtime using
Harmony patches.

- **Only download from sources you trust.** Only this mod's Thunderstore
  page or the [GitHub repo](https://github.com/Dykke/dsp-modding) - treat
  re-uploads elsewhere as unsafe.
- **Game updates can break it.** A DSP update may stop this mod from
  working until a compatibility update is released.
- **Save-safe by design.** This mod stores **nothing** in your save. It
  only tops up existing turret/battle-base ammo from existing station
  stock while you play. Remove it any time and your save stays vanilla -
  your defenses simply stop auto-refilling.

## Configuration

All settings live in the BepInEx config file - there is no in-game
settings window. Edit it with the game closed, then relaunch (config is
read at launch).

| Setting | Default | Description |
|---|---|---|
| `General > Enabled` | `true` | Master switch. Off = fully inert, vanilla belt supply unchanged. |
| `General > SupplyTurrets` | `true` | Auto-refill turret ammo from logistics. |
| `General > SupplyBattleBases` | `true` | Auto-refill battle-base ammo and fighters from logistics. |
| `General > PreferHighestAmmoTier` | `true` | Fill empty turrets with the highest available ammo tier (`false` = cheapest). |
| `General > SupplyRadius` | `0` | Max distance (world units) from a structure to a station. `0` = planetwide (recommended). A standard planet is ~200-unit radius; small values match almost nothing. |
| `General > NearestStationFirst` | `true` | Drain the closest eligible station first. |
| `General > RefillIntervalTicks` | `60` | Ticks between refill scans (~60 = 1s). Higher = cheaper. |
| `Advanced > RequireStationSupplyFlag` | `false` | If `true`, only pull from station slots set to **Supply** (never Demand/Storage). |
| `Advanced > FighterItemFilter` | _(empty)_ | Optional: restrict which fighter/ammo items battle bases pull. |
| `Advanced > VerboseScan` | `false` | With `DebugLog` on, logs a periodic scan heartbeat (and the measured distance to the nearest supplying station - handy for sizing `SupplyRadius`). |
| `Diagnostics > DebugLog` | `false` | Verbose logging to the BepInEx console. Off by default. |

(Full reference: the .cfg file BepInEx writes on first run.)

## Compatibility

- **Game version:** DSP 0.10.x (built against 0.10.34).
- **Other mods:** No known conflicts. Pairs naturally with resource mods
  like **EndlessResources** - keep your stations stocked and your turrets
  effectively never run out.
- **Multiplayer:** built and tested for singleplayer; untested in Nebula
  multiplayer.

## Troubleshooting

**Mod doesn't load:**
- Ensure the file is in `BepInEx\plugins\PlanetwideAmmoSupply\` (not `Mods\`).
- Check `BepInEx\LogOutput.log` for errors near `Loading [PlanetwideAmmoSupply]`.

**Turrets aren't refilling:**
- The ammo has to exist in a logistics station on the same planet. If a
  station doesn't hold that ammo, there's nothing to pull.
- If you set `SupplyRadius` to a small number, widen it or set it to `0`
  (planetwide). Distances are in world units, not build-grid tiles - a
  standard planet is ~200-unit radius.
- Turn on `DebugLog` **and** `VerboseScan` to see the heartbeat, including
  `nearestStation=` (how far your closest supplying station actually is).
- Config edits only take effect on relaunch - change the .cfg with the
  game closed.

## License

MIT. See `LICENSE` for the full text.

## Credits

- `PlanetwideAmmoSupply` by `zicarius`.
- Built on the workspace template at `cursor-stuff\templates\mod-template\`.

### Support

All my mods are free and always will be. If this one made your
playthrough better and you feel like buying me a coffee, that keeps me
motivated to maintain these mods and build new ones:
[https://ko-fi.com/zicarius](https://ko-fi.com/zicarius)

---

**KEEP YOUR GUNS LOADED.**
