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
        LampConfig _cfg = new LampConfig();

        ComboBox   _famCmb, _typCmb, _lvlCmb;
        ComboBox   _presetCmb;
        StackPanel _presetSp, _presetEntriesHost;
        TextBox    _presetNameTb;
        System.Collections.Generic.List<PresetRow> _presetRows;
        Button     _btnArea, _btnGrid, _btnLine;
        Button     _mainBtn, _multiBtn;
        StackPanel _areaSp, _gridSp, _lineSp;

        Button     _btnFace, _btnWP;
        TextBlock  _placeDetectTb;
        Button     _btnLineSpacing, _btnLineCount;
        Button     _btnLineAlong, _btnLinePerp;
        StackPanel _lineSpacingRow, _lineCountRow;
        TextBox    _sqmTb, _rowsTb, _colsTb;
        Button     _btnDimNone, _btnDimAuto, _btnDimCustom;
        TextBox    _lineSpacingTb, _lineCountTb;
        TextBlock  _infoTb, _lvlHelpTb, _guideTb;

        // Alle theming-relevanten Elemente
        ScrollViewer _scroll;
        StackPanel   _body;

        public LampPlacerWindow(ExternalEvent evt, LampPlacerHandler h,
                                List<LampFamilyInfo> fams,
                                List<LevelInfo> levels,
                                ElementId defaultLevelId)
        {
            _evt = evt; _h = h; _fams = fams;
            _levels         = levels          ?? new List<LevelInfo>();
            _defaultLevelId = defaultLevelId  ?? ElementId.InvalidElementId;

            h.OnStatus  = m => Dispatcher.Invoke(() => StatusLeft.Text = m);
            h.OnPlaced  = n => Dispatcher.Invoke(() => { StatusLeft.Text = $"Done: {n} lamps placed."; SetWaiting(false); });
            h.OnWaiting = w => Dispatcher.Invoke(() => SetWaiting(w));
            h.OnPromptSpacing    = def => ShowSpacingDialog(def);
            h.OnPromptCount      = def => ShowCountDialog(def);
            h.OnPromptWallOffset = def => ShowOffsetDialog(def);
            h.OnPromptPreset     = ()  => ShowPresetChooser();

            S.SetLanguage(SettingsStore.Language ?? "en");
            InitWindow(S.Get("lamp.title"), 440);
            Build();
        }

        void Build()
        {
            // Status Bar
            int famCount = _fams.Select(f => f.FamilyName).Distinct().Count();
            BuildStatusBar($"{famCount} " + S.Get("lamp.families_count"));

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
            _body.Children.Add(Sec(S.Get("lamp.lighting_family")));
            _famCmb = METools.MeToolsWindowBase.StyledCombo(28, 12);
            _famCmb.Margin = new Thickness(0, 0, 0, 6);
            _famCmb.IsEditable = true;
            _famCmb.IsTextSearchEnabled = false;
            RebuildFamilyCombo();   // mode-aware + grouped (Lighting / Fire Alarm), wires FamChanged
            _famCmb.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                new TextChangedEventHandler(FamilySearchTextChanged));
            _famCmb.DropDownClosed += (s, e) =>
            {
                RebuildFamilyCombo(""); // restore the full grouped list for next time
                SyncFamilyComboText();
            };
            _body.Children.Add(_famCmb);
            SyncFamilyComboText();

            _typCmb = METools.MeToolsWindowBase.StyledCombo(28, 12); _typCmb.Margin = new Thickness(0, 0, 0, 14);
            _body.Children.Add(_typCmb);

            // PLACEMENT (face vs work plane)
            _body.Children.Add(Sec(S.Get("lamp.placement")));
            var placeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            _btnFace = ToggleBtn(S.Get("lamp.place_on_face"),       false, () => SetSurface(PlacementSurface.Face));
            _btnWP   = ToggleBtn(S.Get("lamp.place_on_wp"), true,  () => SetSurface(PlacementSurface.WorkPlane));
            _btnFace.MinWidth = 120; _btnWP.MinWidth = 150;
            _btnFace.Margin = new Thickness(0, 0, 5, 0);
            placeRow.Children.Add(_btnFace); placeRow.Children.Add(_btnWP);
            _body.Children.Add(placeRow);
            _placeDetectTb = new TextBlock { Text = S.Get("lamp.detect_hosting"), FontSize = 10,
                Foreground = MeToolsTheme.BrMuted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
            _body.Children.Add(_placeDetectTb);
            UpdatePlacementDetection();

            // DISTRIBUTION MODE
            _body.Children.Add(Sec(S.Get("lamp.distribution")));
            var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            _btnArea = ToggleBtn(S.Get("lamp.area_based"),  true,  () => SetDist(DistributionMode.AreaBased));
            _btnGrid = ToggleBtn(S.Get("lamp.manual_grid"), false, () => SetDist(DistributionMode.ManualGrid));
            _btnLine = ToggleBtn(S.Get("lamp.line"),        false, () => SetDist(DistributionMode.Line));
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
                Text = S.Get("lamp.sqm_per_lamp"), FontSize = 11,
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

            _presetSp = BuildPresetSection();
            _body.Children.Add(_presetSp);

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
            _btnLineSpacing = ToggleBtn(S.Get("lamp.by_spacing"), true,  () => SetLineMode(LineMode.BySpacing));
            _btnLineCount   = ToggleBtn(S.Get("lamp.by_count"),   false, () => SetLineMode(LineMode.ByCount));
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
            _btnLineAlong = ToggleBtn(S.Get("lamp.along_line"),    true,  () => SetLineRot(LineRotation.AlongLine));
            _btnLinePerp  = ToggleBtn(S.Get("lamp.perpendicular"), false, () => SetLineRot(LineRotation.Perpendicular));
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

            // DIMENSIONS
            _body.Children.Add(Sec(S.Get("lamp.dimensions")));
            var dimRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            _btnDimNone   = ToggleBtn(S.Get("none"),   false, () => SetDimMode(DimensionMode.None));
            _btnDimAuto   = ToggleBtn(S.Get("auto"),   true,  () => SetDimMode(DimensionMode.Auto));
            _btnDimCustom = ToggleBtn(S.Get("custom"), false, () => SetDimMode(DimensionMode.Custom));
            _btnDimNone.MinWidth   = 55; _btnDimNone.Margin   = new Thickness(0, 0, 4, 0);
            _btnDimAuto.MinWidth   = 55; _btnDimAuto.Margin   = new Thickness(0, 0, 4, 0);
            _btnDimCustom.MinWidth = 65;
            _btnDimCustom.ToolTip  = "After placing, pick pairs of reference points to draw dimensions manually";
            dimRow.Children.Add(_btnDimNone);
            dimRow.Children.Add(_btnDimAuto);
            dimRow.Children.Add(_btnDimCustom);
            _body.Children.Add(dimRow);

            // REFERENCE LEVEL
            _body.Children.Add(Sec(S.Get("lamp.reference_level")));
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
                Text       = S.Get("lamp.fallback_hint"),
                FontSize   = 10,
                FontStyle  = FontStyles.Italic,
                Foreground = MeToolsTheme.BrMuted,
                Margin     = new Thickness(0, 0, 0, 12),
                TextWrapping = TextWrapping.Wrap
            };
            _body.Children.Add(_lvlHelpTb);

            // Info Box
            _body.Children.Add(InfoBox(S.Get("lamp.area_info")));

            // Mode-aware step guide
            _guideTb = new TextBlock
            {
                FontSize     = 11,
                Foreground   = MeToolsTheme.BrMuted,
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(2, 8, 2, 10),
            };
            _body.Children.Add(_guideTb);
            UpdateGuide();

            // BUTTONS - main action is mode-dependent; Multiple Rooms shows for Area-based only
            var btnRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            _mainBtn  = ActionBtn("\u25B6  " + S.Get("lamp.place_in_room"),  false, DoMainAction);
            _multiBtn = ActionBtn("\u2295  " + S.Get("lamp.multiple_rooms"), true,  () => DoPlace(LampAction.PlaceMulti));
            Grid.SetColumn(_mainBtn, 0); Grid.SetColumn(_multiBtn, 2);
            btnRow.Children.Add(_mainBtn); btnRow.Children.Add(_multiBtn);
            _body.Children.Add(btnRow);

            UpdateInfo();
        }

        protected override void OnThemeChanged() => ApplyTheme();
        protected override string AppKey => "LampPlacer";

        void ApplyTheme()
        {
            if (_scroll != null) _scroll.Background = MeToolsTheme.BrBg;
            if (_body   != null) _body.Background   = MeToolsTheme.BrBg;

            foreach (var tb in new[] { _sqmTb, _rowsTb, _colsTb, _lineSpacingTb, _lineCountTb })
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
            if (_infoTb    != null) _infoTb.Foreground    = MeToolsTheme.BrMuted;
            if (_lvlHelpTb != null) _lvlHelpTb.Foreground = MeToolsTheme.BrMuted;
            SetDist(_cfg.Distribution);
            _cfg.Rotation   = RotationMode.Deg0;   // fixed: always use horizontal
            // _cfg.Dimensions already set via SetDimMode toggle
            SetLineMode(_cfg.LineMode);
            SetLineRot(_cfg.LineRotation);
            UpdatePlacementDetection();
        }

        // Rebuilds the family dropdown: grouped into Lighting / Fire Alarm headers, and Fire Alarm
        // families are listed only in Area-based mode. Preserves the current selection when possible.
        // filter (optional): case-insensitive substring match against family name, from the search box.
        // preserveText: true while the user is actively typing to search -- must
        // NOT touch SelectedItem/SelectedIndex in that case (editable combo
        // resyncs Text to match on any such assignment, overwriting what they typed).
        void RebuildFamilyCombo(string filter = "", bool preserveText = false)
        {
            if (_famCmb == null) return;
            _famCmb.SelectionChanged -= FamChanged;

            string current = _cfg.FamilyName;
            bool   area    = _cfg.Distribution == DistributionMode.AreaBased;
            bool   selValid = false;
            bool   searching = !string.IsNullOrWhiteSpace(filter);

            _famCmb.Items.Clear();
            _famCmb.Items.Add(new ComboBoxItem { Content = S.Get("lamp.select_family_dropdown"), Tag = "" });

            void AddGroup(string header, System.Collections.Generic.IEnumerable<string> fams)
            {
                var list = fams.ToList();
                if (searching) list = list.Where(f => f.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                if (list.Count == 0) return;
                _famCmb.Items.Add(new ComboBoxItem
                {
                    Content    = header,
                    Tag        = null,
                    IsEnabled  = false,
                    FontWeight = FontWeights.Bold,
                    Foreground = MeToolsTheme.BrMuted,
                });
                foreach (var fam in list)
                {
                    _famCmb.Items.Add(new ComboBoxItem { Content = "    " + fam, Tag = fam });
                    if (fam == current) selValid = true;
                }
            }

            AddGroup("Lighting", _fams.Where(f => f.Group != "Fire Alarm")
                                      .Select(f => f.FamilyName).Distinct()
                                      .OrderBy(x => x, System.StringComparer.OrdinalIgnoreCase));
            if (area)
                AddGroup("Fire Alarm", _fams.Where(f => f.Group == "Fire Alarm")
                                            .Select(f => f.FamilyName).Distinct()
                                            .OrderBy(x => x, System.StringComparer.OrdinalIgnoreCase));

            if (preserveText)
            {
                // Mid-search: only the candidate list changes, selection state
                // (and therefore the editable Text) stays exactly as typed.
            }
            else if (selValid)
            {
                foreach (var it in _famCmb.Items)
                    if (it is ComboBoxItem ci && (ci.Tag as string) == current) { _famCmb.SelectedItem = ci; break; }
            }
            else if (!searching)
            {
                _famCmb.SelectedIndex = 0;
                _cfg.FamilyName = "";
                _cfg.TypeName   = "";
                if (_typCmb != null)
                {
                    _typCmb.SelectionChanged -= TypChanged;
                    _typCmb.Items.Clear();
                    _typCmb.SelectionChanged += TypChanged;
                }
                UpdatePlacementDetection();
            }
            else
            {
                // Searching and the current selection just isn't in the
                // filtered results right now -- leave _cfg alone, just show
                // the placeholder row selected until they pick something or
                // clear the search.
                _famCmb.SelectedIndex = 0;
            }

            _famCmb.SelectionChanged += FamChanged;
        }

        // Forces the editable text to match the real current selection -- used
        // after the dropdown closes without a new pick being made, so a
        // half-typed search term never lingers as if it were the actual
        // selection.
        bool _suppressFamilyTextSync;
        void SyncFamilyComboText()
        {
            _suppressFamilyTextSync = true;
            try { _famCmb.Text = string.IsNullOrEmpty(_cfg.FamilyName) ? S.Get("lamp.select_family_dropdown") : _cfg.FamilyName; }
            finally { _suppressFamilyTextSync = false; }
        }

        void FamilySearchTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressFamilyTextSync) return;
            if (!_famCmb.IsDropDownOpen) _famCmb.IsDropDownOpen = true;
            RebuildFamilyCombo(_famCmb.Text ?? "", preserveText: true);
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
                    ? S.Get("lamp.detect_hosting")
                    : $"Detected: {pt}" + (faceBased ? S.Get("lamp.face_available") : S.Get("lamp.wp_only"));
        }

        // ===== Room Presets (Area-based only) ====================================
        private class PresetRow
        {
            public ComboBox  FamilyCmb;
            public ComboBox  TypeCmb;
            public TextBox   CountTb;
            public Grid      Root;
        }

        Button MiniBtn(string text, bool primary, System.Action onClick)
        {
            var bgN = primary ? MeToolsTheme.BrPetrol : MeToolsTheme.BrInput;
            var bgH = primary ? MeToolsTheme.BrPetrolDark : MeToolsTheme.BrActiveBg;
            var b = new Button
            {
                Content         = text,
                Height          = 26,
                FontSize        = 11,
                Padding         = new Thickness(10, 0, 10, 0),
                Background      = bgN,
                Foreground      = primary ? System.Windows.Media.Brushes.White : MeToolsTheme.BrText,
                BorderBrush     = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1),
                Cursor          = Cursors.Hand,
                Template        = RoundedBtnTemplate(),
            };
            b.MouseEnter += (s, e) => b.Background = bgH;
            b.MouseLeave += (s, e) => b.Background = bgN;
            b.Click += (s, e) => onClick();
            return b;
        }

        static void SelectComboByTag(ComboBox cmb, string tag)
        {
            foreach (var it in cmb.Items)
                if (it is ComboBoxItem ci && (ci.Tag as string) == tag) { cmb.SelectedItem = ci; return; }
        }

        void PopulateGroupedFamilies(ComboBox cmb)
        {
            cmb.Items.Clear();
            cmb.Items.Add(new ComboBoxItem { Content = "-- Select --", Tag = "" });
            void AddGroup(string header, System.Collections.Generic.IEnumerable<string> fams)
            {
                var list = fams.ToList();
                if (list.Count == 0) return;
                cmb.Items.Add(new ComboBoxItem
                {
                    Content = header, Tag = null, IsEnabled = false,
                    FontWeight = FontWeights.Bold, Foreground = MeToolsTheme.BrMuted,
                });
                foreach (var fam in list) cmb.Items.Add(new ComboBoxItem { Content = "    " + fam, Tag = fam });
            }
            AddGroup("Lighting", _fams.Where(f => f.Group != "Fire Alarm")
                                      .Select(f => f.FamilyName).Distinct()
                                      .OrderBy(x => x, System.StringComparer.OrdinalIgnoreCase));
            AddGroup("Fire Alarm", _fams.Where(f => f.Group == "Fire Alarm")
                                        .Select(f => f.FamilyName).Distinct()
                                        .OrderBy(x => x, System.StringComparer.OrdinalIgnoreCase));
        }

        PresetRow BuildPresetRow(LampPresetEntry data)
        {
            var row = new PresetRow();
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            foreach (var wdt in new[] { -1.0, 6.0, 110.0, 6.0, 50.0, 6.0, 26.0 })
                g.ColumnDefinitions.Add(new ColumnDefinition
                { Width = wdt < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(wdt) });

            var famCmb  = METools.MeToolsWindowBase.StyledCombo(26, 11);
            var typCmb  = METools.MeToolsWindowBase.StyledCombo(26, 11);
            var countTb = Num((data != null ? data.Count : 1).ToString());
            countTb.Width = 50; countTb.TextAlignment = TextAlignment.Center;
            var rm = MiniBtn("x", false, () => { _presetEntriesHost.Children.Remove(g); _presetRows.Remove(row); });

            PopulateGroupedFamilies(famCmb);

            void RefreshTypes()
            {
                typCmb.Items.Clear();
                var fam = (famCmb.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
                foreach (var t in _fams.Where(f => f.FamilyName == fam).OrderBy(f => f.TypeName))
                    typCmb.Items.Add(t.TypeName);
                if (typCmb.Items.Count > 0) typCmb.SelectedIndex = 0;
            }
            famCmb.SelectionChanged += (s, e) => RefreshTypes();

            if (data != null && !string.IsNullOrEmpty(data.FamilyName))
            {
                SelectComboByTag(famCmb, data.FamilyName);   // triggers RefreshTypes
                if (!string.IsNullOrEmpty(data.TypeName))
                    foreach (var it in typCmb.Items)
                        if ((it as string) == data.TypeName) { typCmb.SelectedItem = it; break; }
            }

            Grid.SetColumn(famCmb, 0);
            Grid.SetColumn(typCmb, 2);
            Grid.SetColumn(countTb, 4);
            Grid.SetColumn(rm, 6);
            g.Children.Add(famCmb); g.Children.Add(typCmb); g.Children.Add(countTb); g.Children.Add(rm);

            row.FamilyCmb = famCmb; row.TypeCmb = typCmb; row.CountTb = countTb; row.Root = g;
            return row;
        }

        StackPanel BuildPresetSection()
        {
            _presetRows = new System.Collections.Generic.List<PresetRow>();
            var sp = new StackPanel();
            sp.Children.Add(Sec(S.Get("lamp.room_presets")));

            var top = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _presetCmb = METools.MeToolsWindowBase.StyledCombo(28, 12);
            Grid.SetColumn(_presetCmb, 0);
            var newBtn = MiniBtn(S.Get("lamp.new_preset"), false, OnNewPreset);
            Grid.SetColumn(newBtn, 2);
            top.Children.Add(_presetCmb); top.Children.Add(newBtn);
            sp.Children.Add(top);

            sp.Children.Add(new TextBlock { Text = S.Get("lamp.preset_name"), FontSize = 11,
                Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(2, 0, 0, 2) });
            _presetNameTb = new TextBox
            {
                Height = 28, FontSize = 12,
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrInputFg,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                CaretBrush = MeToolsTheme.BrText, Padding = new Thickness(6, 0, 6, 0),
                VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 8),
            };
            sp.Children.Add(_presetNameTb);

            sp.Children.Add(new TextBlock { Text = S.Get("lamp.families"), FontSize = 11,
                Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(2, 0, 0, 2) });
            _presetEntriesHost = new StackPanel();
            sp.Children.Add(_presetEntriesHost);

            var addBtn = MiniBtn(S.Get("lamp.add_family"), false, () =>
            { var r = BuildPresetRow(null); _presetRows.Add(r); _presetEntriesHost.Children.Add(r.Root); });
            addBtn.Margin = new Thickness(0, 4, 0, 8);
            addBtn.HorizontalAlignment = HorizontalAlignment.Left;
            sp.Children.Add(addBtn);

            var bottom = new StackPanel { Orientation = Orientation.Horizontal };
            var saveBtn = MiniBtn(S.Get("lamp.save_preset"), true, SavePreset); saveBtn.Margin = new Thickness(0, 0, 6, 0);
            var updBtn  = MiniBtn(S.Get("lamp.update_room"), false, UpdateRoom); updBtn.Margin = new Thickness(0, 0, 6, 0);
            var delBtn  = MiniBtn(S.Get("delete"), false, DeletePreset);
            bottom.Children.Add(saveBtn); bottom.Children.Add(updBtn); bottom.Children.Add(delBtn);
            sp.Children.Add(bottom);

            RebuildPresetCombo();
            NewPreset();
            return sp;
        }

        void RebuildPresetCombo(string selectName = null)
        {
            if (_presetCmb == null) return;
            _presetCmb.SelectionChanged -= PresetChanged;
            _presetCmb.Items.Clear();
            _presetCmb.Items.Add(new ComboBoxItem { Content = "-- New preset --", Tag = "" });
            foreach (var p in LampPresetStore.All())
                _presetCmb.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p.Name });
            if (!string.IsNullOrEmpty(selectName)) SelectComboByTag(_presetCmb, selectName);
            else _presetCmb.SelectedIndex = 0;
            _presetCmb.SelectionChanged += PresetChanged;
        }

        void PresetChanged(object s, SelectionChangedEventArgs e)
        {
            var name = (_presetCmb.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            if (string.IsNullOrEmpty(name)) NewPreset();
            else LoadPreset(name);
        }

        void LoadPreset(string name)
        {
            var p = LampPresetStore.Get(name);
            _presetNameTb.Text = p?.Name ?? name;
            _presetEntriesHost.Children.Clear();
            _presetRows.Clear();
            if (p != null)
                foreach (var en in p.Entries)
                { var r = BuildPresetRow(en); _presetRows.Add(r); _presetEntriesHost.Children.Add(r.Root); }
            if (_presetRows.Count == 0)
            { var r = BuildPresetRow(null); _presetRows.Add(r); _presetEntriesHost.Children.Add(r.Root); }
        }

        void NewPreset()
        {
            if (_presetNameTb != null) _presetNameTb.Text = "";
            if (_presetEntriesHost == null) return;
            _presetEntriesHost.Children.Clear();
            _presetRows.Clear();
            var r = BuildPresetRow(null); _presetRows.Add(r); _presetEntriesHost.Children.Add(r.Root);
        }

        void OnNewPreset()
        {
            _presetCmb.SelectionChanged -= PresetChanged;
            _presetCmb.SelectedIndex = 0;
            _presetCmb.SelectionChanged += PresetChanged;
            NewPreset();
        }

        // Builds a LampPreset from the current editor rows; returns null (and sets a
        // status message) if the name is empty or there are no families.
        LampPreset BuildPresetFromRows()
        {
            var name = _presetNameTb.Text?.Trim() ?? "";
            if (name.Length == 0) { StatusLeft.Text = "Enter a preset name first."; return null; }
            var preset = new LampPreset { Name = name };
            foreach (var r in _presetRows)
            {
                var fam = (r.FamilyCmb.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
                if (string.IsNullOrEmpty(fam)) continue;
                var typ = r.TypeCmb.SelectedItem as string ?? "";
                int cnt = int.TryParse(r.CountTb.Text, out int c) ? System.Math.Max(1, c) : 1;
                preset.Entries.Add(new LampPresetEntry { FamilyName = fam, TypeName = typ, Count = cnt });
            }
            if (preset.Entries.Count == 0) { StatusLeft.Text = "Add at least one family to the preset."; return null; }
            return preset;
        }

        void SavePreset()
        {
            var preset = BuildPresetFromRows();
            if (preset == null) return;
            LampPresetStore.Save(preset);
            RebuildPresetCombo(preset.Name);
            StatusLeft.Text = "Saved preset: " + preset.Name;
        }

        // Saves the (edited) preset, then asks the user to pick the room that already
        // has it placed; the handler clears that room and re-applies the preset live.
        void UpdateRoom()
        {
            var preset = BuildPresetFromRows();
            if (preset == null) return;
            LampPresetStore.Save(preset);
            RebuildPresetCombo(preset.Name);
            SyncConfig();
            _h.Request = new LampRequest { Action = LampAction.UpdatePreset, Config = _cfg, PresetName = preset.Name };
            StatusLeft.Text = "Click the room to update with preset '" + preset.Name + "'...";
            _evt.Raise();
        }

        void DeletePreset()
        {
            var name = (_presetCmb.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            if (string.IsNullOrEmpty(name)) { StatusLeft.Text = ("Select a saved preset to delete."); return; }
            LampPresetStore.Delete(name);
            RebuildPresetCombo();
            NewPreset();
            StatusLeft.Text = ("Deleted preset: " + name);
        }

        void SetDist(DistributionMode m)
        {
            _cfg.Distribution = m;
            RebuildFamilyCombo();   // Fire Alarm families only show in Area-based mode
            UpdateToggle(_btnArea, m == DistributionMode.AreaBased);
            UpdateToggle(_btnGrid, m == DistributionMode.ManualGrid);
            UpdateToggle(_btnLine, m == DistributionMode.Line);
            if (_areaSp != null) _areaSp.Visibility = m == DistributionMode.AreaBased ? Visibility.Visible : Visibility.Collapsed;
            if (_presetSp != null) _presetSp.Visibility = m == DistributionMode.AreaBased ? Visibility.Visible : Visibility.Collapsed;
            if (_gridSp != null) _gridSp.Visibility = m == DistributionMode.ManualGrid ? Visibility.Visible : Visibility.Collapsed;
            if (_lineSp != null) _lineSp.Visibility = m == DistributionMode.Line       ? Visibility.Visible : Visibility.Collapsed;
            if (_mainBtn != null)
                _mainBtn.Content = m == DistributionMode.Line      ? "\uD83D\uDCCF  Place Along Line"
                                 : m == DistributionMode.ManualGrid ? "\u25A6  Place Grid"
                                 :                                    "\u25B6  " + S.Get("lamp.place_in_room");
            if (_multiBtn != null)
            {
                bool showMulti = m == DistributionMode.AreaBased;
                _multiBtn.Visibility = showMulti ? Visibility.Visible : Visibility.Collapsed;
                if (_mainBtn != null) Grid.SetColumnSpan(_mainBtn, showMulti ? 1 : 3);
            }
            UpdateInfo();
            UpdateGuide();
        }

        void SetLineMode(LineMode m)
        {
            _cfg.LineMode = m;
            UpdateToggle(_btnLineSpacing, m == LineMode.BySpacing);
            UpdateToggle(_btnLineCount,   m == LineMode.ByCount);
            if (_lineSpacingRow != null) _lineSpacingRow.Visibility = m == LineMode.BySpacing ? Visibility.Visible : Visibility.Collapsed;
            if (_lineCountRow   != null) _lineCountRow.Visibility   = m == LineMode.ByCount   ? Visibility.Visible : Visibility.Collapsed;
            UpdateInfo();
            UpdateGuide();
        }

        // Short, mode-specific how-to shown above the action buttons.
        void UpdateGuide()
        {
            if (_guideTb == null) return;
            string t;
            switch (_cfg.Distribution)
            {
                case DistributionMode.ManualGrid:
                    t = S.Get("lamp.help_grid");
                    break;
                case DistributionMode.Line:
                    t = S.Get("lamp.help_line");
                    break;
                default:
                    t = S.Get("lamp.help_area");
                    break;
            }
            _guideTb.Text = t;
        }

        void SetLineRot(LineRotation r)
        {
            _cfg.LineRotation = r;
            UpdateToggle(_btnLineAlong, r == LineRotation.AlongLine);
            UpdateToggle(_btnLinePerp,  r == LineRotation.Perpendicular);
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
                _infoTb.Text = S.Get("lamp.auto_grid").Replace("{0}", sqm.ToString());
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
        // Prompt for how many lamps to place on a line, pre-filled with a
        // suggestion computed from the line's actual length and the spacing
        // entered in the side panel -- replaces the old flow that asked for
        // spacing a second time after the line was already selected.
        public int? ShowCountDialog(int defaultCount)
        {
            int? result = null;
            var dlg = new Window
            {
                Title = "How many lamps?",
                Width = 300, SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.ToolWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = MeToolsTheme.BrSurface, Owner = this
            };
            var sp = new StackPanel { Margin = new Thickness(16) };
            sp.Children.Add(new TextBlock
            {
                Text = "Number of lamps on this line:",
                Foreground = MeToolsTheme.BrText, Margin = new Thickness(0, 0, 0, 4),
            });
            sp.Children.Add(new TextBlock
            {
                Text = "Suggested from the line's length and your spacing setting -- change it if you want a different count.",
                Foreground = MeToolsTheme.BrMuted, FontSize = 10.5, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
            });
            var tb = Num(Math.Max(1, defaultCount).ToString()); tb.Width = 120;
            sp.Children.Add(tb);
            var row = new StackPanel { Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var ok = new Button { Content = "OK", Width = 72, Margin = new Thickness(0, 0, 6, 0),
                IsDefault = true, Background = MeToolsTheme.BrPetrol, Foreground = Brushes.White,
                BorderBrush = MeToolsTheme.BrPetrol, Padding = new Thickness(0, 4, 0, 4),
                Cursor = Cursors.Hand, Template = RoundedBtnTemplate() };
            var cancel = new Button { Content = "Cancel", Width = 72, IsCancel = true,
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrText,
                BorderBrush = MeToolsTheme.BrBorder, Padding = new Thickness(0, 4, 0, 4),
                Cursor = Cursors.Hand, Template = RoundedBtnTemplate() };
            ok.Click += (s, e) =>
            {
                if (int.TryParse(tb.Text, out int v) && v > 0) { result = v; dlg.DialogResult = true; }
                else { tb.Focus(); tb.SelectAll(); }
            };
            row.Children.Add(ok); row.Children.Add(cancel);
            sp.Children.Add(row);
            dlg.Content = sp;
            dlg.Loaded += (s, e) => { tb.Focus(); tb.SelectAll(); };
            dlg.ShowDialog();
            return result;
        }

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
                BorderBrush = MeToolsTheme.BrPetrol, Padding = new Thickness(0, 4, 0, 4),
                Cursor = Cursors.Hand, Template = RoundedBtnTemplate() };
            var cancel = new Button { Content = "Cancel", Width = 72, IsCancel = true,
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrText,
                BorderBrush = MeToolsTheme.BrBorder, Padding = new Thickness(0, 4, 0, 4),
                Cursor = Cursors.Hand, Template = RoundedBtnTemplate() };
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

        // Prompt for a wall-mounted lamp's offset from host (mm). Null = cancel.
        public double? ShowOffsetDialog(double defaultMm)
        {
            double? result = null;
            var dlg = new Window
            {
                Title = "Offset from host",
                Width = 320, SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.ToolWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = MeToolsTheme.BrSurface, Owner = this
            };
            var sp = new StackPanel { Margin = new Thickness(16) };
            sp.Children.Add(new TextBlock { Text = "Wall-mounted lamp - offset from host (mm):",
                Foreground = MeToolsTheme.BrText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            var tb = Num(((int)Math.Round(defaultMm)).ToString()); tb.Width = 120;
            sp.Children.Add(tb);
            var row = new StackPanel { Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var ok = new Button { Content = "OK", Width = 72, Margin = new Thickness(0, 0, 6, 0),
                IsDefault = true, Background = MeToolsTheme.BrPetrol, Foreground = Brushes.White,
                BorderBrush = MeToolsTheme.BrPetrol, Padding = new Thickness(0, 4, 0, 4),
                Cursor = Cursors.Hand, Template = RoundedBtnTemplate() };
            var cancel = new Button { Content = "Cancel", Width = 72, IsCancel = true,
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrText,
                BorderBrush = MeToolsTheme.BrBorder, Padding = new Thickness(0, 4, 0, 4),
                Cursor = Cursors.Hand, Template = RoundedBtnTemplate() };
            ok.Click += (s, e) =>
            {
                if (double.TryParse(tb.Text, out double v)) { result = v; dlg.DialogResult = true; }
                else { tb.Focus(); tb.SelectAll(); }
            };
            row.Children.Add(ok); row.Children.Add(cancel);
            sp.Children.Add(row);
            dlg.Content = sp;
            dlg.Loaded += (s, e) => { tb.Focus(); tb.SelectAll(); };
            dlg.ShowDialog();
            return result;
        }

        // Modal preset chooser. Returns chosen preset name, "" = no preset / use selected family, null = cancel.
        public string ShowPresetChooser()
        {
            var presets = LampPresetStore.All();
            if (presets.Count == 0) return "";    // no presets saved yet -> use selected family

            string result = null;
            var dlg = new Window
            {
                Title  = "Choose room preset",
                Width  = 340, SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.ToolWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = MeToolsTheme.BrSurface, Owner = this,
            };
            var sp = new StackPanel { Margin = new Thickness(16) };
            sp.Children.Add(new TextBlock
            {
                Text = "Select a preset to apply, or choose \"No preset\" to place the selected family only.",
                Foreground = MeToolsTheme.BrText, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
            });
            var cmb = METools.MeToolsWindowBase.StyledCombo(28, 12);
            cmb.Items.Add(new ComboBoxItem { Content = "-- No preset (use selected family) --", Tag = "" });
            foreach (var p in presets)
                cmb.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p.Name });
            cmb.SelectedIndex = presets.Count > 0 ? 1 : 0;
            sp.Children.Add(cmb);
            var row = new StackPanel { Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var placeBtn = new Button
            {
                Content = "Place", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 6, 0),
                Background = MeToolsTheme.BrPetrol, Foreground = Brushes.White,
                BorderBrush = MeToolsTheme.BrPetrol, Padding = new Thickness(0, 4, 0, 4),
                Cursor = Cursors.Hand, Template = RoundedBtnTemplate(),
            };
            var cancelBtn = new Button
            {
                Content = "Cancel", Width = 80, IsCancel = true,
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrText,
                BorderBrush = MeToolsTheme.BrBorder, Padding = new Thickness(0, 4, 0, 4),
                Cursor = Cursors.Hand, Template = RoundedBtnTemplate(),
            };
            placeBtn.Click += (s, e) => { result = (cmb.SelectedItem as ComboBoxItem)?.Tag as string ?? ""; dlg.DialogResult = true; };
            row.Children.Add(placeBtn); row.Children.Add(cancelBtn);
            sp.Children.Add(row);
            dlg.Content = sp;
            dlg.ShowDialog();
            return result;   // null if cancelled
        }

        void DoMainAction()
        {
            if (_cfg.Distribution == DistributionMode.Line)            DoPlace(LampAction.PlaceLine);
            else if (_cfg.Distribution == DistributionMode.ManualGrid) DoPlace(LampAction.PlaceGrid);
            else                                                       DoPlace(LampAction.PlaceSingle);
        }

        void SyncConfig()
        {
            _cfg.WallMargin      = 500;   // fixed: half a module from wall
            _cfg.OverlapThreshold = 300;   // fixed: min gap to existing fixture
            _cfg.UKDOffset   = 0;   // fixed at zero (directly at UKD)
            _cfg.SqmPerLamp  = double.TryParse(_sqmTb?.Text,        out double s)  ? s  : 12;
            _cfg.ManualRows  = int.TryParse(_rowsTb?.Text,          out int r)     ? r  : 2;
            _cfg.ManualCols  = int.TryParse(_colsTb?.Text,          out int c)     ? c  : 2;
            _cfg.LineSpacing = double.TryParse(_lineSpacingTb?.Text, out double ls) ? ls : 2000;
            _cfg.LineCount   = int.TryParse(_lineCountTb?.Text,      out int lc)    ? lc : 4;

            // Reference level (fallback) aktuell aus der Combo
            _cfg.FallbackLevelId = (_lvlCmb?.SelectedItem as ComboBoxItem)?.Tag as ElementId
                                    ?? ElementId.InvalidElementId;
        }

        void SetDimMode(DimensionMode m)
        {
            _cfg.Dimensions = m;
            UpdateToggle(_btnDimNone,   m == DimensionMode.None);
            UpdateToggle(_btnDimAuto,   m == DimensionMode.Auto);
            UpdateToggle(_btnDimCustom, m == DimensionMode.Custom);
        }

        void SetWaiting(bool waiting)
        {
            if (_mainBtn == null) return;
            if (waiting)
            {
                _mainBtn.Content   = "... Waiting for guide line selection";
                _mainBtn.IsEnabled = false;
                if (StatusLeft != null) StatusLeft.Text = "Select guide line(s) in Revit, then click Finish (green check).";
            }
            else
            {
                _mainBtn.IsEnabled = true;
                if (_cfg.Distribution == DistributionMode.Line)
                    _mainBtn.Content = "Place Along Line";
                else if (_cfg.Distribution == DistributionMode.ManualGrid)
                    _mainBtn.Content = "Place Grid";
                else
                    _mainBtn.Content = S.Get("lamp.place_in_room");
            }
        }

        void DoPlace(LampAction action)
        {
            SyncConfig();

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
