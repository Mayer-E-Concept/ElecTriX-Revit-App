// CollisionCheckerWindow.cs -- ME-Tools | Collision Checker (conduits/cable trays vs walls)
// Mayer E-Concept SRL -- Pure C# WPF, no XAML
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Color      = System.Windows.Media.Color;
using ComboBox   = System.Windows.Controls.ComboBox;
using Grid       = System.Windows.Controls.Grid;
using TextBox    = System.Windows.Controls.TextBox;
using Visibility = System.Windows.Visibility;

namespace METools.CollisionChecker
{
    public class CollisionCheckerWindow : METools.MeToolsWindowBase
    {
        protected override string AppKey => "CollisionChecker";

        private readonly UIApplication          _uiApp;
        private readonly ExternalEvent          _extEvent;
        private readonly CollisionCheckerHandler _handler;

        private ScanScope _scope = ScanScope.WholeModel;
        // Caches FindPlanViewForLevel's result per (level, run category,
        // active view) -- that visibility check it does is a genuinely
        // heavier operation (a view-scoped FilteredElementCollector, which
        // Autodesk's own docs note can force Revit to rebuild that view's
        // visible-element cache), and "Go To" calls it once per click.
        // Most collisions in a real project share only a couple of
        // categories (Cable Trays, Conduits), so caching by category
        // rather than by the exact run element turns dozens of repeated
        // clicks into one real computation per level. Cleared at the
        // start of every Scan so it can never serve a stale answer from
        // before the model or view settings changed.
        private readonly Dictionary<(int LevelId, int CategoryId, int ActiveViewId), View> _goToViewCache = new();
        private Button _btnScopeModel, _btnScopeView, _btnScopeSel;
        private CheckBox _cbIncludeImported;
        private ComboBox _cbImportChoice;
        private CheckBox _cbIncludePlumbing;
        private ComboBox _cbPlumbingChoice;
        private TextBlock _lblSummary;
        private TextBlock _lblLastScanned;

        private ComboBox _cbHoleSymbol;
        private CollisionCheckerSettingsData _settingsData;
        private TextBox  _tbHoleSearch;
        private List<HoleSymbolOption> _holeSymbols = new List<HoleSymbolOption>();

        private List<CollisionInfo> _collisions = new List<CollisionInfo>();
        private readonly List<ElementId> _markerIds = new List<ElementId>();
        private readonly Dictionary<string, List<ElementId>> _markersByCollisionId = new Dictionary<string, List<ElementId>>();
        private StackPanel _resultList;
        // All / Already placed / Not placed -- filters which collisions
        // RenderResultList actually shows, without touching _collisions
        // itself (Place Holes for Selected and everything else still
        // needs the full, unfiltered list).
        private enum ResultStatusFilter { All, Placed, NotPlaced }
        private ResultStatusFilter _statusFilter = ResultStatusFilter.All;
        private readonly HashSet<string> _checkedRowIds = new HashSet<string>();
        private readonly Dictionary<string, CheckBox>  _rowChecks = new Dictionary<string, CheckBox>();
        private readonly Dictionary<string, TextBlock> _rowStatus = new Dictionary<string, TextBlock>();
        private readonly HashSet<string> _expandedGroups = new HashSet<string>();

        private Button _btnPlaceHoles;
        private Button _btnMarkSolved;

        public CollisionCheckerWindow(UIApplication uiApp, ExternalEvent extEvent, CollisionCheckerHandler handler)
        {
            _uiApp = uiApp; _extEvent = extEvent; _handler = handler;
            S.SetLanguage(SettingsStore.Language ?? "en");
            _settingsData = CollisionCheckerSettings.Load();
            InitWindow(S._("collisioncheck.title"), 660);
            MaxHeight = Math.Min(780, SystemParameters.WorkArea.Height - 60);
            // Fixed height, not auto-measured: the results section below
            // uses a star-sized row so it always gets whatever space is
            // left after the (short, fixed) intro/scope/family section --
            // that only works with a real, bounded window height to divide
            // up, not the base class's "measure natural content size"
            // default (SizeToContent.Height), which doesn't have a
            // meaningful answer for a star-sized row.
            SizeToContent = SizeToContent.Manual;
            // Opens at its own maximum allowed height by default, rather
            // than a smaller fixed value -- confirmed as a real, reported
            // annoyance: the window used to open shorter than its own
            // MaxHeight allows, needing a manual resize every single time
            // it's opened, even though the person had already found and
            // preferred the taller size.
            Height = MaxHeight;
            WireHandler();
            Build();
        }

        private void WireHandler()
        {
            _handler.OnStatus = msg => Dispatcher.Invoke(() => UpdateStatusBar(msg));
            _handler.OnDone   = result => Dispatcher.Invoke(() =>
            {
                if (result?.ResultAction == CollisionCheckerAction.MarkCollisions)
                    HandleMarkResult(result);
                else if (result?.ResultAction == CollisionCheckerAction.MarkPlumbingSolved)
                    HandleSolvedResult(result);
                else if (result?.ResultAction == CollisionCheckerAction.Frame3D)
                    HandleFrame3DResult(result);
                else
                    HandlePlaceResult(result);
            });
        }

        // ── Build ─────────────────────────────────────────────────────────
        private void Build()
        {
            BuildStatusBar(S._("collisioncheck.ready"));

            var contentGrid = new Grid { Background = MeToolsTheme.BrBg };
            contentGrid.Children.Add(Watermark());

            var rootGrid = new Grid();
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Top row: intro/scope/hole family -- naturally short, so this
            // scrolls on its own only in the unlikely case it's ever taller
            // than expected, rather than eating into the results row below.
            var topScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = System.Windows.Media.Brushes.Transparent,
                Padding    = new Thickness(16, 12, 16, 0),
            };
            var topStack = new StackPanel();
            topStack.Children.Add(InfoBox(S._("collisioncheck.intro_hint")));
            topStack.Children.Add(BuildScopeSection());
            topStack.Children.Add(Div());
            topStack.Children.Add(BuildHoleFamilySection());
            topScroll.Content = topStack;
            Grid.SetRow(topScroll, 0);
            rootGrid.Children.Add(topScroll);

            // Bottom row (star-sized -- gets whatever space is left):
            // results list + Place Holes button, always both visible. Only
            // the results box itself scrolls internally when there are a
            // lot of collisions to show.
            var resultsDock = new DockPanel { Margin = new Thickness(16, 8, 16, 12), LastChildFill = true };
            Grid.SetRow(resultsDock, 1);
            BuildResultsSectionInto(resultsDock);
            rootGrid.Children.Add(resultsDock);

            contentGrid.Children.Add(rootGrid);
            RootDock.Children.Add(contentGrid);

            TryRestoreCachedScan();
        }

