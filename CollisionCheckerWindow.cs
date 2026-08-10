// CollisionCheckerWindow.cs -- ME-Tools | Collision Checker (conduits/cable trays vs walls)
// Mayer E-Concept SRL -- Pure C# WPF, no XAML
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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

        private ComboBox _cbHoleSymbol;
        private List<HoleSymbolOption> _holeSymbols = new List<HoleSymbolOption>();

        private List<CollisionInfo> _collisions = new List<CollisionInfo>();
        private readonly List<ElementId> _markerIds = new List<ElementId>();
        private StackPanel _resultList;
        private readonly HashSet<string> _checkedRowIds = new HashSet<string>();
        private readonly Dictionary<string, CheckBox>  _rowChecks = new Dictionary<string, CheckBox>();
        private readonly Dictionary<string, TextBlock> _rowStatus = new Dictionary<string, TextBlock>();

        private Button _btnPlaceHoles;

        public CollisionCheckerWindow(UIApplication uiApp, ExternalEvent extEvent, CollisionCheckerHandler handler)
        {
            _uiApp = uiApp; _extEvent = extEvent; _handler = handler;
            S.SetLanguage(SettingsStore.Language ?? "en");
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
            _cbHoleSymbol = StyledCombo();
            _cbHoleSymbol.DisplayMemberPath = "DisplayName";
            _cbHoleSymbol.ToolTip = S._("collisioncheck.hole_family_hint");
            sp.Children.Add(_cbHoleSymbol);
            RefreshHoleSymbols();
            return sp;
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
            var match = prev != null
                ? _holeSymbols.FirstOrDefault(o => o.FamilyName == prev.FamilyName && o.TypeName == prev.TypeName)
                : null;
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

            foreach (var c in _collisions)
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

                var levelText = string.IsNullOrEmpty(c.LevelName) ? S._("collisioncheck.no_level") : c.LevelName;
                var info = new TextBlock
                {
                    Text = $"{c.ElementCategory} \"{c.ElementTypeName}\"  \u2192  {c.WallTypeName}  \u2014  {levelText}",
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

                _resultList.Children.Add(row);
            }
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
            if (uiDoc == null) return;
            try
            {
                var ids = new List<ElementId> { c.ElementId };
                uiDoc.Selection.SetElementIds(ids);
                uiDoc.ShowElements(c.ElementId);
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
                    }

                    // Also clear any leftover element-level override from an
                    // earlier version of this tool, in case one is still on
                    // a run from before.
                    var clear = new OverrideGraphicSettings();
                    foreach (var c in _collisions)
                    {
                        try { view.SetElementOverrides(c.ElementId, clear); }
                        catch { }
                    }

                    // Detail Lines are a 2D, view-specific annotation and
                    // aren't supported in 3D views -- skip drawing marks
                    // there rather than throwing on every single one.
                    if (!(view is View3D))
                    {
                        var red = new Autodesk.Revit.DB.Color(226, 42, 42);
                        var ogs = new OverrideGraphicSettings();
                        try { ogs.SetProjectionLineColor(red); ogs.SetProjectionLineWeight(7); } catch { }

                        double armFt = 300.0 / 304.8; // ~300mm, a visible size regardless of view scale
                        XYZ right = view.RightDirection;
                        XYZ up    = view.UpDirection;

                        foreach (var c in _collisions)
                        {
                            if (c.HasHole || c.Point == null) continue;
                            try
                            {
                                var p = c.Point;
                                var rightArm = right.Multiply(armFt);
                                var upArm    = up.Multiply(armFt);
                                var line1 = Line.CreateBound(p - rightArm - upArm, p + rightArm + upArm);
                                var line2 = Line.CreateBound(p - rightArm + upArm, p + rightArm - upArm);
                                var dc1 = doc.Create.NewDetailCurve(view, line1);
                                var dc2 = doc.Create.NewDetailCurve(view, line2);
                                view.SetElementOverrides(dc1.Id, ogs);
                                view.SetElementOverrides(dc2.Id, ogs);
                                _markerIds.Add(dc1.Id);
                                _markerIds.Add(dc2.Id);
                            }
                            catch { }
                        }
                    }

                    if (tx.GetStatus() == TransactionStatus.Started) tx.Commit();
                }
            }
            catch { }
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
            if (result.Placed > 0) summary += " " + S._("collisioncheck.rescan_to_refresh_marks");
            UpdateStatusBar(summary);
        }
    }
}
