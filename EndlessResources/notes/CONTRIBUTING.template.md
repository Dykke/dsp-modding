# Contributing to <ModName>

<!-- BEGIN:TEMPLATE_USAGE (do not delete) -->
## How to use this template
- Place at `<ModFolder>\CONTRIBUTING.md`.
- Delete this `BEGIN:TEMPLATE_USAGE` block.
- This file is for **outside contributors** - other modders, players who
  want to send a PR, translators. Keep the tone welcoming and the
  instructions exact.
- Internal workflow lives in `docs\DEVELOPMENT.md` at the workspace root
  and `cursor-stuff\new-chat.md`. Do not duplicate that here - link to it.
<!-- END:TEMPLATE_USAGE -->

Thanks for your interest in making `<ModName>` better. This document covers
the practical steps to send a pull request or report an issue.

## Reporting a bug

1. Search [existing issues](../../issues) first.
2. Open a new issue. Include:
   - DSP version (check the bottom-right of the title screen).
   - BepInEx version.
   - Other mods you have installed.
   - The full text of `BepInEx\LogOutput.log` from the affected session.
   - Steps to reproduce.

## Suggesting a feature

Open an issue with the "feature request" template. Explain the player
problem, not the implementation. "I want a config knob for X" is better
than "add a ConfigEntry<bool>".

## Sending a code change

1. Fork the repo and create a branch off `main`:
   `git checkout -b feature/short-description`
2. Read `docs\DEVELOPMENT.md` (at the workspace root) for the
   coding conventions, the gating log rules, and how to validate.
3. Keep the change small. One concern per PR. Do not mix refactor + feature.
4. Make sure `tools\build.cmd <ModName>` exits 0.
5. Update `CHANGELOG.md` under `[Unreleased]`.
6. Open the PR. Fill in the PR template.

## Translation

(Localization is optional and mod-specific. Delete this section if not
applicable.)

Add or update a localization file under `<ModName>\Localization\` and
include a screenshot of the new strings in-game.

## Coding style

- C# 7.3, .NET Framework 4.7.2.
- 4-space indent, LF line endings (enforced by `.editorconfig`).
- All settings in BepInEx Config, not in custom windows.
- Single log helper, gated behind `DebugLog`.
- No frame-by-frame allocations in `Update`.

## License

By contributing, you agree that your contributions are licensed under the
mod's license (see `LICENSE`).
