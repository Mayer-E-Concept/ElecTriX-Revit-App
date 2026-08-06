// BatchParamsWindow.cs -- ME-Tools | Batch Params (Renumber + Bulk Edit)
// Mayer E-Concept SRL -- Pure C# WPF, no XAML
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Color      = System.Windows.Media.Color;
using ComboBox   = System.Windows.Controls.ComboBox;
using Grid       = System.Windows.Controls.Grid;
using TextBox    = System.Windows.Controls.TextBox;

namespace METools.BatchParams
{
    public class BatchParamsWindow : METools.MeToolsWindowBase
    {
        protected override string AppKey => "BatchParams";

        private readonly UIApplication      _uiApp;
        private readonly ExternalEvent      _extEvent;
        private readonly BatchParamsHandler _handler;

        // -- Shared filter state (scope + category) -------------------------
        private ElementScope _scope = ElementScope.CurrentSelection;
        private Button _btnScopeSel, _btnScopeView, _btnScopeModel;
        private StackPanel _categoryList;
        private readonly List<CheckBox> _categoryChecks = new List<CheckBox>();
        private List<Element> _scannedElements = new List<Element>();  // raw, before category filter
        private List<Element> _matchedElements = new List<Element>();  // after category filter -- the working set
        private TextBlock _lblMatchCount;
        private List<ParamOption> _paramOptions = new List<ParamOption>();

        // -- Tabs -------------------------------------------------------------
        private Button _tabRenumber, _tabBulk;
        private StackPanel _panRenumber, _panBulk;

        // -- Renumber tab -----------------------------------------------------
        private ComboBox _cbRenumberParam;
        private TextBox  _tbPrefix, _tbSuffix, _tbStart, _tbStep, _tbPadding;
        private TextBlock _lblPreview;
        private RenumberOrderMode _orderMode = RenumberOrderMode.Manual;
        private Button _btnOrderManual, _btnOrderPath;
        private StackPanel _panManual, _panPath;
        private readonly List<ElementId> _manualOrder = new List<ElementId>();
        private TextBlock _lblManualCount;
        private ElementId _pathCurveId = ElementId.InvalidElementId;
        private TextBlock _lblPathStatus;

        // -- Bulk Edit tab -----------------------------------------------------
        private ComboBox _cbBulkParam;
        private Button _btnActPrefix, _btnActSuffix, _btnActReplace, _btnActSet, _btnActClear;
        private BulkEditAction _bulkAction = BulkEditAction.AddPrefix;
        private TextBox _tbBulkPrefix, _tbBulkSuffix, _tbFind, _tbReplace, _tbSetValue, _tbValueFilter;
        private StackPanel _panBulkPrefix, _panBulkSuffix, _panBulkReplace, _panBulkSet;

        public BatchParamsWindow(UIApplication uiApp, ExternalEvent extEvent, BatchParamsHandler handler)
        {
            _uiApp = uiApp; _extEvent = extEvent; _handler = handler;
            S.SetLanguage(SettingsStore.Language ?? "en");
            InitWindow(S._("batchparams.title"), 600);
            MaxHeight = Math.Min(760, SystemParameters.WorkArea.Height - 60);
            WireHandler();
            Build();
            RunScope();
        }

        private void WireHandler()
        {
            _handler.OnStatus = msg => Dispatcher.Invoke(() => UpdateStatusBar(msg));
            _handler.OnDone   = _   => Dispatcher.Invoke(() => { });
        }

        // ── Build ─────────────────────────────────────────────────────────
        private void Build()
        {
            BuildStatusBar(S._("batchparams.ready"));

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

            outer.Children.Add(InfoBox(S._("batchparams.intro_hint")));
            outer.Children.Add(BuildFilterSection());
            outer.Children.Add(Div());
            outer.Children.Add(BuildTabRow());
            outer.Children.Add(_panRenumber);
            outer.Children.Add(_panBulk);

            scroll.Content = outer;
            contentGrid.Children.Add(scroll);
            RootDock.Children.Add(contentGrid);

            ShowRenumberTab();
        }

