# Packaging EndlessResources

This folder is the source of truth for the mod's Thunderstore distribution
package. `tools\package.cmd EndlessResources` reads from here and writes the
final zip to `artifacts\EndlessResources-1.0.0.zip`.

## Files

| File | Purpose |
|---|---|
| `manifest.template.json` | Placeholders. `package.cmd` substitutes version + name and writes `manifest.json` into the staging tree. |
| `icon.png` (you add this) | 256x256 PNG. Warm palette recommended. |
| `README.md` (you add this) | Player-facing description. Becomes `README.md` in the zip. |

## Required manifest fields

```json
{
  "name": "YourModName",            // PascalCase, no spaces. Must match the Thunderstore namespace.
  "version_number": "1.0.0",        // SemVer
  "website_url": "https://github.com/yourname/yourmod",
  "description": "One-line summary of what the mod does.",
  "dependencies": [
    "xiaoye97-BepInEx-5.4.17"       // BepInEx is always required. Add CommonAPI here if you use it.
  ]
}
```

## When you cut a release

1. Bump `<Version>` in the .csproj and `version_number` here.
2. Run `tools\package.cmd EndlessResources` to build + zip.
3. Upload the zip in `artifacts\` to Thunderstore.
4. (Optional) Upload to Nexus Mods as a separate zipped release.

## Layout of the final zip

```
EndlessResources-1.0.0.zip
├── manifest.json
├── README.md
├── icon.png
└── plugins/
    └── EndlessResources.dll
```