        // If this document already has scan results from an earlier open
        // of this window (this Revit session), show them again instead of
        // starting empty -- closing the tool window shouldn't lose what was
        // already found, only actually closing the document should (see
        // CollisionCheckerWatcher's DocumentClosing cleanup).
        private void TryRestoreCachedScan()
        {
            var doc = _uiApp?.ActiveUIDocument?.Document;
            if (doc == null) return;
            var cached = CollisionCheckerWatcher.GetScanResults(doc);
            if (cached == null) return;

            _collisions = cached.Value.Collisions;

            // The actual fix for markers stacking up across a window
            // close/reopen: _markerIds/_markersByCollisionId used to be
            // window-instance fields with no memory of anything once that
            // instance closed, even though the markers themselves were
            // still sitting in the document, undeleted, from whenever a
            // PREVIOUS window instance last scanned. Restoring them here
            // means the next Scan's cleanup step (which deletes
            // OldMarkerIds before drawing new ones) actually has
            // something to clean up, instead of silently having nothing
            // to delete and drawing a second batch on top of the first.
            _markerIds.Clear();
            _markersByCollisionId.Clear();
            foreach (var kv in cached.Value.MarkersByCollisionId)
            {
                _markersByCollisionId[kv.Key] = kv.Value;
                _markerIds.AddRange(kv.Value);
            }

            _lblSummary.Text = _collisions.Count == 0
                ? S._("collisioncheck.none_found")
                : string.Format(S._("collisioncheck.n_found"), _collisions.Count);
            RenderResultList();
            UpdateLastScannedLabel(cached.Value.ScannedAt);
        }

        private void UpdateLastScannedLabel(DateTime scannedAt)
        {
            if (_lblLastScanned == null) return;
            _lblLastScanned.Visibility = Visibility.Visible;
            _lblLastScanned.Text = string.Format(S._("collisioncheck.last_scanned"), FormatRelativeTime(scannedAt));
            // A gentle nudge, not a hard warning -- the model may well not
            // have changed, but it's been long enough that it's worth a
            // rescan before trusting this list or placing holes from it.
            bool stale = (DateTime.Now - scannedAt) > TimeSpan.FromMinutes(30);
            _lblLastScanned.Foreground = stale ? MeToolsTheme.Br(MeToolsTheme.COrange) : MeToolsTheme.BrMuted;
        }

        private static string FormatRelativeTime(DateTime when)
        {
            var span = DateTime.Now - when;
            if (span.TotalSeconds < 60) return S._("collisioncheck.time_just_now");
            if (span.TotalMinutes < 60) return string.Format(S._("collisioncheck.time_min_ago"), (int)span.TotalMinutes);
            if (span.TotalHours < 24)   return string.Format(S._("collisioncheck.time_hours_ago"), (int)span.TotalHours);
            return string.Format(S._("collisioncheck.time_days_ago"), (int)span.TotalDays);
        }

