# EndlessResources - developer README

> **Audience:** mod authors / maintainers, not players. For
> player-facing docs see the root `README.md`.

EndlessResources is a runtime Harmony plugin (BepInEx 5.4.17+) that
makes DSP planet resources non-depleting. It is a modern
replacement for the
[DSP Infinite Resource Nodes](https://dsp.thunderstore.io/package/GreyHak/DSP_Infinite_Resource_Nodes/)
IL-patcher mod, with added ILS / PLS coverage.

## What it does, in one paragraph

After each consumer's call (`MinerComponent.InternalUpdate` for
miners / oil extractors, `PlayerAction_Mine.GameTick` for Icarus
hand-mining, `StationComponent.UpdateVeinCollection` for ILS / PLS
vein collection, `StationComponent.DetermineDispatch` for ILS / PLS
source dispatch, `StationComponent.UpdateCollection` for orbit
collectors), the relevant `vein.amount` or station storage is
restored to its pre-call value. The result: the source never
depletes, but the consumer's per-tick behaviour is unchanged.

## Why Harmony postfix instead of IL patcher

The mod this replaces uses Mono.Cecil IL modification to no-op the
decrement of `vein.amount`. That approach is fragile - it has
required 6 version-fix updates over the mod's lifetime, each time
because the IL opcodes shifted in a DSP release. Harmony postfixes
match by name + parameter signature, so a postfix patch survives
most game updates without intervention.

The runtime cost is negligible: a few field reads + writes per
consumer call, no allocations.

## Design constraints

- **Gated logging.** All verbose output flows through
  `EndlessResourcesLog`, gated by `Diagnostics.DebugLog`. Warnings
  and errors always print. This is a standing workspace rule and
  it is critical for in-game debugging.
- **No in-game UI in v1.** All configuration is via the BepInEx
  .cfg file. The config is the source of truth (Rule 1).
- **Defensive reflection.** Every Harmony target is looked up via
  `AccessTools.Method` inside a try / catch. If the target
  renames, the patch is skipped with an error log instead of
  crashing the game.
- **Per-source toggles.** Each of the 5 source types
  (miner / oil / Icarus / ILS vein / ILS source) has its own
  config toggle so the player can isolate a misbehaving source.

## File map

| File | Purpose |
|---|---|
| `EndlessResources.csproj` | SDK-style .csproj, references BepInEx + Unity + DSP game DLLs, post-build deploys to `BepInEx\plugins\EndlessResources\`. |
| `Plugin.cs` | BepInEx entry point. Reads the 5 General config entries + the Diagnostics `DebugLog`. Initialises `EndlessResourcesLog` + Harmony + `PatchAll`. |
| `Patches/MinerPatch.cs` | Patch A on `MinerComponent.InternalUpdate`. |
| `Patches/IcarusPatch.cs` | Patch B on `PlayerAction_Mine.GameTick`. |
| `Patches/StationVeinCollectionPatch.cs` | Patch C on `StationComponent.UpdateVeinCollection`. |
| `Patches/StationDispatchPatch.cs` | Patch D on `StationComponent.DetermineDispatch` (riskiest). |
| `Patches/StationOrbitCollectionPatch.cs` | Patch E on `StationComponent.UpdateCollection`. |
| `Logging/EndlessResourcesLog.cs` | The static `EndlessResourcesLog` helper. |
| `notes/endlessresources.md` | Per-mod discovery log (this file's companion). |
| `notes/MODDING_NOTES.md` | Per-mod design decisions and patch design notes. |
| `notes/TESTING.md` | The 8 in-game test scenarios. |
| `notes/RELEASE_CHECKLIST.md` | Pre-release checklist. |
| `packaging/manifest.template.json` | Thunderstore manifest source. `__MOD_NAME__` is substituted at packaging time. |
| `packaging/README.md` | How to package and publish. |

## Build

```cmd
cursor-stuff\tools\build.cmd EndlessResources
```

This calls `dotnet build EndlessResources.csproj -c Release
-p:GameRoot="..."` and the `DeployToBepInEx` target copies the
DLL + PDB to `BepInEx\plugins\EndlessResources\`. The
`ValidateGameReferences` target fails the build with a clear
message if DSP / BepInEx are not installed at `GameRoot`.

## Package

```cmd
cursor-stuff\tools\package.cmd EndlessResources
```

This calls `build.cmd` first, then stages the Thunderstore layout
into `artifacts\staging\EndlessResources\`, substitutes
`__MOD_NAME__` and the version in the manifest, and zips to
`artifacts\EndlessResources-1.0.0.zip`.

## See also

- `notes/MODDING_NOTES.md` - per-patch design notes, version
  stability, defensive reflection rationale.
- `notes/TESTING.md` - 8 in-game test scenarios to verify against.
- `notes/RELEASE_CHECKLIST.md` - pre-release verification steps.
- `cursor-stuff\plans\EndlessResources-v1.0.0-initial.md` - the
  plan that produced this scaffold.
