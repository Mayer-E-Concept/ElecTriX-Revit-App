// CollisionCheckerWindow.cs — ME-Tools | Clash Detector  v5
// Mayer E-Concept SRL — Pure C# WPF (no XAML)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Color    = System.Windows.Media.Color;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox  = System.Windows.Controls.TextBox;

namespace METools.ClashDetector
{
    public class ClashDetectorWindow : METools.MeToolsWindowBase
    {
        readonly ExternalEvent         _evt;
        readonly ClashDetectorHandler  _h;
        readonly List<PenFamilyInfo>   _fams;

        // Settings
        CheckBox _cbTrassen, _cbConduits, _cbDucts, _cbPipes;
        CheckBox _cbWalls, _cbFloors, _cbStructural;
        CheckBox _cbCurrentDoc, _cbLinkedDocs;

        // Family + overhangs
        ComboBox _famCmb;
        TextBox  _tbXUeb, _tbZUeb, _tbVorzug, _tbNachzug;

        // Results
        StackPanel  _navList;
        TextBlock   _countTb, _hlsTb;
        Button      _btnMark, _btnClear, _btnPlaceAll, _btnSync;
        List<ClashResult> _results    = new List<ClashResult>();
        List<NavRow>      _navRows    = new List<NavRow>();

        public ClashDetectorWindow(ExternalEvent evt, ClashDetectorHandler h, List<PenFamilyInfo> fams)
        {
            _evt = evt; _h = h; _fams = fams;
            h.OnStatus  = m => Dispatcher.Invoke(() => StatusLeft.Text = m);
            h.OnResults = r => Dispatcher.Invoke(() => ShowResults(r));
            h.OnRefresh = ()=> Dispatcher.Invoke(() => RefreshNavRows());
            h.OnHlsInfo = m => Dispatcher.Invoke(() => ShowHlsHint(m));
            InitWindow("Clash Detector", 780);
            Build();
        }

        // ════════════════════════════════════════════════════════════════════
        // BUILD
        // ════════════════════════════════════════════════════════════════════
        void Build()
        {
            BuildStatusBar("Clash Detector — ready");

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 980, Background = MeToolsTheme.BrBg,
            };
            var body = new StackPanel { Margin = new Thickness(14, 10, 14, 10) };
            var wrap = new System.Windows.Controls.Grid();
            wrap.Children.Add(scroll);
            wrap.Children.Add(Watermark());
            RootDock.Children.Add(wrap);
            scroll.Content = body;

            // ── SECTION 1: CHECK SETTINGS ─────────────────────────────────
            body.Children.Add(Sec("Check Settings"));
            body.Children.Add(CRow("MEP:",
                _cbTrassen  = CB("Cable Trays",  true),
                _cbConduits = CB("Conduits",     true),
                _cbDucts    = CB("Ducts",        true),
                _cbPipes    = CB("Pipes",        true)));
            body.Children.Add(new TextBlock
            {
                Text = "  (Flex pipes / flex ducts / flex conduits are automatically excluded)",
                FontSize = 9.5, FontStyle = FontStyles.Italic, Foreground = MeToolsTheme.BrMuted,
                Margin = new Thickness(0, 0, 0, 4),
            });
            body.Children.Add(CRow("Obstacles:",
                _cbWalls      = CB("Walls",               true),
                _cbFloors     = CB("Floors",              true),
                _cbStructural = CB("Structural",          true)));
            body.Children.Add(CRow("Source:",
                _cbCurrentDoc = CB("This Model",          true),
                _cbLinkedDocs = CB("Linked Files",        true)));