        // ── Scope ─────────────────────────────────────────────────────────
        private StackPanel BuildScopeSection()
        {
            var sp = new StackPanel();
            sp.Children.Add(SecH(S._("collisioncheck.scope")));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            _btnScopeModel = ToggleBtn(S._("collisioncheck.scope_model"),     true,  () => SetScope(ScanScope.WholeModel));
            _btnScopeView  = ToggleBtn(S._("collisioncheck.scope_view"),      false, () => SetScope(ScanScope.ActiveView));
            _btnScopeSel   = ToggleBtn(S._("collisioncheck.scope_selection"), false, () => SetScope(ScanScope.CurrentSelection));
            _btnScopeModel.Margin = new Thickness(0, 0, 6, 0);
            _btnScopeView.Margin  = new Thickness(0, 0, 6, 0);
            row.Children.Add(_btnScopeModel);
            row.Children.Add(_btnScopeView);
            row.Children.Add(_btnScopeSel);
            sp.Children.Add(row);

            _cbIncludeImported = new CheckBox
            {
                Content = S._("collisioncheck.include_imported_ifc"),
                IsChecked = _settingsData?.IncludeImportedArchitecture ?? false,
                Foreground = MeToolsTheme.BrText,
                Margin = new Thickness(0, 2, 0, 4),
                ToolTip = S._("collisioncheck.include_imported_ifc_hint"),
            };
            _cbIncludeImported.Checked   += (s, e) => { SetIncludeImportedArchitecture(true);  RefreshImportChoicesVisibility(); };
            _cbIncludeImported.Unchecked += (s, e) => { SetIncludeImportedArchitecture(false); RefreshImportChoicesVisibility(); };
            sp.Children.Add(_cbIncludeImported);

            // Which import is "the architecture" -- not auto-detected (see
            // GetAllImportInstances), picked here instead. A project can
            // easily have a dozen+ imports; this combo lists every one of
            // them by its actual name so the person can tell them apart.
            _cbImportChoice = StyledCombo();
            _cbImportChoice.DisplayMemberPath = "Name";
            _cbImportChoice.ToolTip = S._("collisioncheck.import_choice_hint");
            _cbImportChoice.Margin = new Thickness(0, 0, 0, 6);
            _cbImportChoice.Visibility = (_settingsData?.IncludeImportedArchitecture ?? false) ? Visibility.Visible : Visibility.Collapsed;
            _cbImportChoice.SelectionChanged += (s, e) =>
            {
                var chosen = _cbImportChoice.SelectedItem as ArchitectureSourceOption;
                _settingsData = _settingsData ?? new CollisionCheckerSettingsData();
                _settingsData.ImportArchitectureName   = chosen?.Name ?? "";
                _settingsData.ImportArchitectureIsLink = chosen?.IsLink ?? false;
                CollisionCheckerSettings.Save(_settingsData);
            };
            sp.Children.Add(_cbImportChoice);
            RefreshImportChoices();

            _cbIncludePlumbing = new CheckBox
            {
                Content = S._("collisioncheck.include_plumbing"),
                IsChecked = _settingsData?.IncludePlumbing ?? false,
                Foreground = MeToolsTheme.BrText,
                Margin = new Thickness(0, 2, 0, 4),
                ToolTip = S._("collisioncheck.include_plumbing_hint"),
            };
            _cbIncludePlumbing.Checked   += (s, e) => { SetIncludePlumbing(true);  RefreshPlumbingChoicesVisibility(); };
            _cbIncludePlumbing.Unchecked += (s, e) => { SetIncludePlumbing(false); RefreshPlumbingChoicesVisibility(); };
            sp.Children.Add(_cbIncludePlumbing);

            // Plumbing clashes only ever look at a LINKED model -- there's
            // no equivalent "imported CAD file" case the way ImportInstance
            // is for architecture, so this list is links only.
            _cbPlumbingChoice = StyledCombo();
            _cbPlumbingChoice.DisplayMemberPath = "Name";
            _cbPlumbingChoice.ToolTip = S._("collisioncheck.plumbing_choice_hint");
            _cbPlumbingChoice.Margin = new Thickness(0, 0, 0, 6);
            _cbPlumbingChoice.Visibility = (_settingsData?.IncludePlumbing ?? false) ? Visibility.Visible : Visibility.Collapsed;
            _cbPlumbingChoice.SelectionChanged += (s, e) =>
            {
                var chosen = _cbPlumbingChoice.SelectedItem as PlumbingSourceOption;
                _settingsData = _settingsData ?? new CollisionCheckerSettingsData();
                _settingsData.PlumbingLinkName = chosen?.Name ?? "";
                CollisionCheckerSettings.Save(_settingsData);
            };
            sp.Children.Add(_cbPlumbingChoice);
            RefreshPlumbingChoices();

            var scanRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            scanRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            scanRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _lblSummary = new TextBlock { Text = S._("collisioncheck.not_scanned_yet"), FontSize = 11,
                Foreground = MeToolsTheme.BrMuted, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(_lblSummary, 0); scanRow.Children.Add(_lblSummary);
            var btnScan = ActionBtn(S._("collisioncheck.scan"), false, OnScanClicked);
            Grid.SetColumn(btnScan, 1); scanRow.Children.Add(btnScan);
            sp.Children.Add(scanRow);

            _lblLastScanned = new TextBlock { FontSize = 10, Foreground = MeToolsTheme.BrMuted,
                Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
            sp.Children.Add(_lblLastScanned);

            return sp;
        }

        private void SetScope(ScanScope scope)
        {
            _scope = scope;
            UpdateToggle(_btnScopeModel, scope == ScanScope.WholeModel);
            UpdateToggle(_btnScopeView,  scope == ScanScope.ActiveView);
            UpdateToggle(_btnScopeSel,   scope == ScanScope.CurrentSelection);
        }

        private void SetIncludeImportedArchitecture(bool on)
        {
            _settingsData = _settingsData ?? new CollisionCheckerSettingsData();
            _settingsData.IncludeImportedArchitecture = on;
            CollisionCheckerSettings.Save(_settingsData);
        }

        private void RefreshImportChoicesVisibility()
        {
            if (_cbImportChoice == null) return;
            _cbImportChoice.Visibility = (_cbIncludeImported?.IsChecked ?? false) ? Visibility.Visible : Visibility.Collapsed;
        }

        // Lists every ImportInstance in the CURRENT document -- refreshed
        // each time the window opens (imports can differ project to
        // project, and elements ids aren't stable across sessions anyway,
        // so this always re-resolves ImportArchitectureName against
        // whatever's actually in front of the person right now rather than
        // trusting a stale id from a previous session).
        private void RefreshImportChoices()
        {
            if (_cbImportChoice == null) return;
            var doc = _uiApp?.ActiveUIDocument?.Document;
            var options = new List<ArchitectureSourceOption> { new ArchitectureSourceOption { InstanceId = ElementId.InvalidElementId, Name = S._("collisioncheck.import_choice_none") } };
            if (doc != null)
            {
                foreach (var inst in CollisionCheckerHandler.GetAllImportInstances(doc))
                    options.Add(new ArchitectureSourceOption { InstanceId = inst.Id, Name = inst.Name ?? inst.Id.ToString(), IsLink = false });
                foreach (var link in CollisionCheckerHandler.GetAllLoadedRevitLinks(doc))
                {
                    var linkDoc = link.GetLinkDocument();
                    var typeName = (linkDoc?.GetElement(link.GetTypeId()) as RevitLinkType)?.Name ?? link.Name ?? link.Id.ToString();
                    if (typeName.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
                        typeName = typeName.Substring(0, typeName.Length - 4);
                    options.Add(new ArchitectureSourceOption { InstanceId = link.Id, Name = typeName, IsLink = true });
                }
            }

            _cbImportChoice.ItemsSource = options;
            var savedName = _settingsData?.ImportArchitectureName ?? "";
            var savedIsLink = _settingsData?.ImportArchitectureIsLink ?? false;
            var match = !string.IsNullOrEmpty(savedName)
                ? options.FirstOrDefault(o => o.IsLink == savedIsLink && string.Equals(o.Name, savedName, StringComparison.OrdinalIgnoreCase))
                : null;
            _cbImportChoice.SelectedItem = match ?? options[0];
        }

        private void SetIncludePlumbing(bool on)
        {
            _settingsData = _settingsData ?? new CollisionCheckerSettingsData();
            _settingsData.IncludePlumbing = on;
            CollisionCheckerSettings.Save(_settingsData);
        }

        private void RefreshPlumbingChoicesVisibility()
        {
            if (_cbPlumbingChoice == null) return;
            _cbPlumbingChoice.Visibility = (_cbIncludePlumbing?.IsChecked ?? false) ? Visibility.Visible : Visibility.Collapsed;
        }

        // Same idea as RefreshImportChoices, but links only (see the
        // remarks above _cbPlumbingChoice's construction). When there's no
        // saved preference yet, tries a one-time smart guess by name --
        // confirmed live that a project can genuinely have several
        // unrelated links (furniture, structural, architecture) alongside
        // the actual plumbing one, so defaulting to "none selected" would
        // otherwise leave the person to figure out which link is which
        // themselves the first time. Matches loosely on common plumbing/
        // HVAC naming conventions (HLSK = Heizung/Lüftung/Sanitär/Kälte,
        // TGA = Technische Gebäudeausrüstung) rather than requiring an
        // exact name -- still just a guess, not a guarantee, so it only
        // applies once, before any real preference has been saved.
        private static readonly string[] PlumbingLinkNameHints = { "hlsk", "sanit", "plumb", "tga", "hls", "pipe" };

        private void RefreshPlumbingChoices()
        {
            if (_cbPlumbingChoice == null) return;
            var doc = _uiApp?.ActiveUIDocument?.Document;
            var options = new List<PlumbingSourceOption> { new PlumbingSourceOption { InstanceId = ElementId.InvalidElementId, Name = S._("collisioncheck.import_choice_none") } };
            if (doc != null)
            {
                foreach (var link in CollisionCheckerHandler.GetAllLoadedRevitLinks(doc))
                {
                    var linkDoc = link.GetLinkDocument();
                    var typeName = (linkDoc?.GetElement(link.GetTypeId()) as RevitLinkType)?.Name ?? link.Name ?? link.Id.ToString();
                    if (typeName.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
                        typeName = typeName.Substring(0, typeName.Length - 4);
                    options.Add(new PlumbingSourceOption { InstanceId = link.Id, Name = typeName });
                }
            }

            _cbPlumbingChoice.ItemsSource = options;
            var savedName = _settingsData?.PlumbingLinkName ?? "";
            PlumbingSourceOption match = !string.IsNullOrEmpty(savedName)
                ? options.FirstOrDefault(o => string.Equals(o.Name, savedName, StringComparison.OrdinalIgnoreCase))
                : options.FirstOrDefault(o => o.InstanceId != ElementId.InvalidElementId
                    && PlumbingLinkNameHints.Any(hint => o.Name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0));
            _cbPlumbingChoice.SelectedItem = match ?? options[0];
        }

        // ── Hole family picker ───────────────────────────────────────────
        private StackPanel BuildHoleFamilySection()
        {
            var sp = new StackPanel();
            sp.Children.Add(SecH(S._("collisioncheck.hole_family")));

            // Side by side instead of stacked -- frees up the vertical
            // space the dropdown used to take on its own row, which goes
            // straight to the results list below (that row is Star-sized,
            // so it grows to fill whatever's left over automatically).
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var searchField = CompactField(S._("collisioncheck.search"), S._("collisioncheck.search_hint"), 150, out _tbHoleSearch);
            Grid.SetColumn(searchField, 0);
            row.Children.Add(searchField);
            _tbHoleSearch.TextChanged += (s, e) => FilterHoleSymbols(_tbHoleSearch.Text);

            // A blank spacer matching CompactField's own label height, so
            // the dropdown lines up with the search box's INPUT rather
            // than sitting a row too high (CompactField has a label above
            // its box; the dropdown has nothing above it otherwise).
            var dropdownCol = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            dropdownCol.Children.Add(new TextBlock { Text = " ", FontSize = 9.5, Margin = new Thickness(1, 0, 0, 3) });

            _cbHoleSymbol = StyledCombo();
            _cbHoleSymbol.DisplayMemberPath = "DisplayName";
            _cbHoleSymbol.ToolTip = S._("collisioncheck.hole_family_hint");
            _cbHoleSymbol.SelectionChanged += (s, e) =>
            {
                var chosen = _cbHoleSymbol.SelectedItem as HoleSymbolOption;
                if (chosen == null) return;
                _settingsData = _settingsData ?? new CollisionCheckerSettingsData();
                _settingsData.HoleFamilyName = chosen.FamilyName;
                _settingsData.HoleTypeName   = chosen.TypeName;
                CollisionCheckerSettings.Save(_settingsData);
            };
            dropdownCol.Children.Add(_cbHoleSymbol);
            Grid.SetColumn(dropdownCol, 1);
            row.Children.Add(dropdownCol);

            sp.Children.Add(row);
            RefreshHoleSymbols();
            return sp;
        }

        // Filters the combo's list to families/types whose name contains
        // the typed text (case-insensitive), keeping the current selection
        // if it still matches, and picking the first match otherwise.
        // Clearing the search box restores the full list.
        private void FilterHoleSymbols(string search)
        {
            if (_cbHoleSymbol == null) return;
            var prev = _cbHoleSymbol.SelectedItem as HoleSymbolOption;

            var filtered = string.IsNullOrWhiteSpace(search)
                ? _holeSymbols
                : _holeSymbols.Where(o => o.DisplayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            _cbHoleSymbol.ItemsSource = null;
            _cbHoleSymbol.ItemsSource = filtered;
            if (filtered.Count == 0) return;

            var match = prev != null
                ? filtered.FirstOrDefault(o => o.FamilyName == prev.FamilyName && o.TypeName == prev.TypeName)
                : null;
            _cbHoleSymbol.SelectedItem = match ?? filtered[0];
        }

        private void RefreshHoleSymbols()
        {
            var doc = _uiApp?.ActiveUIDocument?.Document;
            if (doc == null || _cbHoleSymbol == null) return;
            var prev = _cbHoleSymbol.SelectedItem as HoleSymbolOption;
            _holeSymbols = CollisionCheckerHandler.GetHoleSymbolOptions(doc);
            _cbHoleSymbol.ItemsSource = null;
            _cbHoleSymbol.ItemsSource = _holeSymbols;
            if (_holeSymbols.Count == 0) return;

            // Prefer: (1) whatever was already selected before this
            // refresh, (2) the family/type remembered from a previous
            // session, (3) the first one alphabetically.
            HoleSymbolOption match = null;
            if (prev != null)
                match = _holeSymbols.FirstOrDefault(o => o.FamilyName == prev.FamilyName && o.TypeName == prev.TypeName);
            if (match == null && _settingsData != null && !string.IsNullOrEmpty(_settingsData.HoleFamilyName))
                match = _holeSymbols.FirstOrDefault(o =>
                    string.Equals(o.FamilyName, _settingsData.HoleFamilyName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(o.TypeName, _settingsData.HoleTypeName, StringComparison.OrdinalIgnoreCase));
            _cbHoleSymbol.SelectedItem = match ?? _holeSymbols[0];
        }

        // ── Results ───────────────────────────────────────────────────────
        private void BuildResultsSectionInto(DockPanel dock)
        {
            var header = new StackPanel();
            header.Children.Add(SecH(S._("collisioncheck.results")));

            // One row instead of two -- the filter dropdown used to sit on
            // its own row above Select All/None, taking up vertical space
            // that goes straight to the results list below once freed
            // (that row is Star-sized, so it grows into whatever's left
            // over automatically).
            var selRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            var btnAll  = ActionBtn(S._("collisioncheck.select_all"),  true, () => SetAllChecked(true));
            var btnNone = ActionBtn(S._("collisioncheck.select_none"), true, () => SetAllChecked(false));
            btnAll.Margin = new Thickness(0, 0, 6, 0);
            selRow.Children.Add(btnAll);
            selRow.Children.Add(btnNone);

            var filterCombo = StyledCombo(36);
            filterCombo.Width = 150;
            filterCombo.VerticalAlignment = VerticalAlignment.Center;
            filterCombo.Margin = new Thickness(10, 0, 0, 0);
            filterCombo.Items.Add(new ComboBoxItem { Content = S._("collisioncheck.filter_all"),        Tag = ResultStatusFilter.All });
            filterCombo.Items.Add(new ComboBoxItem { Content = S._("collisioncheck.filter_notplaced"),  Tag = ResultStatusFilter.NotPlaced });
            filterCombo.Items.Add(new ComboBoxItem { Content = S._("collisioncheck.filter_placed"),     Tag = ResultStatusFilter.Placed });
            filterCombo.SelectedIndex = 0;
            filterCombo.SelectionChanged += (s, e) =>
            {
                if (filterCombo.SelectedItem is ComboBoxItem item && item.Tag is ResultStatusFilter f)
                {
                    _statusFilter = f;
                    RenderResultList();
                }
            };
            selRow.Children.Add(filterCombo);

            header.Children.Add(selRow);
            DockPanel.SetDock(header, Dock.Top);
            dock.Children.Add(header);

            var bottomBtnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            _btnPlaceHoles = ActionBtn(S._("collisioncheck.place_holes"), false, OnPlaceHolesClicked);
            bottomBtnRow.Children.Add(_btnPlaceHoles);
            // Plumbing clashes have no "hole" to place, so this is a
            // separate action rather than something PlaceHoles could ever
            // reasonably do -- a plain manual acknowledgement that
            // someone's looked at a flagged clash and rerouted whatever
            // needed rerouting. Shown alongside PlaceHoles rather than
            // only when plumbing checking is on, matching how the
            // plumbing checkbox itself is always visible too -- clicking
            // it with nothing relevant checked just shows the same
            // "nothing selected" message PlaceHoles already does.
            _btnMarkSolved = ActionBtn(S._("collisioncheck.mark_solved"), true, OnMarkSolvedClicked);
            _btnMarkSolved.Margin = new Thickness(8, 0, 0, 0);
            bottomBtnRow.Children.Add(_btnMarkSolved);
            bottomBtnRow.HorizontalAlignment = HorizontalAlignment.Left;
            DockPanel.SetDock(bottomBtnRow, Dock.Bottom);
            dock.Children.Add(bottomBtnRow);

            // Last child in the DockPanel -- with LastChildFill=true, this
            // one gets whatever space is left after the header above and
            // the button below, instead of a fixed MaxHeight that could cut
            // off the button when there are enough collisions to fill it.
            var box = new Border
            {
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5), ClipToBounds = true, Margin = new Thickness(0, 4, 0, 0),
            };
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            _resultList = new StackPanel { Margin = new Thickness(6) };
            scroll.Content = _resultList; box.Child = scroll;
            dock.Children.Add(box);

            RenderResultList();
        }

        private void OnScanClicked()
        {
            var uiDoc = _uiApp?.ActiveUIDocument;
            var doc = uiDoc?.Document;
            if (doc == null) return;

            _goToViewCache.Clear(); // model/views may have changed since the last scan -- never carry a stale answer into a new one
            UpdateStatusBar(S._("collisioncheck.scanning"));
            ElementId architectureSourceId = null;
            bool architectureSourceIsLink = false;
            if (_settingsData?.IncludeImportedArchitecture ?? false)
            {
                var chosen = _cbImportChoice?.SelectedItem as ArchitectureSourceOption;
                architectureSourceId = chosen?.InstanceId;
                architectureSourceIsLink = chosen?.IsLink ?? false;
            }
            ElementId plumbingLinkId = null;
            if (_settingsData?.IncludePlumbing ?? false)
            {
                var chosenPlumbing = _cbPlumbingChoice?.SelectedItem as PlumbingSourceOption;
                if (chosenPlumbing != null && chosenPlumbing.InstanceId != ElementId.InvalidElementId)
                    plumbingLinkId = chosenPlumbing.InstanceId;
            }
            _collisions = CollisionCheckerHandler.ScanForCollisions(doc, uiDoc, _scope, architectureSourceId, architectureSourceIsLink, (_cbHoleSymbol?.SelectedItem as HoleSymbolOption)?.SymbolId, plumbingLinkId);
            _lblSummary.Text = _collisions.Count == 0
                ? S._("collisioncheck.none_found")
                : string.Format(S._("collisioncheck.n_found"), _collisions.Count);

            RenderResultList();
            UpdateStatusBar(_lblSummary.Text);

            _handler.Request = new CollisionCheckerRequest
            {
                Action       = CollisionCheckerAction.MarkCollisions,
                Collisions   = _collisions,
                OldMarkerIds = new List<ElementId>(_markerIds),
            };
            _extEvent.Raise();

            // Not saved here -- Raise() is asynchronous, so the marking
            // this scan just triggered hasn't actually happened yet at
            // this point. HandleMarkResult saves the cache itself, once
            // marking genuinely completes and _markersByCollisionId
            // reflects reality.
            UpdateLastScannedLabel(DateTime.Now);
        }

        // HasHole only ever checked whether HoleInstanceId was set to a
        // non-null id -- never whether that id still actually resolves to
        // a live element. Confirmed as a real, reproducible case: manually
        // deleting a hole this tool placed (directly in Revit, not through
        // this window) left the in-memory _collisions entry completely
        // unaware, still reporting HasHole=true and "Hole placed" forever
        // after. doc.GetElement(id) is a fast, indexed lookup by id, not a
        // scan over the model's geometry -- checking it for every entry on
        // every render is the actual fix for "don't make me rescan just
        // because I deleted something": it catches deleted holes
        // immediately, without redoing any of the slow geometric
        // collision-detection work Scan does.
        private void RevalidateHoleReferences()
        {
            var doc = _uiApp?.ActiveUIDocument?.Document;
            if (doc == null) return;
            foreach (var c in _collisions)
            {
                if (!c.HasHole) continue;
                try
                {
                    if (doc.GetElement(c.HoleInstanceId) == null)
                        c.HoleInstanceId = Autodesk.Revit.DB.ElementId.InvalidElementId;
                }
                catch { c.HoleInstanceId = Autodesk.Revit.DB.ElementId.InvalidElementId; }
            }
        }

        private void RenderResultList()
        {
            if (_resultList == null) return;
            RevalidateHoleReferences();
            _resultList.Children.Clear();
            _rowChecks.Clear();
            _rowStatus.Clear();
            _checkedRowIds.Clear();

            if (_collisions.Count == 0)
            {
                _resultList.Children.Add(new TextBlock { Text = S._("collisioncheck.not_scanned_yet"),
                    FontSize = 11, Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(4) });
                return;
            }

            // IsResolved, not HasHole -- HasHole is permanently false for a
            // plumbing clash row (there's no "hole" for that kind of
            // finding at all), so filtering on it alone meant a plumbing
            // clash you'd already marked solved would show up under
            // "unresolved" forever, no matter what. Confirmed as a real
            // bug from a live screenshot, not a hypothetical.
            var visible = _statusFilter == ResultStatusFilter.Placed ? _collisions.Where(c => c.IsResolved).ToList()
                        : _statusFilter == ResultStatusFilter.NotPlaced ? _collisions.Where(c => !c.IsResolved).ToList()
                        : _collisions;

            if (visible.Count == 0)
            {
                _resultList.Children.Add(new TextBlock
                {
                    Text = _statusFilter == ResultStatusFilter.Placed
                        ? S._("collisioncheck.filter_none_placed")
                        : S._("collisioncheck.filter_none_notplaced"),
                    FontSize = 11, Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(4),
                    TextWrapping = TextWrapping.Wrap,
                });
                return;
            }

            var byLevel = visible
                .GroupBy(c => string.IsNullOrEmpty(c.LevelName) ? S._("collisioncheck.no_level") : c.LevelName)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var levelGroup in byLevel)
            {
                string levelKey = "L:" + levelGroup.Key;
                bool levelExpanded = _expandedGroups.Contains(levelKey);
                _resultList.Children.Add(BuildGroupHeader($"{levelGroup.Key}  ({levelGroup.Count()})", levelExpanded, () => ToggleGroup(levelKey)));

                var levelContent = new StackPanel { Visibility = levelExpanded ? Visibility.Visible : Visibility.Collapsed, Margin = new Thickness(16, 2, 0, 4) };

                var byCategory = levelGroup.GroupBy(c => c.ElementCategory).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
                foreach (var catGroup in byCategory)
                {
                    string catKey = levelKey + "|C:" + catGroup.Key;
                    bool catExpanded = _expandedGroups.Contains(catKey);
                    levelContent.Children.Add(BuildGroupHeader($"{catGroup.Key}  ({catGroup.Count()})", catExpanded, () => ToggleGroup(catKey)));

                    var catContent = new StackPanel { Visibility = catExpanded ? Visibility.Visible : Visibility.Collapsed, Margin = new Thickness(16, 2, 0, 4) };
                    foreach (var c in catGroup)
                        catContent.Children.Add(BuildCollisionRow(c));
                    levelContent.Children.Add(catContent);
                }

                _resultList.Children.Add(levelContent);
            }
        }

        // Expand state is keyed by a path string ("L:<level>" or
        // "L:<level>|C:<category>") rather than an index, so it survives a
        // full RenderResultList() rebuild (e.g. after a re-scan) as long as
        // the same level/category names still exist.
        private void ToggleGroup(string key)
        {
            if (!_expandedGroups.Remove(key)) _expandedGroups.Add(key);
            RenderResultList();
        }

        private Border BuildGroupHeader(string text, bool expanded, Action onClick)
        {
            var border = new Border
            {
                Background = MeToolsTheme.BrSurface, CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 2, 0, 2), Cursor = Cursors.Hand,
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = expanded ? "\u25BC" : "\u25B6", FontSize = 9,
                Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = MeToolsTheme.BrText });
            border.Child = row;
            border.MouseLeftButtonUp += (s, e) => onClick();
            return border;
        }

        private Grid BuildCollisionRow(CollisionInfo c)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Plumbing clashes have no "hole to place" the way a wall
            // crossing does, but a person can still mark one as manually
            // handled once they've rerouted whatever needed rerouting --
            // the checkbox here feeds "Mark Selected as Solved" instead of
            // "Place Holes for Selected", disabled once IsSolved the same
            // way a wall-crossing row's checkbox disables once HasHole.
            var cb = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), IsEnabled = !c.IsResolved };
            cb.Checked   += (s, e) => _checkedRowIds.Add(c.Id);
            cb.Unchecked += (s, e) => _checkedRowIds.Remove(c.Id);
            Grid.SetColumn(cb, 0); row.Children.Add(cb);
            _rowChecks[c.Id] = cb;

