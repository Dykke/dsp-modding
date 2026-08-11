# EndlessResources - release checklist

> Run through this before every release. Newest entries on
> top per the workspace's append-on-top convention.

## Pre-release

- [ ] All 8 in-game test scenarios pass (`notes/TESTING.md`).
- [ ] `CHANGELOG.md` has a new `## [X.Y.Z] - YYYY-MM-DD`
      section with the change summary.
- [ ] `<Version>` in `EndlessResources.csproj` is bumped
      to the new version.
- [ ] `version_number` in
      `packaging\manifest.template.json` matches.
- [ ] The `com.author.EndlessResources` GUID in
      `Plugin.cs` has the real author namespace (not
      `author`).
- [ ] The `website_url` in
      `packaging\manifest.template.json` points at the real
      GitHub repo.
- [ ] The `description` in
      `packaging\manifest.template.json` is the real
      one-liner, not a placeholder.
- [ ] `LICENSE` has the correct year and contributor name.
- [ ] `README.md` has the real author name (not
      `author`).
- [ ] Screenshots are present in `README.md` (or the
      "TODO before publish" note is explicitly removed if
      intentionally shipping without).
- [ ] `packaging\icon.png` is present (256x256 PNG, warm
      palette).
- [ ] `non-code\NEXUS_DESCRIPTION.bbcode` has the raw
      BBCode description (no `code fences`).
- [ ] No `__MOD_NAME__` or `TODO` placeholders leak into
      any final file.

## Build

- [ ] `cursor-stuff\tools\build.cmd EndlessResources`
      succeeds clean (no warnings).
- [ ] DLL is deployed to
      `BepInEx\plugins\EndlessResources\EndlessResources.dll`.
- [ ] BepInEx log shows the mod loaded with 5 patches
      applied.

## Package

- [ ] `cursor-stuff\tools\package.cmd EndlessResources`
      succeeds.
- [ ] `artifacts\EndlessResources-<version>.zip` exists.
- [ ] The zip contains: `manifest.json`, `README.md`,
      `icon.png`, `plugins\EndlessResources.dll`.
- [ ] The `manifest.json` inside the zip has the
      substituted name + version, not the `__MOD_NAME__`
      placeholder.
- [ ] The `dependencies` array includes
      `xiaoye97-BepInEx-5.4.17`.

## Publish

- [ ] Thunderstore: upload the zip to the mod's manage
      page. Confirm the version is correct, the icon
      renders, the description is right.
- [ ] Nexus: upload the same zip to the mod's Nexus page
      with the `non-code\NEXUS_DESCRIPTION.bbcode` as the
      description.
- [ ] GitHub: tag the release with the version. Attach
      the zip to the release.

## Post-release

- [ ] Add a new `## [Unreleased]` section to
      `CHANGELOG.md` on top, ready for the next iteration.
- [ ] Update `cursor-stuff\new-chat.md` if the mod's
      status changed (e.g. "Active" -> "In maintenance").
- [ ] Close any tracked issues marked "fixed in <version>".