        // ── Shared filter section: scope + category checklist + Scan ──────
        private StackPanel BuildFilterSection()
        {
            var sp = new StackPanel();
            sp.Children.Add(SecH(S._("batchparams.elements")));

            var scopeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            _btnScopeSel   = ToggleBtn(S._("batchparams.scope_selection"), true,  () => SetScope(ElementScope.CurrentSelection));
            _btnScopeView  = ToggleBtn(S._("batchparams.scope_view"),      false, () => SetScope(ElementScope.ActiveView));
            _btnScopeModel = ToggleBtn(S._("batchparams.scope_model"),     false, () => SetScope(ElementScope.WholeModel));
            _btnScopeSel.Margin  = new Thickness(0, 0, 6, 0);
            _btnScopeView.Margin = new Thickness(0, 0, 6, 0);
            scopeRow.Children.Add(_btnScopeSel);
            scopeRow.Children.Add(_btnScopeView);
            scopeRow.Children.Add(_btnScopeModel);
            sp.Children.Add(scopeRow);

            var catBox = new Border
            {
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5), ClipToBounds = true, MinHeight = 60, MaxHeight = 140,
            };
            var catScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            _categoryList = new StackPanel { Margin = new Thickness(6) };
            catScroll.Content = _categoryList; catBox.Child = catScroll;
            sp.Children.Add(catBox);

            var scanRow = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            scanRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            scanRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _lblMatchCount = new TextBlock { Text = S._("batchparams.no_scan_yet"), FontSize = 11,
                Foreground = MeToolsTheme.BrMuted, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(_lblMatchCount, 0); scanRow.Children.Add(_lblMatchCount);
            var btnScan = ActionBtn(S._("batchparams.scan"), true, OnScanClicked);
            Grid.SetColumn(btnScan, 1); scanRow.Children.Add(btnScan);
            sp.Children.Add(scanRow);

            return sp;
        }

        private void SetScope(ElementScope scope)
        {
            _scope = scope;
            UpdateToggle(_btnScopeSel,   scope == ElementScope.CurrentSelection);
            UpdateToggle(_btnScopeView,  scope == ElementScope.ActiveView);
            UpdateToggle(_btnScopeModel, scope == ElementScope.WholeModel);
            RunScope();
        }

        // Re-collects elements for the current scope and repopulates the
        // category checklist. Does NOT touch _matchedElements/_paramOptions
        // -- those only update on an explicit Scan, so switching scope
        // doesn't silently invalidate a filter you already applied further
        // down without you noticing.
        private void RunScope()
        {
            var uiDoc = _uiApp?.ActiveUIDocument;
            var doc   = uiDoc?.Document;
            if (doc == null || uiDoc == null || _categoryList == null) return;

            _scannedElements = BatchParamsHandler.CollectByScope(doc, uiDoc, _scope);
            var cats = BatchParamsHandler.ListCategories(_scannedElements);

            _categoryList.Children.Clear();
            _categoryChecks.Clear();
            foreach (var c in cats)
            {
                var cb = new CheckBox
                {
                    Content = c.DisplayName, Tag = c.CategoryId,
                    Foreground = MeToolsTheme.BrText, Margin = new Thickness(4, 3, 4, 3),
                };
                _categoryChecks.Add(cb);
                _categoryList.Children.Add(cb);
            }

            _lblMatchCount.Text = cats.Count == 0
                ? S._("batchparams.no_scan_yet")
                : string.Format(S._("batchparams.categories_found"), cats.Count, _scannedElements.Count);
        }

