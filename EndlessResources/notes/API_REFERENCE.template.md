# API Reference - <ModName>

<!-- BEGIN:TEMPLATE_USAGE (do not delete) -->
## How to use this template
- Place at `<ModFolder>\notes\api-reference.md`.
- Delete this `BEGIN:TEMPLATE_USAGE` block.
- Document every public class, method, and config entry the player or
  another modder might call. If it is private and only used internally,
  it does not belong here - it belongs in a code comment.
- **Auto-generate where possible.** When using XML doc comments
  (`///<summary>...</summary>`) with a tool like DocFX, this file becomes
  a re-export. Keep it for hand-written narrative; let DocFX cover
  signatures.
<!-- END:TEMPLATE_USAGE -->

## Public C# API

### `class <ModName>.Plugin : BaseUnityPlugin`

The BepInEx entry point. Lives at `<ModName>\Plugin.cs`.

| Member | Visibility | Notes |
|---|---|---|
| `PluginGuid` | public const | The mod's reverse-domain GUID. |
| `PluginName` | public const | The mod's display name. |
| `PluginVersion` | public const | Matches `<Version>` in the .csproj. |
| `Awake()` | private | Reads config, inits Harmony, applies patches. |
| `OnDestroy()` | private | Unpatches Harmony, logs unload. |

### `static class <ModName>Log`

The mod's single logging path.

| Method | When to use |
|---|---|
| `Info(msg)` | Verbose, gated behind `DebugLog`. |
| `Warn(msg)` | Non-fatal, always prints. |
| `Error(msg)` | Fatal or unexpected, always prints. |

## Public config keys

| Section | Key | Type | Default | Description |
|---|---|---|---|---|
| `General` | `EnableFeature` | bool | `true` | Master switch. |
| `Hotkeys` | `FeatureHotkey` | KeyboardShortcut | `F9` | Feature hotkey. |
| `Diagnostics` | `DebugLog` | bool | `false` | Verbose logging gate. |

## Harmony patches (for other modders)

| Source class | Method patched | Behavior |
|---|---|---|
| `GameSomeClass` | `DoThing` | Prefix guard that returns false when feature is off. |
