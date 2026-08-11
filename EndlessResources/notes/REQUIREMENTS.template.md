# Requirements - <ModName>

<!-- BEGIN:TEMPLATE_USAGE (do not delete) -->
## How to use this template
- Place at `<ModFolder>\notes\requirements.md`.
- Delete this `BEGIN:TEMPLATE_USAGE` block.
- Keep this file **technical and exact**. The player-facing README can be
  loose ("DSP 0.9.x or later"). This file is what the maintainer reads
  when something breaks.
- Update it when ANY of the following change: the targeted DSP version, the
  BepInEx version, the mod's BepInEx GUID, the .NET target, or any
  required optional mod.
<!-- END:TEMPLATE_USAGE -->

## Runtime requirements

| Component | Required version | Notes |
|---|---|---|
| Dyson Sphere Program | 0.9.x (build >= X) | exact minimum build number |
| BepInEx | 5.4.17+ | BepInEx 6 is not yet supported |
| .NET Framework | 4.7.2 (game-bundled) | shipped with DSP |
| CommonAPI | (only if used) | required submodule list |

## Build requirements

| Component | Required version | Notes |
|---|---|---|
| .NET SDK | 6.0.400+ | pinned in `global.json` |
| MSBuild | 16.10+ | ships with VS 2022 / .NET SDK 6 |
| Visual Studio | 2022 17.0+ | optional, any IDE works |
| Game installed | yes | the build references the game's Managed folder |

## Mod identity

- **BepInEx GUID:** `com.author.modname`
- **BepInEx Name:** `Mod Display Name`
- **Assembly name:** `ModName.dll` (matches folder name)

## Hard dependencies

- `BepInEx 5.4.17+` (always - this is what loads the mod)
- `<other mod>` (only if required at runtime)

## Soft / optional dependencies

- `<other mod>` - if present, the mod adapts. If absent, the feature is
  disabled. Document this in the README too.

## Known conflicts

- `<other mod>` - both patch the same method, last-loaded wins.

## Incompatibilities

- `<other mod>` - hard conflict, both cannot be enabled at once.