            // Level and category are now conveyed by the group headers
            // above this row, so the row itself only needs to say which
            // specific type crossed which specific wall -- or, for a
            // plumbing clash, which specific type overlaps which kind of
            // plumbing element.
            var info = new TextBlock
            {
                Text = c.Kind == CollisionKind.PlumbingClash
                    ? $"\"{c.ElementTypeName}\"  \u2192  {c.PlumbingElementDescription}"
                    : $"\"{c.ElementTypeName}\"  \u2192  {c.WallTypeName}",
                FontSize = 11, Foreground = MeToolsTheme.BrText,
                VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetColumn(info, 1); row.Children.Add(info);

            var status = new TextBlock
            {
                Text = c.Kind == CollisionKind.PlumbingClash
                    ? (c.IsSolved ? S._("collisioncheck.plumbing_solved_label") : S._("collisioncheck.plumbing_clash_label"))
                    : (c.HasHole ? S._("collisioncheck.hole_placed") : ""),
                FontSize = 10,
                Foreground = c.Kind == CollisionKind.PlumbingClash
                    ? (c.IsSolved ? MeToolsTheme.BrAccent : MeToolsTheme.Br(MeToolsTheme.COrange))
                    : MeToolsTheme.BrAccent,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0),
            };
            Grid.SetColumn(status, 2); row.Children.Add(status);
            _rowStatus[c.Id] = status;