            _hlsTb = new TextBlock
            {
                FontSize = 10.5, FontStyle = FontStyles.Italic, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x44)),
                Margin = new Thickness(0, 4, 0, 0),
                Visibility = System.Windows.Visibility.Collapsed,
            };
            body.Children.Add(_hlsTb);

            body.Children.Add(ActionBtn("▶  Run Clash Check", false, OnRunCheck));

            // ── SECTION 2: OPENING FAMILY ──────────────────────────────────
            body.Children.Add(HSep());
            body.Children.Add(Sec("Opening Family  (Provision for Void)"));
            body.Children.Add(InfoBox(
                "Select the family to place at each clash location (★ = Auxalia CAx, ◆ = IFC ProvisionForVoid).\n" +
                "Dimensions (Trassenhöhe, Trassenbreite, Überstand) are set automatically.  " +
                "The architect uses these families to create the actual openings."));

            // Family selector + overhangs in one compact grid
            body.Children.Add(BuildFamilyRow());

            // ── SECTION 3: RESULTS + NAVIGATION LIST ──────────────────────
            body.Children.Add(HSep());

            // Header row
            var hdr = new System.Windows.Controls.Grid { Margin = new Thickness(0, 0, 0, 6) };
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _countTb = new TextBlock
            {
                Text = "No check run yet.", FontSize = 11, FontStyle = FontStyles.Italic,
                Foreground = MeToolsTheme.BrMuted, VerticalAlignment = VerticalAlignment.Center,
            };
            System.Windows.Controls.Grid.SetColumn(_countTb, 0);
            hdr.Children.Add(_countTb);

            var hdrBtns = new StackPanel { Orientation = Orientation.Horizontal };
            _btnMark  = SBtn("🔴 Mark in Plan", OnMarkPlan);
            _btnClear = SBtn("✕ Clear",         OnClearMarkers);
            _btnMark.Margin  = new Thickness(0, 0, 5, 0);
            _btnClear.Margin = new Thickness(0, 0, 0, 0);
            hdrBtns.Children.Add(_btnMark);
            hdrBtns.Children.Add(_btnClear);
            System.Windows.Controls.Grid.SetColumn(hdrBtns, 1);
            hdr.Children.Add(hdrBtns);
            body.Children.Add(hdr);

            // Column labels
            body.Children.Add(BuildColHeader());

            // Navigation list (scrollable)
            var sv = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 400, Background = MeToolsTheme.BrBg,
            };
            _navList = new StackPanel();
            sv.Content = _navList;
            body.Children.Add(sv);

            // Bottom action bar
            body.Children.Add(BuildActionBar());
        }

        // Family selector + overhangs (compact)
        FrameworkElement BuildFamilyRow()
        {
            var g = new System.Windows.Controls.Grid { Margin = new Thickness(0, 4, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });

            // Left: overhangs in one compact WrapPanel
            var wp = new WrapPanel();
            wp.Children.Add(PL("X-Ov.:")); _tbXUeb   = Num("50");  _tbXUeb.Width  = 50; wp.Children.Add(_tbXUeb); wp.Children.Add(U());
            wp.Children.Add(PL("Z-Ov.:")); _tbZUeb   = Num("100"); _tbZUeb.Width  = 50; wp.Children.Add(_tbZUeb); wp.Children.Add(U());
            wp.Children.Add(PL("Prot.:"));  _tbVorzug  = Num("0"); _tbVorzug.Width  = 50; wp.Children.Add(_tbVorzug); wp.Children.Add(U());
            wp.Children.Add(PL("Rec.:"));  _tbNachzug = Num("0"); _tbNachzug.Width = 50; wp.Children.Add(_tbNachzug); wp.Children.Add(U());
            System.Windows.Controls.Grid.SetColumn(wp, 0);
            g.Children.Add(wp);

            // Right: family dropdown
            var famPanel = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            _famCmb = StyledCombo(28, 11);
            _famCmb.Items.Add(new ComboBoxItem { Content = "— None —", Tag = ElementId.InvalidElementId });
            foreach (var fi in _fams)
                _famCmb.Items.Add(new ComboBoxItem { Content = fi.ToString(), Tag = fi.SymbolId });
            int presel = 0;
            for (int i = 0; i < _fams.Count; i++) if (_fams[i].IsCax) { presel = i + 1; break; }
            if (presel == 0) for (int i = 0; i < _fams.Count; i++) if (_fams[i].IsIfc) { presel = i + 1; break; }
            _famCmb.SelectedIndex = presel;
            famPanel.Children.Add(_famCmb);
            System.Windows.Controls.Grid.SetColumn(famPanel, 1);
            g.Children.Add(famPanel);
            return g;
        }

        FrameworkElement BuildColHeader()
        {
            var b = new Border
            {
                Background   = new SolidColorBrush(Color.FromArgb(88, 0x00, 0x80, 0x72)),
                CornerRadius = new CornerRadius(4, 4, 0, 0),
                Padding      = new Thickness(6, 5, 6, 5),
            };
            var g = MG();
            CH(g, 0, "#");
            CH(g, 1, "MEP Element");
            CH(g, 2, "Obstacle");
            CH(g, 3, "Size");
            CH(g, 4, "Status");
            CH(g, 5, "Actions");
            b.Child = g; return b;
        }

        FrameworkElement BuildActionBar()
        {
            var bar = new System.Windows.Controls.Grid { Margin = new Thickness(0, 8, 0, 4) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { Orientation = Orientation.Horizontal };
            var bAll  = SBtn("✓ All",  () => SetAll(true));
            var bNone = SBtn("— None", () => SetAll(false));
            bAll.Margin = new Thickness(0, 0, 4, 0);
            left.Children.Add(bAll);
            left.Children.Add(bNone);
            System.Windows.Controls.Grid.SetColumn(left, 0);
            bar.Children.Add(left);

            var right = new StackPanel { Orientation = Orientation.Horizontal };
            _btnSync = SBtn("🔄 Sync Positions", OnSync);
            _btnSync.ToolTip = "Reposition all placed opening families after trays were moved.";
            _btnSync.Margin = new Thickness(0, 0, 8, 0);
            _btnPlaceAll = new Button
            {
                Content = "▶  Place Opening for Selected",
                Height = 34, Padding = new Thickness(16, 0, 16, 0), FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Background = MeToolsTheme.BrPetrol, Foreground = Brushes.White,
                BorderBrush = MeToolsTheme.BrPetrol, BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
            };
            _btnPlaceAll.Template = RoundedBtnTemplate();
            _btnPlaceAll.Click   += (s, e) => OnPlaceSelected();
            var bgP = MeToolsTheme.BrPetrol;
            var bgPD = MeToolsTheme.BrPetrolDark;
            _btnPlaceAll.MouseEnter += (s,e) => _btnPlaceAll.Background = bgPD;
            _btnPlaceAll.MouseLeave += (s,e) => _btnPlaceAll.Background = bgP;
            right.Children.Add(_btnSync);
            right.Children.Add(_btnPlaceAll);
            System.Windows.Controls.Grid.SetColumn(right, 1);
            bar.Children.Add(right);
            return bar;
        }

        // ════════════════════════════════════════════════════════════════════
        // POPULATE RESULTS
        // ════════════════════════════════════════════════════════════════════
        void ShowResults(List<ClashResult> results)
        {
            _results = results;
            _navRows = new List<NavRow>();
            _navList.Children.Clear();

            _countTb.Text = results.Any()
                ? $"{results.Count} clash(es) found"
                : "No clashes found — all clear ✓";
            _countTb.Foreground = results.Any()
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0x80, 0x30))
                : MeToolsTheme.BrPetrol;

            foreach (var r in results)
            {
                var row = BuildNavRow(r);
                _navRows.Add(row);
                _navList.Children.Add(row.Border);
            }
        }

        void ShowHlsHint(string msg)
        {
            _hlsTb.Text = string.IsNullOrEmpty(msg) ? "" : "⚠ " + msg;
            _hlsTb.Visibility = string.IsNullOrEmpty(msg)
                ? System.Windows.Visibility.Collapsed
                : System.Windows.Visibility.Visible;
        }

        void RefreshNavRows()
        {
            foreach (var row in _navRows) UpdateNavRow(row);
        }

        // ════════════════════════════════════════════════════════════════════
        // NAV ROW
        // ════════════════════════════════════════════════════════════════════
        private class NavRow
        {
            public Border     Border   { get; set; }
            public CheckBox   Chk      { get; set; }
            public TextBlock  StatusTb { get; set; }
            public Button     PlaceBtn { get; set; }
            public ClashResult Data    { get; set; }
        }

        NavRow BuildNavRow(ClashResult r)
        {
            bool odd = r.Index % 2 == 1;
            var bgN = new SolidColorBrush(odd ? Color.FromArgb(22,255,255,255) : Color.FromArgb(6,255,255,255));
            var bgH = new SolidColorBrush(Color.FromArgb(50, 0, 160, 140));

            var border = new Border
            {
                Background = bgN, Padding = new Thickness(6, 5, 6, 5),
                BorderBrush = new SolidColorBrush(Color.FromArgb(16,255,255,255)),
                BorderThickness = new Thickness(0,0,0,1), Cursor = Cursors.Hand,
            };
            border.MouseEnter += (s,e) => border.Background = bgH;
            border.MouseLeave += (s,e) => border.Background = bgN;

            // Single-click = 3D section box, double-click = plan view
            border.MouseLeftButtonDown += (s,e) =>
            {
                if (e.ClickCount >= 2)
                { _h.Request = new ClashRequest { Action=ClashAction.NavigatePlan, TargetResult=r }; _evt.Raise(); }
                else
                { _h.Request = new ClashRequest { Action=ClashAction.NavigateTo3D, TargetResult=r }; _evt.Raise(); }
                e.Handled = true;
            };

            var g = MG();
            int c = 0;

            // # (index + checkbox)
            var cb = new CheckBox { IsChecked=r.IsSelected, VerticalAlignment=VerticalAlignment.Center,
                HorizontalAlignment=HorizontalAlignment.Center };
            cb.Checked   += (s,e) => r.IsSelected = true;
            cb.Unchecked += (s,e) => r.IsSelected = false;
            cb.PreviewMouseLeftButtonDown += (s,e) => e.Handled = false;
            var idxPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            idxPanel.Children.Add(new TextBlock
            {
                Text = $"{r.Index}", FontSize = 9.5, Foreground = MeToolsTheme.BrMuted,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            idxPanel.Children.Add(cb);
            GC(g, idxPanel, c++);

            // MEP description (2 lines: category + short desc)
            GC(g, BT($"[{r.MepCategory}]\n{Tr(r.MepDescription, 34)}", 9.5), c++);

            // Obstacle
            string obsLabel = r.IsLinked
                ? $"[{r.ObstacleCategory}] 🔗{Tr(r.LinkName,14)}\n{Tr(r.ObstacleDescription,34)}"
                : $"[{r.ObstacleCategory}]\n{Tr(r.ObstacleDescription,34)}";
            var obsTb = BT(obsLabel, 9.5);
            if (r.IsLinked) obsTb.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x44));
            GC(g, obsTb, c++);

            // Size
            double wMm = r.MepWidthFt * 304.8, hMm = r.MepHeightFt * 304.8;
            GC(g, BT((wMm>5&&hMm>5) ? $"{wMm:0}×{hMm:0}" : "–", 9.5, mono:true), c++);

            // Status
            var statusTb = MkStatus(r);
            GC(g, statusTb, c++);

            // Actions: 🔍3D | ▶Place   (click row = 3D, dbl = plan)
            var actRow = new StackPanel { Orientation=Orientation.Horizontal, VerticalAlignment=VerticalAlignment.Center };
            var b3D = IBtn("🔍", "Open in 3D (click row)", () =>
            {
                _h.Request = new ClashRequest { Action=ClashAction.NavigateTo3D, TargetResult=r };
                _evt.Raise();
            }, accent:true);
            var bPlan = IBtn("→", "Go to plan view (double-click row)", () =>
            {
                _h.Request = new ClashRequest { Action=ClashAction.NavigatePlan, TargetResult=r };
                _evt.Raise();
            });
            var bPlace = new Button
            {
                Content = r.FamilyPlaced ? "✓" : "▶",
                Width = 24, Height = 22, FontSize = 10,
                Background = r.FamilyPlaced ? MeToolsTheme.BrBtnBg : MeToolsTheme.BrPetrol,
                Foreground = r.FamilyPlaced ? MeToolsTheme.BrMuted  : Brushes.White,
                BorderBrush = MeToolsTheme.BrPetrol, BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
                ToolTip = r.FamilyPlaced ? "Opening placed" : "Place opening family here",
            };
            bPlace.Template = RoundedBtnTemplate();
            bPlace.Click += (s,e) => { e.Handled=true; OnPlaceOne(r); };
            bPlace.PreviewMouseLeftButtonDown += (s,e) => e.Handled = true;
            b3D.Margin   = new Thickness(0,0,3,0);
            bPlan.Margin = new Thickness(0,0,3,0);
            actRow.Children.Add(b3D);
            actRow.Children.Add(bPlan);
            actRow.Children.Add(bPlace);
            GC(g, actRow, c++);

            border.Child = g;
            return new NavRow { Border=border, Chk=cb, StatusTb=statusTb, PlaceBtn=bPlace, Data=r };
        }

        TextBlock MkStatus(ClashResult r)
        {
            string t; Color clr;
            if (r.FamilyPlaced)      { t="✓ Placed"; clr=Color.FromRgb(0x44,0xDD,0x88); }
            else if (r.HasPlanMarker){ t="● Marked"; clr=Color.FromRgb(0xFF,0xCC,0x00); }
            else                     { t="Open";     clr=Color.FromRgb(0xFF,0x80,0x30); }
            return new TextBlock { Text=t, FontSize=9.5,
                Foreground=new SolidColorBrush(clr),
                HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Center };
        }

        void UpdateNavRow(NavRow row)
        {
            var r = row.Data; var nb = MkStatus(r);
            row.StatusTb.Text = nb.Text; row.StatusTb.Foreground = nb.Foreground;
            row.PlaceBtn.Content    = r.FamilyPlaced ? "✓" : "▶";
            row.PlaceBtn.Background = r.FamilyPlaced ? MeToolsTheme.BrBtnBg : MeToolsTheme.BrPetrol;
            row.PlaceBtn.Foreground = r.FamilyPlaced ? MeToolsTheme.BrMuted  : Brushes.White;
            row.PlaceBtn.ToolTip    = r.FamilyPlaced ? "Opening placed" : "Place opening family here";
        }

        // ════════════════════════════════════════════════════════════════════
        // HANDLERS
        // ════════════════════════════════════════════════════════════════════
        void OnRunCheck()
        {
            _hlsTb.Visibility = System.Windows.Visibility.Collapsed;
            StatusLeft.Text = "Running clash check...";
            _h.Request = new ClashRequest
            {
                Action = ClashAction.RunCheck,
                CheckCableTrays  = _cbTrassen.IsChecked   == true,
                CheckConduits    = _cbConduits.IsChecked   == true,
                CheckDucts       = _cbDucts.IsChecked     == true,
                CheckPipes       = _cbPipes.IsChecked     == true,
                CheckWalls       = _cbWalls.IsChecked     == true,
                CheckFloors      = _cbFloors.IsChecked    == true,
                CheckStructural  = _cbStructural.IsChecked == true,
                CheckCurrentDoc  = _cbCurrentDoc.IsChecked == true,
                CheckLinkedDocs  = _cbLinkedDocs.IsChecked == true,
            };
            _evt.Raise();
        }

        void OnMarkPlan()
        {
            if (!_results.Any()) { StatusLeft.Text = "No results to mark."; return; }
            _h.Request = new ClashRequest { Action=ClashAction.MarkInPlan, ResultsToMark=_results };
            _evt.Raise();
        }

        void OnClearMarkers()
        {
            if (!_results.Any()) return;
            _h.Request = new ClashRequest { Action=ClashAction.ClearPlanMarkers, ResultsToMark=_results };
            _evt.Raise();
        }

        void OnPlaceOne(ClashResult r)
        {
            var symId = SelSym();
            if (symId == ElementId.InvalidElementId)
            { StatusLeft.Text = "Please select an opening family first."; return; }
            _h.Request = new ClashRequest
            { Action=ClashAction.PlaceFamily, ResultsToPlace=new List<ClashResult>{r},
              SymbolId=symId, CaxSettings=ReadCax() };
            _evt.Raise();
        }

        void OnPlaceSelected()
        {
            var sel = _results.Where(r => r.IsSelected && !r.FamilyPlaced).ToList();
            if (!sel.Any()) { StatusLeft.Text = "No open clashes selected."; return; }
            var symId = SelSym();
            if (symId == ElementId.InvalidElementId)
            { StatusLeft.Text = "Please select an opening family first."; return; }
            StatusLeft.Text = $"Placing {sel.Count} opening(s)...";
            _h.Request = new ClashRequest
            { Action=ClashAction.PlaceFamily, ResultsToPlace=sel, SymbolId=symId, CaxSettings=ReadCax() };
            _evt.Raise();
        }

        void OnSync()
        {
            StatusLeft.Text = "Syncing opening positions...";
            _h.Request = new ClashRequest { Action=ClashAction.SyncFamilies };
            _evt.Raise();
        }

        void SetAll(bool v) { foreach (var row in _navRows) { row.Chk.IsChecked=v; row.Data.IsSelected=v; } }

        // ── Helpers ──────────────────────────────────────────────────────
        ElementId SelSym()
        {
            if (_famCmb.SelectedItem is ComboBoxItem ci && ci.Tag is ElementId eid) return eid;
            return ElementId.InvalidElementId;
        }
        CaxSettings ReadCax()
        {
            double.TryParse(_tbXUeb.Text,   out double x); if (x<=0) x=50;
            double.TryParse(_tbZUeb.Text,   out double z); if (z<=0) z=100;
            double.TryParse(_tbVorzug.Text, out double v);
            double.TryParse(_tbNachzug.Text,out double n);
            return new CaxSettings { X_Ueberstand_mm=x, Z_Ueberstand_mm=z, Vorzug_mm=v, Nachzug_mm=n };
        }

        // Column grid for result rows (matches column header)
        System.Windows.Controls.Grid MG()
        {
            var g = new System.Windows.Controls.Grid();
            ACD(g, 36);             // index + checkbox
            ACD(g, 1, star:true);  // MEP
            ACD(g, 1, star:true);  // Obstacle
            ACD(g, 68);            // Size
            ACD(g, 62);            // Status
            ACD(g, 80);            // Actions (3D | plan | place)
            return g;
        }

        // Widget helpers
        static FrameworkElement CRow(string lbl, params CheckBox[] cbs)
        {
            var wp = new WrapPanel { Margin = new Thickness(0,0,0,4) };
            wp.Children.Add(new TextBlock { Text=lbl, FontSize=11, FontWeight=FontWeights.SemiBold,
                Foreground=MeToolsTheme.BrMuted, VerticalAlignment=VerticalAlignment.Center,
                Margin=new Thickness(0,0,10,0) });
            foreach (var cb in cbs) wp.Children.Add(cb);
            return wp;
        }

        static CheckBox CB(string lbl, bool val) => new CheckBox
        { Content=lbl, IsChecked=val, FontSize=11.5, Foreground=MeToolsTheme.BrText,
          Margin=new Thickness(0,0,14,0), VerticalContentAlignment=VerticalAlignment.Center };

        static TextBlock PL(string t) => new TextBlock { Text=t, FontSize=10.5,
            Foreground=MeToolsTheme.BrText, VerticalAlignment=VerticalAlignment.Center,
            Margin=new Thickness(0,0,4,0) };

        static TextBlock U() => new TextBlock { Text="mm", FontSize=10, Foreground=MeToolsTheme.BrMuted,
            VerticalAlignment=VerticalAlignment.Center, Margin=new Thickness(3,0,12,0) };

        Border HSep() => new Border
        { Height=1, Background=MeToolsTheme.BrSecLine, Margin=new Thickness(0,12,0,12) };

        Button SBtn(string lbl, Action onClick)
        {
            var b = new Button { Content=lbl, Height=28, Padding=new Thickness(10,0,10,0),
                FontSize=10.5, Background=MeToolsTheme.BrBtnBg,
                BorderBrush=MeToolsTheme.BrBtnBorder, BorderThickness=new Thickness(1),
                Foreground=MeToolsTheme.BrText, Cursor=Cursors.Hand };
            b.Template = RoundedBtnTemplate();
            b.Click += (s,e) => onClick();
            return b;
        }

        Button IBtn(string icon, string tip, Action onClick, bool accent=false)
        {
            var b = new Button { Content=icon, Width=24, Height=22, FontSize=10,
                Background=accent?MeToolsTheme.BrActiveBg:MeToolsTheme.BrBtnBg,
                Foreground=accent?MeToolsTheme.BrPetrol:MeToolsTheme.BrMuted,
                BorderBrush=MeToolsTheme.BrBtnBorder, BorderThickness=new Thickness(1),
                Cursor=Cursors.Hand, ToolTip=tip, VerticalAlignment=VerticalAlignment.Center };
            b.Template = RoundedBtnTemplate();
            b.Click += (s,e) => { e.Handled=true; onClick(); };
            b.PreviewMouseLeftButtonDown += (s,e) => e.Handled=true;
            return b;
        }

        static TextBlock BT(string t, double fs=11, bool mono=false) => new TextBlock
        { Text=t, FontSize=fs, FontFamily=mono?new FontFamily("Consolas"):null,
          Foreground=MeToolsTheme.BrText, TextWrapping=TextWrapping.NoWrap,
          TextTrimming=TextTrimming.CharacterEllipsis,
          VerticalAlignment=VerticalAlignment.Center, Margin=new Thickness(2,0,2,0) };

        static void ACD(System.Windows.Controls.Grid g, double w, bool star=false)
            => g.ColumnDefinitions.Add(new ColumnDefinition
                { Width=star?new GridLength(w,GridUnitType.Star):new GridLength(w) });

        static void GC(System.Windows.Controls.Grid g, UIElement e, int col)
        { System.Windows.Controls.Grid.SetColumn(e,col); g.Children.Add(e); }

        static void CH(System.Windows.Controls.Grid g, int col, string t)
        {
            var tb = new TextBlock { Text=t, FontSize=9.5, FontWeight=FontWeights.Bold,
                Foreground=MeToolsTheme.BrPetrol,
                VerticalAlignment=VerticalAlignment.Center, Margin=new Thickness(2,0,2,0) };
            System.Windows.Controls.Grid.SetColumn(tb,col); g.Children.Add(tb);
        }

        static string Tr(string s, int max)
            => string.IsNullOrEmpty(s)?"":(s.Length<=max?s:s[..(max-1)]+"…");
    }
}
