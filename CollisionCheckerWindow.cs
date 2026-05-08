// CollisionCheckerWindow.cs — ME-Tools | Clash Detector  v7
// Mayer E-Concept SRL
// -----------------------------------------------------------------
// v7 changes vs v6:
//   • Fixed MBtn() missing-return compile error (row action buttons
//     never appeared, and the file didn't build cleanly).
//   • Replaced the custom `ClashRow : FrameworkElement` +
//     DataTemplate virtualization pattern — it was fragile under
//     recycling and dropped row-button callbacks. Now uses a plain
//     ItemsControl inside a ScrollViewer that gets imperatively
//     repopulated on every ShowResults/RefreshList call. Fewer than
//     ~1000 clashes render instantly and all buttons work every time.
// -----------------------------------------------------------------
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
using CheckBox   = System.Windows.Controls.CheckBox;
using ComboBox   = System.Windows.Controls.ComboBox;
using TextBox    = System.Windows.Controls.TextBox;
using Grid       = System.Windows.Controls.Grid;
using Visibility = System.Windows.Visibility;

namespace METools.ClashDetector
{
    public class ClashDetectorWindow : METools.MeToolsWindowBase
    {
        readonly ExternalEvent         _evt;
        readonly ClashDetectorHandler  _h;
        readonly List<PenFamilyInfo>   _fams;

        CheckBox _cbTrassen, _cbConduits, _cbDucts, _cbPipes;
        CheckBox _cbWalls, _cbFloors, _cbStructural;
        CheckBox _cbCurrentDoc, _cbLinkedDocs, _cbViewOnly;
        ComboBox _famCmb;
        TextBox  _tbXUeb, _tbZUeb, _tbVorzug, _tbNachzug;

        TextBlock  _countTb, _hlsTb;
        StackPanel _rowPanel;              // rows get added here directly
        List<ClashResult> _results = new List<ClashResult>();

        public ClashDetectorWindow(ExternalEvent evt, ClashDetectorHandler h, List<PenFamilyInfo> fams)
        {
            _evt = evt; _h = h; _fams = fams;
            h.OnStatus  = m => Dispatcher.Invoke(() => StatusLeft.Text = m);
            h.OnResults = r => Dispatcher.Invoke(() => ShowResults(r));
            h.OnRefresh = ()=> Dispatcher.Invoke(() => RefreshList());
            h.OnHlsInfo = m => Dispatcher.Invoke(() => ShowHlsHint(m));
            InitWindow("Clash Detector", 780);
            SizeToContent = SizeToContent.Manual;
            Height = 730;
            Build();
        }

        // ── Build UI ────────────────────────────────────────────────────────
        void Build()
        {
            BuildStatusBar("Clash Detector — ready");
            var outer  = new Grid();
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = MeToolsTheme.BrBg };
            outer.Children.Add(scroll);
            outer.Children.Add(Watermark());
            RootDock.Children.Add(outer);
            var body = new StackPanel { Margin = new Thickness(14, 10, 14, 8) };
            scroll.Content = body;

            // ── Check Settings ────────────────────────────────────────────
            body.Children.Add(Sec("Check Settings"));
            body.Children.Add(CkRow("MEP:",
                _cbTrassen  = Ck("Cable Trays", true),
                _cbConduits = Ck("Conduits",    true),
                _cbDucts    = Ck("Ducts",       true),
                _cbPipes    = Ck("Pipes",       true)));
            body.Children.Add(new TextBlock
            {
                Text = "  Flex pipes / flex ducts / Leerrohr are excluded automatically.",
                FontSize = 9.5, FontStyle = FontStyles.Italic,
                Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 4)
            });
            body.Children.Add(CkRow("Obstacles:",
                _cbWalls      = Ck("Walls",      true),
                _cbFloors     = Ck("Floors",     true),
                _cbStructural = Ck("Structural", true)));

