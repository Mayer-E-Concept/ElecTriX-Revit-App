// BatchParamsWindow.cs -- ME-Tools | Batch Params (Renumber + Bulk Edit)
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
using Autodesk.Revit.UI.Selection;
using Color      = System.Windows.Media.Color;
using ComboBox   = System.Windows.Controls.ComboBox;
using Grid       = System.Windows.Controls.Grid;
using TextBox    = System.Windows.Controls.TextBox;
using Visibility = System.Windows.Visibility;

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
        private Button _tabRenumber, _tabBulk, _tabCompleteness;
        private StackPanel _panRenumber, _panBulk, _panCompleteness;

        // -- Renumber tab -----------------------------------------------------
        private ComboBox _cbRenumberParam;
        private TextBox  _tbPrefix, _tbSuffix, _tbStart, _tbStep, _tbPadding;
        private TextBlock _lblPreview;
        private RenumberOrderMode _orderMode = RenumberOrderMode.Manual;
        private Button _btnOrderManual, _btnOrderPath;
        private StackPanel _panManual, _panPath;
        private readonly List<ManualPickInfo> _manualOrder = new List<ManualPickInfo>();
        private StackPanel _manualListPanel;
        private TextBlock _lblManualCount;
        private ElementId _pathCurveId = ElementId.InvalidElementId;
        private TextBlock _lblPathStatus;
        private BatchParamsRequest _pendingRenumberRequest;
        private StackPanel _panRenumberResult;
        private TextBlock  _lblRenumberSummary;
        private StackPanel _renumberResultList;
        private Button     _btnRenumberConfirm, _btnRenumberCancel;

        // -- Bulk Edit tab -----------------------------------------------------
        private ComboBox _cbBulkParam;
        private Button _btnActPrefix, _btnActSuffix, _btnActReplace, _btnActSet, _btnActClear;
        private BulkEditAction _bulkAction = BulkEditAction.AddPrefix;
        private TextBox _tbBulkPrefix, _tbBulkSuffix, _tbFind, _tbReplace, _tbSetValue, _tbValueFilter;
        private StackPanel _panBulkPrefix, _panBulkSuffix, _panBulkReplace, _panBulkSet;
        private BatchParamsRequest _pendingBulkRequest;
        private StackPanel _panBulkResult;
        private TextBlock  _lblBulkSummary;
        private StackPanel _bulkResultList;
        private Button     _btnBulkConfirm, _btnBulkCancel;

        // -- Completeness tab -----------------------------------------------
        private ComboBox _cbCompletenessParam;
        private TextBlock _lblCompletenessSummary;
        private StackPanel _completenessResultList;
        private Button _btnSelectMissing;
        private List<ElementId> _missingElementIds = new List<ElementId>();

        public BatchParamsWindow(UIApplication uiApp, ExternalEvent extEvent, BatchParamsHandler handler)
        {
            _uiApp = uiApp; _extEvent = extEvent; _handler = handler;
            S.SetLanguage(SettingsStore.Language ?? "en");
            InitWindow(S._("batchparams.title"), 600);
            MaxHeight = Math.Min(760, SystemParameters.WorkArea.Height - 60);
            WireHandler();
            Build();
            RunScope();
            // Hide() during a pick session doesn't raise Closed, only an
            // actual window close does -- so this only fires when the user
            // is genuinely done with the tool, not mid-pick. Without this, a
            // manual order picked but never applied or cleared would leave
            // its marks in the view forever: the next window instance
            // starts with a fresh, empty _manualOrder that knows nothing
            // about them.
            Closed += (s, e) => ClearManualMarks();
        }

        private void WireHandler()
        {
            _handler.OnStatus = msg => Dispatcher.Invoke(() => UpdateStatusBar(msg));
            _handler.OnDone   = result => Dispatcher.Invoke(() => HandleApplyResult(result));
            _handler.OnPickSessionDone = (ids, err) => Dispatcher.Invoke(() => HandlePickSessionDone(ids, err));
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
            outer.Children.Add(_panCompleteness);

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
            // BUG FIXED HERE: this used to clear _manualOrder without
            // clearing the marks first -- the magenta overrides on those
            // elements were left in the view forever, since nothing in the
            // tool still referenced them afterward to ever turn them off.
            ClearManualMarks();
            _manualOrder.Clear();
            RefreshManualOrderList();
            _pathCurveId = ElementId.InvalidElementId;
            if (_lblPathStatus != null) _lblPathStatus.Text = S._("batchparams.path_not_picked");

            // Same for any preview/result already on screen -- it was built
            // from the previous matched set.
            _pendingRenumberRequest = null;
            _pendingBulkRequest = null;
            if (_panRenumberResult != null) _panRenumberResult.Visibility = Visibility.Collapsed;
            if (_panBulkResult != null) _panBulkResult.Visibility = Visibility.Collapsed;
            _missingElementIds.Clear();
            if (_completenessResultList != null) _completenessResultList.Children.Clear();
            if (_lblCompletenessSummary != null) _lblCompletenessSummary.Text = "";
            if (_btnSelectMissing != null) _btnSelectMissing.Visibility = Visibility.Collapsed;
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
            if (_cbCompletenessParam != null)
            {
                _cbCompletenessParam.ItemsSource = null;
                _cbCompletenessParam.ItemsSource = _paramOptions;
                if (_paramOptions.Count > 0) _cbCompletenessParam.SelectedIndex = 0;
            }
        }

        // ── Tab row (two toggle buttons, not a full colored tab bar --
        // there are only two, so ToggleBtn already does the job) ──────────
        private StackPanel BuildTabRow()
        {
            _panRenumber      = BuildRenumberPanel();
            _panBulk          = BuildBulkEditPanel();
            _panCompleteness  = BuildCompletenessPanel();

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 10) };
            _tabRenumber     = ToggleBtn(S._("batchparams.tab_renumber"),     true,  ShowRenumberTab);
            _tabBulk         = ToggleBtn(S._("batchparams.tab_bulkedit"),     false, ShowBulkTab);
            _tabCompleteness = ToggleBtn(S._("batchparams.tab_completeness"), false, ShowCompletenessTab);
            _tabRenumber.Margin = new Thickness(0, 0, 6, 0);
            _tabBulk.Margin     = new Thickness(0, 0, 6, 0);
            row.Children.Add(_tabRenumber);
            row.Children.Add(_tabBulk);
            row.Children.Add(_tabCompleteness);
            return row;
        }

        private void ShowRenumberTab()
        {
            UpdateToggle(_tabRenumber, true);
            UpdateToggle(_tabBulk, false);
            UpdateToggle(_tabCompleteness, false);
            _panRenumber.Visibility     = Visibility.Visible;
            _panBulk.Visibility         = Visibility.Collapsed;
            _panCompleteness.Visibility = Visibility.Collapsed;
        }

        private void ShowBulkTab()
        {
            UpdateToggle(_tabRenumber, false);
            UpdateToggle(_tabBulk, true);
            UpdateToggle(_tabCompleteness, false);
            _panRenumber.Visibility     = Visibility.Collapsed;
            _panBulk.Visibility         = Visibility.Visible;
            _panCompleteness.Visibility = Visibility.Collapsed;
        }

        private void ShowCompletenessTab()
        {
            UpdateToggle(_tabRenumber, false);
            UpdateToggle(_tabBulk, false);
            UpdateToggle(_tabCompleteness, true);
            _panRenumber.Visibility     = Visibility.Collapsed;
            _panBulk.Visibility         = Visibility.Collapsed;
            _panCompleteness.Visibility = Visibility.Visible;
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
            _cbRenumberParam.Margin  = new Thickness(0, 0, 0, 10);
            _cbRenumberParam.ToolTip = S._("batchparams.param_combo_hint");
            sp.Children.Add(_cbRenumberParam);

            sp.Children.Add(SecH(S._("batchparams.numbering")));
            var numRow = new WrapPanel { Orientation = Orientation.Horizontal };
            numRow.Children.Add(CompactField(S._("batchparams.prefix"),  S._("batchparams.prefix_hint"),  70, out _tbPrefix));
            numRow.Children.Add(CompactField(S._("batchparams.start"),   S._("batchparams.start_hint"),   50, out _tbStart,   "1"));
            numRow.Children.Add(CompactField(S._("batchparams.step"),    S._("batchparams.step_hint"),    50, out _tbStep,    "1"));
            numRow.Children.Add(CompactField(S._("batchparams.padding"), S._("batchparams.padding_hint"), 50, out _tbPadding, "0"));
            numRow.Children.Add(CompactField(S._("batchparams.suffix"),  S._("batchparams.suffix_hint"),  70, out _tbSuffix));
            sp.Children.Add(numRow);

            _lblPreview = new TextBlock
            {
                Text = "--", FontSize = 15, FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold,
                Foreground = MeToolsTheme.BrAccent, Margin = new Thickness(0, 2, 0, 10),
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
            _btnOrderManual.ToolTip = S._("batchparams.order_manual_hint");
            _btnOrderPath.ToolTip   = S._("batchparams.order_path_hint");
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

            var manualListBox = new Border
            {
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5), MaxHeight = 160, ClipToBounds = true,
                Margin = new Thickness(0, 6, 0, 0),
            };
            var manualListScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            _manualListPanel = new StackPanel();
            manualListScroll.Content = _manualListPanel;
            manualListBox.Child = manualListScroll;
            _panManual.Children.Add(manualListBox);

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

            _panRenumberResult = BuildResultPanel(
                out _lblRenumberSummary, out _renumberResultList,
                out _btnRenumberConfirm, out _btnRenumberCancel,
                OnConfirmRenumberClicked, OnCancelRenumberPreview);
            sp.Children.Add(_panRenumberResult);

            return sp;
        }

        private void SetOrderMode(RenumberOrderMode mode)
        {
            _orderMode = mode;
            UpdateToggle(_btnOrderManual, mode == RenumberOrderMode.Manual);
            UpdateToggle(_btnOrderPath,   mode == RenumberOrderMode.Path);
            _panManual.Visibility = mode == RenumberOrderMode.Manual ? Visibility.Visible : Visibility.Collapsed;
            _panPath.Visibility   = mode == RenumberOrderMode.Path   ? Visibility.Visible : Visibility.Collapsed;
            UpdateRenumberPreview();
        }

        private void UpdateRenumberPreview()
        {
            if (_lblPreview == null) return;
            int start = int.TryParse(_tbStart?.Text,   out var s) ? s : 1;
            int step  = int.TryParse(_tbStep?.Text,    out var st) ? st : 1;
            int pad   = int.TryParse(_tbPadding?.Text, out var p) ? p : 0;
            if (pad < 0) pad = 0;
            string prefix = _tbPrefix?.Text ?? "";
            string suffix = _tbSuffix?.Text ?? "";

            string first = BatchParamsHandler.FormatRenumberValue(prefix, start, pad, suffix);

            // Only Manual mode has a live, known count before Apply (Path
            // mode's final count depends on the picked line, resolved later)
            // -- show the full resulting range there so a step/count mistake
            // is visible before spending a pick session, e.g. "F01 -> F10"
            // rather than just the first value in isolation.
            int count = _orderMode == RenumberOrderMode.Manual ? _manualOrder.Count : 0;
            if (count > 1 && step != 0)
            {
                int lastN = start + step * (count - 1);
                string last = BatchParamsHandler.FormatRenumberValue(prefix, lastN, pad, suffix);
                _lblPreview.Text = $"{first} \u2192 {last}";
            }
            else
            {
                _lblPreview.Text = first;
            }
        }

        // One manually-picked element, in queue order -- gives the manual
        // order an actual visible list (index, category, label) instead of
        // just an opaque count, and lets each entry be removed or moved
        // without clearing and re-picking the whole batch. Mirrors
        // CircuitTaggerWindow.TaggedElementInfo.
        private class ManualPickInfo
        {
            public ElementId ElementId    { get; set; }
            public string    CategoryName { get; set; } = "";
            public string    Label        { get; set; } = "";
        }

        // ROOT CAUSE FIX: the magenta "already picked" mark used to be a
        // Transaction opened directly from this click handler, on a window
        // shown via .Show() (no valid Revit API context) -- Revit silently
        // refuses it there, and the try/catch swallowed the exception, so
        // nothing ever visibly highlighted. The whole pick loop, including
        // every mark, now runs inside BatchParamsHandler.ExecutePickElementsInteractive
        // via the ExternalEvent, which has valid API context throughout.
        private void OnPickManualClicked()
        {
            Hide();
            _handler.Request = new BatchParamsRequest
            {
                Action     = BatchParamsAction.PickElementsInteractive,
                ElementIds = _manualOrder.Select(x => x.ElementId).ToList(),
            };
            _extEvent.Raise();
        }

        // Called once the handler's pick session finishes (Esc, or an
        // error). Resolves category/label per newly-picked id, same as
        // CircuitTaggerWindow.HandlePickSessionDone -- a read, not a
        // document modification, so it's fine to do here rather than in
        // the handler.
        private void HandlePickSessionDone(List<ElementId> newlyPickedIds, string errorMessage)
        {
            if (!string.IsNullOrEmpty(errorMessage))
                MessageBox.Show(errorMessage, S._("batchparams.title"));

            var doc = _uiApp?.ActiveUIDocument?.Document;
            if (doc != null && newlyPickedIds != null)
            {
                foreach (var id in newlyPickedIds)
                {
                    if (_manualOrder.Any(x => x.ElementId == id)) continue;
                    var el = doc.GetElement(id);
                    if (el == null) continue;
                    _manualOrder.Add(new ManualPickInfo
                    {
                        ElementId    = id,
                        CategoryName = el.Category?.Name ?? "Element",
                        Label        = (el as FamilyInstance)?.Symbol?.Family?.Name ?? el.Name ?? "",
                    });
                }
            }

            Show();
            RefreshManualOrderList();
        }

        private void OnClearManualClicked()
        {
            ClearManualMarks();
            _manualOrder.Clear();
            RefreshManualOrderList();
        }

        // Clears the magenta override for whatever's currently in
        // _manualOrder, without touching the list itself -- shared by every
        // place that empties the order (Clear button, a new Scan, a
        // successful Apply, and the window closing) so none of them can
        // leave a stray mark behind with nothing left in the tool that
        // still knows about it.
        private void ClearManualMarks()
        {
            if (_manualOrder.Count == 0) return;
            _handler.Request = new BatchParamsRequest
            {
                Action     = BatchParamsAction.SetPendingMarks,
                ElementIds = _manualOrder.Select(x => x.ElementId).ToList(),
                MarkOn     = false,
            };
            _extEvent.Raise();
        }

        private void UpdateManualCountLabel()
        {
            if (_lblManualCount == null) return;
            _lblManualCount.Text = _manualOrder.Count == 0
                ? S._("batchparams.manual_none_picked")
                : string.Format(S._("batchparams.manual_n_picked"), _manualOrder.Count);
        }

        // Rebuilds the visible, reorderable manual-order list -- the whole
        // point of a REORDERING tool is being able to see the order you've
        // built, not just a count. Each row shows its position, category,
        // and label, with move up/down and a remove button, matching
        // CircuitTaggerWindow's row style.
        private void RefreshManualOrderList()
        {
            UpdateManualCountLabel();
            if (_manualListPanel == null) return;
            _manualListPanel.Children.Clear();

            for (int i = 0; i < _manualOrder.Count; i++)
            {
                var info = _manualOrder[i];
                int index = i; // captured per-row

                var row = new Grid { MinHeight = 26 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });

                var idxTb = new TextBlock
                {
                    Text = (index + 1).ToString(), FontSize = 10, FontWeight = FontWeights.SemiBold,
                    Foreground = MeToolsTheme.BrMuted, VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                };
                var catBadge = new Border
                {
                    CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 1, 5, 1),
                    Margin = new Thickness(4, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
                    Background = MeToolsTheme.BrActiveBg, BorderBrush = MeToolsTheme.BrAccent, BorderThickness = new Thickness(1),
                    Child = new TextBlock { Text = info.CategoryName, FontSize = 9,
                        Foreground = MeToolsTheme.BrAccent, FontWeight = FontWeights.SemiBold },
                };
                var labelTb = new TextBlock
                {
                    Text = info.Label, FontSize = 11, Foreground = MeToolsTheme.BrText,
                    VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 4, 0),
                };

                var upBtn = MiniRowBtn("\u25B2", index > 0, () => MoveManualItem(index, -1));
                var downBtn = MiniRowBtn("\u25BC", index < _manualOrder.Count - 1, () => MoveManualItem(index, 1));
                var removeBtn = MiniRowBtn("\u00D7", true, () => RemoveManualItem(info));

                Grid.SetColumn(idxTb,     0); row.Children.Add(idxTb);
                Grid.SetColumn(catBadge,  1); row.Children.Add(catBadge);
                Grid.SetColumn(labelTb,   2); row.Children.Add(labelTb);
                Grid.SetColumn(upBtn,     3); row.Children.Add(upBtn);
                Grid.SetColumn(downBtn,   4); row.Children.Add(downBtn);
                Grid.SetColumn(removeBtn, 5); row.Children.Add(removeBtn);

                var rowBorder = new Border
                {
                    BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(0, 0, 0, 1),
                    Background = MeToolsTheme.BrRow, Child = row,
                };
                rowBorder.MouseEnter += (s, e) => rowBorder.Background = MeToolsTheme.BrActiveBg;
                rowBorder.MouseLeave += (s, e) => rowBorder.Background = MeToolsTheme.BrRow;
                _manualListPanel.Children.Add(rowBorder);
            }

            UpdateRenumberPreview();
        }

        // Small square icon-only button for the row up/down/remove
        // controls -- distinct from SmallBtn/ActionBtn/FooterBtn (those are
        // all sized for text labels), matching the compact removeBtn style
        // CircuitTaggerWindow uses for its own per-row remove button.
        private Button MiniRowBtn(string glyph, bool enabled, Action onClick)
        {
            var btn = new Button
            {
                Content = glyph, Width = 18, Height = 18, FontSize = 9,
                Background = Brushes.Transparent, BorderBrush = Brushes.Transparent,
                Foreground = enabled ? MeToolsTheme.BrMuted : MeToolsTheme.BrBorder,
                Cursor = enabled ? Cursors.Hand : Cursors.Arrow,
                IsEnabled = enabled, VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(0), Margin = new Thickness(1, 0, 1, 0),
            };
            if (enabled) btn.Click += (s, e) => onClick();
            return btn;
        }

        private void MoveManualItem(int index, int direction)
        {
            int target = index + direction;
            if (target < 0 || target >= _manualOrder.Count) return;
            var item = _manualOrder[index];
            _manualOrder.RemoveAt(index);
            _manualOrder.Insert(target, item);
            RefreshManualOrderList();
        }

        private void RemoveManualItem(ManualPickInfo info)
        {
            _handler.Request = new BatchParamsRequest
            {
                Action     = BatchParamsAction.SetPendingMarks,
                ElementIds = new List<ElementId> { info.ElementId },
                MarkOn     = false,
            };
            _extEvent.Raise();
            _manualOrder.Remove(info);
            RefreshManualOrderList();
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
            List<ElementId> pathExcluded = new List<ElementId>();
            if (_orderMode == RenumberOrderMode.Manual)
            {
                if (_manualOrder.Count == 0)
                {
                    MessageBox.Show(S._("batchparams.manual_none_picked_warn"), S._("batchparams.title"), MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                ordered = _manualOrder.Select(x => x.ElementId).ToList();
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
                // BUG FIXED HERE: elements that couldn't be positioned along
                // the line used to just silently vanish from the result --
                // if 47 of 50 matched, there was no visible reason why.
                // pathExcluded now gets reported to the handler so they show
                // up in the review panel as an explicit, reasoned skip.
                ordered = BatchParamsHandler.OrderByPath(doc, matchedIds, curve, out pathExcluded);
            }

            if (ordered.Count == 0)
            {
                MessageBox.Show(S._("batchparams.nothing_to_apply"), S._("batchparams.title"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int start = int.TryParse(_tbStart?.Text,   out var s0) ? s0 : 1;
            int step  = int.TryParse(_tbStep?.Text,    out var s1) ? s1 : 1;
            int pad   = int.TryParse(_tbPadding?.Text, out var s2) ? s2 : 0;
            if (pad < 0) pad = 0;

            // BUG FIXED HERE: Step == 0 used to sail straight through and
            // silently write the exact same value to every single element
            // in the batch -- a serious, easy-to-typo "oops" that looked
            // like a normal successful run. Now caught before it ever
            // reaches the handler.
            if (step == 0)
            {
                MessageBox.Show(S._("batchparams.step_zero_warn"), S._("batchparams.title"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _pendingRenumberRequest = new BatchParamsRequest
            {
                Action                 = BatchParamsAction.ApplyRenumber,
                OrderedElementIds      = ordered,
                PathExcludedElementIds = pathExcluded,
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
                DryRun = true,
            };
            _handler.Request = _pendingRenumberRequest;
            _extEvent.Raise();
            UpdateStatusBar(S._("batchparams.previewing"));
        }

        private void OnConfirmRenumberClicked()
        {
            if (_pendingRenumberRequest == null) return;
            _pendingRenumberRequest.DryRun = false;
            _handler.Request = _pendingRenumberRequest;
            _extEvent.Raise();
            UpdateStatusBar(S._("batchparams.applying"));
        }

        private void OnCancelRenumberPreview()
        {
            _pendingRenumberRequest = null;
            if (_panRenumberResult != null) _panRenumberResult.Visibility = Visibility.Collapsed;
            UpdateStatusBar(S._("batchparams.ready"));
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
            _cbBulkParam.Margin  = new Thickness(0, 0, 0, 10);
            _cbBulkParam.ToolTip = S._("batchparams.param_combo_bulk_hint");
            sp.Children.Add(_cbBulkParam);

            sp.Children.Add(CompactField(S._("batchparams.value_filter"), S._("batchparams.value_filter_hint"), 180, out _tbValueFilter));

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
            _panBulkPrefix.Children.Add(CompactField(S._("batchparams.prefix"), S._("batchparams.bulk_prefix_hint"), 120, out _tbBulkPrefix));
            sp.Children.Add(_panBulkPrefix);

            _panBulkSuffix = new StackPanel { Visibility = Visibility.Collapsed };
            _panBulkSuffix.Children.Add(CompactField(S._("batchparams.suffix"), S._("batchparams.bulk_suffix_hint"), 120, out _tbBulkSuffix));
            sp.Children.Add(_panBulkSuffix);

            _panBulkReplace = new StackPanel { Visibility = Visibility.Collapsed };
            var replRow = new StackPanel { Orientation = Orientation.Horizontal };
            replRow.Children.Add(CompactField(S._("batchparams.find"),         S._("batchparams.find_hint"),         120, out _tbFind));
            replRow.Children.Add(CompactField(S._("batchparams.replace_with"), S._("batchparams.replace_with_hint"), 120, out _tbReplace));
            _panBulkReplace.Children.Add(replRow);
            sp.Children.Add(_panBulkReplace);

            _panBulkSet = new StackPanel { Visibility = Visibility.Collapsed };
            _panBulkSet.Children.Add(CompactField(S._("batchparams.new_value"), S._("batchparams.new_value_hint"), 180, out _tbSetValue));
            sp.Children.Add(_panBulkSet);

            sp.Children.Add(Div());
            var btnApplyBulk = ActionBtn(S._("batchparams.apply_bulkedit"), false, OnApplyBulkEditClicked);
            btnApplyBulk.HorizontalAlignment = HorizontalAlignment.Left;
            sp.Children.Add(btnApplyBulk);

            _panBulkResult = BuildResultPanel(
                out _lblBulkSummary, out _bulkResultList,
                out _btnBulkConfirm, out _btnBulkCancel,
                OnConfirmBulkEditClicked, OnCancelBulkEditPreview);
            sp.Children.Add(_panBulkResult);

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

            _pendingBulkRequest = new BatchParamsRequest
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
                DryRun = true,
            };
            _handler.Request = _pendingBulkRequest;
            _extEvent.Raise();
            UpdateStatusBar(S._("batchparams.previewing"));
        }

        private void OnConfirmBulkEditClicked()
        {
            if (_pendingBulkRequest == null) return;
            _pendingBulkRequest.DryRun = false;
            _handler.Request = _pendingBulkRequest;
            _extEvent.Raise();
            UpdateStatusBar(S._("batchparams.applying"));
        }

        private void OnCancelBulkEditPreview()
        {
            _pendingBulkRequest = null;
            if (_panBulkResult != null) _panBulkResult.Visibility = Visibility.Collapsed;
            UpdateStatusBar(S._("batchparams.ready"));
        }

        // ═════════════════════════════════════════════════════════════════
        // TAB 3 -- COMPLETENESS CHECK
        // The opposite question from Renumber/Bulk Edit: not "change these,"
        // but "which of these are missing a value?" Read-only start to
        // finish (no ExternalEvent, no transaction, nothing is written), so
        // there's no preview/confirm step -- Check just shows the answer.
        // ═════════════════════════════════════════════════════════════════
        private StackPanel BuildCompletenessPanel()
        {
            var sp = new StackPanel { Visibility = Visibility.Collapsed };

            sp.Children.Add(SecH(S._("batchparams.parameter")));
            _cbCompletenessParam = StyledCombo();
            _cbCompletenessParam.DisplayMemberPath = "DisplayName";
            _cbCompletenessParam.Margin  = new Thickness(0, 0, 0, 10);
            _cbCompletenessParam.ToolTip = S._("batchparams.param_combo_bulk_hint");
            sp.Children.Add(_cbCompletenessParam);

            sp.Children.Add(Div());
            var btnCheck = ActionBtn(S._("batchparams.check_completeness"), false, OnCheckCompletenessClicked);
            btnCheck.HorizontalAlignment = HorizontalAlignment.Left;
            sp.Children.Add(btnCheck);

            _lblCompletenessSummary = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) };
            sp.Children.Add(_lblCompletenessSummary);

            var box = new Border
            {
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5), MaxHeight = 200, ClipToBounds = true,
            };
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            _completenessResultList = new StackPanel { Margin = new Thickness(6) };
            scroll.Content = _completenessResultList; box.Child = scroll;
            sp.Children.Add(box);

            _btnSelectMissing = ActionBtn(S._("batchparams.select_missing"), true, OnSelectMissingClicked);
            _btnSelectMissing.HorizontalAlignment = HorizontalAlignment.Left;
            _btnSelectMissing.Margin = new Thickness(0, 8, 0, 0);
            _btnSelectMissing.Visibility = Visibility.Collapsed;
            sp.Children.Add(_btnSelectMissing);

            return sp;
        }

        private void OnCheckCompletenessClicked()
        {
            var chosen = _cbCompletenessParam?.SelectedItem as ParamOption;
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
            var doc = _uiApp?.ActiveUIDocument?.Document;
            if (doc == null) return;

            var missing = BatchParamsHandler.FindMissingValues(doc, _matchedElements, chosen.Name, chosen.IsInstance);
            _missingElementIds = missing.Select(m => m.ElementId).ToList();

            int total = _matchedElements.Count;
            _lblCompletenessSummary.Text = string.Format(S._("batchparams.n_missing"), missing.Count, total);
            _lblCompletenessSummary.Foreground = missing.Count > 0 ? MeToolsTheme.Br(MeToolsTheme.COrange) : MeToolsTheme.BrAccent;

            _completenessResultList.Children.Clear();
            const int cap = 200;
            int shown = 0;
            foreach (var m in missing)
            {
                if (shown >= cap)
                {
                    _completenessResultList.Children.Add(new TextBlock
                    {
                        Text = string.Format(S._("batchparams.n_more"), missing.Count - cap),
                        FontSize = 10, Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 2, 0, 0),
                    });
                    break;
                }
                _completenessResultList.Children.Add(new TextBlock
                {
                    Text = $"{m.ElementLabel}: {m.Reason}", FontSize = 11,
                    Foreground = MeToolsTheme.Br(MeToolsTheme.COrange), TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 1, 0, 1),
                });
                shown++;
            }

            _btnSelectMissing.Visibility = _missingElementIds.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnSelectMissingClicked()
        {
            var uiDoc = _uiApp?.ActiveUIDocument;
            if (uiDoc == null || _missingElementIds.Count == 0) return;
            try { uiDoc.Selection.SetElementIds(_missingElementIds); }
            catch { }
        }

        // ═════════════════════════════════════════════════════════════════
        // SHARED RESULT/PREVIEW PANEL -- one layout, used by both tabs.
        // A dry-run result shows Confirm/Cancel (this is what WOULD happen);
        // a real-apply result hides them and just leaves the list up so you
        // can see exactly what got skipped and why, rather than a bare count.
        // ═════════════════════════════════════════════════════════════════
        private StackPanel BuildResultPanel(out TextBlock lblSummary, out StackPanel list,
            out Button btnConfirm, out Button btnCancel, Action onConfirm, Action onCancel)
        {
            var panel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };

            lblSummary = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
            panel.Children.Add(lblSummary);

            var box = new Border
            {
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5), MaxHeight = 160, ClipToBounds = true,
            };
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var innerList = new StackPanel { Margin = new Thickness(6) };
            scroll.Content = innerList; box.Child = scroll;
            panel.Children.Add(box);
            list = innerList;

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            var confirm = ActionBtn(S._("batchparams.confirm_apply"), false, onConfirm);
            var cancel  = ActionBtn(S._("batchparams.cancel"), true, onCancel);
            confirm.Margin = new Thickness(0, 0, 6, 0);
            btnRow.Children.Add(confirm);
            btnRow.Children.Add(cancel);
            panel.Children.Add(btnRow);
            btnConfirm = confirm; btnCancel = cancel;

            return panel;
        }

        private void HandleApplyResult(ApplyResult result)
        {
            if (result == null) return;

            // Only once the renumber genuinely commits, not during the
            // dry-run preview -- the highlight is still useful while
            // reviewing the preview before confirming.
            // BUG FIXED HERE: this used to clear the marks but leave
            // _manualOrder itself populated -- the count label kept
            // showing stale entries, and a follow-up Apply would silently
            // re-include the same already-renumbered elements. Now clears
            // both together.
            if (result.WhichAction == BatchParamsAction.ApplyRenumber && !result.WasDryRun && _orderMode == RenumberOrderMode.Manual && _manualOrder.Count > 0)
            {
                ClearManualMarks();
                _manualOrder.Clear();
                RefreshManualOrderList();
            }

            if (result.WhichAction == BatchParamsAction.ApplyRenumber)
                ShowResultPanel(result, _panRenumberResult, _lblRenumberSummary, _renumberResultList, _btnRenumberConfirm, _btnRenumberCancel);
            else if (result.WhichAction == BatchParamsAction.ApplyBulkEdit)
                ShowResultPanel(result, _panBulkResult, _lblBulkSummary, _bulkResultList, _btnBulkConfirm, _btnBulkCancel);
        }

        private void ShowResultPanel(ApplyResult result, StackPanel panel, TextBlock lblSummary,
            StackPanel list, Button btnConfirm, Button btnCancel)
        {
            if (panel == null) return;
            panel.Visibility = Visibility.Visible;

            string verb = result.WasDryRun ? S._("batchparams.would_update") : S._("batchparams.did_update");
            string summary = string.Format(verb, result.Updated);
            if (result.Skipped > 0) summary += string.Format(S._("batchparams.n_skipped"), result.Skipped);
            if (result.Errors  > 0) summary += string.Format(S._("batchparams.n_errors"), result.Errors);
            lblSummary.Text = summary;
            lblSummary.Foreground = result.Errors > 0 ? MeToolsTheme.Br(MeToolsTheme.CRed)
                                   : result.Skipped > 0 ? MeToolsTheme.Br(MeToolsTheme.COrange)
                                   : MeToolsTheme.BrAccent;

            list.Children.Clear();
            const int cap = 200;
            int shown = 0;
            foreach (var c in result.Changes)
            {
                if (shown >= cap)
                {
                    list.Children.Add(new TextBlock
                    {
                        Text = string.Format(S._("batchparams.n_more"), result.Changes.Count - cap),
                        FontSize = 10, Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 2, 0, 0),
                    });
                    break;
                }
                var row = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) };
                switch (c.Status)
                {
                    case ChangeStatus.Updated:
                        row.Text = $"{c.ElementLabel}: '{c.OldValue}' \u2192 '{c.NewValue}'";
                        row.Foreground = MeToolsTheme.BrText;
                        break;
                    case ChangeStatus.Skipped:
                        row.Text = $"{c.ElementLabel}: {S._("batchparams.skipped_because")} {c.Reason}";
                        row.Foreground = MeToolsTheme.Br(MeToolsTheme.COrange);
                        break;
                    default:
                        row.Text = $"{c.ElementLabel}: {S._("batchparams.error_prefix")} {c.Reason}";
                        row.Foreground = MeToolsTheme.Br(MeToolsTheme.CRed);
                        break;
                }
                list.Children.Add(row);
                shown++;
            }

            bool isPreview = result.WasDryRun;
            btnConfirm.Visibility = isPreview ? Visibility.Visible : Visibility.Collapsed;
            btnCancel.Visibility  = isPreview ? Visibility.Visible : Visibility.Collapsed;
            btnConfirm.IsEnabled  = result.Updated > 0;
            if (!isPreview)
            {
                if (result.WhichAction == BatchParamsAction.ApplyRenumber) _pendingRenumberRequest = null;
                else if (result.WhichAction == BatchParamsAction.ApplyBulkEdit) _pendingBulkRequest = null;
            }
        }
    }
}