        private void OnScanClicked()
        {
            var checkedIds = _categoryChecks
                .Where(cb => cb.IsChecked == true)
                .Select(cb => (ElementId)cb.Tag)
                .ToList();
            if (checkedIds.Count == 0)
            {
                MessageBox.Show(S._("batchparams.pick_category_first"), S._("batchparams.title"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var doc = _uiApp?.ActiveUIDocument?.Document;
            if (doc == null) return;

            _matchedElements = BatchParamsHandler.FilterByCategories(_scannedElements, checkedIds);
            _paramOptions    = BatchParamsHandler.GetParameterOptions(doc, _matchedElements);
            _lblMatchCount.Text = string.Format(S._("batchparams.elements_matched"), _matchedElements.Count, _paramOptions.Count);

            RefreshParamCombos();

            // A new scan invalidates any earlier manual pick order / path
            // pick, since the underlying matched set may have changed.
            _manualOrder.Clear();
            UpdateManualCountLabel();
            _pathCurveId = ElementId.InvalidElementId;
            if (_lblPathStatus != null) _lblPathStatus.Text = S._("batchparams.path_not_picked");
        }

        private void RefreshParamCombos()
        {
            if (_cbRenumberParam != null)
            {
                var instanceOnly = _paramOptions.Where(o => o.IsInstance).ToList();
                _cbRenumberParam.ItemsSource = null;
                _cbRenumberParam.ItemsSource = instanceOnly;
                if (instanceOnly.Count > 0) _cbRenumberParam.SelectedIndex = 0;
            }
            if (_cbBulkParam != null)
            {
                _cbBulkParam.ItemsSource = null;
                _cbBulkParam.ItemsSource = _paramOptions;
                if (_paramOptions.Count > 0) _cbBulkParam.SelectedIndex = 0;
            }
        }

        // ── Tab row (two toggle buttons, not a full colored tab bar --
        // there are only two, so ToggleBtn already does the job) ──────────
        private StackPanel BuildTabRow()
        {
            _panRenumber = BuildRenumberPanel();
            _panBulk     = BuildBulkEditPanel();

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 10) };
            _tabRenumber = ToggleBtn(S._("batchparams.tab_renumber"), true,  ShowRenumberTab);
            _tabBulk     = ToggleBtn(S._("batchparams.tab_bulkedit"), false, ShowBulkTab);
            _tabRenumber.Margin = new Thickness(0, 0, 6, 0);
            row.Children.Add(_tabRenumber);
            row.Children.Add(_tabBulk);
            return row;
        }

        private void ShowRenumberTab()
        {
            UpdateToggle(_tabRenumber, true);
            UpdateToggle(_tabBulk, false);
            _panRenumber.Visibility = Visibility.Visible;
            _panBulk.Visibility     = Visibility.Collapsed;
        }

        private void ShowBulkTab()
        {
            UpdateToggle(_tabRenumber, false);
            UpdateToggle(_tabBulk, true);
            _panRenumber.Visibility = Visibility.Collapsed;
            _panBulk.Visibility     = Visibility.Visible;
        }

        // ═════════════════════════════════════════════════════════════════
        // TAB 1 -- RENUMBER
        // ═════════════════════════════════════════════════════════════════
        private StackPanel BuildRenumberPanel()
        {
            var sp = new StackPanel { Visibility = Visibility.Collapsed };

            sp.Children.Add(SecH(S._("batchparams.parameter")));
            _cbRenumberParam = StyledCombo();
            _cbRenumberParam.DisplayMemberPath = "DisplayName";
            _cbRenumberParam.Margin = new Thickness(0, 0, 0, 10);
            sp.Children.Add(_cbRenumberParam);

            sp.Children.Add(SecH(S._("batchparams.numbering")));
            var numRow = new WrapPanel { Orientation = Orientation.Horizontal };
            numRow.Children.Add(LabeledField(S._("batchparams.prefix"),  "",  70, out _tbPrefix));
            numRow.Children.Add(LabeledField(S._("batchparams.start"),   "1", 50, out _tbStart));
            numRow.Children.Add(LabeledField(S._("batchparams.step"),    "1", 50, out _tbStep));
            numRow.Children.Add(LabeledField(S._("batchparams.padding"), "0", 50, out _tbPadding));
            numRow.Children.Add(LabeledField(S._("batchparams.suffix"),  "",  70, out _tbSuffix));
            sp.Children.Add(numRow);

            _lblPreview = new TextBlock
            {
                Text = "--", FontSize = 15, FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold,
                Foreground = MeToolsTheme.BrPetrol, Margin = new Thickness(0, 2, 0, 10),
            };
            sp.Children.Add(_lblPreview);

            _tbPrefix.TextChanged  += (s, e) => UpdateRenumberPreview();
            _tbSuffix.TextChanged  += (s, e) => UpdateRenumberPreview();
            _tbStart.TextChanged   += (s, e) => UpdateRenumberPreview();
            _tbStep.TextChanged    += (s, e) => UpdateRenumberPreview();
            _tbPadding.TextChanged += (s, e) => UpdateRenumberPreview();
            UpdateRenumberPreview();

            sp.Children.Add(Div());

            sp.Children.Add(SecH(S._("batchparams.order_mode")));
            var orderRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            _btnOrderManual = ToggleBtn(S._("batchparams.order_manual"), true,  () => SetOrderMode(RenumberOrderMode.Manual));
            _btnOrderPath   = ToggleBtn(S._("batchparams.order_path"),   false, () => SetOrderMode(RenumberOrderMode.Path));
            _btnOrderManual.Margin = new Thickness(0, 0, 6, 0);
            orderRow.Children.Add(_btnOrderManual);
            orderRow.Children.Add(_btnOrderPath);
            sp.Children.Add(orderRow);

            // Manual sub-panel
            _panManual = new StackPanel();
            var manualRow = new Grid();
            manualRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            manualRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _lblManualCount = new TextBlock { Text = S._("batchparams.manual_none_picked"), FontSize = 11,
                Foreground = MeToolsTheme.BrMuted, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(_lblManualCount, 0); manualRow.Children.Add(_lblManualCount);
            var btnPickManual = ActionBtn(S._("batchparams.pick_elements"), true, OnPickManualClicked);
            Grid.SetColumn(btnPickManual, 1); manualRow.Children.Add(btnPickManual);
            _panManual.Children.Add(manualRow);
            var btnClearManual = FooterBtn(S._("batchparams.clear_order"), false, OnClearManualClicked);
            btnClearManual.Margin = new Thickness(0, 6, 0, 0);
            btnClearManual.HorizontalAlignment = HorizontalAlignment.Left;
            _panManual.Children.Add(btnClearManual);
            sp.Children.Add(_panManual);

            // Path sub-panel
            _panPath = new StackPanel { Visibility = Visibility.Collapsed };
            var pathRow = new Grid();
            pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _lblPathStatus = new TextBlock { Text = S._("batchparams.path_not_picked"), FontSize = 11,
                Foreground = MeToolsTheme.BrMuted, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(_lblPathStatus, 0); pathRow.Children.Add(_lblPathStatus);
            var btnPickPath = ActionBtn(S._("batchparams.pick_line"), true, OnPickPathClicked);
            Grid.SetColumn(btnPickPath, 1); pathRow.Children.Add(btnPickPath);
            _panPath.Children.Add(pathRow);
            sp.Children.Add(_panPath);

            sp.Children.Add(Div());
            var btnApplyRenumber = ActionBtn(S._("batchparams.apply_renumber"), false, OnApplyRenumberClicked);
            btnApplyRenumber.HorizontalAlignment = HorizontalAlignment.Left;
            sp.Children.Add(btnApplyRenumber);

            return sp;
        }

        private void SetOrderMode(RenumberOrderMode mode)
        {
            _orderMode = mode;
            UpdateToggle(_btnOrderManual, mode == RenumberOrderMode.Manual);
            UpdateToggle(_btnOrderPath,   mode == RenumberOrderMode.Path);
            _panManual.Visibility = mode == RenumberOrderMode.Manual ? Visibility.Visible : Visibility.Collapsed;
            _panPath.Visibility   = mode == RenumberOrderMode.Path   ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateRenumberPreview()
        {
            if (_lblPreview == null) return;
            int start = int.TryParse(_tbStart?.Text, out var s) ? s : 1;
            int pad   = int.TryParse(_tbPadding?.Text, out var p) ? p : 0;
            string numStr = pad > 0 ? start.ToString().PadLeft(pad, '0') : start.ToString();
            _lblPreview.Text = (_tbPrefix?.Text ?? "") + numStr + (_tbSuffix?.Text ?? "");
        }

        // Incremental single-object picking loop, exactly like Circuit
        // Tagger's element selection (see CircuitTaggerWindow.OnSelectClicked)
        // -- one PickObject at a time, committed to the order list
        // immediately, rather than PickObjects' all-or-nothing behavior.
        // Runs directly here (not via ExternalEvent) since PickObject is an
        // interactive call that has to happen on the calling/main thread.
        private void OnPickManualClicked()
        {
            Hide();
            var uiDoc = _uiApp?.ActiveUIDocument;
            if (uiDoc == null) { Show(); return; }
            try
            {
                while (true)
                {
                    var r = uiDoc.Selection.PickObject(ObjectType.Element,
                        "Click elements in the order you want them numbered. Press Esc when done.");
                    if (r == null) break;
                    if (!_manualOrder.Contains(r.ElementId)) _manualOrder.Add(r.ElementId);
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { /* Esc -- normal way to finish */ }
            catch { }
            finally
            {
                Show();
                UpdateManualCountLabel();
            }
        }

        private void OnClearManualClicked()
        {
            _manualOrder.Clear();
            UpdateManualCountLabel();
        }

        private void UpdateManualCountLabel()
        {
            if (_lblManualCount == null) return;
            _lblManualCount.Text = _manualOrder.Count == 0
                ? S._("batchparams.manual_none_picked")
                : string.Format(S._("batchparams.manual_n_picked"), _manualOrder.Count);
        }

        // Restricts the pick to detail lines only.
        private class DetailLineSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is DetailCurve;
            public bool AllowReference(Reference reference, XYZ position) => true;
        }

        private void OnPickPathClicked()
        {
            if (_matchedElements.Count == 0)
            {
                MessageBox.Show(S._("batchparams.scan_first"), S._("batchparams.title"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Hide();
            var uiDoc = _uiApp?.ActiveUIDocument;
            if (uiDoc == null) { Show(); return; }
            try
            {
                var r = uiDoc.Selection.PickObject(ObjectType.Element, new DetailLineSelectionFilter(),
                    "Select a detail line -- elements will be ordered along it.");
                if (r != null) _pathCurveId = r.ElementId;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
            catch { }
            finally
            {
                Show();
                if (_lblPathStatus != null)
                    _lblPathStatus.Text = _pathCurveId != ElementId.InvalidElementId
                        ? S._("batchparams.path_picked")
                        : S._("batchparams.path_not_picked");
            }
        }

        private void OnApplyRenumberClicked()
        {
            var chosen = _cbRenumberParam?.SelectedItem as ParamOption;
            if (chosen == null)
            {
                MessageBox.Show(S._("batchparams.pick_param_first"), S._("batchparams.title"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            List<ElementId> ordered;
            if (_orderMode == RenumberOrderMode.Manual)
            {
                if (_manualOrder.Count == 0)
                {
                    MessageBox.Show(S._("batchparams.manual_none_picked_warn"), S._("batchparams.title"), MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                ordered = new List<ElementId>(_manualOrder);
            }
            else
            {
                if (_pathCurveId == ElementId.InvalidElementId)
                {
                    MessageBox.Show(S._("batchparams.path_not_picked_warn"), S._("batchparams.title"), MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var doc = _uiApp?.ActiveUIDocument?.Document;
                var curveEl = doc?.GetElement(_pathCurveId) as DetailCurve;
                var curve = curveEl?.GeometryCurve;
                if (doc == null || curve == null)
                {
                    MessageBox.Show(S._("batchparams.path_not_picked_warn"), S._("batchparams.title"), MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var matchedIds = _matchedElements.Select(e => e.Id).ToList();
                ordered = BatchParamsHandler.OrderByPath(doc, matchedIds, curve);
            }

            if (ordered.Count == 0)
            {
                MessageBox.Show(S._("batchparams.nothing_to_apply"), S._("batchparams.title"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int start = int.TryParse(_tbStart?.Text,   out var s0) ? s0 : 1;
            int step  = int.TryParse(_tbStep?.Text,    out var s1) ? s1 : 1;
            int pad   = int.TryParse(_tbPadding?.Text, out var s2) ? s2 : 0;

            _handler.Request = new BatchParamsRequest
            {
                Action            = BatchParamsAction.ApplyRenumber,
                OrderedElementIds = ordered,
                Renumber = new RenumberConfig
                {
                    ParameterName = chosen.Name,
                    Prefix        = _tbPrefix?.Text ?? "",
                    Suffix        = _tbSuffix?.Text ?? "",
                    StartNumber   = start,
                    Step          = step,
                    Padding       = pad,
                    OrderMode     = _orderMode,
                },
            };
            _extEvent.Raise();
            UpdateStatusBar(S._("batchparams.applying"));
        }

        // ═════════════════════════════════════════════════════════════════
        // TAB 2 -- BULK EDIT
        // ═════════════════════════════════════════════════════════════════
        private StackPanel BuildBulkEditPanel()
        {
            var sp = new StackPanel { Visibility = Visibility.Collapsed };

            sp.Children.Add(SecH(S._("batchparams.parameter")));
            _cbBulkParam = StyledCombo();
            _cbBulkParam.DisplayMemberPath = "DisplayName";
            _cbBulkParam.Margin = new Thickness(0, 0, 0, 10);
            sp.Children.Add(_cbBulkParam);

            sp.Children.Add(LabeledField(S._("batchparams.value_filter"), "", 180, out _tbValueFilter));

            sp.Children.Add(Div());
            sp.Children.Add(SecH(S._("batchparams.action")));

            var actRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            _btnActPrefix  = ToggleBtn(S._("batchparams.act_prefix"),  true,  () => SetBulkAction(BulkEditAction.AddPrefix));
            _btnActSuffix  = ToggleBtn(S._("batchparams.act_suffix"),  false, () => SetBulkAction(BulkEditAction.AddSuffix));
            _btnActReplace = ToggleBtn(S._("batchparams.act_replace"), false, () => SetBulkAction(BulkEditAction.FindReplace));
            _btnActSet     = ToggleBtn(S._("batchparams.act_set"),     false, () => SetBulkAction(BulkEditAction.SetValue));
            _btnActClear   = ToggleBtn(S._("batchparams.act_clear"),   false, () => SetBulkAction(BulkEditAction.ClearValue));
            foreach (var b in new[] { _btnActPrefix, _btnActSuffix, _btnActReplace, _btnActSet })
                b.Margin = new Thickness(0, 0, 6, 6);
            actRow.Children.Add(_btnActPrefix);
            actRow.Children.Add(_btnActSuffix);
            actRow.Children.Add(_btnActReplace);
            actRow.Children.Add(_btnActSet);
            actRow.Children.Add(_btnActClear);
            sp.Children.Add(actRow);

            _panBulkPrefix = new StackPanel();
            _panBulkPrefix.Children.Add(LabeledField(S._("batchparams.prefix"), "", 120, out _tbBulkPrefix));
            sp.Children.Add(_panBulkPrefix);

            _panBulkSuffix = new StackPanel { Visibility = Visibility.Collapsed };
            _panBulkSuffix.Children.Add(LabeledField(S._("batchparams.suffix"), "", 120, out _tbBulkSuffix));
            sp.Children.Add(_panBulkSuffix);

            _panBulkReplace = new StackPanel { Visibility = Visibility.Collapsed };
            var replRow = new StackPanel { Orientation = Orientation.Horizontal };
            replRow.Children.Add(LabeledField(S._("batchparams.find"),         "", 120, out _tbFind));
            replRow.Children.Add(LabeledField(S._("batchparams.replace_with"), "", 120, out _tbReplace));
            _panBulkReplace.Children.Add(replRow);
            sp.Children.Add(_panBulkReplace);

            _panBulkSet = new StackPanel { Visibility = Visibility.Collapsed };
            _panBulkSet.Children.Add(LabeledField(S._("batchparams.new_value"), "", 180, out _tbSetValue));
            sp.Children.Add(_panBulkSet);

            sp.Children.Add(Div());
            var btnApplyBulk = ActionBtn(S._("batchparams.apply_bulkedit"), false, OnApplyBulkEditClicked);
            btnApplyBulk.HorizontalAlignment = HorizontalAlignment.Left;
            sp.Children.Add(btnApplyBulk);

            return sp;
        }

        private void SetBulkAction(BulkEditAction action)
        {
            _bulkAction = action;
            UpdateToggle(_btnActPrefix,  action == BulkEditAction.AddPrefix);
            UpdateToggle(_btnActSuffix,  action == BulkEditAction.AddSuffix);
            UpdateToggle(_btnActReplace, action == BulkEditAction.FindReplace);
            UpdateToggle(_btnActSet,     action == BulkEditAction.SetValue);
            UpdateToggle(_btnActClear,   action == BulkEditAction.ClearValue);

            _panBulkPrefix.Visibility  = action == BulkEditAction.AddPrefix   ? Visibility.Visible : Visibility.Collapsed;
            _panBulkSuffix.Visibility  = action == BulkEditAction.AddSuffix   ? Visibility.Visible : Visibility.Collapsed;
            _panBulkReplace.Visibility = action == BulkEditAction.FindReplace ? Visibility.Visible : Visibility.Collapsed;
            _panBulkSet.Visibility     = action == BulkEditAction.SetValue    ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnApplyBulkEditClicked()
        {
            var chosen = _cbBulkParam?.SelectedItem as ParamOption;
            if (chosen == null)
            {
                MessageBox.Show(S._("batchparams.pick_param_first"), S._("batchparams.title"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (_matchedElements.Count == 0)
            {
                MessageBox.Show(S._("batchparams.scan_first"), S._("batchparams.title"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _handler.Request = new BatchParamsRequest
            {
                Action            = BatchParamsAction.ApplyBulkEdit,
                OrderedElementIds = _matchedElements.Select(e => e.Id).ToList(),
                BulkEdit = new BulkEditConfig
                {
                    ParameterName = chosen.Name,
                    IsInstance    = chosen.IsInstance,
                    Action        = _bulkAction,
                    PrefixText    = _tbBulkPrefix?.Text ?? "",
                    SuffixText    = _tbBulkSuffix?.Text ?? "",
                    FindText      = _tbFind?.Text ?? "",
                    ReplaceText   = _tbReplace?.Text ?? "",
                    SetText       = _tbSetValue?.Text ?? "",
                    ValueFilter   = _tbValueFilter?.Text ?? "",
                },
            };
            _extEvent.Raise();
            UpdateStatusBar(S._("batchparams.applying"));
        }

        // ── Small local UI helpers (each window in this app keeps its own
        // copies rather than sharing via the base class) ───────────────────
        private void UpdateStatusBar(string msg) { if (StatusLeft != null) StatusLeft.Text = msg; }

        private static TextBlock SecH(string text) => new TextBlock
        {
            Text = text.ToUpper(), FontSize = 10, FontWeight = FontWeights.SemiBold,
            Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 10, 0, 6),
        };

        private Border Div(double vmargin = 10) => new Border
        {
            Height = 1, Background = MeToolsTheme.BrBorder, Margin = new Thickness(0, vmargin, 0, vmargin),
        };

        // Compact label-above-narrow-input field, sized to what actually
        // goes in it rather than stretching to fill its column -- same idea
        // as Circuit Tagger's CompactField.
        private StackPanel LabeledField(string label, string defaultText, double width, out TextBox tb)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 14, 8) };
            sp.Children.Add(new TextBlock { Text = label.ToUpper(), FontSize = 8, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(1, 0, 0, 3) });
            var box = new TextBox
            {
                Text = defaultText, Width = width, Height = 26, FontSize = 12,
                FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.SemiBold,
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrInputFg,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                Padding = new Thickness(5, 0, 5, 0), VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            sp.Children.Add(box);
            tb = box;
            return sp;
        }
    }
}