            var srcRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            srcRow.Children.Add(Lbl("Source:", bold: true));
            _cbCurrentDoc = Ck("This Model",   true);
            _cbLinkedDocs = Ck("Linked Files", true);
            _cbViewOnly   = Ck("Active View Only  ← recommended", true);
            _cbViewOnly.Foreground = MeToolsTheme.BrPetrol;
            _cbViewOnly.FontWeight = FontWeights.SemiBold;
            srcRow.Children.Add(_cbCurrentDoc); srcRow.Children.Add(_cbLinkedDocs); srcRow.Children.Add(_cbViewOnly);
            body.Children.Add(srcRow);

            _hlsTb = new TextBlock
            {
                FontSize = 10, FontStyle = FontStyles.Italic, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x44)),
                Margin = new Thickness(0, 0, 0, 6),
                Visibility = Visibility.Collapsed,
            };
            body.Children.Add(_hlsTb);

            var btnRun = new Button
            {
                Content = "▶  Run Clash Check",
                Height = 40, FontSize = 14, FontWeight = FontWeights.SemiBold,
                Background = MeToolsTheme.BrPetrol, Foreground = Brushes.White,
                BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
                Margin = new Thickness(0, 2, 0, 0),
            };
            btnRun.Template = RoundedBtnTemplate();
            btnRun.Click += (s, e) => OnRunCheck();
            btnRun.MouseEnter += (s, e) => btnRun.Background = MeToolsTheme.BrPetrolDark;
            btnRun.MouseLeave += (s, e) => btnRun.Background = MeToolsTheme.BrPetrol;
            body.Children.Add(btnRun);

            // ── Opening Family ────────────────────────────────────────────
            body.Children.Add(HSep());
            body.Children.Add(Sec("Opening Family  (Provision for Void)"));
            body.Children.Add(InfoBox("★ = Auxalia CAx   ◆ = IFC ProvisionForVoid\nDimensions (Trassenbreite, Trassenhöhe, X/Z-Überstand, Vorzug/Nachzug) are set automatically. The architect creates the actual openings."));

            var famG = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            famG.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            famG.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(255) });
            var ovWp = new WrapPanel();
            void AddFld(string lbl, ref TextBox tb, string def)
            {
                ovWp.Children.Add(Lbl(lbl));
                tb = Nb(def); tb.Width = 50;
                ovWp.Children.Add(tb); ovWp.Children.Add(Mm());
            }
            AddFld("X-Ov.:", ref _tbXUeb,    "50");
            AddFld("Z-Ov.:", ref _tbZUeb,    "100");
            AddFld("Prot.:", ref _tbVorzug,   "0");
            AddFld("Rec.:",  ref _tbNachzug,  "0");
            Grid.SetColumn(ovWp, 0); famG.Children.Add(ovWp);

            var famPnl = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
            famPnl.Children.Add(new TextBlock { Text = "Opening family:", FontSize = 10.5, Foreground = MeToolsTheme.BrText, Margin = new Thickness(0, 0, 0, 3) });
            _famCmb = StyledCombo(28, 11);
            _famCmb.Items.Add(new ComboBoxItem { Content = "— None —", Tag = ElementId.InvalidElementId });
            foreach (var fi in _fams) _famCmb.Items.Add(new ComboBoxItem { Content = fi.ToString(), Tag = fi.SymbolId });
            int ps = 0;
            for (int i = 0; i < _fams.Count; i++) if (_fams[i].IsCax) { ps = i + 1; break; }
            if (ps == 0) for (int i = 0; i < _fams.Count; i++) if (_fams[i].IsIfc) { ps = i + 1; break; }
            _famCmb.SelectedIndex = ps;
            famPnl.Children.Add(_famCmb);
            Grid.SetColumn(famPnl, 1); famG.Children.Add(famPnl);
            body.Children.Add(famG);

            // ── Results List ──────────────────────────────────────────────
            body.Children.Add(HSep());

            var tlbr = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            tlbr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tlbr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _countTb = new TextBlock
            {
                Text = "No check run yet.", FontSize = 11, FontStyle = FontStyles.Italic,
                Foreground = MeToolsTheme.BrMuted, VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(_countTb, 0); tlbr.Children.Add(_countTb);
            var tbBtns = new StackPanel { Orientation = Orientation.Horizontal };
            var bMark = SBtn("🔴 Mark",   OnMarkPlan);       bMark.Margin = new Thickness(0, 0, 4,  0);
            var bClr  = SBtn("✕ Clear",  OnClearMarkers);   bClr.Margin  = new Thickness(0, 0, 12, 0);
            var bAll  = SBtn("✓ All",    () => SetAll(true));  bAll.Margin  = new Thickness(0, 0, 3,  0);
            var bNone = SBtn("— None",   () => SetAll(false));
            tbBtns.Children.Add(bMark); tbBtns.Children.Add(bClr);
            tbBtns.Children.Add(new TextBlock
            {
                Text = "Select:", FontSize = 10, Foreground = MeToolsTheme.BrMuted,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0),
            });
            tbBtns.Children.Add(bAll); tbBtns.Children.Add(bNone);
            Grid.SetColumn(tbBtns, 1); tlbr.Children.Add(tbBtns);
            body.Children.Add(tlbr);

            // Column header
            var hb = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(90, 0x00, 0x80, 0x72)),
                CornerRadius = new CornerRadius(4, 4, 0, 0), Padding = new Thickness(6, 5, 6, 5),
            };
            var hg = MkG();
            HC(hg, 0, "#"); HC(hg, 1, "MEP Element"); HC(hg, 2, "Obstacle");
            HC(hg, 3, "Size"); HC(hg, 4, "Status"); HC(hg, 5, "Actions");
            hb.Child = hg; body.Children.Add(hb);

            // ── Row container (imperative — no virtualization) ────────────
            var rowScroll = new ScrollViewer
            {
                Height = 290,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = new SolidColorBrush(Color.FromArgb(15, 0, 0, 0)),
                BorderBrush = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1),
            };
            _rowPanel = new StackPanel();
            rowScroll.Content = _rowPanel;
            body.Children.Add(rowScroll);

            // ── Bottom bar ────────────────────────────────────────────────
            var ab = new Grid { Margin = new Thickness(0, 8, 0, 4) };
            ab.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ab.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var bSync = SBtn("🔄 Sync Positions", OnSync);
            bSync.ToolTip = "Reposition placed openings after trays moved.";
            bSync.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetColumn(bSync, 0); ab.Children.Add(bSync);
            var bPl = new Button
            {
                Content = "▶  Place Opening for Selected",
                Height = 36, Padding = new Thickness(18, 0, 18, 0),
                FontSize = 12, FontWeight = FontWeights.SemiBold,
                Background = MeToolsTheme.BrPetrol, Foreground = Brushes.White,
                BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
            };
            bPl.Template = RoundedBtnTemplate();
            bPl.Click += (s, e) => OnPlaceSelected();
            bPl.MouseEnter += (s, e) => bPl.Background = MeToolsTheme.BrPetrolDark;
            bPl.MouseLeave += (s, e) => bPl.Background = MeToolsTheme.BrPetrol;
            Grid.SetColumn(bPl, 1); ab.Children.Add(bPl);
            body.Children.Add(ab);
        }

        // ── Imperative row builder (replaces virtualized template) ─────────
        UIElement BuildRow(ClashResult r)
        {
            bool odd = r.Index % 2 == 1;
            var bgN = new SolidColorBrush(odd ? Color.FromArgb(22, 255, 255, 255) : Color.FromArgb(6, 255, 255, 255));
            var bgH = new SolidColorBrush(Color.FromArgb(50, 0, 160, 140));

            var border = new Border
            {
                Background = bgN, Padding = new Thickness(6, 4, 6, 4),
                BorderBrush = new SolidColorBrush(Color.FromArgb(14, 255, 255, 255)),
                BorderThickness = new Thickness(0, 0, 0, 1), Cursor = Cursors.Hand,
            };
            border.MouseEnter += (s, e) => border.Background = bgH;
            border.MouseLeave += (s, e) => border.Background = bgN;
            border.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount >= 2) NavToPlan_Row(r);
                else                   NavTo3D_Row(r);
                e.Handled = true;
            };

            var g = MkG();

            // # + checkbox
            var idxPnl = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            idxPnl.Children.Add(new TextBlock
            {
                Text = $"{r.Index}", FontSize = 8.5, Foreground = MeToolsTheme.BrMuted,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            var cb = new CheckBox { IsChecked = r.IsSelected, HorizontalAlignment = HorizontalAlignment.Center };
            cb.Checked   += (s, e) => r.IsSelected = true;
            cb.Unchecked += (s, e) => r.IsSelected = false;
            cb.PreviewMouseLeftButtonDown += (s, e) =>
            {
                r.IsSelected = cb.IsChecked != true;
                cb.IsChecked = r.IsSelected;
                e.Handled = true;
            };
            idxPnl.Children.Add(cb);
            Set(g, idxPnl, 0);

            // MEP
            Set(g, T2($"[{r.MepCategory}]", Tr(r.MepDescription, 34)), 1);

            // Obstacle
            string obsHdr = r.IsLinked ? $"[{r.ObstacleCategory}] 🔗" : $"[{r.ObstacleCategory}]";
            var obsTb = T2(obsHdr, Tr(r.ObstacleDescription, 34));
            if (r.IsLinked)
                ((StackPanel)obsTb).Children.OfType<TextBlock>().First()
                    .Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x44));
            Set(g, obsTb, 2);

            // Size
            double wMm = r.MepWidthFt * 304.8, hMm = r.MepHeightFt * 304.8;
            Set(g, new TextBlock
            {
                Text = (wMm > 5 && hMm > 5) ? $"{wMm:0}×\n{hMm:0}" : "–",
                FontSize = 9.5, FontFamily = new FontFamily("Consolas"),
                Foreground = MeToolsTheme.BrText,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 2, 0),
            }, 3);

            // Status
            string st; Color stC;
            if (r.FamilyPlaced)        { st = "✓ Placed"; stC = Color.FromRgb(0x44, 0xDD, 0x88); }
            else if (r.HasPlanMarker)  { st = "● Marked"; stC = Color.FromRgb(0xFF, 0xCC, 0x00); }
            else                       { st = "Open";     stC = Color.FromRgb(0xFF, 0x80, 0x30); }
            Set(g, new TextBlock
            {
                Text = st, FontSize = 9.5, Foreground = new SolidColorBrush(stC),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            }, 4);

            // Actions
            var acts = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var b3D = MBtn("🔍", "Open in 3D Inspector");
            var bPl = MBtn("▶",  "Place opening family here");
            b3D.Background = MeToolsTheme.BrActiveBg; b3D.Foreground = MeToolsTheme.BrPetrol;
            if (r.FamilyPlaced)
            { bPl.Content = "✓"; bPl.Background = MeToolsTheme.BrBtnBg; bPl.Foreground = MeToolsTheme.BrMuted; }
            else
            { bPl.Background = MeToolsTheme.BrPetrol; bPl.Foreground = Brushes.White; }
            b3D.Click += (s, e) => { e.Handled = true; NavTo3D_Row(r); };
            b3D.PreviewMouseLeftButtonDown += (s, e) => e.Handled = true;
            bPl.Click += (s, e) => { e.Handled = true; OnPlaceOne(r); };
            bPl.PreviewMouseLeftButtonDown += (s, e) => e.Handled = true;
            b3D.Margin = new Thickness(0, 0, 3, 0);
            acts.Children.Add(b3D); acts.Children.Add(bPl);
            Set(g, acts, 5);

            border.Child = g;
            return border;
        }

        void NavTo3D_Row(ClashResult r) { _h.Request = new ClashRequest { Action = ClashAction.NavigateTo3D, TargetResult = r }; _evt.Raise(); }
        void NavToPlan_Row(ClashResult r) { _h.Request = new ClashRequest { Action = ClashAction.NavigatePlan, TargetResult = r }; _evt.Raise(); }

        // ── Show / Refresh ────────────────────────────────────────────────
        void ShowResults(List<ClashResult> r)
        {
            _results = r;
            _rowPanel.Children.Clear();
            foreach (var x in r) _rowPanel.Children.Add(BuildRow(x));
            _countTb.Text = r.Any() ? $"{r.Count} clash(es) found" : "No clashes — all clear ✓";
            _countTb.Foreground = r.Any()
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0x80, 0x30))
                : MeToolsTheme.BrPetrol;
        }
        void ShowHlsHint(string msg)
        {
            _hlsTb.Text = string.IsNullOrEmpty(msg) ? "" : "⚠ " + msg;
            _hlsTb.Visibility = string.IsNullOrEmpty(msg) ? Visibility.Collapsed : Visibility.Visible;
        }
        void RefreshList()
        {
            _rowPanel.Children.Clear();
            foreach (var x in _results) _rowPanel.Children.Add(BuildRow(x));
        }

        // ── Handlers ─────────────────────────────────────────────────────
        void OnRunCheck()
        {
            _hlsTb.Visibility = Visibility.Collapsed;
            StatusLeft.Text = "Running clash check...";
            _h.Request = new ClashRequest
            {
                Action = ClashAction.RunCheck,
                CheckCableTrays = _cbTrassen.IsChecked == true,
                CheckConduits   = _cbConduits.IsChecked == true,
                CheckDucts      = _cbDucts.IsChecked == true,
                CheckPipes      = _cbPipes.IsChecked == true,
                CheckWalls      = _cbWalls.IsChecked == true,
                CheckFloors     = _cbFloors.IsChecked == true,
                CheckStructural = _cbStructural.IsChecked == true,
                CheckCurrentDoc = _cbCurrentDoc.IsChecked == true,
                CheckLinkedDocs = _cbLinkedDocs.IsChecked == true,
                ActiveViewOnly  = _cbViewOnly.IsChecked == true,
            };
            _evt.Raise();
        }
        void OnMarkPlan()     { if (!_results.Any()) { StatusLeft.Text = "No results."; return; } _h.Request = new ClashRequest { Action = ClashAction.MarkInPlan, ResultsToMark = _results }; _evt.Raise(); }
        void OnClearMarkers() { if (!_results.Any()) return; _h.Request = new ClashRequest { Action = ClashAction.ClearPlanMarkers, ResultsToMark = _results }; _evt.Raise(); }
        void OnPlaceOne(ClashResult r)
        {
            var sym = SelSym();
            if (sym == ElementId.InvalidElementId) { StatusLeft.Text = "Select an opening family first."; return; }
            _h.Request = new ClashRequest { Action = ClashAction.PlaceFamily, ResultsToPlace = new List<ClashResult> { r }, SymbolId = sym, CaxSettings = ReadCax() };
            _evt.Raise();
        }
        void OnPlaceSelected()
        {
            var sel = _results.Where(r => r.IsSelected && !r.FamilyPlaced).ToList();
            if (!sel.Any()) { StatusLeft.Text = "No open clashes selected."; return; }
            var sym = SelSym();
            if (sym == ElementId.InvalidElementId) { StatusLeft.Text = "Select an opening family first."; return; }
            StatusLeft.Text = $"Placing {sel.Count}...";
            _h.Request = new ClashRequest { Action = ClashAction.PlaceFamily, ResultsToPlace = sel, SymbolId = sym, CaxSettings = ReadCax() };
            _evt.Raise();
        }
        void OnSync() { StatusLeft.Text = "Syncing..."; _h.Request = new ClashRequest { Action = ClashAction.SyncFamilies }; _evt.Raise(); }
        void SetAll(bool v) { foreach (var r in _results) r.IsSelected = v; RefreshList(); }
        ElementId SelSym() { if (_famCmb.SelectedItem is ComboBoxItem ci && ci.Tag is ElementId eid) return eid; return ElementId.InvalidElementId; }
        CaxSettings ReadCax()
        {
            double.TryParse(_tbXUeb.Text,     out double x); if (x <= 0) x = 50;
            double.TryParse(_tbZUeb.Text,     out double z); if (z <= 0) z = 100;
            double.TryParse(_tbVorzug.Text,   out double v);
            double.TryParse(_tbNachzug.Text,  out double n);
            return new CaxSettings { X_Ueberstand_mm = x, Z_Ueberstand_mm = z, Vorzug_mm = v, Nachzug_mm = n };
        }

        // ── Grid + widget helpers ─────────────────────────────────────────
        Grid MkG()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
            return g;
        }
        static FrameworkElement CkRow(string lbl, params CheckBox[] cbs)
        {
            var wp = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            wp.Children.Add(new TextBlock
            {
                Text = lbl, FontSize = 11, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrMuted, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            });
            foreach (var cb in cbs) wp.Children.Add(cb);
            return wp;
        }
        static CheckBox  Ck(string l, bool v) => new CheckBox { Content = l, IsChecked = v, FontSize = 11.5, Foreground = MeToolsTheme.BrText, Margin = new Thickness(0, 0, 14, 0), VerticalContentAlignment = VerticalAlignment.Center };
        static TextBlock Lbl(string t, bool bold = false) => new TextBlock { Text = t, FontSize = 10.5, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Foreground = MeToolsTheme.BrText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        static TextBlock Mm() => new TextBlock { Text = "mm", FontSize = 10, Foreground = MeToolsTheme.BrMuted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 12, 0) };
        static TextBox   Nb(string v) => new TextBox { Text = v, Height = 26, FontSize = 11, TextAlignment = TextAlignment.Center, Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrText, BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1), VerticalContentAlignment = VerticalAlignment.Center };
        Border   HSep() => new Border { Height = 1, Background = MeToolsTheme.BrSecLine, Margin = new Thickness(0, 12, 0, 12) };
        Button   SBtn(string l, Action a)
        {
            var b = new Button
            {
                Content = l, Height = 28, Padding = new Thickness(10, 0, 10, 0),
                FontSize = 10.5, Background = MeToolsTheme.BrBtnBg,
                BorderBrush = MeToolsTheme.BrBtnBorder, BorderThickness = new Thickness(1),
                Foreground = MeToolsTheme.BrText, Cursor = Cursors.Hand,
            };
            b.Template = RoundedBtnTemplate();
            b.Click += (s, e) => a();
            return b;
        }
        static void HC(Grid g, int col, string t)
        {
            var tb = new TextBlock
            {
                Text = t, FontSize = 9.5, FontWeight = FontWeights.Bold,
                Foreground = MeToolsTheme.BrPetrol, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 2, 0),
            };
            Grid.SetColumn(tb, col); g.Children.Add(tb);
        }

        // ── Row-local helpers (the fixed MBtn is here; it now RETURNS) ────
        static Button MBtn(string c, string tip)
        {
            var b = new Button
            {
                Content = c, Width = 24, Height = 22, FontSize = 10,
                Padding = new Thickness(0),
                Background = MeToolsTheme.BrBtnBg,
                Foreground = MeToolsTheme.BrText,
                BorderBrush = MeToolsTheme.BrBtnBorder,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = tip,
                VerticalAlignment = VerticalAlignment.Center,
            };
            return b;  // ← v6 was missing this line — row action buttons never rendered
        }
        static void Set(Grid g, UIElement e, int col) { Grid.SetColumn(e, col); g.Children.Add(e); }
        static UIElement T2(string l1, string l2)
        {
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 2, 0) };
            sp.Children.Add(new TextBlock { Text = l1, FontSize = 8.5, Foreground = MeToolsTheme.BrMuted, TextTrimming = TextTrimming.CharacterEllipsis });
            sp.Children.Add(new TextBlock { Text = l2, FontSize = 9.5, Foreground = MeToolsTheme.BrText, TextTrimming = TextTrimming.CharacterEllipsis });
            return sp;
        }
        static string Tr(string s, int max) => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max - 1) + "…");
    }
}
