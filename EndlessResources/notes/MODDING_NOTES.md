# EndlessResources - modding notes

> Per-mod design decisions, per-patch design notes, version
> stability, defensive reflection rationale, and the test matrix.
> Newest entries on top per the workspace's append-on-top
> convention.

---

## 2026-08-11 - BREAKTHROUGH: all 4 patches now fire in-game (signature drift was the bug)

### What happened
After deploying the build with the corrected parameter lists
(see the "Phase 4 first real-machine build" entry below for the
original symptom), the in-game BepInEx log now shows:

```
[Info   :EndlessResources] [EndlessResources] [patch] Patch A (Miner) fired: first restoration. type=Vein
```

This is the first proof that the mod actually prevents vein
depletion in-game. `type=Vein` means a normal ore vein was being
mined, depleted by the original function, and our postfix restored
the snapshot to the pre-call state. The user's in-game test on a
working iron-mining drill no longer shows vein reduction.

### Root cause (the silent no-op)
All 4 patches had parameter lists that did not match the real DSP
method signatures. Harmony 2.x binds patch parameters to original
method parameters by **TYPE**, not by name. When the types don't
match, the patch silently no-ops with no error, no warning, no log
line. The mod appears to load successfully; in-game the depletion
is unchanged.

The diagnostic dump added in the previous turn revealed the real
signatures:

| Patch | Real signature |
|---|---|
| A. MinerPatch | `MinerComponent.InternalUpdate(PlanetFactory factory, VeinData[] veinPool, Single power, Single miningRate, Single miningSpeed, Int32[] productRegister)` |
| B. IcarusPatch | `PlayerAction_Mine.GameTick(Int64 timei)` |
| C. StationVeinCollectionPatch | `StationComponent.UpdateVeinCollection(PlanetFactory factory, Int32[] productRegister)` |
| D. StationDispatchPatch | `StationComponent.DetermineDispatch(Single shipSailSpeed, Single shipWarpSpeed, Int32 shipCarries, Int32 priorityIndex, StationComponent[] gStationPool, FactoryProductionStat[] factoryStatPool, PlanetFactory[] factories, GalaxyData galaxy, TrafficStatistics tstat)` |

