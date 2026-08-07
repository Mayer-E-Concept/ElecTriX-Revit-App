# ME-Tools for Autodesk Revit

**Mayer E-Concept SRL** — electrical/MEP tools for Revit, all in one **ElecTriX** ribbon tab.

For build instructions, architecture notes, and bug-fix history, see [NOTES.md](NOTES.md).

---

## Supported Revit Versions

| Version | .NET | Status |
|---------|------|--------|
| Revit 2025 | .NET 8.0 | ✅ Supported |
| Revit 2026 | .NET 8.0 (same binary as 2025) | ✅ Supported |

---

## Installation

1. Download `setup_metools_vX.X.X.exe`
2. Run the installer and select which Revit version(s) to install for
3. Restart Revit — the **ElecTriX** tab appears in the ribbon

### License / Beta access
Free for **14 days** as a trial. After that, every tool except Settings refuses to open until activated. Get your Machine ID from **Settings → License** and send it to **office@mayer-econcept.ro** for a permanent key.

---

## How to use each tool

### Setup panel

**Settings** — Configure the add-in once: language, theme, license activation, standard worksets for new workshared projects, and default mounting heights per family category.

**Project Health Check** — Run this first on any new or detached project, before using Circuit Tagger. It checks whether the tag family and the 6 shared parameters are set up. If anything's missing, click **Fix All** to load/bind it in one step.

### Placement panel

**Family Placer** — Add one or more family "slots," each with its own mounting height and offset, then click **Place** (one drop) or **Multi-Place** (several, Esc to finish). Save a combination as a **Template** to reuse later.

**Family Browser** — Browse every loaded electrical family by category. Hover one and click **Place** to drop it, or **+ Load Family from Disk** first if it isn't loaded yet.

**Lamp Placer** — Pick a lamp family, choose **Area** (fills a room automatically), **Grid** (fixed rows × columns), or **Line** (along a drawn line), then place. Save your setup as a preset.

### Levels & Structure panel

**Fix Level** — Pick a scope and categories, click **Preview** to see how many elements would change, then **Fix Levels** to apply it.

**Level & IFC Manager** — *Project Levels* tab: view every level like a section, add new ones by name + elevation. *Import from IFC* tab: point at an IFC file (auto-detected if one's already linked), tick which levels to bring in, and create them as real Revit Levels.

**Project Transfer** — Pick another open project (or browse for one), tick which Filters/Views/Sheets/Schedules you want, and copy them across.

### Circuits & Reporting panel

**Circuit Tagger** — Select elements, fill in the circuit fields, click **Apply & Tag** (the label previews live as you type). The **Circuit Stats** tab groups totals by Building → Apartment → Circuit, with bulk-clear for old circuits.

**Statistics** — Live counts for everything in the model — lamps, sockets, switches, cable/conduit lengths, and more. **Export CSV** for a snapshot.

**Batch Params** — Pick a scope and category, **Scan**, then either **Renumber** (prefix + counter + suffix, ordered by clicking elements or along a drawn line) or **Bulk Edit** (add prefix/suffix, find & replace, set, or clear one parameter across every matched element). Every Apply shows a preview first — review it, then **Confirm & Apply**. Works on any category and any text parameter, not just electrical ones.

### Team panel

**Comments** — Leave a note tied to a level or a specific element (**+ Reference Item** to pin it to one thing). Teammates get a popup with **Go There** to jump straight to it.

**Activity Log** — See who added, changed, or deleted which electrical elements, and when. Filter by user/action, or **Export CSV**.

---

© 2025–2026 Mayer E-Concept SRL · All rights reserved
