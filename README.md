# ME-Tools for Autodesk Revit

**Mayer E-Concept SRL** — Professional electrical and MEP tools for Revit

---

## Tools

### Settings
Central configuration panel for the entire add-in.
- **Appearance** — switch between Dark and Light themes for all ME-Tools windows simultaneously
- **Language** — English / Deutsch / Română
- **License** — activate / deactivate license key, copy Machine ID for key generation; shows license status **and how long it's valid for** (`Licensed — N days left` or `Licensed — Permanent`)
- **Worksets** — define standard worksets created in workshared projects; separate read-only view of the worksets that already exist in the current project
- **Heights** — configure default mounting heights per family category

---

### Family Placer
Place stacked combinations of electrical families at a single click.
- Add multiple slots (families), each with its own mounting height (Niveau), 2D offset (Off Y) and 3D level offset
- **Drag-reorder** — hold and drag the ⠿ handle to reorder slots
- **Auto-offsets** — 2D stacking offset increments automatically as slots are added; 3D offset detects height collisions and avoids them
- **Y=Frame** — inline checkbox per slot (default on), aligns element to the frame edge
- Save and reload named **Templates** for repeatable multi-family groups
- **Stacked** or **Side by Side** arrangement
- **Place** (single drop) or **Multi-Place** (multiple drops, ESC to finish)
- SPACEBAR rotates before placing; wall detection and free workplane both supported

### Family Browser
Browse all loaded `_E_CAx` families grouped by category, with live search.
- **9 categories**, auto-detected by name (case-insensitive, so German compound words like `Wechselschalter`/`Serienschalter` classify correctly): Sockets, Switches, Lighting, Panels, Fire Alarm, Data/Comms, Sensors, **Lightning Protection**, **Conduit & Fittings**
- Hover a family to reveal a rounded **Place** button (enters placement mode, like drag-and-drop)
- **+ Load Family from Disk** — load new `.rfa` files directly into the project
- Group tabs show live counts; only a couple of genuinely generic family names (no category-indicating keyword at all) fall back to "Other"

---

### Lamp Placer
Automate lighting fixture placement in ceiling plans.
- **Area-based** — lamps distributed automatically by room area and sqm/lamp ratio
- **Manual grid** — fixed rows × columns
- **Line mode** — place along one or several existing model/detail lines; spacing by distance or count; Along Line or Perpendicular orientation; multiple selected lines are filled independently (not just the closest one)
- Lamp family and type selection with live detection of loaded families; save/reuse **presets**
- Face-based or workplane-based placement
- Wall margin and overlap threshold configurable
- UKD offset for correct ceiling height

---

