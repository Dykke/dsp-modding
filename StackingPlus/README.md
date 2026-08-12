# Mod Template

This folder is the **canonical scaffold** every DSP mod in this workspace is
built from. Do not edit files here directly when starting a new mod - copy
this folder to a sibling location and rename.

## How to start a new mod from this template

From the workspace root, run:

```cmd
tools\new-mod.cmd MyAwesomeMod
```

This will:

1. Copy every file in this folder to `MyAwesomeMod\`.
2. Rename `Template.csproj` to `MyAwesomeMod.csproj`.
3. Replace every `Template` / `StackingPlus` placeholder with `MyAwesomeMod`.
4. Create the standard `notes\`, `non-code\`, and `packaging\` folders.
5. Print the next-step commands (build, package).

## Files in this template

| File | Purpose |
|---|---|
| `Template.csproj` | SDK-style .csproj. Per-mod: only carries AssemblyName, RootNamespace, and PostBuild. Shared MSBuild defaults come from `Directory.Build.props`. |
| `Plugin.cs` | BepInEx plugin entry. Includes a `<ModName>Log` helper for gated debug logging and a `Debug log` config toggle. |
| `Properties\AssemblyInfo.cs` | Standard assembly info. |
| `packaging\manifest.template.json` | Thunderstore manifest. Renamed to `manifest.json` by `package.cmd`. |
| `packaging\README.md` | How to package and publish the mod. |
| `notes\.gitkeep` | Placeholder for per-mod technical notes. |
| `non-code\.gitkeep` | Placeholder for distribution assets (BBCode, logo, thumbnail). |

## Customizing the template

If you find yourself editing the same boilerplate into every new mod
(a new field on every config, a new helper class), add it here so the next
mod gets it for free. The reverse is also true: do NOT add mod-specific
behaviour here. Anything that ships in a mod must be optional in the
template.

## Why a separate template and not just `cp -r` the sample mod?

DSPMirrorBlueprint and DSPCalculator are full production mods with their own
opinions, dependencies, and structure. A bare template keeps the scaffold
honest: the only code in here is the universal boilerplate.
