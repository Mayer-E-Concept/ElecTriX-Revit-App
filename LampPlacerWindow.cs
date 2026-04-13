// LampPlacerWindow.cs — ME-Tools | Lamp Placer
// Mayer E-Concept SRL — WPF code-behind
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Grid       = System.Windows.Controls.Grid;
using ComboBox   = System.Windows.Controls.ComboBox;
using TextBox    = System.Windows.Controls.TextBox;
using Visibility = System.Windows.Visibility;

namespace METools.LampPlacer
{
    public class LampPlacerWindow : METools.MeToolsWindowBase
    {
        readonly ExternalEvent        _evt;
        readonly LampPlacerHandler    _h;
        readonly List<LampFamilyInfo> _fams;
        LampConfig _cfg = new LampConfig();

        ComboBox   _famCmb, _typCmb;
        Button     _btnArea, _btnGrid;
        StackPanel _areaSp, _gridSp;
        Button     _btnAuto, _btn0, _btn90;
        TextBox    _sqmTb, _wallTb, _offsetTb, _rowsTb, _colsTb;
        TextBlock  _distInfoTb;

        TextBox    _lineCountTb;
        Button     _btnAlong, _btnPerp;
        TextBlock  _lineInfoTb;

        ScrollViewer _scroll;
        StackPanel   _body;

        public LampPlacerWindow(ExternalEvent evt, LampPlacerHandler h, List<LampFamilyInfo> fams)
        {
            _evt = evt; _h = h; _fams = fams;
            h.OnStatus = m => Dispatcher.Invoke(() => StatusLeft.Text = m);
            h.OnPlaced = n => Dispatcher.Invoke(() => StatusLeft.Text = $"Done: {n} lamps placed.");
            InitWindow("Lamp Placer", 440);
            Build();
        }

        void Build()
        {
            int famCount = _fams.Select(f => f.FamilyName).Distinct().Count();
            BuildStatusBar($"{famCount} lighting families loaded");

            _scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 820, Background = MeToolsTheme.BrBg,
            };
            _body = new StackPanel { Margin = new Thickness(14, 12, 14, 14) };
            var root = new Grid();
            root.Children.Add(_scroll); root.Children.Add(Watermark());
            RootDock.Children.Add(root);
            _scroll.Content = _body;

            // ── FAMILY ──────────────────────────────────────────────────────
            _body.Children.Add(Sec("Lighting Family"));
            _famCmb = MeToolsWindowBase.StyledCombo(28, 12); _famCmb.Margin = new Thickness(0,0,0,6);
            _famCmb.Items.Add(new ComboBoxItem { Content = "-- Select Family --", Tag = "" });
            var seen = new HashSet<string>();
            foreach (var f in _fams.OrderBy(x => x.FamilyName))
                if (seen.Add(f.FamilyName))
                    _famCmb.Items.Add(new ComboBoxItem { Content = f.FamilyName, Tag = f.FamilyName });
            _famCmb.SelectedIndex = 0; _famCmb.SelectionChanged += FamChanged;
            _body.Children.Add(_famCmb);
            _typCmb = MeToolsWindowBase.StyledCombo(28, 12); _typCmb.Margin = new Thickness(0,0,0,14);
            _typCmb.SelectionChanged += TypChanged; _body.Children.Add(_typCmb);

            // ── DISTRIBUTION ────────────────────────────────────────────────
            _body.Children.Add(Sec("Distribution Mode"));
            var modeRow = HRow(0, 8);
            _btnArea = ToggleBtn("Area-based",  true,  () => SetDist(DistributionMode.AreaBased));
            _btnGrid = ToggleBtn("Manual Grid", false, () => SetDist(DistributionMode.ManualGrid));
            _btnArea.Margin = new Thickness(0,0,5,0);
            modeRow.Children.Add(_btnArea); modeRow.Children.Add(_btnGrid); _body.Children.Add(modeRow);

            _areaSp = new StackPanel { Margin = new Thickness(0,0,0,6) };
            var arRow = HRow(0, 0);
            arRow.Children.Add(Lbl("m² per lamp:", 0, 8));
            _sqmTb = Num("12"); _sqmTb.Width = 60; _sqmTb.TextChanged += (s,e) => UpdateDistInfo();
            arRow.Children.Add(_sqmTb);
            _distInfoTb = new TextBlock { FontSize=11, FontStyle=FontStyles.Italic,
                Foreground=MeToolsTheme.BrMuted, VerticalAlignment=VerticalAlignment.Center,
                Margin=new Thickness(10,0,0,0) };
            arRow.Children.Add(_distInfoTb); _areaSp.Children.Add(arRow); _body.Children.Add(_areaSp);

