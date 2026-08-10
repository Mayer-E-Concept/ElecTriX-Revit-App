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
        private Button _btnScopeModel, _btnScopeView, _btnScopeSel;
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
        private readonly HashSet<string> _checkedRowIds = new HashSet<string>();
        private readonly Dictionary<string, CheckBox>  _rowChecks = new Dictionary<string, CheckBox>();
        private readonly Dictionary<string, TextBlock> _rowStatus = new Dictionary<string, TextBlock>();
        private readonly HashSet<string> _expandedGroups = new HashSet<string>();

        private Button _btnPlaceHoles;

        public CollisionCheckerWindow(UIApplication uiApp, ExternalEvent extEvent, CollisionCheckerHandler handler)
        {
            _uiApp = uiApp; _extEvent = extEvent; _handler = handler;
            S.SetLanguage(SettingsStore.Language ?? "en");
            _settingsData = CollisionCheckerSettings.Load();
            InitWindow(S._("collisioncheck.title"), 660);
            MaxHeight = Math.Min(780, SystemParameters.WorkArea.Height - 60);
            WireHandler();
            Build();
        }

        private void WireHandler()
        {
            _handler.OnStatus = msg => Dispatcher.Invoke(() => UpdateStatusBar(msg));
            _handler.OnDone   = result => Dispatcher.Invoke(() => HandlePlaceResult(result));
        }

        // ── Build ─────────────────────────────────────────────────────────
        private void Build()
        {
            BuildStatusBar(S._("collisioncheck.ready"));

            var contentGrid = new Grid { Background = MeToolsTheme.BrBg };
            contentGrid.Children.Add(Watermark());
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = System.Windows.Media.Brushes.Transparent,
                Padding    = new Thickness(16, 12, 16, 10),
            };
            var outer = new StackPanel();

            outer.Children.Add(InfoBox(S._("collisioncheck.intro_hint")));
            outer.Children.Add(BuildScopeSection());
            outer.Children.Add(Div());
            outer.Children.Add(BuildHoleFamilySection());
            outer.Children.Add(Div());
            outer.Children.Add(BuildResultsSection());

            scroll.Content = outer;
            contentGrid.Children.Add(scroll);
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

        // ── Hole family picker ───────────────────────────────────────────
        private StackPanel BuildHoleFamilySection()
        {
            var sp = new StackPanel();
            sp.Children.Add(SecH(S._("collisioncheck.hole_family")));

            sp.Children.Add(CompactField(S._("collisioncheck.search"), S._("collisioncheck.search_hint"), 220, out _tbHoleSearch));
            _tbHoleSearch.TextChanged += (s, e) => FilterHoleSymbols(_tbHoleSearch.Text);

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
            sp.Children.Add(_cbHoleSymbol);
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
        private StackPanel BuildResultsSection()
        {
            var sp = new StackPanel();
            sp.Children.Add(SecH(S._("collisioncheck.results")));

            var selRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            var btnAll  = ActionBtn(S._("collisioncheck.select_all"),  true, () => SetAllChecked(true));
            var btnNone = ActionBtn(S._("collisioncheck.select_none"), true, () => SetAllChecked(false));
            btnAll.Margin = new Thickness(0, 0, 6, 0);
            selRow.Children.Add(btnAll);
            selRow.Children.Add(btnNone);
            sp.Children.Add(selRow);

            var box = new Border
            {
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5), MinHeight = 100, MaxHeight = 320, ClipToBounds = true,
            };
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            _resultList = new StackPanel { Margin = new Thickness(6) };
            scroll.Content = _resultList; box.Child = scroll;
            sp.Children.Add(box);

            sp.Children.Add(Div());
            _btnPlaceHoles = ActionBtn(S._("collisioncheck.place_holes"), false, OnPlaceHolesClicked);
            _btnPlaceHoles.HorizontalAlignment = HorizontalAlignment.Left;
            sp.Children.Add(_btnPlaceHoles);

            RenderResultList();
            return sp;
        }

        private void OnScanClicked()
        {
            var uiDoc = _uiApp?.ActiveUIDocument;
            var doc = uiDoc?.Document;
            if (doc == null) return;

            UpdateStatusBar(S._("collisioncheck.scanning"));
            _collisions = CollisionCheckerHandler.ScanForCollisions(doc, uiDoc, _scope);
            _lblSummary.Text = _collisions.Count == 0
                ? S._("collisioncheck.none_found")
                : string.Format(S._("collisioncheck.n_found"), _collisions.Count);

            RenderResultList();
            HighlightCollisions(doc, uiDoc.ActiveView);
            UpdateStatusBar(_lblSummary.Text);
            CollisionCheckerWatcher.SaveScanResults(doc, _collisions);
            UpdateLastScannedLabel(DateTime.Now);
            Dispatcher.BeginInvoke(new Action(ResizeToFitContent), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void RenderResultList()
        {
            if (_resultList == null) return;
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

            var byLevel = _collisions
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

            var cb = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), IsEnabled = !c.HasHole };
            cb.Checked   += (s, e) => _checkedRowIds.Add(c.Id);
            cb.Unchecked += (s, e) => _checkedRowIds.Remove(c.Id);
            Grid.SetColumn(cb, 0); row.Children.Add(cb);
            _rowChecks[c.Id] = cb;

            // Level and category are now conveyed by the group headers
            // above this row, so the row itself only needs to say which
            // specific type crossed which specific wall.
            var info = new TextBlock
            {
                Text = $"\"{c.ElementTypeName}\"  \u2192  {c.WallTypeName}",
                FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetColumn(info, 1); row.Children.Add(info);

            var status = new TextBlock
            {
                Text = c.HasHole ? S._("collisioncheck.hole_placed") : "",
                FontSize = 10, Foreground = MeToolsTheme.BrPetrol, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0),
            };
            Grid.SetColumn(status, 2); row.Children.Add(status);
            _rowStatus[c.Id] = status;

            var btnGo = ActionBtn(S._("collisioncheck.go_to"), true, () => OnGoToClicked(c));
            btnGo.Padding = new Thickness(10, 0, 10, 0);
            Grid.SetColumn(btnGo, 3); row.Children.Add(btnGo);

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
            try
            {
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

        // Marks every collision that doesn't have a hole yet in red, in
        // whatever view is currently active -- a graphic override on the
        // conduit/cable tray itself, not a separate marker element. Runs
        // directly (lightweight transaction from a click handler), matching
        // Circuit Tagger's established SetPendingMark convention for
        // graphic overrides -- this is display metadata, not model data,
        // and safely re-appliable on every Scan.
        private void HighlightCollisions(Document doc, View view)
        {
            if (view == null || doc == null) return;
            try
            {
                using (var tx = new Transaction(doc, "ME-Tools: Mark collisions"))
                {
                    tx.Start();

                    // Clear this window's own markers from a previous Scan.
                    if (_markerIds.Count > 0)
                    {
                        try { doc.Delete(_markerIds); } catch { }
                        _markerIds.Clear();
                        _markersByCollisionId.Clear();
                    }

                    // Detail Lines are a 2D, view-specific annotation and
                    // aren't supported in 3D views -- skip drawing marks
                    // there rather than throwing on every single one.
                    if (!(view is View3D))
                    {
                        // The actual bug that made nothing ever appear:
                        // NewDetailCurve throws "Curve must be in the
                        // plane" (a documented, deliberate Revit behavior)
                        // unless the curve lies EXACTLY on the view's own
                        // sketch plane -- the collision point's real 3D
                        // height essentially never matches that exactly, so
                        // every single creation attempt was throwing and
                        // being silently swallowed by the catch below.
                        Plane viewPlane = null;
                        try { viewPlane = view.SketchPlane?.GetPlane(); } catch { }

                        var red = new Autodesk.Revit.DB.Color(226, 42, 42);
                        var ogs = new OverrideGraphicSettings();
                        try { ogs.SetProjectionLineColor(red); ogs.SetProjectionLineWeight(7); } catch { }

                        double radiusFt = 250.0 / 304.8; // ~250mm radius -- visible regardless of view scale

                        foreach (var c in _collisions)
                        {
                            if (c.HasHole || c.Point == null) continue;
                            try
                            {
                                XYZ center; XYZ xAxis, yAxis;
                                if (viewPlane != null)
                                {
                                    // Project onto the view's plane: remove
                                    // whatever component of the point sits
                                    // along the plane's own normal.
                                    var offset = c.Point - viewPlane.Origin;
                                    var alongNormal = offset.DotProduct(viewPlane.Normal);
                                    center = c.Point - viewPlane.Normal.Multiply(alongNormal);
                                    xAxis = viewPlane.XVec;
                                    yAxis = viewPlane.YVec;
                                }
                                else
                                {
                                    center = c.Point;
                                    xAxis = view.RightDirection;
                                    yAxis = view.UpDirection;
                                }

                                // A circle, as two half-circle arcs (Revit
                                // detail curves can't be a single closed
                                // loop) on a plane centered exactly at the
                                // (projected) collision point.
                                var centeredPlane = Plane.CreateByOriginAndBasis(center, xAxis, yAxis);
                                var arc1 = Arc.Create(centeredPlane, radiusFt, 0, Math.PI);
                                var arc2 = Arc.Create(centeredPlane, radiusFt, Math.PI, 2 * Math.PI);
                                var dc1 = doc.Create.NewDetailCurve(view, arc1);
                                var dc2 = doc.Create.NewDetailCurve(view, arc2);
                                view.SetElementOverrides(dc1.Id, ogs);
                                view.SetElementOverrides(dc2.Id, ogs);

                                _markerIds.Add(dc1.Id);
                                _markerIds.Add(dc2.Id);
                                if (!_markersByCollisionId.TryGetValue(c.Id, out var list))
                                {
                                    list = new List<ElementId>();
                                    _markersByCollisionId[c.Id] = list;
                                }
                                list.Add(dc1.Id);
                                list.Add(dc2.Id);
                            }
                            catch { }
                        }
                    }

                    if (tx.GetStatus() == TransactionStatus.Started) tx.Commit();
                }
            }
            catch { }
        }

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
            var selected = _collisions.Where(c => _checkedRowIds.Contains(c.Id) && !c.HasHole).ToList();
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
    }
}
