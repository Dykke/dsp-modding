# Requirements - EndlessResources

## Runtime requirements

| Component | Required version | Notes |
|---|---|---|
| Dyson Sphere Program | 0.9.27+ or 0.10.33+ | Multi-version with defensive reflection. Primary target 0.10.33.27024. Secondary 0.9.27.x. |
| BepInEx | 5.4.17+ | BepInEx 6 is not yet supported. x64 build. |
| .NET Framework | 4.7.2 (game-bundled) | Shipped with DSP. |
| CommonAPI | Not used in v1 | Required for v1.1's in-game window. |

## Build requirements

| Component | Required version | Notes |
|---|---|---|
| .NET SDK | 6.0.400+ | Pinned in `cursor-stuff\global.json`. |
| MSBuild | 16.10+ | Ships with VS 2022 / .NET SDK 6. |
| Visual Studio | 2022 17.0+ | Optional; any IDE works. |
| Game installed | yes | Build references the game's `Managed` folder. |

## Mod identity

- **BepInEx GUID:** `com.author.EndlessResources` (TODO: replace
  `author` with the user's chosen namespace before publish).
- **BepInEx Name:** `EndlessResources`
- **Assembly name:** `EndlessResources.dll` (matches folder name)

## Hard dependencies

- `BepInEx 5.4.17+` (always - this is what loads the mod).

## Soft / optional dependencies

- None in v1. CommonAPI will be required for the v1.1 in-game
  window.

## Known conflicts

- `DSP Infinite Resource Nodes`
  (GreyHak/dsp-infinite-resource-nodes-main): both patch the
  same methods. The two are not designed to co-exist. If both
  are installed, this mod's Harmony postfix restores the value
  after the consumer's call, which makes the IL patcher's
  decrement-no-op redundant. No crash, but redundant work.

## Incompatibilities

None known. The Harmony targets are postfix-only and read-only
with respect to the consumer's behaviour. They should be
compatible with any other mod that does not itself patch
`MinerComponent.InternalUpdate`,
`PlayerAction_Mine.GameTick`,
`StationComponent.UpdateVeinCollection`,
`StationComponent.DetermineDispatch`, or
`StationComponent.UpdateCollection` with a conflicting
behaviour.

## Game version strategy

- **Primary target:** DSP 0.10.33.27024 (the current stable as
  of 2026-08-11).
- **Secondary target:** DSP 0.9.27.x (the previous LTS line).
- **Fallback for renames:** every Harmony target is looked up
  via `AccessTools.Method` inside a try / catch. If the target
  renames, the patch is skipped with an error log instead of
  crashing the game. The user can then either downgrade DSP,
  wait for a mod update, or disable the affected feature via
  the relevant config toggle.

## In-game test plan

See `notes/TESTING.md` for the 8 test scenarios.
