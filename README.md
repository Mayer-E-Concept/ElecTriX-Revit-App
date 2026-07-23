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
- Each tab now re-measures the window to fit its own content when you switch to it — previously the window's height froze at whatever the first tab needed, so License/Worksets could look cut off until manually resized

---

### Project Health Check
Checks the things that silently break Circuit Tagger on a project that didn't inherit the full company template (e.g. a detached or brand-new project) — born directly from a real debugging session where these two gaps took hours to track down by hand.
- **Tag family check** — is `ME-Tools_CircuitTag` loaded in this project?
- **Shared parameter check** — are `Vorsicherung` / `FI-Kreis` / `Stromkreis Tag` / `Schaltkreis` / `CAx_Apartment` / `CAx_Building` bound to all 8 electrical/MEP categories?
- **Fix All** — loads the tag family and binds any missing parameter/category combinations in one click, from files bundled with the installer (see [Installer bundled resources](#installer-bundled-resources) below) — no more per-project Transfer Project Standards
- **Refresh** — re-runs the check on demand
- Read-only scan; **Fix All** is the only action that writes to the document, wrapped in its own transaction

---

### Activity Log
Tracks who added, modified, or deleted which electrical/MEP elements, and when — for tracing down "who deleted my lamps" after the fact.
- Scoped to the electrical/MEP categories ElecTriX actually works with (Electrical Fixtures, Lighting Fixtures, Lighting Devices, Electrical Equipment, Data/Fire Alarm/Communication/Security Devices, Cable Tray + Fitting, Conduit + Fitting, Wire) — not every category in the model
- **Deleted elements still show what they WERE** (category, family, type, level) via an in-memory snapshot cache, refreshed on every Added/Modified event and primed with a one-time scan when a document opens — since the element itself is already gone by the time a Deleted event fires
- **Go to Level** button per entry — jumps the active view to that element's floor plan, using the same `INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM`-first level-detection Fix Level already relies on (plain `Element.LevelId` is frequently blank for these families — see Key architecture notes)
- Filter by user, action (Added/Modified/Deleted), or free-text search; **Export CSV**
- Shares Comments' shared network folder and per-project ID system — nothing new to configure if Comments is already set up
- **Not live** — reads the shared log on open and on manual Refresh; another teammate's changes won't appear until you refresh

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
- Per-slot parameter overrides now report which ones failed to apply (not found, read-only, or wrong value type) instead of silently doing nothing

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
- Its `CurrentLevelId(FamilyInstance)` method (`INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM` first, `Element.LevelId` as fallback) is the reference implementation Activity Log's own level detection was fixed to match

---

### Circuit Tagger
Tag electrical elements with circuit data and generate circuit annotations.
- **Tag Elements tab** — select elements, fill in circuit parameters (Vorsicherung, FI, Stromkreis, Sub-index, Beleuchtungskreis, Apartment, Building), click Apply & Tag
- Circuit label preview updates live (e.g. `1F2_1`)
- **Secondary tag** — optional text annotation (a, b, c…) placed independently near each element
- Tag orientation auto-detected from element facing direction (horizontal / vertical)
- Smart tag alignment: tag head positioned relative to element bounding box after placement
- **Circuit Stats tab** — grouped by Building → Apartment → Circuit; sub-circuits listed under parent; **Clear** button on each row wipes circuit parameters from all elements in that circuit; Sock./Lamp/Sw./Total columns use fixed-width columns end to end (header and rows previously used different column-sizing rules and could drift out of alignment — see Key architecture notes)
- Auto-refresh stats on document change (DocumentChanged event)
- Reports plainly when the tag family isn't loaded or a shared parameter isn't bound to a category, instead of reporting "Done" as if it fully succeeded
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
- **Reference an Item** — pin a comment to a specific element instead of (or alongside) a level, for pointing at one thing on a crowded plan (e.g. "fix these lamps"). Click **+ Reference Item**, pick the element in the model, a small removable chip confirms what was picked
- **Go to Item** — selects and zooms to that exact element via `UIDocument.ShowElements`, on any comment that has one; degrades to a clear "that element no longer exists" message if it's since been deleted, rather than failing silently
- Stored on a shared network folder (configured once, shared by the whole team) rather than inside the model — visible to teammates the moment they open the same project, no round-trip through Central required
- A corner popup notifies other users of new comments relevant to them, with a sound cue (togglable) and one-click **Mark Done** / **Ignore** / **Go There** / **Go to Item**
- **Go There** switches the active view straight to the correct level/building-section combination the comment was left on
- Main window lists every comment, grouped by author then by level, filterable by status (Open / Done / Ignored / All)
- **Delete** available per comment, with a confirmation step since it can't be undone the way Ignore/Done can (those can always be Reopened)

---

## Supported Revit Versions

| Version | .NET | Status |
|---------|------|--------|
| Revit 2024 | .NET Framework 4.8 | ✅ Supported |
| Revit 2025 | .NET 8.0 | ✅ Supported |
| Revit 2026 | .NET 8.0 (same binary as 2025) | ✅ Supported |

`METools.csproj` multi-targets `net48;net8.0-windows` — these are genuinely different compiled binaries, not one DLL that happens to run everywhere. A handful of Revit API members changed between the 2024 and 2025/2026 API surfaces (see **Revit API version differences** below); anything touching those needs to compile cleanly on both targets.

---

## Installation (end users)

1. Download `setup_metools_vX.X.X.exe`
2. Run the installer and select which Revit version(s) to install for (2024 / 2025 / 2026 — any combination)
3. Restart Revit
4. The **ElecTriX** tab appears in the Revit ribbon, organized into 5 panels: **Setup** (Settings, Project Health Check), **Placement** (Family Placer, Family Browser, Lamp Placer), **Levels & Structure** (Fix Level, Level Manager, Project Transfer), **Circuits & Reporting** (Circuit Tagger, Statistics), **Team** (Comments, Activity Log)

### License / Beta access
The add-in runs free for **14 days** as a beta trial. Once the trial ends, every tool (except Settings, so you can always activate a key) shows a clear "trial expired" message and refuses to open — it doesn't just nag, it actually stops working. For a permanent activation code, send your **Machine ID** (visible in Settings → License) to:

**office@mayer-econcept.ro**

The License tab also shows how long an activated key is valid for (`Licensed — N days left`, or `Licensed — Permanent`).

### Installer bundled resources
The installer deploys two files to `%ProgramData%\METools\Resources\`, used only by Project Health Check's **Fix All** button:
- `ME-Tools_CircuitTag.rfa` — the Multi-Category Tag family Circuit Tagger needs
- `METools_SharedParameters.txt` — the shared-parameter definition file (Vorsicherung / FI-Kreis / Stromkreis Tag / Schaltkreis / CAx_Apartment / CAx_Building), carrying the **same GUIDs** already in use everywhere else

Both are overwritten on every install/update (unlike the Comments/worksets seed files, which use `onlyifdoesntexist`) since they're app-owned dependency files, not user data. **If you ever add a parameter to the real master shared-parameter file, re-export and re-bundle this copy** — there's no live sync between "the real file" and this shipped snapshot.

Fix All is safe by construction, not just by convention: it briefly repoints `Application.SharedParametersFilename` at the bundled copy just long enough to read the 6 definitions it needs, then restores whatever the user had configured before — byte for byte, including the case where nothing was configured at all. It cannot affect any already-bound parameter in any project, because a bound parameter's data lives inside the project file itself once bound, not in whatever external `.txt` happens to be configured at the moment you look.

---

## Visual design

All windows share one dark teal/near-black background with a bright cyan accent (`MeToolsTheme.CAccent`), inspired by the me-concept.ro brand site — a deliberate move away from a generic neutral-grey dark mode. Every window shows a thin cyan accent line under its title bar as a small nod to the circuit-trace lines on the site. All buttons across every tool share one rounded template (`MeToolsWindowBase.RoundedBtnTemplate()`, `internal` so plain `Window` dialogs that don't inherit the shared base — e.g. small popup prompts, or nested classes like `FamilyPlacerWindow`'s `SlotRow`/`SaveTemplateDialog` — can still use the exact same look).

Ribbon icons are two-tone: a white/light-grey outline for the main shape, with **one** mint-teal accent element (≈ RGB 90,200,165) for the focal detail — the gear's center dot, the speech bubble's middle dot, a checkmark, a clock's hands. New icons should match this exactly rather than introduce a new style. Icon file suffix is the *opposite* of what's intuitive: `_dark_16/32.png` is shown when Revit's theme **is** dark (so it needs to be the light-colored version), `_light_*` is shown on Revit's light theme (needs to be the dark-colored version) — see `RibbonThemeWatcher.ApplyCurrentTheme()`'s `variant = dark ? "dark" : "light"`.

Ribbon panel *backgrounds* cannot be colored through the public Revit API — confirmed on Autodesk's own API forum, including a paid App Store extension that did this being pulled from the store. `RibbonPanelColorizer.cs` is a best-effort, clearly-labeled experimental attempt via an internal, undocumented `Autodesk.Windows.RibbonPanel` property (`CustomPanelTitleBarBackground`, found via reflection, not the same as the whole-panel `CustomPanelBackground`) — treat it as liable to break on a future Revit update, not as a normal part of the app's supported surface.

---

## Development

### Repository
- **GitHub:** `https://github.com/Mayer-E-Concept/ElecTriX-Revit-App`
- **Local:** `X:\02_sabloane\01_Revit\ElecTriX-Revit-App\`
- **Auto-deploy (Debug):** `C:\ProgramData\Autodesk\Revit\Addins\2024\`, `\2025\`, `\2026\` (whichever you're actively building/testing against)

### Build
```
# Daily development (auto-deploys to Revit add-ins folder)
Build → Debug

# Release (for installer only) -- build BOTH net48 and net8.0-windows
# configurations before compiling the installer; it packages whichever
# Revit-version tasks the end user selects from either one.
Build → Release
```

### Versioning
The version shown in Settings and in ribbon tooltips is read live from `setup.iss`'s `#define AppVersion "X.X.X"` — it must be listed under `<EmbeddedResource>` in `METools.csproj` (not just `<None>`) or the app silently falls back to the assembly's default version instead of erroring, which is a confusing failure mode to debug. Bump the version in `setup.iss` only; nowhere else.

### Create installer (after Release build)
1. Make sure `Resources\ME-Tools_CircuitTag.rfa` and `Resources\METools_SharedParameters.txt` exist next to the project (see [Installer bundled resources](#installer-bundled-resources))
2. Open `setup.iss` in Inno Setup Compiler
3. Press **F9** (Build → Compile)
4. Output: `installer_output\setup_metools_vX.X.X.exe`

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
├── Resources/                      ← Bundled installer assets (see Installer bundled resources)
│   ├── ME-Tools_CircuitTag.rfa
│   └── METools_SharedParameters.txt
├── App.cs                          ← Ribbon setup (ElecTriX tab, 5 panels: Setup/Placement/Levels & Structure/Circuits & Reporting/Team)
├── AppSwitcher.cs                  ← Title-bar app switcher dropdown
├── MeToolsWindowBase.cs            ← Base class for all windows (theme, title bar, status bar, shared button styles, resize-glitch fix)
├── MeToolsTheme.cs                 ← Color palette (Dark / Light), brand accent, brushes
├── RibbonThemeWatcher.cs           ← Swaps ribbon icons to match Revit's own light/dark theme
├── RibbonPanelColorizer.cs         ← EXPERIMENTAL panel-title coloring via undocumented internal API
├── Strings.cs                      ← EN / DE / RO localisation table
│
├── SettingsWindow.cs               ← Settings (Appearance, Language, License, Worksets, Heights)
├── SettingsCommand.cs
├── CreateStandardWorksetsCommand.cs ← Creates worksets from config/standard_worksets.json
├── standard_worksets.json
├── ThemeToggleCommand.cs           ← Dark/Light toggle (not gated by trial expiry — cosmetic only)
│
├── ProjectHealthCheckWindow.cs     ← Project Health Check UI
├── ProjectHealthCheckCommand.cs    ← Collector (read-only scan) + Fixer (Fix All, transactional) + Handler
│
├── ActivityLogWindow.cs            ← Activity Log UI (filters, cards, Export CSV)
├── ActivityLogCommand.cs           ← Refresh + Go-To-Level handlers/ExternalEvents
├── ActivityLogWatcher.cs           ← Background DocumentChanged tracker + element-snapshot cache
├── ActivityLogStorage.cs           ← Shared JSON-Lines storage (reuses Comments' shared folder + project ID)
├── ActivityLogModels.cs            ← ActivityLogEntry, ElementSnapshot, ActivityAction
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
├── FixLevelCommand.cs              ← Reference implementation for INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM level detection
│
├── CircuitTaggerWindow.cs          ← Circuit Tagger UI (3 tabs)
├── CircuitTaggerHandler.cs         ← Writes params, places tags, clears data; honest reporting on missing family/params
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
├── CommentsWindow.cs               ← Comments UI (composer, Reference an Item, comment list)
├── CommentsHandler.cs              ← Load/Add/SetStatus/Delete/JumpToLevel/GoToElement
├── CommentsCommand.cs
├── CommentsModels.cs               ← ProjectComment (incl. ReferencedElementId/Summary), CommentsRequest
├── CommentsStorage.cs              ← Shared folder + persistent project ID (also reused by Activity Log)
├── CommentsWatcher.cs              ← Background popup notifications
├── CommentPopupWindow.cs           ← Corner notification popup (Go There / Go to Item / Mark Done / Ignore)
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
└── setup.iss                       ← Inno Setup installer script (AppVersion lives here; bundles Resources/)
```

---

## Key architecture notes

- **Pure WPF, no XAML** — all UI built in C# code-behind
- **Namespace:** `METools` (sub-namespaces in use: `METools.FamilyPlacer`, `METools.LampPlacer`, `METools.LevelManager`, `METools.ProjectTransfer`, `METools.Comments`, `METools.ActivityLog`)
- **Transaction isolation:** SubTransactions per item (circuit, filter/view/sheet/schedule category, etc.) are mandatory — a single transaction causes silent rollback of everything on any failure
- **Honest reporting:** a batch operation's success count must reflect what actually succeeded, not just "did we attempt it." Two real bugs this shape were found and fixed — Circuit Tagger reporting "Done" when the tag family wasn't loaded or a shared parameter wasn't bound, and Family Placer silently ignoring a bad parameter-override name in a saved template. Both now surface the failure in the status message instead of looking like success.
- **Level detection for CAx family instances:** never trust plain `Element.LevelId` alone — it's frequently `InvalidElementId` for these families even when they have a perfectly good, user-meaningful level. Always check `BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM` first (the same "Schedule Level" parameter Lamp Placer sets post-placement and Fix Level exists to repair), falling back to `Element.LevelId` only if that's unset. See `FixLevelCommand.CurrentLevelId(FamilyInstance)` for the reference implementation; Activity Log's `ResolveLevel` was fixed to match it after shipping with the naive version first.
- **Unit awareness:** Auxalia CAx family parameters are LENGTH type in internal feet — never assume millimetres
- **Revit API version differences (2024 vs 2025/2026):** a few API members present in Revit 2024 (net48) were fully removed, not just deprecated, by 2025/2026 (net8.0-windows) — code touching these needs the newer replacement to compile on both targets:
  - `DisplayUnitType` → `UnitTypeId` (e.g. `UnitTypeId.Millimeters`)
  - `BuiltInParameterGroup` → `GroupTypeId` (e.g. `GroupTypeId.Data`, used in `BindingMap.Insert`/`ReInsert`)
  - `ElementIntersectsGeometryFilter` requires a separate assembly → use `BoundingBoxIntersectsFilter`
  - `NewOpening(Wall, …)` removed → use `Level.Create(doc, elevation)` for new levels
  - `ColorSelectionDialog.SelectedColor` is read-only
  - `System.Windows.Controls.ComboBox`/`TextBox`/`Grid`/`Button`/`Color`/`Ellipse`/`Image`/`Brushes`/`Visibility` all collide with same-named types in `Autodesk.Revit.UI` — always alias (`using Grid = System.Windows.Controls.Grid;` etc.) in any file that imports both `System.Windows.Controls`/`System.Windows.Media` and `Autodesk.Revit.UI`; only alias the ones a given file actually uses as bare type names
- **External Events for all model writes** — never run transactions directly from WPF click handlers (causes "outside API context" error). One established exception: a modeless window that already holds a `UIApplication` reference can call `Selection.PickObject`/`PickObjects` directly from its own button-click handler by calling `Hide()` first and `Show()` after — no ExternalEvent round-trip needed just to capture a selection (see `CircuitTaggerWindow.OnSelectClicked`, `CommentsWindow.OnReferenceItemClicked`)
- **Cross-document copy** (Project Transfer): `ElementTransformUtils.CopyElements(sourceDoc, ids, targetDoc, Transform.Identity, options)` works well for Filters, Schedules, Drafting Views/Legends and Sheets between two open documents; Plan/Section/Elevation/3D views don't transfer meaningfully since they're tied to their own project's Levels/Grids
- **DockPanel fill order matters:** in `MeToolsWindowBase`, whichever element is added to `RootDock.Children` **last** gets the remaining space, regardless of its own `Dock` value (`LastChildFill = true`). Always call `BuildStatusBar()` **before** building the main content panel, or the status bar silently steals the content area's layout space
- **Window resize-glitch fix:** `SizeToContent` and a free resize grip (`ResizeMode.CanResizeWithGrip`) fight each other in WPF — the window can visibly glitch and snap toward one screen edge mid-drag. Fixed by measuring the window once via `SizeToContent.Height` in the `Loaded` handler, then freezing it to a fixed `Height` with `SizeToContent = SizeToContent.Manual`. Tradeoff: any window with tabs of very different content heights needs to re-run that same measure-then-freeze sequence on every tab switch (`SizeToContent.Height` → `UpdateLayout()` → capture `ActualHeight` → back to `Manual`), or later tabs stay stuck at whatever height the first tab needed — see `SettingsWindow.ResizeToFitActiveTab()`.
- **Button padding:** `RoundedBtnTemplate()`'s `ContentPresenter` must bind its `Margin` to the button's own `Padding` (`TemplateBinding`-equivalent via `RelativeSource.TemplatedParent`) — without that binding, every button's `Padding` setting is silently ignored and text renders touching the button's edges. This one binding fixes the look of every button in the app that sets `Padding`, since nearly all of them share this one template.
- **License gate:** every tool command's `Open()`/`Execute()` calls `LicenseManager.CheckAccessOrExplain()` as its first line — except `SettingsCommand` (must always stay reachable to activate a key) and `ThemeToggleCommand` (cosmetic, left ungated on purpose)
- **AppSwitcher** — all modeless windows show a dropdown in the title bar to switch to any other ME-Tools app; register a window by overriding `protected override string AppKey => "MyKey";`, and add the same key to `AppSwitcher.Apps` and `AppSwitchHandler.Execute()`

---

© 2025–2026 Mayer E-Concept SRL · All rights reserved