            _gridSp = new StackPanel { Visibility=Visibility.Collapsed, Margin=new Thickness(0,0,0,6) };
            var grRow = HRow(0, 0);
            grRow.Children.Add(Lbl("Rows:", 0, 6));
            _rowsTb = Num("2"); _rowsTb.Width=50; _rowsTb.TextChanged += (s,e) => UpdateDistInfo();
            grRow.Children.Add(_rowsTb); grRow.Children.Add(Lbl("×  Cols:", 10, 6));
            _colsTb = Num("2"); _colsTb.Width=50; _colsTb.TextChanged += (s,e) => UpdateDistInfo();
            grRow.Children.Add(_colsTb); _gridSp.Children.Add(grRow); _body.Children.Add(_gridSp);

            // ── WALL MARGIN ─────────────────────────────────────────────────
            _body.Children.Add(Sec("Wall Margin"));
            var wmRow = HRow(0, 14);
            wmRow.Children.Add(Lbl("Min. distance from wall (mm):", 0, 8));
            _wallTb = Num("1500"); _wallTb.Width=80; wmRow.Children.Add(_wallTb);
            _body.Children.Add(wmRow);

            // ── ROTATION ────────────────────────────────────────────────────
            _body.Children.Add(Sec("Rotation"));
            var rotRow = HRow(0, 14);
            _btnAuto = ToggleBtn("Auto", true,  () => SetRot(RotationMode.Auto));
            _btn0    = ToggleBtn("0°",   false, () => SetRot(RotationMode.Deg0));
            _btn90   = ToggleBtn("90°",  false, () => SetRot(RotationMode.Deg90));
            _btnAuto.MinWidth=60; _btn0.MinWidth=50; _btn90.MinWidth=50;
            _btnAuto.Margin=new Thickness(0,0,4,0); _btn0.Margin=new Thickness(0,0,4,0);
            rotRow.Children.Add(_btnAuto); rotRow.Children.Add(_btn0); rotRow.Children.Add(_btn90);
            _body.Children.Add(rotRow);

            // ── HEIGHT ──────────────────────────────────────────────────────
            _body.Children.Add(Sec("Height (UKD)"));
            var hhRow = HRow(0, 14);
            hhRow.Children.Add(Lbl("Offset below ceiling (mm):", 0, 8));
            _offsetTb = Num("0"); _offsetTb.Width=70; hhRow.Children.Add(_offsetTb);
            hhRow.Children.Add(new TextBlock { Text="  (0 = flush with ceiling)", FontSize=10,
                Foreground=MeToolsTheme.BrMuted, VerticalAlignment=VerticalAlignment.Center });
            _body.Children.Add(hhRow);

            _body.Children.Add(InfoBox("Lamps placed on ceiling face · Room boundaries checked · Height = room ceiling"));

            // ── PLACE ───────────────────────────────────────────────────────
            _body.Children.Add(Sec("Place"));
            _body.Children.Add(TwoCol(
                ActionBtn("▶  Place in Room",  false, () => DoPlace(LampAction.PlaceSingle)),
                ActionBtn("⊕  Multiple Rooms", true,  () => DoPlace(LampAction.PlaceMulti))));

            // ── REFRESH ─────────────────────────────────────────────────────
            _body.Children.Add(Sec("Refresh"));
            var bRef1 = ActionBtn("⟳  Refresh Room",  false, () => DoPlace(LampAction.RefreshRoom));
            var bRef2 = ActionBtn("⟳  Refresh Multi", true,  () => DoPlace(LampAction.RefreshMulti));
            bRef1.ToolTip = "Delete lamps in selected room and re-place.";
            bRef2.ToolTip = "Pick multiple rooms — delete and re-place in each.";
            _body.Children.Add(TwoCol(bRef1, bRef2));

            // ── TOOLS ───────────────────────────────────────────────────────
            _body.Children.Add(Sec("Tools"));
            var bRot = ActionBtn("↻ 90°  Rotate Room Lamps", true, () => DoPlace(LampAction.RotateRoom));
            bRot.HorizontalAlignment = HorizontalAlignment.Stretch;
            _body.Children.Add(bRot);

            // ── LINE PLACEMENT ───────────────────────────────────────────────
            _body.Children.Add(Sec("Line Placement"));

            // Count input (only user input for line placement)
            var lineRow = HRow(4, 4);
            lineRow.Children.Add(Lbl("Number of lamps:", 0, 8));
            _lineCountTb = Num("4"); _lineCountTb.Width = 50;
            _lineCountTb.TextChanged += (s, e) => UpdateLineInfo();
            lineRow.Children.Add(_lineCountTb);
            _body.Children.Add(lineRow);