            var btnGo = ActionBtn(S._("collisioncheck.go_to"), true, () => OnGoToClicked(c));
            btnGo.Padding = new Thickness(10, 0, 10, 0);
            Grid.SetColumn(btnGo, 3); row.Children.Add(btnGo);

            var btnGo3D = ActionBtn(S._("collisioncheck.go_to_3d"), false, () => OnGoTo3DClicked(c));
            btnGo3D.Padding = new Thickness(10, 0, 10, 0);
            btnGo3D.Margin = new Thickness(4, 0, 0, 0);
            Grid.SetColumn(btnGo3D, 4); row.Children.Add(btnGo3D);

            return row;
        }

        private void SetAllChecked(bool value)
        {
            foreach (var kv in _rowChecks)
            {
                if (!kv.Value.IsEnabled) continue; // already has a hole -- nothing to select
                kv.Value.IsChecked = value;
            }
        }

        // Selects the run in Revit and asks Revit to show it -- switches to
        // an appropriate view and zooms, the same as right-click "Show
        // Element(s) in View" does. Pure UI navigation, no document
        // modification, so this runs directly rather than through the
        // ExternalEvent (matches the established convention elsewhere in
        // this app for Selection calls).
        private void OnGoToClicked(CollisionInfo c)
        {
            var uiDoc = _uiApp?.ActiveUIDocument;
            if (uiDoc == null || c.Point == null) return;
            var doc = uiDoc.Document;
            try
            {
                // Switch to a plan view on the collision's own level first,
                // if the active view isn't already on that level --
                // otherwise zooming just pans/zooms whatever's currently
                // open, which may not show this level's geometry at all.
                // The active view only wins the tie-break if it actually
                // SHOWS the run (mustShowElementId) -- confirmed live that
                // "same Level" alone isn't enough: a Mechanical/Heating
                // coordination plan and an Electrical plan can share a
                // Level while one of them hides Cable Trays entirely via
                // discipline filtering. Without that check, staying on a
                // technically-valid-but-wrong-discipline view was exactly
                // what sent "Go To" to a point with nothing visible there.
                //
                // Cached by (level, run category, active view) rather than
                // calling FindPlanViewForLevel fresh every click -- that
                // visibility check is a real cost (a view-scoped
                // FilteredElementCollector), and on a real project the
                // overwhelming majority of collisions share just a
                // category or two, so the answer for THIS run is almost
                // always identical to the last one already computed. This
                // does assume visibility is driven by category rather
                // than a one-off "Hide Element" on this specific instance
                // -- true in every case seen so far, but if a specific row
                // ever behaves oddly after this, that's the first thing to
                // suspect.
                var runEl = doc.GetElement(c.ElementId);
                var activeViewId = uiDoc.ActiveView?.Id;
                var cacheKey = (
                    (int)(c.LevelId?.Value ?? -1),
                    (int)(runEl?.Category?.Id?.Value ?? -1),
                    (int)(activeViewId?.Value ?? -1));

                View targetView;
                if (!_goToViewCache.TryGetValue(cacheKey, out targetView))
                {
                    targetView = CollisionCheckerHandler.FindPlanViewForLevel(doc, c.LevelId, activeViewId, c.ElementId);
                    _goToViewCache[cacheKey] = targetView;
                }

                if (targetView != null && targetView.Id != uiDoc.ActiveView?.Id)
                {
                    try { uiDoc.ActiveView = targetView; } catch { }
                }

                var idToSelect = c.HasHole ? c.HoleInstanceId : c.ElementId;
                uiDoc.Selection.SetElementIds(new List<ElementId> { idToSelect });

                // ShowElements() fits the WHOLE run element, which is why
                // it used to jump out to show the entire cable tray/conduit
                // instead of the specific spot. Zooming a tight box around
                // the actual collision point instead keeps the crossing
                // itself centered and legible regardless of how long the
                // run is.
                var view = uiDoc.ActiveView;
                var uiView = uiDoc.GetOpenUIViews().FirstOrDefault(uv => uv.ViewId.Equals(view.Id));
                if (uiView != null)
                {
                    double half = 3.0; // ~0.9m half-extent -- a tight, legible close-up
                    var p = c.Point;
                    uiView.ZoomAndCenterRectangle(
                        new XYZ(p.X - half, p.Y - half, p.Z - half),
                        new XYZ(p.X + half, p.Y + half, p.Z + half));
                }
            }
            catch { }
        }

        // Replicates Revit's own "Selection Box" behavior (View panel ->
        // Selection Box, or right-click on a selection) rather than just
        // switching to the 3D view and showing the whole model -- a
        // section box cropped tightly around the clash point, the same
        // "here's just the little section that matters" result you'd get
        // clicking that button yourself. Reuses the same default 3D view
        // every time (matching how Revit's own Selection Box behaves when
        // you're not already in a 3D view: it reuses/creates ONE default
        // 3D view and adjusts ITS box), so repeated clicks update the same
        // view's crop rather than piling up new views.
        //
        // Setting the section box is a document change, so it has to go
        // through the ExternalEvent just like every other document-
        // modifying action in this window -- confirmed live as a real bug
        // when this used to run the transaction directly here instead:
        // Revit's own API reported the UI as blocked right after, and the
        // button would spin for a moment then silently do nothing.
        // HandleFrame3DResult below does the actual view-switch/select/
        // zoom once this genuinely completes.
        private void OnGoTo3DClicked(CollisionInfo c)
        {
            var doc = _uiApp?.ActiveUIDocument?.Document;
            if (doc == null || c.Point == null) return;

            _handler.Request = new CollisionCheckerRequest
            {
                Action     = CollisionCheckerAction.Frame3D,
                Collisions = new List<CollisionInfo> { c },
            };
            _extEvent.Raise();
        }


        // Moved to CollisionCheckerHandler (as FindPlanViewForLevel) so the
        // mark-drawing code can use the exact same lookup "Go To" does --
        // a mark is now guaranteed to land in the same view "Go To" opens.

        // Removes just the circle for one specific collision, immediately
        // after its hole is placed, rather than waiting for the next Scan
        // to redraw everything. Runs in its own small transaction -- called
        // from HandlePlaceResult, which itself runs via Dispatcher.Invoke
        // from inside the ExternalEvent's Execute(), i.e. still within a
        // valid API context on the same thread.
        private void RemoveMarkerFor(Document doc, string collisionId)
        {
            if (doc == null || !_markersByCollisionId.TryGetValue(collisionId, out var ids) || ids.Count == 0) return;
            try
            {
                using (var tx = new Transaction(doc, "ME-Tools: Clear resolved collision mark"))
                {
                    tx.Start();
                    try { doc.Delete(ids); } catch { }
                    if (tx.GetStatus() == TransactionStatus.Started) tx.Commit();
                }
            }
            catch { }
            finally
            {
                foreach (var id in ids) _markerIds.Remove(id);
                _markersByCollisionId.Remove(collisionId);
            }
        }

        // ── Place holes ───────────────────────────────────────────────────
        private void OnPlaceHolesClicked()
        {
            var symbol = _cbHoleSymbol?.SelectedItem as HoleSymbolOption;
            if (symbol == null)
            {
                MessageBox.Show(S._("collisioncheck.pick_family_first"), S._("collisioncheck.title"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            // Kind != PlumbingClash is a deliberate, explicit guard, not
            // just relying on !HasHole -- a plumbing clash row can now be
            // checked too (its checkbox feeds Mark Solved instead), so
            // without this a checked plumbing row would otherwise be
            // sent into a hole-placement request that makes no sense for
            // it.
            var selected = _collisions.Where(c => _checkedRowIds.Contains(c.Id) && !c.HasHole && c.Kind != CollisionKind.PlumbingClash).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(S._("collisioncheck.nothing_selected"), S._("collisioncheck.title"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _handler.Request = new CollisionCheckerRequest
            {
                Action       = CollisionCheckerAction.PlaceHoles,
                Collisions   = selected,
                HoleSymbolId = symbol.SymbolId,
            };
            _extEvent.Raise();
            UpdateStatusBar(S._("collisioncheck.placing"));
        }

        private void OnMarkSolvedClicked()
        {
            var selected = _collisions.Where(c => _checkedRowIds.Contains(c.Id) && c.Kind == CollisionKind.PlumbingClash && !c.IsSolved).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(S._("collisioncheck.nothing_selected"), S._("collisioncheck.title"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _handler.Request = new CollisionCheckerRequest
            {
                Action     = CollisionCheckerAction.MarkPlumbingSolved,
                Collisions = selected,
            };
            _extEvent.Raise();
            UpdateStatusBar(S._("collisioncheck.marking_solved"));
        }

        // Merges newly-drawn markers into this window's own tracking
        // dictionaries (needed later by RemoveMarkerFor when a hole gets
        // placed for one of them), and surfaces any failure via a popup --
        // not the status bar, which gets silently overwritten by whatever
        // happens next and was why the diagnostic went unseen before.
        private void HandleMarkResult(PlaceHolesResult result)
        {
            if (result == null) return;
            _markerIds.Clear();
            _markersByCollisionId.Clear();
            foreach (var kv in result.MarkersByCollisionId)
            {
                _markersByCollisionId[kv.Key] = kv.Value;
                _markerIds.AddRange(kv.Value);
            }

            // Saved HERE, not at the point Scan raises the mark request --
            // _extEvent.Raise() is asynchronous, so _markersByCollisionId
            // wasn't actually populated yet at that point. This is the
            // first moment after marking where the cached state (which
            // rows exist, which markers they each have) is genuinely
            // consistent with what's actually drawn in the document.
            var doc = _uiApp?.ActiveUIDocument?.Document;
            if (doc != null) CollisionCheckerWatcher.SaveScanResults(doc, _collisions, _markersByCollisionId);

            if (result.MarksFailed > 0)
                MessageBox.Show(
                    string.Format(S._("collisioncheck.mark_failed"), result.MarksFailed, result.MarksAttempted, result.FirstMarkError),
                    S._("collisioncheck.title"), MessageBoxButton.OK, MessageBoxImage.Warning);

            if (result.MarksSkippedNoView > 0)
                MessageBox.Show(
                    string.Format(S._("collisioncheck.mark_skipped_no_view"), result.MarksSkippedNoView),
                    S._("collisioncheck.title"), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void HandlePlaceResult(PlaceHolesResult result)
        {
            if (result == null) return;
            var doc = _uiApp?.ActiveUIDocument?.Document;

            foreach (var kv in result.PlacedHoleByRowId)
            {
                var c = _collisions.FirstOrDefault(x => x.Id == kv.Key);
                if (c == null) continue;
                c.HoleInstanceId = kv.Value;

                // Tell the watcher about the new link right away, so a move
                // immediately after placing is still caught -- otherwise it
                // would only be picked up the next time this document opens.
                if (doc != null)
                {
                    try
                    {
                        var runEl  = doc.GetElement(c.ElementId);
                        var wallEl = doc.GetElement(c.WallId);
                        var holeEl = doc.GetElement(c.HoleInstanceId);
                        if (runEl != null && wallEl != null && holeEl != null)
                            CollisionCheckerWatcher.NotifyHoleLinked(doc, holeEl.UniqueId, runEl.UniqueId, wallEl.UniqueId);
                    }
                    catch { }
                }

                if (_rowChecks.TryGetValue(c.Id, out var cb)) { cb.IsChecked = false; cb.IsEnabled = false; }
                if (_rowStatus.TryGetValue(c.Id, out var st)) st.Text = S._("collisioncheck.hole_placed");
                _checkedRowIds.Remove(c.Id);

                RemoveMarkerFor(doc, c.Id);
            }

            // Rows that specifically failed (not just skipped) show their
            // exact error message right in the list, not just a total
            // count -- needed to actually diagnose a placement failure
            // instead of guessing at it blind.
            foreach (var kv in result.ErrorByRowId)
            {
                if (_rowStatus.TryGetValue(kv.Key, out var st))
                {
                    st.Text = S._("collisioncheck.error_prefix") + " " + kv.Value;
                    st.Foreground = MeToolsTheme.Br(MeToolsTheme.CRed);
                    st.ToolTip = kv.Value;
                }
            }

            var summary = string.Format(S._("collisioncheck.placed_summary"), result.Placed);
            if (result.Skipped > 0) summary += string.Format(S._("collisioncheck.n_skipped"), result.Skipped);
            if (result.Errors  > 0) summary += string.Format(S._("collisioncheck.n_errors"), result.Errors);
            UpdateStatusBar(summary);
        }

        // MarkPlumbingSolved's own equivalent of HandlePlaceResult -- no
        // watcher notification needed (there's no hole element for a live-
        // follow watcher to track a move on), just flipping IsSolved on
        // the matching in-memory rows and their UI so the list reflects
        // the change immediately, without a re-scan.
        private void HandleSolvedResult(PlaceHolesResult result)
        {
            if (result == null) return;
            var doc = _uiApp?.ActiveUIDocument?.Document;

            foreach (var rowId in result.SolvedRowIds)
            {
                var c = _collisions.FirstOrDefault(x => x.Id == rowId);
                if (c == null) continue;
                c.IsSolved = true;

                if (_rowChecks.TryGetValue(c.Id, out var cb)) { cb.IsChecked = false; cb.IsEnabled = false; }
                if (_rowStatus.TryGetValue(c.Id, out var st))
                {
                    st.Text = S._("collisioncheck.plumbing_solved_label");
                    st.Foreground = MeToolsTheme.BrAccent;
                }
                _checkedRowIds.Remove(c.Id);

                RemoveMarkerFor(doc, c.Id);
            }

            foreach (var kv in result.ErrorByRowId)
            {
                if (_rowStatus.TryGetValue(kv.Key, out var st))
                {
                    st.Text = S._("collisioncheck.error_prefix") + " " + kv.Value;
                    st.Foreground = MeToolsTheme.Br(MeToolsTheme.CRed);
                    st.ToolTip = kv.Value;
                }
            }

            var summary = string.Format(S._("collisioncheck.solved_summary"), result.Placed);
            if (result.Errors > 0) summary += string.Format(S._("collisioncheck.n_errors"), result.Errors);
            UpdateStatusBar(summary);
        }

        // Runs after ExecuteFrame3D genuinely completes -- the section
        // box itself was already set inside the transaction there; this
        // is just the view-switch/select/zoom, none of which are document
        // changes, so they belong here rather than in the handler.
        private void HandleFrame3DResult(PlaceHolesResult result)
        {
            var uiDoc = _uiApp?.ActiveUIDocument;
            if (uiDoc == null || result == null) return;

            if (result.Frame3DViewId == null || result.Frame3DViewId == ElementId.InvalidElementId)
            {
                MessageBox.Show(S._("collisioncheck.no_3d_view"), S._("collisioncheck.title"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var view3D = uiDoc.Document.GetElement(result.Frame3DViewId) as View;
                if (view3D != null && view3D.Id != uiDoc.ActiveView?.Id)
                {
                    try { uiDoc.ActiveView = view3D; } catch { }
                }

                if (result.Frame3DElementId != null && result.Frame3DElementId != ElementId.InvalidElementId)
                    uiDoc.Selection.SetElementIds(new List<ElementId> { result.Frame3DElementId });

                var uiView = uiDoc.GetOpenUIViews().FirstOrDefault(uv => uv.ViewId.Equals(result.Frame3DViewId));
                uiView?.ZoomToFit();
            }
            catch { }
        }
    }
}