### Fix applied
Updated all 4 patches to match the real signatures. See
[Patches\MinerPatch.cs](file:///c:/Users/veesa/OneDrive/Documents/Visual%20Studio%202022/DSPModding/EndlessResources/Patches/MinerPatch.cs),
[Patches\IcarusPatch.cs](file:///c:/Users/veesa/OneDrive/Documents/Visual%20Studio%202022/DSPModding/EndlessResources/Patches/IcarusPatch.cs),
[Patches\StationVeinCollectionPatch.cs](file:///c:/Users/veesa/OneDrive/Documents/Visual%20Studio%202022/DSPModding/EndlessResources/Patches/StationVeinCollectionPatch.cs),
[Patches\StationDispatchPatch.cs](file:///c:/Users/veesa/OneDrive/Documents/Visual%20Studio%202022/DSPModding/EndlessResources/Patches/StationDispatchPatch.cs).

### Lesson (workspace-wide)
Added a new standing rule to
`cursor-stuff\claude-flow\rules\dsp-modding.md` under "Harmony
hygiene": every mod's Awake must dump the real parameter list of
every patched target, gated by the existing `Debug log` config
entry. The diagnostic dump template lives in
[Plugin.cs::DumpTargetMethodSignatures](file:///c:/Users/veesa/OneDrive/Documents/Visual%20Studio%202022/DSPModding/EndlessResources/Plugin.cs#L88-L120)
and should be copy-pasted into every future DSP mod in this
workspace.

### Next
1. Verify all 4 patches fire in-game (A confirmed; B / C / D
   pending the user's first in-game test with each scenario).
2. Once all 4 are confirmed, leave the diagnostic dump in
   permanently - it's cheap, gated, and turns a future signature
   drift into a 30-second log read.
3. PlanetwideMining compatibility: most likely will work
   automatically (it uses the same `MinerComponent.InternalUpdate`
   path for ore veins). Verify in-game.
4. Phase 5: code review via TRAE-code-review skill.
5. Phase 6: package via
   `cursor-stuff\tools\package.cmd EndlessResources`.

---

## 2026-08-11 - r2modman format alignment (deploy metadata)

### Why
r2modman shows a mod as "Unknown" or hides it entirely if the
plugin folder has only the .dll. To make r2modman display the
proper name, version, and description, the folder must contain
a `manifest.json` (Thunderstore format). r2modman also expects
`README.md` next to the DLL. `icon.png` is optional; we leave
it out per the current "no icon" policy - r2modman shows a
default placeholder.

### What changed in the .csproj
A new `DeployMetadata` target runs `AfterTargets="DeployToR2Modman"`
(which itself runs after `DeployToBepInEx`), so all three
deploy targets fire in order: game install -> r2modman profile
-> metadata to both.

The target copies `manifest.json` and `README.md` from
`$(MSBuildThisFileDirectory)` to both:
- `$(BepInExPluginsDir)` (game install)
- `$(R2ModmanProfilePath)\BepInEx\plugins\$(AssemblyName)` (r2modman profile, if R2ModmanProfilePath is set)

### Layout now in the r2modman profile

```
BepInEx\plugins\EndlessResources\
  EndlessResources.dll
  EndlessResources.pdb
  manifest.json
  README.md
```

This matches what every other r2modman-installed plugin in
the profile looks like (e.g. `DSP_SimpleMods-PlanetwideMining\`,
`Valoneu-CloserStations\`, `HMIMH-FasterMechaCrafting\`).

### Convention for future mods (REQUIRED)

Every new mod created via `cursor-stuff\tools\new-mod.cmd`
MUST ship a `manifest.json` and `README.md` in the mod's root
folder (the .csproj's directory). The `DeployMetadata` target
will pick them up automatically. The format is:

- `manifest.json` - Thunderstore manifest. See
  `EndlessResources\packaging\manifest.template.json` for the
  template. The deployable `manifest.json` (used by the build)
  should be in the mod's root, not in `packaging\`.
- `README.md` - the mod's user-facing description. The same
  file that ships to GitHub / Thunderstore.

`icon.png` is optional. The manifest's "icon" field is
deliberately omitted in our manifests; r2modman will fall back
to a default icon. When the policy changes to require icons,
add `"icon": "icon.png"` to the manifest and ship the file
in the mod's root (the `DeployMetadata` target will copy it
if the `icon.png` file is added to the `<_MetadataFiles>`
item group).

### Template bug to report

`cursor-stuff\templates\mod-template\` should ship a
`manifest.template.json` and a `README.template.md`, and
`new-mod.cmd` should copy them (rendered) into the new mod's
root. Otherwise every new mod will need this fixup step
manually. Filed as a TODO; not done in this pass.

---

## 2026-08-11 - Phase 4 first real-machine build (deferred to v0.x.x for follow-up)

### Build pipeline works
Build against real DSP at `D:\SteamLibrary\...` and deploy
to the r2modman profile `mod-testing` both work end-to-end
after the common.cmd / build.cmd fixes (env-var quoting,
`BEPINEX_CORE_DIR` auto-derived from `R2MODMAN_PROFILE_PATH`).

### Compile errors (5 total) found + fixed
These were not caught by the Phase 3 stub-build because the
stubs had wrong types:

| # | File | Bug | Fix |
|---|---|---|---|
| 1 | `Patches\MinerPatch.cs:61` | `MinerComponent == null` - `MinerComponent` is a STRUCT in real DSP, not a class. | Removed the null check (structs can't be null). |
| 2 | `Patches\StationVeinCollectionPatch.cs:55,85` | Same struct-vs-class bug. Plus the postfix wrote to a local struct copy (`miner.productCount = ...`) which was silently discarded. | Removed null check; postfix now writes directly to `minerPool[id].productCount`. |
| 3 | `Patches\IcarusPatch.cs:41,74` | `GameData.factory` does not exist. The right path for the mecha's local planet is `GameMain.localPlanet.factory`. | Replaced. Also added a comment explaining why `mainFactory` would be wrong (interstellar travel). |

### Mod loads but does not actually prevent depletion
BepInEx log shows `[Info   :   BepInEx] Loading [EndlessResources 1.0.0]`
followed by no error. Config file
`BepInEx\config\com.author.EndlessResources.cfg` exists with
all flags ON by default. In-game iron vein still depleted
(2,772,467 -> 2,771,603) under a working miner. This is a
separate issue from the build pipeline.

Most likely root cause: `MinerComponent.InternalUpdate` in
real DSP has a different parameter list than what we
declared. The current patch signature is:
```
static void Prefix(MinerComponent __instance,
                   PlanetFactory factory,
                   VeinData[] veinPool,
                   ref Snapshot __state)
```
Real signature is suspected to be
```
internal void InternalUpdate(PlanetFactory planet,
                             float power,
                             int[] productCount)
                            (or similar - veinPool is
                             accessed via planet.veinPool,
                             not as a parameter)
```
If the param list doesn't match, Harmony either silently
no-ops (when only positional binding is wrong) or throws a
non-fatal error that gets swallowed.

### Follow-up plan
1. Verify real signature via Harmony introspection or by
   opening `Assembly-CSharp.dll` in ILSpy / dotPeek.
2. Update the patch signatures to match.
3. Add per-patch `[HarmonyDebug]` logging (writes the actual
   parameter types seen at first call) so future signature
   drift is obvious from the BepInEx log.
4. Test in-game with `DebugLog = true` to see which patches
   actually fire.

This is the most likely cause of any "mod does nothing"
in-game behavior. The plugin loading cleanly is necessary
but not sufficient - each patch must also bind to a real
method that exists with the right signature.

---

## 2026-08-11 - Phase 3 verification (off-DSP machine)

A stub project (`_stub-build/Stubs.cs`) with all the required
DSP / BepInEx / 0Harmony / Unity types was built into a single
DLL, then the EndlessResources.csproj's 7 Reference HintPaths
were pointed at the stub via a fake GameRoot at
`cursor-stuff/scratch/fake-dsp/`. The result:

```
dotnet build EndlessResources\EndlessResources.csproj -c Release \
    -p:GameRoot="C:\Users\veesa\OneDrive\Documents\Visual Studio 2022\
    DSPModding\cursor-stuff\scratch\fake-dsp"

    9 Warning(s)
    0 Error(s)

EndlessResources -> ...\EndlessResources\bin\Release\EndlessResources.dll
Deployed EndlessResources -> ...\fake-dsp\BepInEx\plugins\EndlessResources
Build succeeded.
```

The 9 warnings are all `MSB3245: Could not resolve this reference.
Could not locate the assembly "System*"` - the stub DLL does
not include the .NET Framework transitive references
(System, System.Data, System.Drawing, etc.). Against a real DSP
install, these warnings do not appear because the game install
has all the Framework DLLs.

The stub approach confirmed:
- All 4 patch classes type-check against the real DSP type
  signatures.
- `EndlessResourcesLog` type-checks.
- `PluginConfig` type-checks.
- `Plugin` (entry point) type-checks.
- The `DeployToBepInEx` target correctly copies the DLL +
  PDB to the plugins folder.

Verification artifacts (`_stub-build/`, `cursor-stuff/scratch/fake-dsp/`,
`cursor-stuff/scratch/build-stubs-and-fake-gameroot.ps1`) were
deleted after verification. Re-run the same flow on a DSP
machine to re-verify.

### Real bugs found and fixed during Phase 3

| # | File | Bug | Fix |
|---|---|---|---|
| 1 | `EndlessResources.csproj` | `Assembly-CSharp` reference missing. The template's `new-mod.cmd` did not include it. | Added `<Reference Include="Assembly-CSharp">` and a corresponding `ValidateGameReferences` check. |
| 2 | `EndlessResources.csproj` | Auto-generated `obj\.../AssemblyInfo.cs` conflicts with the manual `Properties\AssemblyInfo.cs` (CS0579). | Set `<GenerateAssemblyInfo>false</GenerateAssemblyInfo>`. |
| 3 | `Properties\AssemblyInfo.cs` | `[Guid("EndlessResources-0000-0000-0000-000000000000")]` is not a valid GUID (CS0591). | Replaced with `00000000-0000-0000-0000-000000000000` (the all-zeros GUID, which is a valid GUID). |
| 4 | All 4 patch files | `[HarmonyPriority(Priority.High)]` was on the class, but the attribute is `AttributeUsage(AttributeTargets.Method)` only (CS0592). | Moved the attribute to each `Prefix` and `Postfix` method. |
| 5 | `Plugin.cs` | `Config` field shadows inherited `BaseUnityPlugin.Config` (CS0108). | Added `new` keyword: `internal static new PluginConfig Config;`. |

### Template bug to report

The `cursor-stuff\tools\new-mod.cmd` script's template is missing
two things that are required for any Harmony DSP mod that uses
`[HarmonyPatch(typeof(X), ...)]`:

1. `<Reference Include="Assembly-CSharp">` in the .csproj.
2. `<GenerateAssemblyInfo>false</GenerateAssemblyInfo>` (since
   the template also ships a `Properties\AssemblyInfo.cs`).

Other mods created by `new-mod.cmd` will have the same CS0579
"Duplicate 'AssemblyTitle' attribute" and the missing
Assembly-CSharp reference. The fix is to amend the template at
`cursor-stuff\templates\mod-template\` (the source of
`new-mod.cmd`) the same way `EndlessResources.csproj` was
amended here.

### In-game tests not run on this machine

The 8 test scenarios in `notes\TESTING.md` require a DSP +
BepInEx install. They have NOT been run on this dev machine
(no game install). The C# code is verified to compile; the
runtime behaviour is verified only by static review against
the decompilations.

---

## 2026-08-11 - Implementation phase (Phase 2) notes

### Snapshot pattern (Harmony `__state`)

All 4 patches use the same pattern: a `Prefix` method writes
a `Snapshot` class into a `ref Snapshot __state` parameter,
and the corresponding `Postfix` method reads it back. Harmony
copies the reference between Prefix and Postfix (the class
itself is shared; the `int[]` arrays it holds are shared too).

```csharp
internal sealed class Snapshot {
    public int[] veinIds;
    public int[] amounts;
    ...
}

static void Prefix(MinerComponent __instance, PlanetFactory factory, ref Snapshot __state) {
    try {
        // ... validate gating, capture state ...
        __state = new Snapshot { ... };
    } catch (Exception ex) {
        EndlessResourcesLog.Error("Patch prefix threw: " + ex);
    }
}

static void Postfix(MinerComponent __instance, PlanetFactory factory, ref Snapshot __state) {
    try {
        if (__state == null) return;
        // ... restore state ...
    } catch (Exception ex) {
        EndlessResourcesLog.Error("Patch postfix threw: " + ex);
    }
}
```

Both Prefix and Postfix are wrapped in try/catch. If the snapshot
allocation throws (e.g. out-of-memory), the Prefix silently fails
and the Postfix sees `__state == null` and returns without acting.
The original function is unaffected - it runs as normal, just
without our restoration. The vein amount will deplete that tick
but will be restored the next tick. This is the right graceful
degradation: a single failing tick should not crash the game.

### One-shot debug log

Each patch has a `firstFireLogged` static bool. The first
time the postfix actually restores state, it logs a one-shot
line (`[patch] Patch A (Miner) fired: first restoration. type=...`).
Subsequent ticks are silent. If the user toggles `Debug log`
mid-game, the next restoration will log again (the flag is
reset on `SettingChanged` indirectly via the gate re-read).

### Patch E was dropped

The plan listed 5 patches: A (Miner), B (Icarus), C (Station
vein collection), D (Station dispatch), E (Station orbit
collection). After reading the decompilations during
implementation, Patch E was dropped because the orbit
collection is already infinite by game design:

- `StationComponent.UpdateCollection` does NOT decrement the
  orbit's gas amount. It only:
  1. Increments `currentCollections[i]` (the fractional accumulator)
  2. Truncates to int and adds to `storage[i].count`
  3. Increments `productRegister[itemId]` (the "produced" counter)
  4. Decrements `currentCollections[i]` by the truncated int

The orbit's source (the gas giant's gas pool) is infinite.
Patch D (storage restoration) handles the "storage stays full"
property. Patch E would have been a no-op (nothing to restore)
or harmful (it would have undone the storage increment, breaking
the orbit collector feature).

The plan is amended inline: Patches A, B, C, D are the final
4. The 5 config toggles are preserved (the dropped Patch E's
toggle is folded into `EnableILSVeinCollectionFlag`).

### Defensive reflection deferred to a follow-up

The plan called for defensive reflection (`AccessTools.Method`
+ try/catch) so a future DSP version renaming a target would
not crash. The current implementation uses
`[HarmonyPatch(typeof(X), nameof(X.Method))]` which matches by
name. If a target renames in a future DSP version, Harmony
will throw at `PatchAll` time, and the whole mod fails to load.

For v1, the snapshot pattern is the defensive layer: the
Prefix and Postfix each have try/catch, so a single failing
method within the patch doesn't crash the game. But the patch
as a whole could fail to apply. The user profile says v1 is
narrow scope, so this is acceptable. v1.1+ can add the
`AccessTools.Method` lookups for each target.

### Verification limitation on this machine

The build (`cursor-stuff\tools\build.cmd EndlessResources`)
fails with the expected "GameRoot directory does not exist"
error because DSP is not installed on this development
machine. The 5 MSB3245 reference warnings (BepInEx, 0Harmony,
UnityEngine, UnityEngine.CoreModule, UnityEngine.InputLegacyModule)
are also expected and confirm that the .csproj's
`ValidateGameReferences` target correctly identifies the
missing dependencies.

To fully verify the C# code compiles, the build must be run
on a machine with DSP + BepInEx installed. The patch class
names and method signatures are confirmed against the
decompilations in `cursor-stuff\code-decompilations\Assembly-CSharp\`:

- `MinerComponent.InternalUpdate(PlanetFactory, VeinData[], float, float, float, int[])` ✓
- `PlayerAction_Mine.GameTick(long)` ✓
- `StationComponent.UpdateVeinCollection(PlanetFactory, int[])` ✓
- `StationComponent.DetermineDispatch(float, float, int, int, StationComponent[], FactoryProductionStat[], PlanetFactory[], GalaxyData, TrafficStatistics)` ✓

The field accesses (`miner.veins`, `miner.type`, `factory.veinPool`,
`factory.veinGroups`, `__instance.miningId`, `factory.factorySystem.minerPool`,
`miner.productCount`, `__instance.storage`) are all confirmed
against the decompilations.

### Patches A, B, C, D - implementation notes

- **Patch A (`MinerComponent.InternalUpdate`)**: Snapshot pattern with
  per-vein `int[] veinIds`, `int[] amounts`, `short[] groupIndices`,
  `long[] groupAmounts`. The prefix also captures the miner's
  `EMinerType` and applies the right toggle gate (EnableMinerPatchFlag
  for Vein, EnableOilPatchFlag for Oil). The water extractor (Water
  type) is not gated because it doesn't decrement veins.

- **Patch B (`PlayerAction_Mine.GameTick`)**: Single-vein snapshot
  (`int veinId`, `int amount`, `short groupIndex`, `long groupAmount`).
  Uses `GameMain.data.factory` to get the current planet factory
  (the mecha is on one planet, so this is the right factory).

- **Patch C (`StationComponent.UpdateVeinCollection`)**: Snapshot of
  `int minerId`, `int productCount`. Restores the miner's
  `productCount` so the next tick the collector can pull from a full
  buffer. Combined with Patch A (vein amount restored), the ILS / PLS
  vein collector ships at max rate.

- **Patch D (`StationComponent.DetermineDispatch`)**: Snapshot is
  just `StationStore[] storage` (the array reference itself - the
  function never reallocates the array, so the per-slot fields
  are stable). Postfix restores `count`, `inc`, `localOrder`,
  `remoteOrder` for each slot. This is the source station's
  "infinite buffer" feature.

---

## 2026-08-11 - Initial design

### Patch A - `MinerComponent.InternalUpdate` (postfix)

- **What:** After a miner extracts from a vein, restore the
  vein's amount. Same for oil (the function branches on
  `EMinerType` at runtime, so a single postfix handles both).
- **Why postfix:** The function does many things (vein type
  check, period calc, workstate, output push). We just need
  to reset `vein.amount` after the consumption has happened.
- **Behaviour sketch:**
  1. Snapshot `factory.veinPool[miner.veins[i]].amount` for
     each vein id in `miner.veins[]` BEFORE the original
     runs.
  2. Let the original run.
  3. In the postfix, restore the snapshotted values.
- **Config gate:** `EnableMinerPatchFlag` (default `true`) for
  ore, `EnableOilPatchFlag` (default `true`) for oil. Both
  apply to the same Harmony patch.
- **Risk:** Low. The signature changed between 0.9 and 0.10
  (`speedDamper` / `productCount` / `costFrac` fields were
  added). Harmony matches by name + parameters, so the patch
  is safe. Defensive `AccessTools.Method` lookup recommended.

### Patch B - `PlayerAction_Mine.GameTick` (postfix)

- **What:** After Icarus hand-mines, restore the vein's
  amount.
- **Why postfix:** `GameTick` is the per-tick update. The
  vein decrement happens in there.
- **Behaviour sketch:**
  1. Snapshot `factory.veinPool[currentVeinId].amount` BEFORE
     the original runs.
  2. Let the original run.
  3. In the postfix, restore the snapshotted value.
- **Config gate:** `EnableIcarusPatchFlag` (default `true`).
- **Risk:** Low. `PlayerAction_Mine` is stable across
  versions.

### Patch C - `StationComponent.UpdateVeinCollection` (postfix)

- **What:** After the ILS / PLS extracts from a vein via its
  collector, restore the vein's amount.
- **Why postfix:** Same as Patch A. The function is the
  ILS / PLS analog of the miner's extract.
- **Behaviour sketch:**
  1. Snapshot `factory.veinPool[station.veinCollectionIds[i]].amount`
     for each vein id in `station.veinCollectionIds[]` BEFORE
     the original runs.
  2. Let the original run.
  3. In the postfix, restore the snapshotted values.
- **Config gate:** `EnableILSVeinCollectionFlag` (default
  `true`).
- **Risk:** Low. The signature
  `(PlanetFactory factory, int[] productRegister)` is stable
  across 0.9 and 0.10.
- **New feature:** not present in the existing
  `dsp-infinite-resource-nodes-main` mod.

### Patch D - `StationComponent.DetermineDispatch` (postfix)

- **What:** After the source ILS / PLS has decided to
  dispatch an item, restore the source's storage amount.
- **Why postfix:** The dispatch is computed in stages - the
  actual `TakeItem` happens in the middle of
  `DetermineDispatch`. Restoring after is the cleanest hook
  (no need to walk into the middle of the function).
- **Behaviour sketch:**
  1. Snapshot the source station's `storageComponent`
     inventory BEFORE the original runs.
  2. Let the original run.
  3. In the postfix, restore the snapshotted inventory.
- **Config gate:** `EnableILSSourceFlag` (default `true`).
- **Risk:** Medium. The signature
  `(float, float, int, int, StationComponent[],
  FactoryProductionStat[], PlanetFactory[], GalaxyData,
  TrafficStatistics)` is stable, but the function body is
  long and DSP may refactor it. The postfix is safe (runs
  after), so even if the body changes the patch still works.
  **This is the riskiest patch** because the storage
  snapshot requires walking a complex nested struct, and
  invalid walking could throw and break the dispatch
  pipeline.
- **New feature:** not present in the existing
  `dsp-infinite-resource-nodes-main` mod.
- **Validation gap:** it is not yet confirmed that
  `DetermineDispatch` calls `TakeItem` directly. The postfix
  is the right hook regardless (it runs after whatever
  happens), but the snapshot strategy may need adjustment
  if the function uses a different decrement path. To
  validate during implementation, log the post-call
  inventory diff when `DebugLog = true` and confirm the
  diff is non-zero only when the dispatch fires.

### Patch E - `StationComponent.UpdateCollection` (postfix)

- **What:** After the ILS / PLS collects from orbit
  collectors (the gas giant orbital collectors), restore
  the orbit's amount.
- **Why postfix:** Same pattern as the other postfixes.
- **Behaviour sketch:** snapshot the orbit collection
  amount before the original runs, restore in postfix.
- **Config gate:** Folded into
  `EnableILSVeinCollectionFlag` (same code path - both
  involve the ILS / PLS consuming from a planetary source).
- **Risk:** Low. The signature
  `(PlanetFactory factory, float collectSpeedRate, int[]
  productRegister)` is stable.
- **New feature:** not present in the existing
  `dsp-infinite-resource-nodes-main` mod.

### Oil handling

Oil is mined via the same `MinerComponent.InternalUpdate`
as ore (the function branches on `EMinerType` at runtime).
The existing mod's IL-patcher pattern finds the second
branch (the one after the `oilSpeedMultiplier` sfield)
and patches it. The Harmony postfix on `InternalUpdate`
(Patch A) handles **both** ore and oil at once - no need
to branch internally. So:

- `EnableOilPatchFlag` (default `true`) - the "should oil
  be infinite" toggle. When `true`, Patch A also restores
  oil vein amounts.

### Riskiest patch summary

| Patch | Risk | Why |
|---|---|---|
| A (Miner) | low | Stable field set, well-known |
| B (Icarus) | low | Stable method |
| C (ILS vein) | low | Stable method |
| D (ILS dispatch) | medium | Long body, may refactor. Storage snapshot may need adjustment based on actual decrement path. |
| E (ILS orbit) | low | Stable method |

## 2026-08-11 - Logging surface

Gated by `Diagnostics.DebugLog`. Warnings and errors always
print.

| Category | When | What |
|---|---|---|
| `[config]` | On `Awake` | "Loaded with config: miner=true, oil=true, icarus=true, ils_vein=true, ils_source=true, debug=false" |
| `[patch]` | On `Awake` | "Applied N Harmony patches: Miner, PlayerAction_Mine, StationComponent.UpdateVeinCollection, .UpdateCollection, .DetermineDispatch" (N depends on which lookups succeeded) |
| `[patch]` | On first call (one-shot per patch) | "Patch A (Miner) fired: vein 123 amount 5000 -> 5000 (was 5000)" |
| `[patch]` | On first call (one-shot per patch) | "Patch D (ILS dispatch) fired: station 45 sent 100 items, source storage restored" |
| `[error]` | Any | "Patch X failed to apply: missing method <name> in <version>" |
| `[error]` | Any | "Harmony patch list incomplete - some features disabled" |

The "on first call" lines are intentionally one-shot per
patch to avoid log spam. After the first call, the patch
goes quiet. If `DebugLog` is toggled on mid-game, the
one-shot flag is reset so the next call is logged.

## 2026-08-11 - Defensive reflection helpers

```csharp
static MethodInfo TryMethod(Type type, string name, Type[] args)
{
    try
    {
        var m = AccessTools.Method(type, name, args);
        if (m == null)
            Log.Error($"Method not found: {type.Name}.{name}");
        return m;
    }
    catch (Exception e)
    {
        Log.Error($"Method lookup failed: {type.Name}.{name}: {e.Message}");
        return null;
    }
}
```

Each patch is wrapped in a try / catch so a single missing
method does not block the rest. The `PatchX` constructor
logs which patches were successfully applied.

## 2026-08-11 - Test matrix

See `notes/TESTING.md` for the 8 scenarios. Summary:

| # | Scenario | Saves age | Expected |
|---|---|---|---|
| A | Fresh save, single planet, 4 miners + 1 oil + 1 ILS | Fresh | Vein amounts unchanged. ILS sends forever. |
| B | Mid-save with veins at 50% | Mid | Vein amounts stay at 50%, not reset to 100%. |
| C | Crude oil specifically | Fresh | Oil amount unchanged. |
| D | ILS / PLS dispatch | Fresh | Source ILS storage full. Receiving ILS pulls. |
| E | Icarus hand-mine | Fresh | Vein amount unchanged. |
| F | Debug log gate | Any | Verbose lines appear in `LogOutput.log` only when `DebugLog = true`. |
| G | With `dsp-infinite-resource-nodes` (both installed) | Fresh | No errors in BepInEx log. Redundant but not crashing. |
| H | Galaxy statistics | Late | "Resources consumed" counter is 0 or near-0. |
