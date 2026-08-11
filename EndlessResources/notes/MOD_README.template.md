# <ModName>

> One-line tagline / elevator pitch for the mod.

**Author:** your-name
**Version:** 1.0.0
**Game:** Dyson Sphere Program (latest stable)
**Loader:** BepInEx 5

<!-- BEGIN:TEMPLATE_USAGE (do not delete) -->
## How to use this template
This is the **player-facing** README for a mod. It is shown on Thunderstore,
Nexus, and the GitHub repo. Keep it short, screenshot-friendly, and free of
internal jargon. Internal notes go in `notes\`.

### When to use it
- New mod first cut.
- Every release that changes install steps, hotkeys, or config keys.

### When NOT to use it
- Internal design notes (use `notes\requirements.md` instead).
- A change log (use `CHANGELOG.md`).
- API documentation (use `notes\api-reference.md`).

### When copying
- Place at `<ModFolder>\README.md` (the file at the root of the mod folder).
- Delete this `BEGIN:TEMPLATE_USAGE` block.
- Replace every `<placeholder>` with real content.
- Keep the final README under 200 lines if you can.
<!-- END:TEMPLATE_USAGE -->

---

## What it does

Describe the mod's effect in 2-3 sentences. No setup yet, just "what
changes in the game when this mod is on".

## Features

- Bullet list of the main features, one line each.
- Lead with the most player-visible feature.
- Avoid implementation details ("uses Harmony transpiler on GameData.X").

## Installation

1. Install [BepInEx 5 for Dyson Sphere Program](https://thunderstore.io/c/dyson-sphere-program/p/xiaoye97/BepInEx/).
2. Download the latest release ZIP.
3. Extract the ZIP into your `Dyson Sphere Program` folder so the `plugins\`
   path lands inside `BepInEx\plugins\`.
4. Launch the game once. BepInEx will create `BepInEx\config\<your-guid>.cfg`
   with the default settings.
5. (Optional) Edit that .cfg to taste.

## Configuration

| Setting | Default | Description |
|---|---|---|
| `General > EnableFeature` | `true` | Master switch. |
| `Hotkeys > FeatureHotkey` | `F9` | Hotkey to trigger the feature. |
| `Diagnostics > DebugLog`  | `false` | Verbose logging, off by default. |

(Full reference: see the .cfg file BepInEx writes on first run.)

## Compatibility

- **Game version:** DSP 0.9.x or later.
- **Other mods:** Known compatible with `<list>`. Known to conflict with `<list>`.
- **Multiplayer:** Singleplayer only / works in MP / untested in MP.

## Screenshots

(Optional) One or two screenshots showing the mod in action. Use the warm
palette thumbnails - avoid "AI slop" bright cyan.

## License

MIT / GPL-3.0 / etc. Pick one and put the full text in `LICENSE`.

## Credits

- `<ModName>` by `<author>`.
- Built on the workspace template at `templates\mod-template\`.
