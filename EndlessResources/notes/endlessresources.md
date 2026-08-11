# EndlessResources - per-mod discovery log

This file is the per-mod technical discovery log. New findings from
implementation, debugging, and in-game testing are appended here.
Per the workspace convention, newest entries go on top.

---

## 2026-08-11 - Mod scaffolded

- Plan produced via `dsp-mod-plan` skill, written to
  `cursor-stuff\plans\EndlessResources-v1.0.0-initial.md` (Pass 2,
  96% confidence).
- 5 open questions from Pass 1 resolved:
  1. Name: `EndlessResources`
  2. Strategy: standalone replacement of
     `dsp-infinite-resource-nodes-main`
  3. UI scope: config-only for v1, CommonAPI window deferred to v1.1
  4. Crude oil: separate `EnableOilPatchFlag`
  5. ILS / PLS scope: both vein collection AND source dispatch
- Folder scaffolded via `cursor-stuff\tools\new-mod.cmd
  EndlessResources`. 9 doc templates copied to `notes\`. Standalone
  plugin tree lives at the workspace root (`EndlessResources\`),
  not under `cursor-stuff\`.
- Plugin entry (`Plugin.cs`) inherits the gated `EndlessResourcesLog`
  helper and the `Diagnostics.DebugLog` config from the template.
  Implementation will replace the example `enableFeature` /
  `featureHotkey` config with the 5 real `General` toggles.

## 2026-08-11 - Design intent captured from user's chat

(For implementation-phase reference. These observations are also
recorded in the plan's "Notes captured during dev" section.)

1. **"Why not patch the resource itself?"** - The existing
   `dsp-infinite-resource-nodes-main` IL approach IS patching the
   resource (no-oping the decrement of `vein.amount`). The new
   Harmony postfix approach does the same thing (restore the
   value after the consumer's call). There is no single
   "resource setter" to hook because `VeinData.amount` is a
   public field, not a property. The Harmony postfix is the
   modern / robust equivalent of the IL no-op.
2. **"Add option for the miners too and crude oils"** - both
   implemented as separate config toggles
   (`EnableMinerPatchFlag`, `EnableOilPatchFlag`).
3. **"Add debug gated where if its toggled it shows the
   details"** - standard `Diagnostics.DebugLog` config + the
   `EndlessResourcesLog` helper. The plugin skeleton's existing
   `DebugLog` config satisfies this.
4. **"When ILS/PLS is used, it will patch those so it wont
   consume the resources from the planet"** - this is the
   primary new feature, captured as Patches C, D, E.
5. **"This needed to be noted because this similars notes
   will be showing during devs and we will adapt it"** - the
   plan's "Notes captured during dev" section is the formal
   record. This file is the per-mod companion.
6. **"First is somewhat simple"** - v1 scope is intentionally
   narrow. v1.1+ will add CommonAPI window, multi-version
   expansion, and any feedback from the user.

## 2026-08-11 - Harmony targets locked

| Patch | Target | Behaviour |
|---|---|---|
| A | `MinerComponent.InternalUpdate` | Restore `vein.amount` for ore and oil (gated by `EnableMinerPatchFlag` + `EnableOilPatchFlag`). |
| B | `PlayerAction_Mine.GameTick` | Restore `vein.amount` after Icarus hand-mine (gated by `EnableIcarusPatchFlag`). |
| C | `StationComponent.UpdateVeinCollection` | Restore `vein.amount` for ILS / PLS vein collection (gated by `EnableILSVeinCollectionFlag`). |
| D | `StationComponent.DetermineDispatch` | Restore source ILS / PLS storage after dispatch (gated by `EnableILSSourceFlag`). Riskiest patch. |
| E | `StationComponent.UpdateCollection` | Restore orbit collection amount (folded into `EnableILSVeinCollectionFlag`). |

Riskiest patch: D (`DetermineDispatch`). The function body is long
and may be refactored by DSP across versions. The postfix is safe
(runs after the original), so a refactor of the body does not
break the patch.

## 2026-08-11 - References consulted

- `cursor-stuff\sample-working-mods\dsp-infinite-resource-nodes-main\DSPInfiniteResourceNodes.cs`
  - the mod this one replaces. IL patcher on `MinerComponent.InternalUpdate`
    and `PlayerAction_Mine.GameTick`. Confirms vein amount is the
    canonical restoration point. The 3 `const bool enableDebug_*`
    flags are replaced with the BepInEx `DebugLog` config.
- `cursor-stuff\sample-working-mods\DysonSphereMods-main\src\MaxLVLIncrease\MaxLVLIncrease.cs`
  - BepInEx pattern reference (`[BepInPlugin]` + `[HarmonyPatch]`
    + `Config.Bind`).
- `cursor-stuff\sample-working-mods\DysonSphereMods-main\src\StacksizeMultiplier\StacksizeMultiplier.cs`
  - CommonAPI UI pattern reference for v1.1.
- `cursor-stuff\notes\game-api.md`
  - the working API reference for `StationComponent`,
    `MinerComponent`, `PlayerAction_Mine`, `VeinData`,
    `VeinGroup`. Cross-referenced during implementation.