            // Auto notes
            _body.Children.Add(new TextBlock
            {
                Text = "Lamp length: auto from selected family  ·  Axis = line length ÷ count  ·  Margin = axis ÷ 2",
                FontSize=10, FontStyle=FontStyles.Italic,
                Foreground=MeToolsTheme.BrMuted, Margin=new Thickness(0,0,0,8),
                TextWrapping=TextWrapping.Wrap
            });

            // Orientation
            _body.Children.Add(Sec("Lamp Orientation on Line"));
            var orientRow = HRow(0, 6);
            _btnAlong = ToggleBtn("Along Line",    true,  () => SetLineOrientation(LineOrientation.AlongLine));
            _btnPerp  = ToggleBtn("Perpendicular", false, () => SetLineOrientation(LineOrientation.Perpendicular));
            _btnAlong.MinWidth=100; _btnPerp.MinWidth=110; _btnAlong.Margin=new Thickness(0,0,5,0);
            orientRow.Children.Add(_btnAlong); orientRow.Children.Add(_btnPerp);
            _body.Children.Add(orientRow);

            // Live info
            _lineInfoTb = new TextBlock
            {
                FontSize=11, FontStyle=FontStyles.Italic,
                Foreground=MeToolsTheme.BrMuted, Margin=new Thickness(0,4,0,6),
                TextWrapping=TextWrapping.Wrap
            };
            _body.Children.Add(_lineInfoTb);

            // Draw button
            var drawBtn = ActionBtn("✏  Draw Line  (click start → end)", true,
                () => DoPlace(LampAction.PlaceLine));
            drawBtn.ToolTip =
                "Click start point, then end point in a ceiling plan view.\n" +
                "H+V crosshair guide lines appear at start point (deleted after placement).\n" +
                "Snaps to 0°/45°/90° within ±5°. Diagonal lines work freely.\n" +
                "Lamp length is read automatically from the selected family.\n" +
                "Count is reduced automatically if lamps don't fit the line.";
            _body.Children.Add(drawBtn);

            SetLineOrientation(LineOrientation.AlongLine);
            UpdateDistInfo();
            UpdateLineInfo();
        }

        void SetLineOrientation(LineOrientation o)
        {
            _cfg.LineOrientation = o;
            UpdateToggle(_btnAlong, o == LineOrientation.AlongLine);
            UpdateToggle(_btnPerp,  o == LineOrientation.Perpendicular);
        }

        void UpdateLineInfo()
        {
            if (_lineInfoTb == null) return;
            int n = int.TryParse(_lineCountTb?.Text, out int ni) ? ni : 4;
            _lineInfoTb.Text =
                $"→ {n} lamps  ·  each lamp centered in line length ÷ {n}  ·  no lamp extends beyond line";
        }

        void DoPlace(LampAction action)
        {
            _cfg.WallMargin = double.TryParse(_wallTb?.Text,   out double w)  ? w  : 1500;
            _cfg.UKDOffset  = double.TryParse(_offsetTb?.Text, out double o)  ? o  : 0;
            _cfg.SqmPerLamp = double.TryParse(_sqmTb?.Text,    out double s)  ? s  : 12;
            _cfg.ManualRows = int.TryParse(_rowsTb?.Text,       out int    r)  ? r  : 2;
            _cfg.ManualCols = int.TryParse(_colsTb?.Text,       out int    c)  ? c  : 2;
            _cfg.LineCount  = int.TryParse(_lineCountTb?.Text,  out int    lc) ? lc : 4;

            bool needsFamily = action != LampAction.RotateRoom;
            ElementId symId  = ElementId.InvalidElementId;
            if (needsFamily)
            {
                symId = _fams.FirstOrDefault(f =>
                    f.FamilyName == _cfg.FamilyName && f.TypeName == _cfg.TypeName)?.SymbolId
                    ?? ElementId.InvalidElementId;
                if (symId == ElementId.InvalidElementId)
                { StatusLeft.Text = "Please select a family and type first."; return; }
            }

            _h.Request = new LampRequest { Action = action, Config = _cfg, SymbolId = symId };
            StatusLeft.Text = action switch
            {
                LampAction.RotateRoom   => "Click a room to rotate all lamps 90°...",
                LampAction.RefreshRoom  => "Click a room to refresh lamps...",
                LampAction.RefreshMulti => "Click rooms — ESC when done...",
                LampAction.PlaceMulti   => "Click rooms — ESC when done",
                LampAction.PlaceLine    => "Click start point of lamp line...",
                _                       => "Click a room to place lamps"
            };
            _evt.Raise();
        }

        protected override void OnThemeChanged() => ApplyTheme();

