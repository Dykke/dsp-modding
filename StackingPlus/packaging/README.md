# StackingPlus

> One-line tagline / elevator pitch for the mod.

**Author:** `zicarius`
**Version:** 1.0.0
**Game:** Dyson Sphere Program <version range>
**Loader:** BepInEx 5.4.17+

<identity line - one sentence, who this mod is for>

---

## What it does

Describe the mod's effect in 2-3 sentences. No setup yet, just "what
changes in the game when this mod is on".

## Features

- Bullet list of the main features, one line each.
- Lead with the most player-visible feature.
- Avoid implementation details ("uses Harmony transpiler on GameData.X").

## Installation

**Via r2modman / Thunderstore Mod Manager (recommended):**
1. Install [r2modman](https://thunderstore.io/package/ebkr/r2modman/) or the Thunderstore Mod Manager.
2. Search "Dyson Sphere Program" and set up a profile.
3. Search "StackingPlus" in the Online tab and install.
4. Launch the game through the mod manager.

**Manual install:**
1. Install [BepInEx 5 for Dyson Sphere Program](https://thunderstore.io/c/dyson-sphere-program/p/xiaoye97/BepInEx/).
2. Download the latest `StackingPlus-<version>.zip` from the
   [Releases page](https://github.com/Dykke/dsp-modding/releases).
3. Extract the ZIP into your `Dyson Sphere Program` folder so the
   `plugins\` path lands inside `BepInEx\plugins\`.
4. Launch the game once. BepInEx creates
   `BepInEx\config\com.zicarius.StackingPlus.cfg` with the defaults.
5. (Optional) Edit that .cfg to taste.

## Before you install

This is a BepInEx plugin - it patches the game's code at runtime
using <Harmony patches / reflection>.

- **Only download from sources you trust.** Only this mod's
  Thunderstore page or the
  [GitHub repo](https://github.com/Dykke/dsp-modding) - treat
  re-uploads elsewhere as unsafe.
- **Game updates can break it.** A DSP update may stop this mod from
  working until a compatibility update is released.
- **Back up your save first.** <one honest sentence on this mod's
  actual save impact - check what it serializes before writing this,
  don't guess>.

## Configuration

All settings live in the BepInEx config file - there is no in-game
settings window.

| Setting | Default | Description |
|---|---|---|
| `General > EnableFeature` | `true` | Master switch. |
| `Hotkeys > FeatureHotkey` | `F9` | Hotkey to trigger the feature. |
| `Diagnostics > DebugLog`  | `false` | Verbose logging, off by default. |

(Full reference: see the .cfg file BepInEx writes on first run.)

## Compatibility

- **Game version:** DSP <version range>.
- **Other mods:** Known compatible with `<list>`. Known to conflict with `<list>`.
- **Multiplayer:** untested in Nebula multiplayer / confirmed working / singleplayer only.

## Troubleshooting

**Mod doesn't load:**
- Ensure the file is in `BepInEx\plugins\StackingPlus\` (not `Mods\`).
- Check `BepInEx\LogOutput.log` for errors near `Loading [StackingPlus]`.

**<Common issue>:**
- <Solution step>

## License

MIT / GPL-3.0 / etc. Pick one and put the full text in `LICENSE`.

## Credits

- `StackingPlus` by `zicarius`.
- Built on the workspace template at `cursor-stuff\templates\mod-template\`.

### Support

All my mods are free and always will be. If this one made your
playthrough better and you feel like buying me a coffee, that keeps
me motivated to maintain these mods and build new ones:
[https://ko-fi.com/zicarius](https://ko-fi.com/zicarius)

---

**<CLOSING LINE - short, all caps, mod-specific>**
