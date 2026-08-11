# Troubleshooting - <ModName>

<!-- BEGIN:TEMPLATE_USAGE (do not delete) -->
## How to use this template
- Place at `<ModFolder>\notes\troubleshooting.md`.
- Delete this `BEGIN:TEMPLATE_USAGE` block.
- Format: one section per known issue. The section heading is the symptom
  the player will see. Inside: root cause, how to verify, and the fix.
- Order sections from "most common" to "rare edge case".
- This is the file you check first when a player opens an issue.
<!-- END:TEMPLATE_USAGE -->

## Symptom 1: Game does not start / BepInEx log does not mention the mod

**Cause:** DLL not in the right folder.
**Verify:** `BepInEx\plugins\<ModName>\<ModName>.dll` exists.
**Fix:** Re-extract the mod zip into the game root. The zip is structured
so files land in the correct places.

## Symptom 2: Mod loads but the feature does not work

**Cause:** Master switch is off in the .cfg.
**Verify:** Open `BepInEx\config\<guid>.cfg` and check `General > EnableFeature = true`.
**Fix:** Set to `true`, save, relaunch the game.

## Symptom 3: Game crashes when the feature is triggered

**Cause:** Conflict with another mod.
**Verify:** Disable all other mods, then re-enable one at a time. Check
`BepInEx\LogOutput.log` for the stack trace.
**Fix:** Report the stack trace to the mod's GitHub issues. The trace
should mention the mod's class names.

## Symptom 4: Hotkey does nothing

**Cause:** Hotkey is bound to something the game already uses, or the
hotkey field is empty.
**Verify:** `Hotkeys > FeatureHotkey` has a value. Try a different key.
**Fix:** Set a different key in the .cfg, save, relaunch.

## Symptom 5: Debug log shows the wrong version

**Cause:** The mod is reading a stale cached .cfg.
**Verify:** Check the file timestamp on `BepInEx\config\<guid>.cfg` is
after the last edit.
**Fix:** Delete the .cfg, relaunch the game (BepInEx will re-create it
with defaults).
