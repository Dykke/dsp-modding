# DSP Modding

A workspace for creating **Dyson Sphere Program mod development. This repository contains BepInEx 5 mods that enhance or extend the game.

---

## For Players

### What is this?

This is the source repository for a collection of Dyson Sphere Program mods. Each mod lives in its own top-level folder. Compiled and packaged releases are published on mod distribution platforms.

### Installation

All mods in this repository require **BepInEx 5** for Dyson Sphere Program.

1. Install BepInEx 5 into your Dyson Sphere Program game folder if you have not already.
2. Download the mod ZIP from the mod page.
3. Extract the contents into your `Dyson Sphere Program` folder. The ZIP is structured so files land in the correct places.
4. Launch the game. Mod configs are written to `BepInEx\config\` after first run.

### Where to get help

- Each mod folder contains its own README, change log, and description on its distribution page.
- Configuration options are documented in each mod's configuration file inside `BepInEx\config\` after the first run.

---

## For Developers

### Repository layout

```
DSPModding/
├── <ModName>/              One folder per mod (source code)
│   ├── <ModName>.csproj   Build file (SDK-style, net472)
│   ├── Plugin.cs           BepInEx plugin entry point
│   ├── Properties/
│   │   └── AssemblyInfo.cs
│   ├── notes/            Per-mod technical notes and discoveries
│   └── non-code/       Nexus BBCode descriptions, logo prompts, thumbnails
├── cursor-stuff/          Private working folder (gitignored, private notes, plans, session logs)
└── DSPModding.sln       Visual Studio solution file
```

### Prerequisites

- **Visual Studio 2022** (or any .NET SDK-capable IDE)
- **.NET Framework 4.7.2 targeting pack
- A local install of **Dyson Sphere Program with **BepInEx 5 installed
- .NET SDK 6 or newer for the SDK-style project builds

### Building a mod

Each mod project references BepInEx and Unity assemblies directly from your game installation. You do NOT copy game DLLs into the repository.

Edit the `GameRoot` MSBuild property to point at your Dyson Sphere Program install folder. The default path assumes the standard Steam location.

**Using the default Steam location:
```
dotnet build <ModName>/<ModName>.csproj --configuration Release
```

**Using a non-standard location:
```
dotnet build <ModName>/<ModName>.csproj --configuration Release -p:GameRoot="D:\Games\Dyson Sphere Program"
```

or via the PostBuild event in the .csproj copies the compiled DLL into `BepInEx\plugins\<ModName>\`.

### Conventions

- **Language:** C# 7.3
- **Target:** .NET Framework 4.7.2 (`net472`)
- **Loader: **BepInEx 5 plugin entry point, sealed class extending `BaseUnityPlugin`
- **Patching:** Harmony 2.x (supplied by BepInEx
- **References:** All game and BepInEx references set `<Private>False</Private>False`
- **Config:** Use BepInEx `Config.Bind<T>` for all user-facing settings. Settings are written to `BepInEx\config\<guid>.cfg
- **Logging:** Every mod ships with a single `Debug log` toggle (off by default) that gates verbose output. Warnings and errors are always logged.

### Creating a new mod

When adding a new mod to this workspace:

1. Duplicate the template pattern used by existing mods in this repo. Start with the DSPMirrorBlueprint project structure as the cleanest reference.
2. Pick a unique BepInEx GUID (reverse-domain style, e.g. `com.author.modname`).
3. Add the project to the solution.
4. Set up the `GameRoot` property in the .csproj.
5. Implement the plugin entry in `Plugin.cs`. Awake reads config, initializes Harmony, applies patches. OnDestroy unpatches.
6. Add gated verbose logging helper.
7. non-code folder for distribution assets.

### Distribution

Releases follow the Thunderstore package format:

```
<ModName-author-version.zip
├── manifest.json
├── README.md
├── icon.png (256x256)
└── plugins/
│   └── <ModName>.dll
```

`manifest.json` format:
```json
{
  "name": "ModName",
  "version_number": "1.0.0",
  "website_url": "https://github.com/your/repo",
  "description": "Short description.",
  "dependencies": [
    "xiaoye97-BepInEx-5.4.17"
  ]
}
```

### Debug builds always list BepInEx. A hard dependency. Add other mod dependencies when needed.

---

## License

Mods in this repository. Individual mods. Each mod folder specifies its own license if applicable.
