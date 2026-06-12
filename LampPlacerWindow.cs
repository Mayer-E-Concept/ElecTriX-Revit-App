// LampPlacerWindow.cs — ME-Tools | Lamp Placer
// Mayer E-Concept SRL — Reines C# WPF
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Ellipse = System.Windows.Shapes.Ellipse;
using Color   = System.Windows.Media.Color;
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
        readonly List<LevelInfo>      _levels;
        readonly ElementId            _defaultLevelId;
        readonly List<string>         _lineStyles;
        LampConfig _cfg = new LampConfig();

        ComboBox   _famCmb, _typCmb, _lvlCmb, _lineStyleCmb;
        Button     _btnArea, _btnGrid, _btnLine;
        Button     _mainBtn, _multiBtn;
        StackPanel _areaSp, _gridSp, _lineSp;
        Button     _btnAuto, _btn0, _btn90;
        Button     _btnFace, _btnWP;
        TextBlock  _placeDetectTb;
        Button     _btnLineSpacing, _btnLineCount;
        Button     _btnLineAlong, _btnLinePerp;
        StackPanel _lineSpacingRow, _lineCountRow;
        TextBox    _sqmTb, _wallTb, _offsetTb, _rowsTb, _colsTb, _overlapTb;
        TextBox    _lineSpacingTb, _lineCountTb;
        TextBlock  _infoTb, _lvlHelpTb;

        // Alle theming-relevanten Elemente
        ScrollViewer _scroll;
        StackPanel   _body;
        Border       _infoBox;

        public LampPlacerWindow(ExternalEvent evt, LampPlacerHandler h,
                                List<LampFamilyInfo> fams,
                                List<LevelInfo> levels,
                                ElementId defaultLevelId,
                                List<string> lineStyles)
        {
            _evt = evt; _h = h; _fams = fams;
            _lineStyles     = lineStyles      ?? new List<string>();
            _levels         = levels          ?? new List<LevelInfo>();
            _defaultLevelId = defaultLevelId  ?? ElementId.InvalidElementId;

            h.OnStatus = m => Dispatcher.Invoke(() => StatusLeft.Text = m);
            h.OnPlaced = n => Dispatcher.Invoke(() => StatusLeft.Text = $"Done: {n} lamps placed.");
            h.OnPromptSpacing = def => ShowSpacingDialog(def);

            InitWindow("Lamp Placer", 440);
            Build();
        }

        void Build()
        {
            // Status Bar
            int famCount = _fams.Select(f => f.FamilyName).Distinct().Count();
            BuildStatusBar($"{famCount} lighting families");

            // Body
            _scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight   = 820,
                Background  = MeToolsTheme.BrBg,
            };
            _body = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };

            // Wasserzeichen
            var grid = new Grid();
            grid.Children.Add(_scroll);
            grid.Children.Add(Watermark());
            RootDock.Children.Add(grid);

            _scroll.Content = _body;

            // FAMILY
            _body.Children.Add(Sec("Lighting Family"));
            _famCmb = METools.MeToolsWindowBase.StyledCombo(28, 12); _famCmb.Margin = new Thickness(0, 0, 0, 6);
            _famCmb.Items.Add(new ComboBoxItem { Content = "-- Select Family --", Tag = "" });
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var f in _fams.OrderBy(x => x.FamilyName))
                if (seen.Add(f.FamilyName))
                    _famCmb.Items.Add(new ComboBoxItem { Content = f.FamilyName, Tag = f.FamilyName });
            _famCmb.SelectedIndex      = 0;
            _famCmb.SelectionChanged  += FamChanged;
            _body.Children.Add(_famCmb);

            _typCmb = METools.MeToolsWindowBase.StyledCombo(28, 12); _typCmb.Margin = new Thickness(0, 0, 0, 14);
            _body.Children.Add(_typCmb);

            // PLACEMENT (face vs work plane)
            _body.Children.Add(Sec("Placement"));
            var placeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            _btnFace = ToggleBtn("Place on Face",       false, () => SetSurface(PlacementSurface.Face));
            _btnWP   = ToggleBtn("Place on Work Plane", true,  () => SetSurface(PlacementSurface.WorkPlane));
            _btnFace.MinWidth = 120; _btnWP.MinWidth = 150;
            _btnFace.Margin = new Thickness(0, 0, 5, 0);
            placeRow.Children.Add(_btnFace); placeRow.Children.Add(_btnWP);
            _body.Children.Add(placeRow);
            _placeDetectTb = new TextBlock { Text = "Select a family to detect hosting.", FontSize = 10,
                Foreground = MeToolsTheme.BrMuted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
            _body.Children.Add(_placeDetectTb);
            UpdatePlacementDetection();

            // DISTRIBUTION MODE
            _body.Children.Add(Sec("Distribution Mode"));
            var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            _btnArea = ToggleBtn("Area-based",  true,  () => SetDist(DistributionMode.AreaBased));
            _btnGrid = ToggleBtn("Manual Grid", false, () => SetDist(DistributionMode.ManualGrid));
            _btnLine = ToggleBtn("Line",        false, () => SetDist(DistributionMode.Line));
            _btnArea.Margin = new Thickness(0, 0, 5, 0);
            _btnGrid.Margin = new Thickness(0, 0, 5, 0);
            modeRow.Children.Add(_btnArea);
            modeRow.Children.Add(_btnGrid);
            modeRow.Children.Add(_btnLine);
            _body.Children.Add(modeRow);

            // Area panel
            _areaSp = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            var ar = new StackPanel { Orientation = Orientation.Horizontal };
            ar.Children.Add(new TextBlock
            {
                Text = "m² per lamp:", FontSize = 11,
                Foreground = MeToolsTheme.BrText,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0)
            });
            _sqmTb = Num("12"); _sqmTb.Width = 60; _sqmTb.TextChanged += (s, e) => UpdateInfo();
            ar.Children.Add(_sqmTb);
            _infoTb = new TextBlock
            {
                FontSize = 11, FontStyle = FontStyles.Italic,
                Foreground = MeToolsTheme.BrMuted,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0)
            };
            ar.Children.Add(_infoTb);
            _areaSp.Children.Add(ar);
            _body.Children.Add(_areaSp);

            // Grid panel
            _gridSp = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
            var gr = new StackPanel { Orientation = Orientation.Horizontal };
            gr.Children.Add(new TextBlock { Text = "Rows:", FontSize = 11, Foreground = MeToolsTheme.BrText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            _rowsTb = Num("2"); _rowsTb.Width = 50; _rowsTb.TextChanged += (s, e) => UpdateInfo();
            gr.Children.Add(_rowsTb);
            gr.Children.Add(new TextBlock { Text = "×  Cols:", FontSize = 11, Foreground = MeToolsTheme.BrText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 6, 0) });
            _colsTb = Num("2"); _colsTb.Width = 50; _colsTb.TextChanged += (s, e) => UpdateInfo();
            gr.Children.Add(_colsTb);
            _gridSp.Children.Add(gr);
            _body.Children.Add(_gridSp);

            // Line panel
            _lineSp = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };

            // Line: spacing / count toggle
            var lineModeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            _btnLineSpacing = ToggleBtn("By spacing", true,  () => SetLineMode(LineMode.BySpacing));
            _btnLineCount   = ToggleBtn("By count",   false, () => SetLineMode(LineMode.ByCount));
            _btnLineSpacing.Margin = new Thickness(0, 0, 5, 0);
            lineModeRow.Children.Add(_btnLineSpacing);
            lineModeRow.Children.Add(_btnLineCount);
            _lineSp.Children.Add(lineModeRow);

            _lineSpacingRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            _lineSpacingRow.Children.Add(new TextBlock { Text = "Spacing (mm):", FontSize = 11, Foreground = MeToolsTheme.BrText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            _lineSpacingTb = Num("2000"); _lineSpacingTb.Width = 70; _lineSpacingTb.TextChanged += (s, e) => UpdateInfo();
            _lineSpacingRow.Children.Add(_lineSpacingTb);
            _lineSpacingRow.Children.Add(new TextBlock { Text = "  (default spacing; you confirm it after clicking the first lamp)", FontSize = 10, Foreground = MeToolsTheme.BrMuted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
            _lineSp.Children.Add(_lineSpacingRow);

            _lineCountRow = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 6) };
            _lineCountRow.Children.Add(new TextBlock { Text = "Count:", FontSize = 11, Foreground = MeToolsTheme.BrText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            _lineCountTb = Num("4"); _lineCountTb.Width = 50; _lineCountTb.TextChanged += (s, e) => UpdateInfo();
            _lineCountRow.Children.Add(_lineCountTb);
            _lineCountRow.Children.Add(new TextBlock { Text = "  (evenly divided along the line: equal wall + lamp gaps)", FontSize = 10, Foreground = MeToolsTheme.BrMuted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
            _lineSp.Children.Add(_lineCountRow);

            // Line: orientation toggle (along line / perpendicular)
            _lineSp.Children.Add(new TextBlock
            {
                Text = "Lamp orientation:", FontSize = 11,
                Foreground = MeToolsTheme.BrMuted,
                Margin = new Thickness(0, 4, 0, 4)
            });
            var lineRotRow = new StackPanel { Orientation = Orientation.Horizontal };
            _btnLineAlong = ToggleBtn("↔ Along line",    true,  () => SetLineRot(LineRotation.AlongLine));
            _btnLinePerp  = ToggleBtn("⟂ Perpendicular", false, () => SetLineRot(LineRotation.Perpendicular));
            _btnLineAlong.Margin = new Thickness(0, 0, 5, 0);
            lineRotRow.Children.Add(_btnLineAlong);
            lineRotRow.Children.Add(_btnLinePerp);
            _lineSp.Children.Add(lineRotRow);

            // Drawing is done with Revit's native Detail Line tool (rubber-band + snaps).
            _lineSp.Children.Add(new TextBlock {
                Text = "Draw guide line(s) with Revit's Detail Line tool, then click Place Along "
                     + "Line and select them. Keep/delete of the lines is asked afterwards.",
                FontSize = 10, Foreground = MeToolsTheme.BrMuted, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 4) });

            _body.Children.Add(_lineSp);

            // WALL MARGIN
            _body.Children.Add(Sec("Wall Margin"));
            var wm = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            wm.Children.Add(new TextBlock { Text = "Min. distance from wall (mm):", FontSize = 11, Foreground = MeToolsTheme.BrText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            _wallTb = Num("1500"); _wallTb.Width = 80;
            wm.Children.Add(_wallTb);
            _body.Children.Add(wm);

            var ov = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            ov.Children.Add(new TextBlock { Text = "Min. gap to existing fixture (mm):", FontSize = 11, Foreground = MeToolsTheme.BrText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            _overlapTb = Num("300"); _overlapTb.Width = 80;
            ov.Children.Add(_overlapTb);
            _body.Children.Add(ov);

            // ROTATION (Area / Grid)
            _body.Children.Add(Sec("Rotation"));
            var rr = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            _btnAuto = ToggleBtn("Auto", true,  () => SetRot(RotationMode.Auto));
            _btn0    = ToggleBtn("0°",   false, () => SetRot(RotationMode.Deg0));
            _btn90   = ToggleBtn("90°",  false, () => SetRot(RotationMode.Deg90));
            _btnAuto.MinWidth = 60; _btn0.MinWidth = 50; _btn90.MinWidth = 50;
            _btnAuto.Margin = new Thickness(0, 0, 4, 0); _btn0.Margin = new Thickness(0, 0, 4, 0);
            rr.Children.Add(_btnAuto); rr.Children.Add(_btn0); rr.Children.Add(_btn90);
            _body.Children.Add(rr);

            // HEIGHT
            _body.Children.Add(Sec("Height (UKD)"));
            var hh = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            hh.Children.Add(new TextBlock { Text = "Offset below UKD (mm):", FontSize = 11, Foreground = MeToolsTheme.BrText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            _offsetTb = Num("0"); _offsetTb.Width = 70;
            hh.Children.Add(_offsetTb);
            hh.Children.Add(new TextBlock { Text = "  (0 = directly at UKD)", FontSize = 10, Foreground = MeToolsTheme.BrMuted, VerticalAlignment = VerticalAlignment.Center });
            _body.Children.Add(hh);

            // REFERENCE LEVEL
            _body.Children.Add(Sec("Reference Level"));
            _lvlCmb = METools.MeToolsWindowBase.StyledCombo(28, 12);
            _lvlCmb.Margin = new Thickness(0, 0, 0, 4);
            int defaultIdx = -1;
            for (int i = 0; i < _levels.Count; i++)
            {
                var l   = _levels[i];
                double mm = UnitUtils.ConvertFromInternalUnits(l.Elevation, UnitTypeId.Millimeters);
                _lvlCmb.Items.Add(new ComboBoxItem
                {
                    Content = $"{l.Name}  ({mm:0} mm)",
                    Tag     = l.Id
                });
                if (l.Id == _defaultLevelId) defaultIdx = i;
            }
            if (_lvlCmb.Items.Count == 0)
                _lvlCmb.Items.Add(new ComboBoxItem { Content = "-- No levels found --", Tag = ElementId.InvalidElementId });
            _lvlCmb.SelectedIndex = defaultIdx >= 0 ? defaultIdx : 0;
            _lvlCmb.SelectionChanged += (s, e) =>
            {
                _cfg.FallbackLevelId = (_lvlCmb.SelectedItem as ComboBoxItem)?.Tag as ElementId
                                        ?? ElementId.InvalidElementId;
            };
            // Initial in config schreiben
            _cfg.FallbackLevelId = (_lvlCmb.SelectedItem as ComboBoxItem)?.Tag as ElementId
                                    ?? ElementId.InvalidElementId;
            _body.Children.Add(_lvlCmb);

            _lvlHelpTb = new TextBlock
            {
                Text       = "Used as a reliable fallback when no slab face is found at the UKD.",
                FontSize   = 10,
                FontStyle  = FontStyles.Italic,
                Foreground = MeToolsTheme.BrMuted,
                Margin     = new Thickness(0, 0, 0, 12),
                TextWrapping = TextWrapping.Wrap
            };
            _body.Children.Add(_lvlHelpTb);

            // Info Box
            _body.Children.Add(InfoBox("Lamps distributed symmetrically from room center · Room boundaries checked · Height = room UKD"));

            // BUTTONS - main action is mode-dependent; Multiple Rooms shows for Area-based only
            var btnRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            _mainBtn  = ActionBtn("\u25B6  Place in Room",  false, DoMainAction);
            _multiBtn = ActionBtn("\u2295  Multiple Rooms", true,  () => DoPlace(LampAction.PlaceMulti));
            Grid.SetColumn(_mainBtn, 0); Grid.SetColumn(_multiBtn, 2);
            btnRow.Children.Add(_mainBtn); btnRow.Children.Add(_multiBtn);
            _body.Children.Add(btnRow);

            var rb = ActionBtn("↺  Redistribute Selected Lamps", true, () => DoPlace(LampAction.Redistribute));
            rb.Margin = new Thickness(0, 4, 0, 0);
            _body.Children.Add(rb);

            var rfBtn = ActionBtn("⟳  Refresh Room", true, () => DoPlace(LampAction.RefreshRoom));
            rfBtn.Margin   = new Thickness(0, 4, 0, 0);
            rfBtn.ToolTip  = "Delete existing lamps of this family in the selected room and re-place with current settings.";
            _body.Children.Add(rfBtn);

            UpdateInfo();
        }

        protected override void OnThemeChanged() => ApplyTheme();
        protected override string AppKey => "LampPlacer";

        void ApplyTheme()
        {
            if (_scroll != null) _scroll.Background = MeToolsTheme.BrBg;
            if (_body   != null) _body.Background   = MeToolsTheme.BrBg;

            foreach (var tb in new[] { _sqmTb, _wallTb, _offsetTb, _rowsTb, _colsTb, _lineSpacingTb, _lineCountTb, _overlapTb })
            {
                if (tb == null) continue;
                tb.Background  = MeToolsTheme.BrInput;
                tb.Foreground  = MeToolsTheme.BrText;
                tb.BorderBrush = MeToolsTheme.BrBorder;
                tb.CaretBrush  = MeToolsTheme.BrText;
            }
            if (_famCmb    != null) METools.MeToolsWindowBase.ApplyComboStyle(_famCmb);
            if (_typCmb    != null) METools.MeToolsWindowBase.ApplyComboStyle(_typCmb);
            if (_lvlCmb    != null) METools.MeToolsWindowBase.ApplyComboStyle(_lvlCmb);
            if (_lineStyleCmb != null) METools.MeToolsWindowBase.ApplyComboStyle(_lineStyleCmb);
            if (_infoTb    != null) _infoTb.Foreground    = MeToolsTheme.BrMuted;
            if (_lvlHelpTb != null) _lvlHelpTb.Foreground = MeToolsTheme.BrMuted;
            SetDist(_cfg.Distribution);
            SetRot(_cfg.Rotation);
            SetLineMode(_cfg.LineMode);
            SetLineRot(_cfg.LineRotation);
            UpdatePlacementDetection();
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
            UpdatePlacementDetection();
        }

        void TypChanged(object s, SelectionChangedEventArgs e)
            => _cfg.TypeName = _typCmb.SelectedItem as string ?? "";

        void SetSurface(PlacementSurface s)
        {
            if (s == PlacementSurface.Face && _btnFace != null && !_btnFace.IsEnabled) return;
            _cfg.Surface = s;
            UpdateToggle(_btnFace, s == PlacementSurface.Face);
            UpdateToggle(_btnWP,   s == PlacementSurface.WorkPlane);
        }

        void UpdatePlacementDetection()
        {
            if (_btnFace == null || _btnWP == null) return;
            var info = _fams.FirstOrDefault(f => f.FamilyName == _cfg.FamilyName);
            var pt   = info?.Placement ?? FamilyPlacementType.Invalid;
            bool faceBased = pt == FamilyPlacementType.WorkPlaneBased;

            _btnFace.IsEnabled = faceBased;
            _btnFace.Opacity   = faceBased ? 1.0 : 0.45;
            if (!faceBased) SetSurface(PlacementSurface.WorkPlane);
            else            SetSurface(_cfg.Surface);

            if (_placeDetectTb != null)
                _placeDetectTb.Text = pt == FamilyPlacementType.Invalid
                    ? "Select a family to detect hosting."
                    : $"Detected: {pt}" + (faceBased ? " - face placement available." : " - work plane / level only.");
        }

        void SetDist(DistributionMode m)
        {
            _cfg.Distribution = m;
            UpdateToggle(_btnArea, m == DistributionMode.AreaBased);
            UpdateToggle(_btnGrid, m == DistributionMode.ManualGrid);
            UpdateToggle(_btnLine, m == DistributionMode.Line);
            if (_areaSp != null) _areaSp.Visibility = m == DistributionMode.AreaBased ? Visibility.Visible : Visibility.Collapsed;
            if (_gridSp != null) _gridSp.Visibility = m == DistributionMode.ManualGrid ? Visibility.Visible : Visibility.Collapsed;
            if (_lineSp != null) _lineSp.Visibility = m == DistributionMode.Line       ? Visibility.Visible : Visibility.Collapsed;
            if (_mainBtn != null)
                _mainBtn.Content = m == DistributionMode.Line      ? "\uD83D\uDCCF  Place Along Line"
                                 : m == DistributionMode.ManualGrid ? "\u25A6  Place Grid"
                                 :                                    "\u25B6  Place in Room";
            if (_multiBtn != null)
            {
                bool showMulti = m == DistributionMode.AreaBased;
                _multiBtn.Visibility = showMulti ? Visibility.Visible : Visibility.Collapsed;
                if (_mainBtn != null) Grid.SetColumnSpan(_mainBtn, showMulti ? 1 : 3);
            }
            UpdateInfo();
        }

        void SetLineMode(LineMode m)
        {
            _cfg.LineMode = m;
            UpdateToggle(_btnLineSpacing, m == LineMode.BySpacing);
            UpdateToggle(_btnLineCount,   m == LineMode.ByCount);
            if (_lineSpacingRow != null) _lineSpacingRow.Visibility = m == LineMode.BySpacing ? Visibility.Visible : Visibility.Collapsed;
            if (_lineCountRow   != null) _lineCountRow.Visibility   = m == LineMode.ByCount   ? Visibility.Visible : Visibility.Collapsed;
            UpdateInfo();
        }

        void SetLineRot(LineRotation r)
        {
            _cfg.LineRotation = r;
            UpdateToggle(_btnLineAlong, r == LineRotation.AlongLine);
            UpdateToggle(_btnLinePerp,  r == LineRotation.Perpendicular);
        }

        void SetRot(RotationMode r)
        {
            _cfg.Rotation = r;
            UpdateToggle(_btnAuto, r == RotationMode.Auto);
            UpdateToggle(_btn0,    r == RotationMode.Deg0);
            UpdateToggle(_btn90,   r == RotationMode.Deg90);
        }

        new void UpdateToggle(Button b, bool active)
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
                _infoTb.Text = $"→ auto grid based on room area ÷ {sqm} m²/lamp";
            }
            else if (_cfg.Distribution == DistributionMode.ManualGrid)
            {
                int r = int.TryParse(_rowsTb?.Text, out int rv) ? rv : 2;
                int c = int.TryParse(_colsTb?.Text, out int cv) ? cv : 2;
                _infoTb.Text = $"→ {r} × {c} = {r * c} lamps per room";
            }
            else // Line
            {
                if (_cfg.LineMode == LineMode.BySpacing)
                {
                    double s = double.TryParse(_lineSpacingTb?.Text, out double sv) ? sv : 2000;
                    _infoTb.Text = $"→ one lamp every {s} mm along the line";
                }
                else
                {
                    int n = int.TryParse(_lineCountTb?.Text, out int nv) ? nv : 4;
                    _infoTb.Text = $"→ {n} lamps evenly distributed along the line";
                }
            }
        }

        // Small themed modal asking for the lamp spacing (mm). Returns null if cancelled.
        public double? ShowSpacingDialog(double defaultMm)
        {
            double? result = null;
            var dlg = new Window
            {
                Title = "Lamp spacing",
                Width = 300, SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.ToolWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = MeToolsTheme.BrSurface, Owner = this
            };
            var sp = new StackPanel { Margin = new Thickness(16) };
            sp.Children.Add(new TextBlock { Text = "Distance between lamps (mm):",
                Foreground = MeToolsTheme.BrText, Margin = new Thickness(0, 0, 0, 8) });
            var tb = Num(((int)Math.Round(defaultMm)).ToString()); tb.Width = 120;
            sp.Children.Add(tb);
            var row = new StackPanel { Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var ok = new Button { Content = "OK", Width = 72, Margin = new Thickness(0, 0, 6, 0),
                IsDefault = true, Background = MeToolsTheme.BrPetrol, Foreground = Brushes.White,
                BorderBrush = MeToolsTheme.BrPetrol, Padding = new Thickness(0, 4, 0, 4) };
            var cancel = new Button { Content = "Cancel", Width = 72, IsCancel = true,
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrText,
                BorderBrush = MeToolsTheme.BrBorder, Padding = new Thickness(0, 4, 0, 4) };
            ok.Click += (s, e) =>
            {
                if (double.TryParse(tb.Text, out double v) && v > 0) { result = v; dlg.DialogResult = true; }
                else { tb.Focus(); tb.SelectAll(); }
            };
            row.Children.Add(ok); row.Children.Add(cancel);
            sp.Children.Add(row);
            dlg.Content = sp;
            dlg.Loaded += (s, e) => { tb.Focus(); tb.SelectAll(); };
            dlg.ShowDialog();
            return result;
        }

        void DoMainAction()
        {
            if (_cfg.Distribution == DistributionMode.Line)            DoPlace(LampAction.PlaceLine);
            else if (_cfg.Distribution == DistributionMode.ManualGrid) DoPlace(LampAction.PlaceGrid);
            else                                                       DoPlace(LampAction.PlaceSingle);
        }

        void DoPlace(LampAction action)
        {
            _cfg.WallMargin  = double.TryParse(_wallTb.Text,        out double w)  ? w  : 1500;
            _cfg.OverlapThreshold = double.TryParse(_overlapTb.Text, out double og) ? og : 300;
            _cfg.UKDOffset   = double.TryParse(_offsetTb.Text,      out double o)  ? o  : 0;
            _cfg.SqmPerLamp  = double.TryParse(_sqmTb?.Text,        out double s)  ? s  : 12;
            _cfg.ManualRows  = int.TryParse(_rowsTb?.Text,          out int r)     ? r  : 2;
            _cfg.ManualCols  = int.TryParse(_colsTb?.Text,          out int c)     ? c  : 2;
            _cfg.LineSpacing = double.TryParse(_lineSpacingTb?.Text, out double ls) ? ls : 2000;
            _cfg.LineCount   = int.TryParse(_lineCountTb?.Text,      out int lc)    ? lc : 4;
            _cfg.LineStyleName = _lineStyleCmb?.SelectedItem as string ?? "";

            // Reference level (fallback) aktuell aus der Combo
            _cfg.FallbackLevelId = (_lvlCmb?.SelectedItem as ComboBoxItem)?.Tag as ElementId
                                    ?? ElementId.InvalidElementId;

            ElementId symId = ElementId.InvalidElementId;
            if (action != LampAction.Redistribute)
            {
                symId = _fams.FirstOrDefault(f => f.FamilyName == _cfg.FamilyName && f.TypeName == _cfg.TypeName)?.SymbolId
                        ?? ElementId.InvalidElementId;
                if (symId == ElementId.InvalidElementId)
                { StatusLeft.Text = "Please select a family and type first."; return; }
            }

            _h.Request = new LampRequest { Action = action, Config = _cfg, SymbolId = symId };
            StatusLeft.Text = action == LampAction.Redistribute ? "Redistributing selected lamps..." :
                              action == LampAction.PlaceMulti   ? "Click rooms — ESC when done" :
                              action == LampAction.RefreshRoom  ? "Click a room to refresh lamps..." :
                              action == LampAction.PlaceLine    ? "Select the guide line(s) you drew with the Detail Line tool..." :
                              action == LampAction.PlaceGrid    ? "Click the 4 corners of the area for the grid..." :
                                                                  "Click a room to place lamps";
            _evt.Raise();
        }
    }
}
