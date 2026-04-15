// LampPlacerWindow.cs — ME-Tools | Lamp Placer
// Mayer E-Concept SRL
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

        // Family
        ComboBox _famCmb, _typCmb;

        // Distribution
        Button     _btnArea, _btnGrid;
        StackPanel _areaSp, _gridSp;
        TextBox    _sqmTb, _rowsTb, _colsTb;
        TextBlock  _infoTb;

        // Wall / Rotation / Height
        TextBox _wallTb, _offsetTb;
        Button  _btnAuto, _btn0, _btn90;

        // Line Placement
        Button     _btnBySpacing, _btnByCount;
        Button     _btnAlongLine, _btnPerp;
        StackPanel _spacingSp, _countSp;
        TextBox    _spacingTb, _countTb;

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
            BuildStatusBar($"{famCount} lighting families");

            _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 820, Background = MeToolsTheme.BrBg };
            _body   = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
            var root = new Grid();
            root.Children.Add(_scroll);
            root.Children.Add(Watermark());
            RootDock.Children.Add(root);
            _scroll.Content = _body;

            // ── FAMILY ───────────────────────────────────────────────────
            _body.Children.Add(Sec("Lighting Family"));
            _famCmb = StyledCombo(28, 12); _famCmb.Margin = new Thickness(0, 0, 0, 6);
            _famCmb.Items.Add(new ComboBoxItem { Content = "-- Select Family --", Tag = "" });
            var seen = new HashSet<string>();
            foreach (var f in _fams.OrderBy(x => x.FamilyName))
                if (seen.Add(f.FamilyName))
                    _famCmb.Items.Add(new ComboBoxItem { Content = f.FamilyName, Tag = f.FamilyName });
            _famCmb.SelectedIndex = 0;
            _famCmb.SelectionChanged += FamChanged;
            _body.Children.Add(_famCmb);
            _typCmb = StyledCombo(28, 12); _typCmb.Margin = new Thickness(0, 0, 0, 14);
            _body.Children.Add(_typCmb);

            // ── DISTRIBUTION MODE ─────────────────────────────────────────
            _body.Children.Add(Sec("Distribution Mode"));
            var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            _btnArea = ToggleBtn("Area-based",  true,  () => SetDist(DistributionMode.AreaBased));
            _btnGrid = ToggleBtn("Manual Grid", false, () => SetDist(DistributionMode.ManualGrid));
            _btnArea.Margin = new Thickness(0, 0, 5, 0);
            modeRow.Children.Add(_btnArea); modeRow.Children.Add(_btnGrid);
            _body.Children.Add(modeRow);

            // Area
            _areaSp = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            var ar = new StackPanel { Orientation = Orientation.Horizontal };
            ar.Children.Add(Lbl("m² per lamp:"));
            _sqmTb = Num("12"); _sqmTb.Width = 60; _sqmTb.TextChanged += (s, e) => UpdateInfo();
            ar.Children.Add(_sqmTb);
            _infoTb = new TextBlock { FontSize = 11, FontStyle = FontStyles.Italic, Foreground = MeToolsTheme.BrMuted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            ar.Children.Add(_infoTb);
            _areaSp.Children.Add(ar);
            _body.Children.Add(_areaSp);

            // Grid
            _gridSp = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
            var gr = new StackPanel { Orientation = Orientation.Horizontal };
            gr.Children.Add(Lbl("Rows:"));
            _rowsTb = Num("2"); _rowsTb.Width = 50; _rowsTb.TextChanged += (s, e) => UpdateInfo();
            gr.Children.Add(_rowsTb);
            gr.Children.Add(Lbl("  ×  Cols:", 10));
            _colsTb = Num("2"); _colsTb.Width = 50; _colsTb.TextChanged += (s, e) => UpdateInfo();
            gr.Children.Add(_colsTb);
            _gridSp.Children.Add(gr);
            _body.Children.Add(_gridSp);

            // ── WALL MARGIN ───────────────────────────────────────────────
            _body.Children.Add(Sec("Wall Margin"));
            var wm = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            wm.Children.Add(Lbl("Min. distance from wall (mm):"));
            _wallTb = Num("1500"); _wallTb.Width = 80;
            wm.Children.Add(_wallTb);
            _body.Children.Add(wm);

            // ── ROTATION ─────────────────────────────────────────────────
            _body.Children.Add(Sec("Rotation"));
            var rr = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            _btnAuto = ToggleBtn("Auto", true,  () => SetRot(RotationMode.Auto));
            _btn0    = ToggleBtn("0°",   false, () => SetRot(RotationMode.Deg0));
            _btn90   = ToggleBtn("90°",  false, () => SetRot(RotationMode.Deg90));
            _btnAuto.MinWidth = 60; _btn0.MinWidth = 50; _btn90.MinWidth = 50;
            _btnAuto.Margin = new Thickness(0, 0, 4, 0); _btn0.Margin = new Thickness(0, 0, 4, 0);
            rr.Children.Add(_btnAuto); rr.Children.Add(_btn0); rr.Children.Add(_btn90);
            _body.Children.Add(rr);

            // ── HEIGHT ───────────────────────────────────────────────────
            _body.Children.Add(Sec("Height (UKD)"));
            var hh = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            hh.Children.Add(Lbl("Offset below UKD (mm):"));
            _offsetTb = Num("0"); _offsetTb.Width = 70;
            hh.Children.Add(_offsetTb);
            hh.Children.Add(new TextBlock { Text = "  (0 = flush with ceiling)", FontSize = 10, Foreground = MeToolsTheme.BrMuted, VerticalAlignment = VerticalAlignment.Center });
            _body.Children.Add(hh);

            _body.Children.Add(InfoBox("Height auto-detected from room/space ceiling (bb.Max.Z). Lamps centered symmetrically, room boundaries checked."));

            // ── PLACE BUTTONS ─────────────────────────────────────────────
            _body.Children.Add(Sec("Room Placement"));
            var placeRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            placeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            placeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            placeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var bPlace   = PlaceBtn("▶  Place",   true,  () => DoPlace(LampAction.PlaceMulti));
            var bRefresh = PlaceBtn("↺  Refresh", false, () => DoPlace(LampAction.RefreshMulti));
            bPlace.ToolTip   = "Click rooms or MEP spaces to place lamps. ESC to finish.";
            bRefresh.ToolTip = "Click rooms to delete and re-place lamps with current settings.";
            Grid.SetColumn(bPlace,   0);
            Grid.SetColumn(bRefresh, 2);
            placeRow.Children.Add(bPlace);
            placeRow.Children.Add(bRefresh);
            _body.Children.Add(placeRow);

            // ── DIVIDER ───────────────────────────────────────────────────
            _body.Children.Add(new Border
            {
                Height     = 1,
                Background = MeToolsTheme.BrBorder,
                Margin     = new Thickness(-14, 14, -14, 14),
            });

            // ── LINE PLACEMENT ────────────────────────────────────────────
            _body.Children.Add(Sec("Line Placement"));

            var lineModeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            _btnBySpacing = ToggleBtn("By Spacing", true,  () => SetLineMode(LineMode.BySpacing));
            _btnByCount   = ToggleBtn("By Count",   false, () => SetLineMode(LineMode.ByCount));
            _btnBySpacing.Margin = new Thickness(0, 0, 5, 0);
            lineModeRow.Children.Add(_btnBySpacing);
            lineModeRow.Children.Add(_btnByCount);
            _body.Children.Add(lineModeRow);

            // Spacing
            _spacingSp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            _spacingSp.Children.Add(Lbl("Spacing (mm):"));
            _spacingTb = Num("2000"); _spacingTb.Width = 80;
            _spacingSp.Children.Add(_spacingTb);
            _spacingSp.Children.Add(new TextBlock { Text = "  → auto count based on line length", FontSize = 10, Foreground = MeToolsTheme.BrMuted, VerticalAlignment = VerticalAlignment.Center });
            _body.Children.Add(_spacingSp);

            // Count
            _countSp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6), Visibility = Visibility.Collapsed };
            _countSp.Children.Add(Lbl("Number of lamps:"));
            _countTb = Num("4"); _countTb.Width = 60;
            _countSp.Children.Add(_countTb);
            _countSp.Children.Add(new TextBlock { Text = "  → evenly distributed on line", FontSize = 10, Foreground = MeToolsTheme.BrMuted, VerticalAlignment = VerticalAlignment.Center });
            _body.Children.Add(_countSp);

            // Orientation
            _body.Children.Add(Lbl("Lamp orientation on line:", 11));
            var orientRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
            _btnAlongLine = ToggleBtn("Along Line",    true,  () => SetLineOrientation(LineOrientation.AlongLine));
            _btnPerp      = ToggleBtn("Perpendicular", false, () => SetLineOrientation(LineOrientation.Perpendicular));
            _btnAlongLine.Margin = new Thickness(0, 0, 5, 0);
            orientRow.Children.Add(_btnAlongLine);
            orientRow.Children.Add(_btnPerp);
            _body.Children.Add(orientRow);

            _body.Children.Add(InfoBox(
                "Draw a model line or detail line in Revit first.\n" +
                "Then click 'Place on Line' and pick that line.\n" +
                "The line acts as reference — lamps are centered along it."));

            var bLine = PlaceBtn("→  Place on Line", true, () => DoPlace(LampAction.PlaceOnLine));
            bLine.Margin  = new Thickness(0, 4, 0, 0);
            bLine.ToolTip = "Pick an existing model line or detail line — lamps are placed along it.";
            _body.Children.Add(bLine);

            UpdateInfo();
        }

        // ── Helpers ───────────────────────────────────────────────────────
        private TextBlock Lbl(string text, int size = 11) => new TextBlock
        {
            Text = text, FontSize = size, Foreground = MeToolsTheme.BrText,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0)
        };

        private Button PlaceBtn(string label, bool primary, Action onClick)
        {
            var b = new Button
            {
                Content         = label,
                Height          = 36,
                FontSize        = 13,
                FontWeight      = FontWeights.SemiBold,
                Cursor          = Cursors.Hand,
                BorderThickness = new Thickness(1.5),
                Padding         = new Thickness(0, 0, 0, 0),
            };
            if (primary)
            {
                b.Background  = MeToolsTheme.BrPetrol;
                b.Foreground  = Brushes.White;
                b.BorderBrush = MeToolsTheme.BrPetrol;
            }
            else
            {
                b.Background  = MeToolsTheme.BrSurface;
                b.Foreground  = MeToolsTheme.BrPetrol;
                b.BorderBrush = MeToolsTheme.BrPetrol;
            }
            b.Template = RoundedBtnTemplate();
            b.Click += (s, e) => onClick?.Invoke();
            return b;
        }

        // ── Theme ─────────────────────────────────────────────────────────
        protected override void OnThemeChanged() => ApplyTheme();

        void ApplyTheme()
        {
            if (_scroll != null) _scroll.Background = MeToolsTheme.BrBg;
            foreach (var tb in new[] { _sqmTb, _wallTb, _offsetTb, _rowsTb, _colsTb, _spacingTb, _countTb })
            {
                if (tb == null) continue;
                tb.Background = MeToolsTheme.BrInput; tb.Foreground = MeToolsTheme.BrText;
                tb.BorderBrush = MeToolsTheme.BrBorder; tb.CaretBrush = MeToolsTheme.BrText;
            }
            if (_famCmb != null) ApplyComboStyle(_famCmb);
            if (_typCmb != null) ApplyComboStyle(_typCmb);
            if (_infoTb != null) _infoTb.Foreground = MeToolsTheme.BrMuted;
            SetDist(_cfg.Distribution); SetRot(_cfg.Rotation); SetLineMode(_cfg.LineMode); SetLineOrientation(_cfg.LineOrientation);
        }

        void FamChanged(object s, SelectionChangedEventArgs e)
        {
            var name = (_famCmb.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            _cfg.FamilyName = name;
            _typCmb.SelectionChanged -= TypChanged;
            _typCmb.Items.Clear();
            foreach (var t in _fams.Where(f => f.FamilyName == name).OrderBy(f => f.TypeName))
                _typCmb.Items.Add(t.TypeName);
            if (_typCmb.Items.Count > 0) { _typCmb.SelectedIndex = 0; _cfg.TypeName = _typCmb.Items[0] as string ?? ""; }
            _typCmb.SelectionChanged += TypChanged;
        }

        void TypChanged(object s, SelectionChangedEventArgs e) => _cfg.TypeName = _typCmb.SelectedItem as string ?? "";

        void SetDist(DistributionMode m)
        {
            _cfg.Distribution = m;
            bool isArea = m == DistributionMode.AreaBased;
            UpdateToggle(_btnArea, isArea); UpdateToggle(_btnGrid, !isArea);
            if (_areaSp != null) _areaSp.Visibility = isArea ? Visibility.Visible : Visibility.Collapsed;
            if (_gridSp != null) _gridSp.Visibility = !isArea ? Visibility.Visible : Visibility.Collapsed;
            UpdateInfo();
        }

        void SetRot(RotationMode r)
        {
            _cfg.Rotation = r;
            UpdateToggle(_btnAuto, r == RotationMode.Auto);
            UpdateToggle(_btn0,    r == RotationMode.Deg0);
            UpdateToggle(_btn90,   r == RotationMode.Deg90);
        }

        void SetLineOrientation(LineOrientation o)
        {
            _cfg.LineOrientation = o;
            UpdateToggle(_btnAlongLine, o == LineOrientation.AlongLine);
            UpdateToggle(_btnPerp,      o == LineOrientation.Perpendicular);
        }

        void SetLineMode(LineMode m)
        {
            _cfg.LineMode = m;
            bool bySpacing = m == LineMode.BySpacing;
            UpdateToggle(_btnBySpacing, bySpacing); UpdateToggle(_btnByCount, !bySpacing);
            if (_spacingSp != null) _spacingSp.Visibility = bySpacing ? Visibility.Visible : Visibility.Collapsed;
            if (_countSp   != null) _countSp.Visibility   = !bySpacing ? Visibility.Visible : Visibility.Collapsed;
        }

        void UpdateToggle(Button b, bool active)
        {
            if (b == null) return;
            b.Background  = active ? MeToolsTheme.BrActiveBg  : MeToolsTheme.BrSurface;
            b.BorderBrush = active ? MeToolsTheme.BrPetrol     : MeToolsTheme.BrBtnBorder;
            b.Foreground  = active ? MeToolsTheme.BrActiveFg   : MeToolsTheme.BrMuted;
        }

        void UpdateInfo()
        {
            if (_infoTb == null) return;
            if (_cfg.Distribution == DistributionMode.AreaBased)
            {
                double sqm = double.TryParse(_sqmTb?.Text, out double v) ? v : 12;
                _infoTb.Text = $"→ auto grid, room area ÷ {sqm} m²/lamp";
            }
            else
            {
                int r = int.TryParse(_rowsTb?.Text, out int rv) ? rv : 2;
                int c = int.TryParse(_colsTb?.Text, out int cv) ? cv : 2;
                _infoTb.Text = $"→ {r} × {c} = {r * c} lamps per room";
            }
        }

        void DoPlace(LampAction action)
        {
            _cfg.WallMargin  = double.TryParse(_wallTb?.Text,    out double w)  ? w  : 1500;
            _cfg.UKDOffset   = double.TryParse(_offsetTb?.Text,  out double o)  ? o  : 0;
            _cfg.SqmPerLamp  = double.TryParse(_sqmTb?.Text,     out double s)  ? s  : 12;
            _cfg.ManualRows  = int.TryParse(_rowsTb?.Text,        out int r)     ? r  : 2;
            _cfg.ManualCols  = int.TryParse(_colsTb?.Text,        out int c)     ? c  : 2;
            _cfg.LineSpacing = double.TryParse(_spacingTb?.Text,  out double sp) ? sp : 2000;
            _cfg.LineCount   = int.TryParse(_countTb?.Text,       out int lc)    ? lc : 4;

            ElementId symId = ElementId.InvalidElementId;
            if (action != LampAction.Redistribute)
            {
                symId = _fams.FirstOrDefault(f => f.FamilyName == _cfg.FamilyName && f.TypeName == _cfg.TypeName)?.SymbolId
                        ?? ElementId.InvalidElementId;
                if (symId == ElementId.InvalidElementId)
                { StatusLeft.Text = "Please select a family and type first."; return; }
            }

            _h.Request = new LampRequest { Action = action, Config = _cfg, SymbolId = symId };
            StatusLeft.Text =
                action == LampAction.PlaceMulti   ? "Click rooms/spaces — ESC when done..." :
                action == LampAction.RefreshMulti  ? "Click rooms to refresh — ESC when done..." :
                action == LampAction.PlaceOnLine   ? "Click start point of line..." :
                                                     "Placing...";
            _evt.Raise();
        }
    }
}
