# EndlessResources - in-game test plan

> The 8 scenarios that must pass before any release. Newest
> results appended on top per the workspace's append-on-top
> convention.

## How to run a test

1. Make sure BepInEx is installed at
   `Dyson Sphere Program\BepInEx\`.
2. Build and deploy the mod:
   ```cmd
   cursor-stuff\tools\build.cmd EndlessResources
   ```
3. Edit `BepInEx\config\com.author.EndlessResources.cfg` to
   match the test's needs (e.g. enable `DebugLog` for Test F).
4. Launch DSP. Watch `BepInEx\LogOutput.log` for the
   `[EndlessResources] Patches applied.` line on load.
5. Run the in-game flow.
6. Verify the expected behaviour.
7. Record the result below.

## Expected BepInEx log output on a clean load

When `Debug log = false` (the default), the mod logs only on load:

```
[Info   :   BepInEx] Loading [EndlessResources 1.0.0]
[Info   :   BepInEx] ... plugin EndlessResources is loaded!
```

When `Debug log = true`, the mod additionally prints:

```
[Info   : EndlessResources] [config] Loaded with config: miner=True, oil=True, icarus=True, ils_vein=True, ils_source=True, debug=True
[Info   : EndlessResources] [patch] Applied 4 Harmony patches: MinerPatch, IcarusPatch, StationVeinCollectionPatch, StationDispatchPatch.
```

The first time each patch actually restores a state value, a
one-shot line is printed. Subsequent fires are quiet:

```
[Info   : EndlessResources] [patch] Patch A (Miner) fired: first restoration. type=Vein
[Info   : EndlessResources] [patch] Patch B (Icarus) fired: vein 42 restored to 5000
[Info   : EndlessResources] [patch] Patch C (Station vein collection) fired: miner 7 productCount restored to 4
[Info   : EndlessResources] [patch] Patch D (Station dispatch) fired: first storage restoration. slots=3
```

These one-shot lines appear once per process. If you toggle
`Debug log` off and on again, the next restoration of each
patch will log again (the gate re-reads each call; the
one-shot bool persists until the first fire).

## Test results log

(No runs yet. Append test runs on top of this section.)

---

## Test A - Fresh save, single planet

**Setup:**
1. New game, single player, default settings.
2. Land on a planet with 4+ vein types (iron, copper, stone,
   titanium, etc.).
3. Place 4 miners, 1 oil extractor, 1 ILS / PLS.
4. Run for 30 minutes (in-game).

**Expected:**
- Vein amounts unchanged after 30 min.
- ILS / PLS sends shipments forever.
- Save + quit + reload: vein amounts persist.
- Toggle `EnableMinerPatchFlag = false` in config, restart:
  veins DO deplete.
- Toggle back, restart: veins stay.

**How to inspect vein amount in-game:**
- Hover a miner / vein in the build tool. The vein amount
  is shown in the tooltip ("Remaining: 5000").
- Or use the mecha to scout the planet and the planet's
  resource overlay shows the per-vein amount.

**Log output to expect (with `Debug log = true`):**
```
[Info   : EndlessResources] [patch] Patch A (Miner) fired: first restoration. type=Vein
```
One line only, the first time a miner extract happens. After
that, silent. Other patch lines (B, C, D) appear later when
those code paths trigger.

## Test B - Mid-save upgrade

**Setup:**
1. Load a mid-save where some veins are at 50% or less
   (existing player progression).
2. Install mod, launch.
3. Verify vein amounts stay at their current level
   (don't reset to 100%).
4. Continue mining, verify they stay at that level.

**Expected:**
- The mod applies on load. It does not "refill" veins -
  it only prevents further depletion.
- The current vein amount at the time of install becomes
  the new floor.

**Risk to watch for:** if the Prefix sees a vein that
was depleted to 0 in the previous session, the snapshot
captures amount=0. The Postfix sees `vein.id == 0` (vein
was removed by the game) and skips restoration. The vein
stays removed. This is the right behaviour.

## Test C - Crude oil specifically

**Setup:**
1. Planet with oil seep.
2. Place oil extractor, run 20 min.

**Expected:**
- Oil amount unchanged.
- Other veins on the planet behave per Test A.

**Log output to expect:**
```
[Info   : EndlessResources] [patch] Patch A (Miner) fired: first restoration. type=Oil
```
Note `type=Oil`, not `type=Vein`. This confirms the `EnableOilPatchFlag`
gate is correctly distinguishing the two miner types.

## Test D - ILS / PLS dispatch

**Setup:**
1. Two planets, each with an ILS / PLS in the same
   network.
2. Configure planet A to send iron plates to planet B.
3. Place 8 smelters on planet B, requesting iron.
4. Run 10 min.

**Expected:**
- Planet A's iron vein amount unchanged.
- Planet B's ILS / PLS storage is full.
- Smelters on planet B run continuously.

**Log output to expect:**
```
[Info   : EndlessResources] [patch] Patch A (Miner) fired: first restoration. type=Vein
[Info   : EndlessResources] [patch] Patch C (Station vein collection) fired: miner 7 productCount restored to 4
[Info   : EndlessResources] [patch] Patch D (Station dispatch) fired: first storage restoration. slots=3
```

**Risk to watch for:** if Patch D's storage snapshot misses a
field (e.g. `takeInc` was renamed), the source storage will
visibly drain. The fix is to add the new field to the snapshot
struct in `StationDispatchPatch.cs`.

## Test E - Icarus hand-mine

**Setup:**
1. Land, walk to a vein, press F to mine.
2. Mine 10 units.

**Expected:**
- Vein amount unchanged.
- Items appear in the player's inventory.

**Log output to expect:**
```
[Info   : EndlessResources] [patch] Patch B (Icarus) fired: vein 42 restored to 5000
```

## Test F - Debug log

**Setup:**
1. Set `DebugLog = true` in
   `BepInEx\config\com.author.EndlessResources.cfg`.
2. Restart, run for 1 min.
3. Inspect `BepInEx\LogOutput.log`.

**Expected:**
- `[EndlessResources] [config] Loaded with config: ...`
  line on load.
- `[EndlessResources] [patch] Applied 4 Harmony
  patches: ...` line on load.
- `[EndlessResources] [patch] Patch A (Miner) fired: ...`
  one-shot line on first miner extract.
- `[EndlessResources] [patch] Patch D (ILS dispatch)
  fired: ...` one-shot line on first dispatch.
- After one-shot lines, no further per-tick log spam.
- Set `DebugLog = false`, restart: no verbose lines.

**Edge case to watch for:** if the test is run immediately
after a previous run (same process), the one-shot bools
may already be `false` from the previous run. The expected
behaviour: no first-fire log on the second run. The user can
restart the game to reset.

## Test G - With `dsp-infinite-resource-nodes` (both installed)

**Setup:**
1. Install both `EndlessResources` and
   `dsp-infinite-resource-nodes` mods.
2. Run Test A again.

**Expected:**
- No errors in BepInEx log.
- Test A's expected behaviour still holds (veins stay
  full).
- Both mods' load-order messages appear in the log.

**Risk to watch for:** BepInEx does NOT prevent two mods
with overlapping patches from both loading. The result is
both patches fire, in patch-priority order. The first one
to write wins for the postfix. Since both patches restore
the vein to the pre-call value, the order doesn't matter
for the result. But the BepInEx log will show BOTH mod
load lines.

## Test H - Galaxy statistics

**Setup:**
1. Run for 30 min with the mod on.
2. Open the Statistics panel (Galaxy view).

**Expected:**
- "Resources consumed" counter is 0 or near-0.
- The exact behaviour (whether the counter increments
  per-call and is then reset, or is not incremented at
  all) depends on whether the postfix runs before or
  after the statistics are tallied. Document the actual
  behaviour.

**Known limitation:** the "Resources consumed" counter
in DSP is tallied by the consumer code. Patch A's postfix
runs AFTER `MinerComponent.InternalUpdate` finishes its
statistics. So the counter is incremented per-tick and
shows the amount consumed that tick, even though the
vein amount is restored. The net effect: the counter
shows the same number as if the mod were not installed,
but the actual planet state is non-depleting. This is
expected.