### Level Manager
See every level in the project laid out like a section, and add new ones.
- **Section view** — all levels stacked top-to-bottom by real elevation, with a colored tick + bubble per row, name, zone chip and elevation value
- **Compact mode** (default) — even spacing so dense clusters of levels (e.g. UKD/FFB pairs a metre apart) stay readable
- **True Scale mode** — spacing proportional to actual elevation gaps (clamped so one large gap doesn't crush the rest)
- **Auto-grouping** — groups levels by whatever prefix recurs across the project's own naming (e.g. `UKD`, `FFB`, `Obergeschoss`) and by a trailing zone/house tag (e.g. `H1`, `H2`) — nothing is hardcoded to any one project's convention
- **Group and zone filters**, plus a live count of what's shown
- **Add Level** — name + elevation (metres); clicking a level in the list prefills the elevation field at +3.000 m as a starting point

---

### Project Transfer
Copy Filters, Views, Sheets and Schedules from the active project into another one.
- **Copy To** — pick any other Revit project already open in the same session, or **Browse…** to open one from disk (re-uses it if it's already open instead of erroring)
- **4 categories**, checkable and searchable: Filters, Views, Sheets, Schedules, with live per-category counts
- **Views** is intentionally limited to **Drafting Views and Legends** — Plan/Section/Elevation/3D views are tied to their own project's Levels and Grids, so copying them into an unrelated project rarely produces a working result (same limitation Revit's own "Insert Views from File" has)
- **Sheets** are copied together with whatever is placed on them (Revit brings viewports/titleblocks along automatically); a sheet gets a ⚠ warning icon if it holds a plan/section/3D view, since that part may not transfer cleanly
- Copies run **per category** so one failure doesn't take the rest down with it; duplicate type names in the target keep the target project's own version

---

### Fix Level
Assigns the correct schedule level to electrical elements.
- Corrects the `CAx_Trassenbezugsebene` and `CAx_Ebenenhöhenwert` CAx level parameters
- Scope: **Active view only**, **Current storey**, or **Whole model**
- Categories: Sockets (Electrical Fixtures → FFB), Switches (Lighting Devices → FFB), Lamps (Lighting Fixtures → UKD ceiling level)
- Skip wall-mounted lamps option (by host offset or family name)
- **Preview** mode — counts affected elements without making changes
- **Fix Levels** — applies corrections in a single transaction
- Dropdown menu to switch to any other ME-Tools app

---

### Circuit Tagger
Tag electrical elements with circuit data and generate circuit annotations.
- **Tag Elements tab** — select elements, fill in circuit parameters (Vorsicherung, FI, Stromkreis, Sub-index, Beleuchtungskreis, Apartment, Building), click Apply & Tag
- Circuit label preview updates live (e.g. `1F2_1`)
- **Secondary tag** — optional text annotation (a, b, c…) placed independently near each element
- Tag orientation auto-detected from element facing direction (horizontal / vertical)
- Smart tag alignment: tag head positioned relative to element bounding box after placement
- **Circuit Stats tab** — grouped by Building → Apartment → Circuit; sub-circuits listed under parent; **Clear** button on each row wipes circuit parameters from all elements in that circuit
- Auto-refresh stats on document change (DocumentChanged event)
- **Settings tab** — tag placement (X offset, Y offset, Stack gap); full secondary label style matching Revit's TextNoteType parameters (Color with native Revit color picker, Line Weight, Background, Show Border, Leader/Border Offset, Leader Arrowhead, Font, Size, Tab Size, Width Factor, Bold, Italic, Underline); saved to `%APPDATA%\METools\circuit-tagger.json`
- **Export Excel** — exports circuit data to CSV

---

### Statistics
Live element count and length summary for the active model.
- **Electrical** — Lamps, Sockets, Switches, Panels, Circuits, Fire Alarm, Data, Communication, Security, Nurse Call, Telephone devices
- **Sockets by type / Switches by type** — breakdown by family type name, sorted alphabetically
- **By workset** — Sockets/Switches/Lamps by workset (workshared projects only)
- **Per floor** — Sockets / Switches / Lamps per level (reads `CAx_Trassenbezugsebene`)
- **Cable & Containment** — Cable Trays and Conduits shown as **total length in metres**; fittings shown as count; Wires shown as count
- **Mechanical & Plumbing** — Mechanical Equipment, Ducts, Air Terminals, Pipes, Plumbing Fixtures, Sprinklers
- **Spaces & Levels** — Rooms, MEP Spaces, Levels
- **Refresh** button recomputes on demand
- **Export CSV** — exports all rows, in a fixed section order, to `Documents\METools\statistics_<model>_<date>.csv`

---

### Comments
Cross-machine, per-project comment/notification system for team coordination.
- Comments are tagged to whichever level (and Scope Box, for projects with multiple building sections sharing level names) the active view is on when left
- Stored on a shared network folder (configured once, shared by the whole team) rather than inside the model — visible to teammates the moment they open the same project, no round-trip through Central required
- A corner popup notifies other users of new comments relevant to them, with a sound cue (togglable) and one-click **Mark Done** / **Ignore** / **Go There**
- **Go There** switches the active view straight to the correct level/building-section combination the comment was left on
- Main window lists every comment, grouped by author then by level, filterable by status (Open / Done / Ignored / All)
- **Delete** available per comment, with a confirmation step since it can't be undone the way Ignore/Done can (those can always be Reopened)

---

## Supported Revit Versions

| Version | .NET | Status |
|---------|------|--------|
| Revit 2025 | .NET 8.0 | ✅ Supported |

---

## Installation (end users)

1. Download `setup_metools_vX.X.X.exe`
2. Run the installer and select your Revit version
3. Restart Revit
4. The **ElecTriX** tab appears in the Revit ribbon

### License / Beta access
The add-in runs free for **14 days** as a beta trial. Once the trial ends, every tool (except Settings, so you can always activate a key) shows a clear "trial expired" message and refuses to open — it doesn't just nag, it actually stops working. For a permanent activation code, send your **Machine ID** (visible in Settings → License) to:

**office@mayer-econcept.ro**

The License tab also shows how long an activated key is valid for (`Licensed — N days left`, or `Licensed — Permanent`).

---

## Visual design

All windows share one dark teal/near-black background with a bright cyan accent (`MeToolsTheme.CAccent`), inspired by the me-concept.ro brand site — a deliberate move away from a generic neutral-grey dark mode. Every window shows a thin cyan accent line under its title bar as a small nod to the circuit-trace lines on the site. All buttons across every tool share one rounded template (`MeToolsWindowBase.RoundedBtnTemplate()`, public so even plain `Window` dialogs that don't inherit the shared base — e.g. small popup prompts — can use the exact same look).

---

## Development

### Repository
- **GitHub:** `https://github.com/Mayer-E-Concept/ElecTriX-Revit-App`
- **Local:** `X:\02_sabloane\01_Revit\ElecTriX-Revit-App\`
- **Auto-deploy (Debug):** `C:\ProgramData\Autodesk\Revit\Addins\2025\`

### Build
```
# Daily development (auto-deploys to Revit add-ins folder)
Build → Debug

# Release (for installer only)
Build → Release
```

### Versioning
The version shown in Settings and in ribbon tooltips is read live from `setup.iss`'s `#define AppVersion "X.X.X"` — it must be listed under `<EmbeddedResource>` in `METools.csproj` (not just `<None>`) or the app silently falls back to the assembly's default version instead of erroring, which is a confusing failure mode to debug. Bump the version in `setup.iss` only; nowhere else.

### Create installer (after Release build)
1. Open `setup.iss` in Inno Setup Compiler
2. Press **F9** (Build → Compile)
3. Output: `installer_output\setup_metools_vX.X.X.exe`

### Generate a license key
Open `KeyGenerator.html` in a browser → enter the customer's Machine ID → copy the generated code.

The Machine ID is shown in **Settings → License → Machine ID** inside the running add-in.

### GitHub workflow (after each coding session)
1. Open **GitHub Desktop** — changed files appear automatically
2. Write a short summary (e.g. `Fix tag alignment, add settings tab`)
3. **Commit to main**
4. **Push origin**

---

## Project structure

```
ElecTriX-Revit-App/
├── Icons/                          ← All PNG icons (light/dark × 16/32 per tool)
├── App.cs                          ← Ribbon setup (ElecTriX tab)
├── AppSwitcher.cs                  ← Title-bar app switcher dropdown
├── MeToolsWindowBase.cs            ← Base class for all windows (theme, title bar, status bar, shared button styles)
├── MeToolsTheme.cs                 ← Color palette (Dark / Light), brand accent, brushes
├── RibbonThemeWatcher.cs           ← Swaps ribbon icons to match Revit's own light/dark theme
├── Strings.cs                      ← EN / DE / RO localisation table
│
├── SettingsWindow.cs               ← Settings (Appearance, Language, License, Worksets, Heights)
├── SettingsCommand.cs
├── CreateStandardWorksetsCommand.cs ← Creates worksets from config/standard_worksets.json
├── standard_worksets.json
├── ThemeToggleCommand.cs           ← Dark/Light toggle (not gated by trial expiry — cosmetic only)
│
├── FamilyPlacerWindow.cs           ← Family Placer UI + drag-reorder + slot management
├── FamilyPlacerHandler.cs          ← Family placement (PromptForFamilyInstancePlacement)
├── FamilyPlacerCommand.cs
├── FamilyBrowserWindow.cs          ← Family Browser (_E_CAx families, 9 auto-detected categories)
├── FamilyBrowserCommand.cs
├── FamilyParamInspector.cs         ← Reads editable instance params on demand
├── FamilyHeightScanner.cs          ← Derives default Niveau from placed instances
├── FamilyHeightStore.cs
├── FamilyLoader.cs
├── Models.cs                       ← FamilySlot, PlacerTemplate, FamilyTypeInfo, etc.
├── TemplateManager.cs              ← Load/save placement templates to JSON
│
├── LampPlacerWindow.cs             ← Lamp Placer UI
├── LampPlacerHandler.cs            ← Lamp placement logic (area, grid, line modes)
├── LampPlacerCommand.cs
├── LampPlacerModels.cs
├── LampPresetStore.cs
├── RasterRoomDetector.cs
├── RaumHelper.cs
├── NumberRoomsDialog.cs
├── LevelGuard.cs                   ← "Are you on the right level?" confirmation before placing
│
├── LevelManagerWindow.cs           ← Level Manager UI (section view, filters, add level)
├── LevelManagerHandler.cs          ← Gathers/creates levels
├── LevelManagerCommand.cs
├── LevelManagerModels.cs           ← LevelRow + project-agnostic name-based auto-grouping
│
├── ProjectTransferWindow.cs        ← Project Transfer UI (target picker, 4 category tabs)
├── ProjectTransferHandler.cs       ← Cross-document CopyElements, per-category SubTransactions
├── ProjectTransferCommand.cs
├── ProjectTransferModels.cs
│
├── FixLevelWindow.cs               ← Fix Level UI
├── FixLevelCommand.cs
│
├── CircuitTaggerWindow.cs          ← Circuit Tagger UI (3 tabs)
├── CircuitTaggerHandler.cs         ← Writes params, places tags, clears data
├── CircuitTaggerCommand.cs         ← Singleton + DocumentChanged auto-refresh
├── CircuitTaggerModels.cs          ← Request/response models
├── CircuitTaggerSettings.cs        ← Persistent settings to JSON
├── CircuitBuilderHandler.cs
├── KonfigurationsModels.cs         ← ProjektKonfiguration data model — used by Circuit Tagger, not just the old tool
├── KonfigStorage.cs                ← Persists circuit config (ExtensibleStorage + JSON backup) — used by Circuit Tagger
├── KonfigViewModel.cs              ← Match-prefix derivation logic — used by Circuit Tagger
│
├── StatisticsWindow.cs             ← Statistics UI (length for trays/conduits, by-workset)
├── StatisticsCommand.cs            ← StatRow, StatisticsCollector, StatisticsHandler
│
├── Command.cs                      ← DistributeCommand (symmetric object distribution, "Verteilen")
├── Dialog.cs                       ← DistDialog — the "Verteilen" popup (plain Window, not MeToolsWindowBase)
│
├── LicenseManager.cs               ← 14-day trial + key-based licensing (ECDSA-signed codes); CheckAccessOrExplain() gate used by every tool command except Settings
├── LicenseWindow.cs                ← Purchase / activation window
├── LicenseCheck.cs
├── SplashGate.cs                   ← Trial splash on Revit startup + live app version resolution
├── SplashWindow.cs
│
├── RevitDatenHelper.cs
├── METools.csproj
├── METools.addin
└── setup.iss                       ← Inno Setup installer script (AppVersion lives here)
```

---

## Key architecture notes

- **Pure WPF, no XAML** — all UI built in C# code-behind
- **Namespace:** `METools` (sub-namespaces in use: `METools.FamilyPlacer`, `METools.LampPlacer`, `METools.LevelManager`, `METools.ProjectTransfer`)
- **Transaction isolation:** SubTransactions per item (circuit, filter/view/sheet/schedule category, etc.) are mandatory — a single transaction causes silent rollback of everything on any failure
- **Unit awareness:** Auxalia CAx family parameters are LENGTH type in internal feet — never assume millimetres
- **Revit API constraints (2025):** `ElementIntersectsGeometryFilter` requires separate assembly → use `BoundingBoxIntersectsFilter`; `NewOpening(Wall, …)` removed, use `Level.Create(doc, elevation)` for new levels; `ColorSelectionDialog.SelectedColor` is read-only; `System.Windows.Controls.Grid` collides with `Autodesk.Revit.DB.Grid` — always alias (`using Grid = System.Windows.Controls.Grid;`) in any file that imports both `System.Windows.Controls` and `Autodesk.Revit.DB`
- **External Events for all model writes** — never run transactions directly from WPF click handlers (causes "outside API context" error)
- **Cross-document copy** (Project Transfer): `ElementTransformUtils.CopyElements(sourceDoc, ids, targetDoc, Transform.Identity, options)` works well for Filters, Schedules, Drafting Views/Legends and Sheets between two open documents; Plan/Section/Elevation/3D views don't transfer meaningfully since they're tied to their own project's Levels/Grids
- **DockPanel fill order matters:** in `MeToolsWindowBase`, whichever element is added to `RootDock.Children` **last** gets the remaining space, regardless of its own `Dock` value (`LastChildFill = true`). Always call `BuildStatusBar()` **before** building the main content panel, or the status bar silently steals the content area's layout space
- **License gate:** every tool command's `Open()`/`Execute()` calls `LicenseManager.CheckAccessOrExplain()` as its first line — except `SettingsCommand` (must always stay reachable to activate a key) and `ThemeToggleCommand` (cosmetic, left ungated on purpose)
- **AppSwitcher** — all modeless windows show a dropdown in the title bar to switch to any other ME-Tools app; register a window by overriding `protected override string AppKey => "MyKey";`, and add the same key to `AppSwitcher.Apps` and `AppSwitchHandler.Execute()`

---

© 2025–2026 Mayer E-Concept SRL · All rights reserved