        void ApplyTheme()
        {
            if (_scroll != null) _scroll.Background = MeToolsTheme.BrBg;
            if (_body   != null) _body.Background   = MeToolsTheme.BrBg;
            foreach (var tb in new[] { _sqmTb, _wallTb, _offsetTb, _rowsTb, _colsTb, _lineCountTb })
            {
                if (tb == null) continue;
                tb.Background=MeToolsTheme.BrInput; tb.Foreground=MeToolsTheme.BrText;
                tb.BorderBrush=MeToolsTheme.BrBorder; tb.CaretBrush=MeToolsTheme.BrText;
            }
            if (_famCmb    != null) MeToolsWindowBase.ApplyComboStyle(_famCmb);
            if (_typCmb    != null) MeToolsWindowBase.ApplyComboStyle(_typCmb);
            if (_distInfoTb != null) _distInfoTb.Foreground=MeToolsTheme.BrMuted;
            if (_lineInfoTb != null) _lineInfoTb.Foreground=MeToolsTheme.BrMuted;
            SetDist(_cfg.Distribution); SetRot(_cfg.Rotation);
            SetLineOrientation(_cfg.LineOrientation);
        }

        void FamChanged(object s, SelectionChangedEventArgs e)
        {
            var name = (_famCmb.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            _cfg.FamilyName = name;
            _typCmb.SelectionChanged -= TypChanged; _typCmb.Items.Clear();
            foreach (var t in _fams.Where(f => f.FamilyName == name).OrderBy(f => f.TypeName))
                _typCmb.Items.Add(t.TypeName);
            if (_typCmb.Items.Count > 0) { _typCmb.SelectedIndex=0; _cfg.TypeName=_typCmb.Items[0] as string ?? ""; }
            _typCmb.SelectionChanged += TypChanged;
        }

        void TypChanged(object s, SelectionChangedEventArgs e)
            => _cfg.TypeName = _typCmb.SelectedItem as string ?? "";

        void SetDist(DistributionMode m)
        {
            _cfg.Distribution = m;
            bool isArea = m == DistributionMode.AreaBased;
            UpdateToggle(_btnArea, isArea); UpdateToggle(_btnGrid, !isArea);
            if (_areaSp != null) _areaSp.Visibility = isArea ? Visibility.Visible : Visibility.Collapsed;
            if (_gridSp != null) _gridSp.Visibility = !isArea ? Visibility.Visible : Visibility.Collapsed;
            UpdateDistInfo();
        }

        void SetRot(RotationMode r)
        {
            _cfg.Rotation = r;
            UpdateToggle(_btnAuto, r==RotationMode.Auto);
            UpdateToggle(_btn0,    r==RotationMode.Deg0);
            UpdateToggle(_btn90,   r==RotationMode.Deg90);
        }

        new void UpdateToggle(Button b, bool active)
        {
            if (b == null) return;
            b.Background  = active ? MeToolsTheme.BrActiveBg  : MeToolsTheme.BrSurface;
            b.BorderBrush = active ? MeToolsTheme.BrPetrol     : MeToolsTheme.BrBtnBorder;
            b.Foreground  = active ? MeToolsTheme.BrActiveFg   : MeToolsTheme.BrMuted;
        }

        void UpdateDistInfo()
        {
            if (_distInfoTb == null) return;
            if (_cfg.Distribution == DistributionMode.AreaBased)
            {
                double sqm = double.TryParse(_sqmTb?.Text, out double v) ? v : 12;
                _distInfoTb.Text = $"→ auto grid  ·  room area ÷ {sqm} m²/lamp";
            }
            else
            {
                int ri = int.TryParse(_rowsTb?.Text, out int rv) ? rv : 2;
                int ci = int.TryParse(_colsTb?.Text, out int cv) ? cv : 2;
                _distInfoTb.Text = $"→ {ri} × {ci} = {ri*ci} lamps per room";
            }
        }

        StackPanel HRow(double top, double bottom)
            => new StackPanel { Orientation=Orientation.Horizontal, Margin=new Thickness(0,top,0,bottom) };

        Grid TwoCol(Button left, Button right)
        {
            var g = new Grid { Margin=new Thickness(0,2,0,4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(5) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1.3,GridUnitType.Star) });
            Grid.SetColumn(left,0); Grid.SetColumn(right,2);
            g.Children.Add(left); g.Children.Add(right); return g;
        }

        TextBlock Lbl(string text, double left=0, double right=0)
            => new TextBlock { Text=text, FontSize=11, Foreground=MeToolsTheme.BrText,
                VerticalAlignment=VerticalAlignment.Center, Margin=new Thickness(left,0,right,0) };
    }
}
