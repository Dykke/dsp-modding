# How to use the document templates

This folder holds fill-in templates for every recurring document a DSP mod
needs. Each template has the **usage instructions embedded at the top** so
the next author does not have to read this guide to know what to do.

<!-- BEGIN:TEMPLATE_USAGE (do not delete) -->
## When to use these templates
- You are about to start a new mod, or
- You are adding a new doc type to an existing mod, or
- You want to bring an existing mod up to workspace documentation standards.

## How to use them
1. Pick the template that matches the document you are creating.
2. Open it. Read the `<!-- BEGIN:TEMPLATE_USAGE -->` block at the top first.
3. Copy the file into the right destination inside the mod folder
   (see "Where each template goes" below).
4. Rename it: drop the `.template` and any extension change as the
   instructions say.
5. Fill in the sections. Delete any section that does not apply.
6. Update the "Last updated" line.

## Where each template goes
| Template | Destination | Final name |
|---|---|---|
| `MOD_README.template.md` | `<ModFolder>\` | `README.md` |
| `CHANGELOG.template.md` | `<ModFolder>\` | `CHANGELOG.md` |
| `RELEASE_NOTES.template.md` | `<ModFolder>\` | `RELEASE_NOTES.md` |
| `REQUIREMENTS.template.md` | `<ModFolder>\notes\` | `requirements.md` |
| `TROUBLESHOOTING.template.md` | `<ModFolder>\notes\` | `troubleshooting.md` |
| `SESSION_LOG.template.md` | `<ModFolder>\notes\` | `session-YYYY-MM-DD.md` |
| `API_REFERENCE.template.md` | `<ModFolder>\notes\` | `api-reference.md` |
| `CONTRIBUTING.template.md` | `<ModFolder>\` | `CONTRIBUTING.md` |

## Rules of thumb
- One purpose per file. If a doc is trying to be README + changelog + notes,
  split it.
- Keep the `BEGIN:TEMPLATE_USAGE` block in templates that live in
  `docs/_TEMPLATES/`. **Delete it from the final doc that ships with the mod.**
- Do not edit the templates to add mod-specific content. If a section is
  always present for one mod, generalize it here so every mod benefits.
<!-- END:TEMPLATE_USAGE -->

---

## Quick reference: minimum doc set per mod

A new mod is "documented enough" when it has at least:

1. `README.md` - what the mod does, install steps, config keys.
2. `CHANGELOG.md` - one entry per release.
3. `notes/requirements.md` - what game version, BepInEx version, and other
   mods it requires or conflicts with.
4. `notes/troubleshooting.md` - the top 3 issues players will hit and how
   to fix them.

The other templates are added as the mod grows.
