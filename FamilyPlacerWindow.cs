// FamilyPlacerWindow.cs — ME-Tools | Family Placer
// Mayer E-Concept SRL — Pure C# WPF, no XAML
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Color      = System.Windows.Media.Color;
using Grid       = System.Windows.Controls.Grid;
using ComboBox   = System.Windows.Controls.ComboBox;
using TextBox    = System.Windows.Controls.TextBox;
using Popup      = System.Windows.Controls.Primitives.Popup;
using Visibility = System.Windows.Visibility;

namespace METools.FamilyPlacer
{
    public class FamilyPlacerWindow : METools.MeToolsWindowBase
    {
        // ── Colors ────────────────────────────────────────────────────────────
        // Mirrors MeToolsTheme so this window follows the Light/Dark toggle and
        // the ME-Concept brand palette, instead of a frozen, always-light copy.
        private static Color CPetrol     => MeToolsTheme.CPetrol;
        private static Color CPetrolDark => MeToolsTheme.CPetrolDark;
        private static Color CStatusBar  => MeToolsTheme.CStatusBar;
        private static Color CBg         => MeToolsTheme.CBg;
        private static Color CSurface    => MeToolsTheme.CSurface;
        private static Color CBorder     => MeToolsTheme.CBorder;
        private static Color CText       => MeToolsTheme.CText;
        private static Color CMuted      => MeToolsTheme.CMuted;
        private static Color CDim        => MeToolsTheme.Current == MeTheme.Dark ? Color.FromRgb(0x5A, 0x7E, 0x7C) : Color.FromRgb(0xa8, 0xb4, 0xbb);

        // ── State ─────────────────────────────────────────────────────────────
        private readonly ExternalEvent         _extEvent;
        private readonly FamilyPlacerHandler   _handler;
        private readonly ExternalEvent                _inspectEvent;
        private readonly FamilyParamInspectorHandler  _inspectHandler;
        private readonly Dictionary<string, List<FamilyParamInfo>> _paramCache
            = new Dictionary<string, List<FamilyParamInfo>>();
        private readonly List<FamilyTypeInfo>  _allFamilies;
        private          List<PlacerTemplate>  _templates;
        private          string                _orientation = "Vertical";
        private          ElementId             _selectedLevelId = ElementId.InvalidElementId;
        private          List<LevelInfo>       _levels = new List<LevelInfo>();

        // ── UI refs ───────────────────────────────────────────────────────────
        private ComboBox    _tplCombo;
        private ComboBox    _levelCombo;
        private StackPanel  _slotPanel;
        private TextBlock   _statusTxt;
        private Button      _btnOri1, _btnOri2;
        private TextBlock   _statusCount;

        private readonly List<SlotRow> _rows = new List<SlotRow>();

        public FamilyPlacerWindow(ExternalEvent extEvent,
                                  FamilyPlacerHandler handler,
                                  List<FamilyTypeInfo> families,
                                  List<LevelInfo> levels,
                                  ElementId defaultLevelId,
                                  ExternalEvent inspectEvent,
                                  FamilyParamInspectorHandler inspectHandler)
        {
            _extEvent        = extEvent;
            _handler         = handler;
            _allFamilies     = families;
            _templates       = TemplateManager.Load();
            _levels          = levels;
            _selectedLevelId = defaultLevelId;
            _inspectEvent    = inspectEvent;
            _inspectHandler  = inspectHandler;

            // Wire handler callbacks
            _handler.OnStatus = msg => Dispatcher.Invoke(() => SetStatus(msg));
            _handler.OnPlaced = n  => Dispatcher.Invoke(UpdateCount);

            // Window setup
            InitWindow("Family Placer", 520);
            BuildUI();
            AddInitialSlot();
        }

        protected override void OnThemeChanged() { Background = MeToolsTheme.BrBg; }
        protected override string AppKey => "FamilyPlacer";

        // ─────────────────────────────────────────────────────────────────────
        // UI BUILD
        // ─────────────────────────────────────────────────────────────────────
        private void BuildUI()
        {
            // RootDock von MeToolsWindowBase

            // Titelleiste von MeToolsWindowBase

            BuildStatusBar("0 families configured", "Revit 2025");
            _statusCount = StatusLeft;
            _statusTxt   = StatusRight;

            // ── Body ──────────────────────────────────────────────────────────
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 700,
            };
            var body = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };

            // Logo watermark — large, transparent, bottom-right
            var wm = LoadEmbeddedLogo();
            if (wm != null)
            {
                var wmGrid = new Grid();
                var wmImg = new System.Windows.Controls.Image
                {
                    Source              = wm,
                    Width               = 380,
                    Height              = 380,
                    Opacity             = 0.05,
                    IsHitTestVisible    = false,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment   = VerticalAlignment.Center,
                    Margin              = new Thickness(0, 40, -20, 0),
                };
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(
                    wmImg, System.Windows.Media.BitmapScalingMode.HighQuality);
                wmGrid.Children.Add(body);
                wmGrid.Children.Add(wmImg);
                scroll.Content = wmGrid;
            }
            else
            {
                scroll.Content = body;
            }
            RootDock.Children.Add(scroll);

            // Template section
            body.Children.Add(SectionLabel("Template"));
            var tplRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            tplRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tplRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            tplRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _tplCombo = StyledCombo(30, 12);
            RefreshTemplateCombo();
            _tplCombo.SelectionChanged += TplCombo_SelectionChanged;
            Grid.SetColumn(_tplCombo, 0);

            var btnSave = MakeIconBtn("\uE74E", "Save current config as template", SaveTemplate);
            btnSave.Margin = new Thickness(5, 0, 0, 0);
            Grid.SetColumn(btnSave, 1);

            var btnDel = MakeIconBtn("\uE74D", "Delete selected template", DeleteTemplate);
            btnDel.Margin = new Thickness(4, 0, 0, 0);
            Grid.SetColumn(btnDel, 2);

            tplRow.Children.Add(_tplCombo);
            tplRow.Children.Add(btnSave);
            tplRow.Children.Add(btnDel);
            body.Children.Add(tplRow);

            // Orientation section
            body.Children.Add(SectionLabel("Arrangement"));
            var oriRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin      = new Thickness(0, 0, 0, 12)
            };

            _btnOri1 = MakeOriBtn("↕  Stacked",     "Vertical",    true);
            _btnOri2 = MakeOriBtn("↔  Side by Side", "Horizontal",  false);
            _btnOri1.Margin = new Thickness(0, 0, 5, 0);
            oriRow.Children.Add(_btnOri1);
            oriRow.Children.Add(_btnOri2);
            body.Children.Add(oriRow);

            // ── Level — always visible, sets workplane for placement ──────────
            body.Children.Add(SectionLabel("Level"));
            _levelCombo = StyledCombo(30, 12); _levelCombo.Margin = new Thickness(0, 0, 0, 14);
            foreach (var lv in _levels)
                _levelCombo.Items.Add($"{lv.Name}  ({lv.Elevation:F2} m)");
            var defIdx = _levels.FindIndex(l => l.Id == _selectedLevelId);
            int startIdx = defIdx >= 0 ? defIdx : (_levels.Count > 0 ? 0 : -1);
            _levelCombo.SelectedIndex = startIdx;
            if (startIdx >= 0 && startIdx < _levels.Count)
                _selectedLevelId = _levels[startIdx].Id;
            _levelCombo.SelectionChanged += (s, e) =>
            {
                int i = _levelCombo.SelectedIndex;
                _selectedLevelId = (i >= 0 && i < _levels.Count)
                    ? _levels[i].Id : ElementId.InvalidElementId;
            };
            body.Children.Add(_levelCombo);

            // Column headers
            var hdr = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });   // 0 handle
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });   // 1 #
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 2 family
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });    // 3 gap
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });   // 4 niveau
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });    // 5 gap
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });   // 6 off X
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });    // 7 gap
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(75) });   // 8 off Y + Y=Frame
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });    // 9 gap
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });   // 10 gear
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });    // 11 gap
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });   // 12 del
            AddHdr(hdr, "Family", 2); AddHdr(hdr, "Niveau", 4); AddHdr(hdr, "Off X", 6); AddHdr(hdr, "Off Y / Y=Frame", 8);
            body.Children.Add(hdr);

            _slotPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            body.Children.Add(_slotPanel);

            // Add slot button
            var btnAdd = new Button
            {
                Content             = "+ Add Slot",
                Height              = 30,
                FontSize            = 12,
                Background          = Brushes.Transparent,
                BorderBrush         = MeToolsTheme.BrBorder,
                BorderThickness     = new Thickness(1),
                Foreground          = new SolidColorBrush(CDim),
                Margin              = new Thickness(0, 0, 0, 12),
                Cursor              = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Template            = RoundedBtnTemplate(),
            };
            btnAdd.Click       += (s, e) => AddSlot();
            btnAdd.MouseEnter  += (s, e) => {
                btnAdd.BorderBrush = MeToolsTheme.BrPetrol;
                btnAdd.Foreground  = MeToolsTheme.BrPetrol;
            };
            btnAdd.MouseLeave  += (s, e) => {
                btnAdd.BorderBrush = MeToolsTheme.BrBorder;
                btnAdd.Foreground  = new SolidColorBrush(CDim);
            };
            body.Children.Add(btnAdd);

            // Divider
            body.Children.Add(new Separator { Margin = new Thickness(0, 0, 0, 10) });

            // Info box (same style as Lamp Placer)
            body.Children.Add(InfoBox(
                "Place: position the family (SPACEBAR rotates, click a wall face to host) and click once - it finishes after that one drop. "
                + "Multi-Place: same, but click several positions and press ESC to finish. "
                + "Wall detection and free work plane are supported."));

            // Place buttons row
            var btnRow = new Grid();
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });

            var btnPlace = MakePlaceBtn("▶  Place", false, PlaceSingle); btnPlace.Template = RoundedBtnTemplate();
            Grid.SetColumn(btnPlace, 0);

            var btnMulti = MakePlaceBtn("⊕  Multi-Place", true, PlaceMulti); btnMulti.Template = RoundedBtnTemplate();
            Grid.SetColumn(btnMulti, 2);

            btnRow.Children.Add(btnPlace);
            btnRow.Children.Add(btnMulti);
            body.Children.Add(btnRow);
        }

        // ─────────────────────────────────────────────────────────────────────
        // DRAG REORDER
        // ─────────────────────────────────────────────────────────────────────
        private SlotRow              _dragRow;
        private System.Windows.Point _dragStart;
        private bool                 _dragging;

        private void StartDrag(SlotRow row, MouseButtonEventArgs e)
        {
            _dragRow   = row;
            _dragStart = e.GetPosition(_slotPanel);
            _dragging  = false;

            _slotPanel.MouseMove  += OnDragMove;
            _slotPanel.MouseLeave += OnDragEnd;
            Mouse.Capture(row.Handle);
            row.Handle.MouseLeftButtonUp += OnDragDrop;
        }

        private void OnDragMove(object sender, MouseEventArgs e)
        {
            if (_dragRow == null) return;
            var pos = e.GetPosition(_slotPanel);

            // Highlight target slot based on Y position
            int targetIdx = GetDropIndex(pos.Y);
            int currentIdx = _rows.IndexOf(_dragRow);
            if (targetIdx == currentIdx) return;

            _dragging = true;
            // Visual: dim dragged row
            _dragRow.Container.Opacity = 0.5;

            // Highlight target slot
            for (int i = 0; i < _rows.Count; i++)
                _rows[i].Container.BorderBrush = i == targetIdx
                    ? MeToolsTheme.BrPetrol
                    : MeToolsTheme.BrBorder;
        }

        private void OnDragDrop(object sender, MouseButtonEventArgs e)
        {
            OnDragEnd(null, null);
            if (_dragRow == null) return;

            var pos = e.GetPosition(_slotPanel);
            int targetIdx  = GetDropIndex(pos.Y);
            int currentIdx = _rows.IndexOf(_dragRow);

            if (targetIdx != currentIdx && targetIdx >= 0 && targetIdx < _rows.Count)
            {
                _rows.Remove(_dragRow);
                _rows.Insert(targetIdx, _dragRow);

                _slotPanel.Children.Clear();
                foreach (var r in _rows) _slotPanel.Children.Add(r.Container);

                // Recalculate indices and offsets
                for (int i = 0; i < _rows.Count; i++)
                {
                    _rows[i].SetIndex(i + 1);
                    SetStackOffset(_rows[i], i);
                }
                RecalculateOffsetY();
                UpdateCount();
            }

            _dragRow = null;
        }

        private void OnDragEnd(object sender, EventArgs e)
        {
            _slotPanel.MouseMove  -= OnDragMove;
            _slotPanel.MouseLeave -= OnDragEnd;

            if (_dragRow != null)
            {
                _dragRow.Handle.MouseLeftButtonUp -= OnDragDrop;
                _dragRow.Container.Opacity = 1.0;
                _dragRow = null;
            }
            Mouse.Capture(null);

            // Reset border highlights
            foreach (var r in _rows) r.Container.BorderBrush = MeToolsTheme.BrBorder;
        }

        private int GetDropIndex(double y)
        {
            double cumY = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                double h = _rows[i].Container.ActualHeight + 4; // 4 = margin
                if (y < cumY + h * 0.5) return i;
                cumY += h;
            }
            return _rows.Count - 1;
        }
        private void AddInitialSlot() => AddSlot();

        // Sets the "auto-stacking" offset on the axis that matches the current
        // arrangement: Off Y when Stacked (vertical), Off X when Side by Side
        // (horizontal). Both AddSlot/RemoveSlot/drag-reorder route through this
        // so they never fall out of sync with whichever mode is active.
        private void SetStackOffset(SlotRow row, int index)
        {
            if (_orientation == "Horizontal") row.SetOffsetX(index);
            else row.SetOffsetY(index);
        }

        private void AddSlot(FamilySlot data = null)
        {
            var row = new SlotRow(_allFamilies, data, _rows.Count + 1, RequestFamilyParams, RequestNiveauSample);
            row.OnRemove       = () => RemoveSlot(row);
            row.OnChanged      = UpdateCount;
            row.OnHeightChanged = () => RecalculateOffsetY();

            // Wire drag-reorder: hold and drag the ⠿ handle to move slot up/down
            row.Container.Tag = row;
            row.Handle.PreviewMouseLeftButtonDown += (s, e) => StartDrag(row, e);

            _rows.Add(row);
            _slotPanel.Children.Add(row.Container);

            // Auto-set offsets only when adding a blank slot (not loading from template)
            if (data == null)
            {
                int newIndex = _rows.Count - 1; // 0-based position of the new slot

                // 2D stacking offset — Off Y when Stacked, Off X when Side by Side:
                // each new slot gets the next index so they line up along whichever
                // axis matches the current arrangement.
                SetStackOffset(row, newIndex);

                // 3D vertical offset (3DZ_Niveau_Versatzfaktor via ParamOverrides) is a
                // Stacked-only concern: Side by Side already spreads slots out in space,
                // so there's no collision to avoid and this must not touch Z there.
                if (_orientation == "Vertical")
                {
                    // When a new slot has the same Height as existing slots, add offset in
                    // 3D so they don't physically collide in the model.
                    // First at this height = 0, second = -1, third = -2, ...
                    double newHeight = row.Slot.Height;
                    int collisions = _rows
                        .Take(_rows.Count - 1) // all slots before this one
                        .Count(r => Math.Abs(r.Slot.Height - newHeight) < 0.5);

                    if (collisions > 0)
                        row.Slot.ParamOverrides["3DZ_Niveau_Versatzfaktor"] = (-collisions).ToString();
                }
            }

            UpdateCount();
        }

        private void RemoveSlot(SlotRow row)
        {
            _rows.Remove(row);
            _slotPanel.Children.Remove(row.Container);
            // Renumber and recalculate offsets
            for (int i = 0; i < _rows.Count; i++)
            {
                _rows[i].SetIndex(i + 1);
                SetStackOffset(_rows[i], i); // 2D stacking offset = position index, on the active axis
            }
            RecalculateOffsetY();
            UpdateCount();
        }

        private void UpdateCount()
        {
            int valid = _rows.Count(r => !string.IsNullOrEmpty(r.Slot.FamilyName));
            _statusCount.Text = $"{valid} famil{(valid != 1 ? "ies" : "y")} configured";
        }

        // Recalculate 3DZ_Niveau_Versatzfaktor for all slots based on height collisions.
        // Called when any slot's height changes.
        // Groups slots by height, assigns 3DZ: 0, -1, -2, ... within each group.
        // Stacked-only: Side by Side already spreads slots out in space so there's no
        // collision to avoid, and Z must not be touched there — any stale value from
        // before switching modes gets cleared instead.
        private void RecalculateOffsetY()
        {
            if (_orientation != "Vertical")
            {
                foreach (var row in _rows)
                    row.Slot.ParamOverrides.Remove("3DZ_Niveau_Versatzfaktor");
                return;
            }

            var heightCount = new Dictionary<double, int>();
            foreach (var row in _rows)
            {
                double h = Math.Round(row.Slot.Height, 0);
                if (!heightCount.ContainsKey(h)) heightCount[h] = 0;
                int count = heightCount[h];

                if (count == 0)
                    row.Slot.ParamOverrides.Remove("3DZ_Niveau_Versatzfaktor");
                else
                    row.Slot.ParamOverrides["3DZ_Niveau_Versatzfaktor"] = (-count).ToString();

                heightCount[h]++;
            }
        }

        private void RequestFamilyParams(string fam, string type, Action<List<FamilyParamInfo>> cb)
        {
            if (cb == null) return;
            if (string.IsNullOrEmpty(fam)) { cb(new List<FamilyParamInfo>()); return; }
            string key = fam + "|" + (type ?? "");
            if (_paramCache.TryGetValue(key, out var cached)) { cb(cached); return; }
            if (_inspectEvent == null || _inspectHandler == null) { cb(new List<FamilyParamInfo>()); return; }

            _inspectHandler.SampleNiveauOnly = false;
            _inspectHandler.FamilyName = fam;
            _inspectHandler.TypeName   = type ?? "";
            _inspectHandler.OnResult   = list => Dispatcher.Invoke(() =>
            {
                var safe = list ?? new List<FamilyParamInfo>();
                _paramCache[key] = safe;
                cb(safe);
            });
            _inspectEvent.Raise();
        }

        // Samples the most-common Niveau (mm) from placed instances of the family -- no EditFamily,
        // so it never triggers Revit's family-constraint warning dialog.
        private readonly Dictionary<string, double?> _niveauCache = new Dictionary<string, double?>();
        private void RequestNiveauSample(string fam, Action<double?> cb)
        {
            if (cb == null) return;
            if (string.IsNullOrEmpty(fam)) { cb(null); return; }
            if (_niveauCache.TryGetValue(fam, out var cached)) { cb(cached); return; }
            if (_inspectEvent == null || _inspectHandler == null) { cb(null); return; }

            _inspectHandler.SampleNiveauOnly = true;
            _inspectHandler.FamilyName       = fam;
            _inspectHandler.OnNiveau         = v => Dispatcher.Invoke(() =>
            {
                _niveauCache[fam] = v;
                cb(v);
            });
            _inspectEvent.Raise();
        }

        private void SetStatus(string msg) => _statusTxt.Text = msg;

        // ─────────────────────────────────────────────────────────────────────
        // PLACEMENT ACTIONS
        // ─────────────────────────────────────────────────────────────────────
        private void PlaceSingle()
        {
            _handler.Request = new PlacerRequest
            {
                Action      = HandlerAction.PlaceSingle,
                Slots       = _rows.Select(r => r.Slot).ToList(),
                Orientation = _orientation,
                LevelId     = _selectedLevelId,
            };
            SetStatus("SPACEBAR rotates - click a face/point - places one, then finishes.");
            _extEvent.Raise();
        }

        private void PlaceMulti()
        {
            _handler.Request = new PlacerRequest
            {
                Action      = HandlerAction.PlaceMulti,
                Slots       = _rows.Select(r => r.Slot).ToList(),
                Orientation = _orientation,
                LevelId     = _selectedLevelId,
            };
            SetStatus("Click each position - SPACEBAR rotates - ESC to finish.");
            _extEvent.Raise();
        }

        // ─────────────────────────────────────────────────────────────────────
        // TEMPLATE MANAGEMENT
        // ─────────────────────────────────────────────────────────────────────
        private void RefreshTemplateCombo()
        {
            _tplCombo.Items.Clear();
            _tplCombo.Items.Add("— Select Template —");
            foreach (var t in _templates)
                _tplCombo.Items.Add(t.Name);
            _tplCombo.SelectedIndex = 0;
        }

        private void TplCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_tplCombo.SelectedIndex <= 0)
            {
                // "Select Template" / new -> reset to a single blank slot
                _rows.Clear();
                _slotPanel.Children.Clear();
                AddSlot();
                return;
            }
            var tpl = _templates[_tplCombo.SelectedIndex - 1];

            // Load orientation
            _orientation = tpl.Orientation;
            _btnOri1.Background = _orientation == "Vertical"
                ? MeToolsTheme.BrActiveBg : MeToolsTheme.BrBtnBg;
            _btnOri2.Background = _orientation == "Horizontal"
                ? MeToolsTheme.BrActiveBg : MeToolsTheme.BrBtnBg;

            // Load slots
            _rows.Clear();
            _slotPanel.Children.Clear();
            foreach (var slot in tpl.Slots)
                AddSlot(slot);
        }

        private void SaveTemplate()
        {
            string name;

            if (_tplCombo.SelectedIndex > 0)
            {
                // A named template is loaded -> overwrite it silently, no prompt
                name = _templates[_tplCombo.SelectedIndex - 1].Name;
            }
            else
            {
                // No template selected -> Save As (ask for a name)
                var dlg = new SaveTemplateDialog();
                if (dlg.ShowDialog() != true || string.IsNullOrEmpty(dlg.TemplateName)) return;
                name = dlg.TemplateName.Trim();
                if (name.Length == 0) return;
            }

            var slots = _rows.Select(r => r.Slot).ToList();
            var existing = _templates.FirstOrDefault(t => t.Name == name);
            if (existing != null)
            {
                // update in place (keeps list order)
                existing.Orientation = _orientation;
                existing.Slots       = slots;
            }
            else
            {
                _templates.Add(new PlacerTemplate
                {
                    Name        = name,
                    Orientation = _orientation,
                    Slots       = slots,
                });
            }

            TemplateManager.Save(_templates);
            RefreshTemplateCombo();

            // Re-select the saved template
            var idx = _templates.FindIndex(t => t.Name == name);
            if (idx >= 0) _tplCombo.SelectedIndex = idx + 1;
        }

        private void DeleteTemplate()
        {
            if (_tplCombo.SelectedIndex <= 0) return;
            var tpl = _templates[_tplCombo.SelectedIndex - 1];
            var res = MessageBox.Show($"Delete template \"{tpl.Name}\"?",
                "ME-Tools", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;
            _templates.Remove(tpl);
            TemplateManager.Save(_templates);
            RefreshTemplateCombo();
        }

        // ─────────────────────────────────────────────────────────────────────
        // UI HELPERS
        // ─────────────────────────────────────────────────────────────────────
        private System.Windows.Media.ImageSource LoadEmbeddedLogo(int size = 32)
        {
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream("METools.Icons.icon_logo.png")
                                 ?? asm.GetManifestResourceStream("METools.Icons.base_icon_transparent_background.png"))
                {
                    if (stream == null) return null;
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.StreamSource = stream;
                    bmp.CacheOption  = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
            }
            catch { return null; }
        }

        private FrameworkElement SectionLabel(string text)
        {
            var sp = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin      = new Thickness(0, 0, 0, 6)
            };
            sp.Children.Add(new Border { Height = 1, Width = 10,
                Background = MeToolsTheme.BrBorder,
                VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock
            {
                Text       = $"  {text.ToUpper()}  ",
                FontSize   = 9.5,
                FontWeight = FontWeights.Bold,
                Foreground = MeToolsTheme.BrMuted,
                VerticalAlignment = VerticalAlignment.Center,
            });
            sp.Children.Add(new Border { Height = 1, HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = MeToolsTheme.BrBorder,
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 200 });
            return sp;
        }

        private Button MakeIconBtn(string icon, string tip, Action onClick)
        {
            var btn = new Button
            {
                Content         = icon,
                Width           = 30, Height = 30,
                FontSize        = 14,
                FontFamily      = new FontFamily("Segoe MDL2 Assets"),
                Foreground      = MeToolsTheme.Current == MeTheme.Dark ? Brushes.White : MeToolsTheme.BrText,
                ToolTip         = tip,
                Background      = MeToolsTheme.BrBtnBg,
                BorderBrush     = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1),
                Cursor          = Cursors.Hand,
            };
            btn.Template = RoundedBtnTemplate();
            btn.Click    += (s, e) => onClick();
            btn.MouseEnter += (s, e) => { btn.Background = MeToolsTheme.BrActiveBg; btn.BorderBrush = MeToolsTheme.BrPetrol; };
            btn.MouseLeave += (s, e) => { btn.Background = MeToolsTheme.BrBtnBg;    btn.BorderBrush = MeToolsTheme.BrBorder; };
            return btn;
        }

        private Button MakeOriBtn(string label, string mode, bool active)
        {
            var btn = new Button
            {
                Content         = label,
                Height          = 32,
                MinWidth        = 120,
                Padding         = new Thickness(12, 0, 12, 0),
                FontSize        = 12,
                Background      = active ? MeToolsTheme.BrActiveBg : MeToolsTheme.BrBtnBg,
                BorderBrush     = active ? MeToolsTheme.BrPetrol    : MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1),
                Foreground      = active ? MeToolsTheme.BrActiveFg  : MeToolsTheme.BrMuted,
                Cursor          = Cursors.Hand,
            };
            btn.Template = RoundedBtnTemplate();
            btn.Click += (s, e) => SetOrientation(mode);
            return btn;
        }

        private void SetOrientation(string mode)
        {
            bool changed = mode != _orientation;
            _orientation = mode;
            bool isV = mode == "Vertical";
            UpdateOriBtn(_btnOri1, isV);
            UpdateOriBtn(_btnOri2, !isV);

            if (!changed) return;

            // Swap Off X <-> Off Y on every slot so whatever stacking order was
            // already set up (e.g. 0,1,2,3 from auto-increment) carries over to
            // the newly-active axis instead of being left behind on the old one.
            foreach (var row in _rows)
            {
                int oldX = row.Slot.OffsetX;
                int oldY = row.Slot.OffsetY;
                row.SetOffsetX(oldY);
                row.SetOffsetY(oldX);
            }

            // 3D height-collision avoidance is Stacked-only: this restores it when
            // switching back to Vertical, and clears any stale value when switching
            // to Side by Side (RecalculateOffsetY handles both, based on _orientation).
            RecalculateOffsetY();
        }
        private void UpdateOriBtn(Button b, bool active)
        {
            if (b == null) return;
            b.Background  = active ? MeToolsTheme.BrActiveBg : MeToolsTheme.BrBtnBg;
            b.BorderBrush = active ? MeToolsTheme.BrPetrol    : MeToolsTheme.BrBorder;
            b.Foreground  = active ? MeToolsTheme.BrActiveFg  : MeToolsTheme.BrMuted;
        }

        private Button MakePlaceBtn(string label, bool isOutline, Action onClick)
        {
            var btn = new Button
            {
                Content         = label,
                Height          = 36,
                FontSize        = 13,
                Padding         = new Thickness(16, 0, 16, 0),
                FontWeight      = FontWeights.SemiBold,
                Background      = isOutline ? MeToolsTheme.BrBtnBg : MeToolsTheme.BrPetrol,
                BorderBrush     = MeToolsTheme.BrPetrol,
                BorderThickness = new Thickness(1.5),
                Foreground      = isOutline ? (MeToolsTheme.Current == MeTheme.Dark ? Brushes.White : MeToolsTheme.BrPetrol) : Brushes.White,
                Cursor          = Cursors.Hand,
            };
            btn.Template   = RoundedBtnTemplate();
            btn.Click      += (s, e) => onClick();
            btn.MouseEnter += (s, e) => btn.Background = isOutline
                ? MeToolsTheme.BrActiveBg
                : MeToolsTheme.BrPetrolDark;
            btn.MouseLeave += (s, e) => btn.Background = isOutline
                ? MeToolsTheme.BrBtnBg
                : MeToolsTheme.BrPetrol;
            return btn;
        }

        private void AddHdr(Grid g, string text, int col)
        {
            var tb = new TextBlock
            {
                Text      = text,
                FontSize  = 9.5,
                Foreground = MeToolsTheme.BrMuted,
                FontWeight = FontWeights.Bold,
                Margin    = new Thickness(2, 0, 0, 0),
            };
            Grid.SetColumn(tb, col);
            g.Children.Add(tb);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SlotRow — individual family slot UI row
    // ─────────────────────────────────────────────────────────────────────────
    internal class SlotRow
    {
        public Action OnRemove;
        public Action OnChanged;
        public Action OnHeightChanged;
        public TextBlock Handle { get; private set; } // the ⠿ drag handle

        public Border    Container { get; }
        public FamilySlot Slot    { get; } = new FamilySlot();

        private readonly List<FamilyTypeInfo> _all;
        private readonly Action<string, string, Action<List<FamilyParamInfo>>> _requestParams;
        private readonly Action<string, Action<double?>> _requestNiveau;
        private ComboBox   _typeCmb;
        private TextBox    _familyText;
        private Popup      _familyPopup;
        private StackPanel _familyPopupList;
        private bool       _suppressFamilyTextChanged;
        private TextBox    _heightTxt, _offXTxt, _offYTxt;
        private TextBlock  _idxBadge;
        private int        _index;

        private Button     _gearBtn;
        private TextBlock  _gearDot;
        private System.Windows.Controls.Primitives.Popup _popup;
        private StackPanel _paramFieldsPanel;
        private string     _paramsBuiltKey;

        public SlotRow(List<FamilyTypeInfo> all, FamilySlot data, int index,
                       Action<string, string, Action<List<FamilyParamInfo>>> requestParams,
                       Action<string, Action<double?>> requestNiveau)
        {
            _all           = all;
            _index         = index;
            _requestParams = requestParams;
            _requestNiveau = requestNiveau;

            if (data != null)
            {
                Slot.FamilyName   = data.FamilyName;
                Slot.TypeName     = data.TypeName;
                Slot.Height       = data.Height;
                Slot.OffsetX      = data.OffsetX;
                Slot.OffsetY      = data.OffsetY;
                if (data.ParamOverrides != null)
                    Slot.ParamOverrides = new Dictionary<string, string>(data.ParamOverrides);
            }

            Container = BuildRow();
            UpdateGearDot();
        }

        public void SetIndex(int i)
        {
            _index = i;
            if (_idxBadge != null) _idxBadge.Text = i.ToString();
        }

        // Called by FamilyPlacerWindow to update offset values and refresh UI
        public void SetOffsetX(int val)
        {
            Slot.OffsetX = val;
            if (_offXTxt != null && _offXTxt.Text != val.ToString())
                _offXTxt.Text = val.ToString();
        }

        public void SetOffsetY(int val)
        {
            Slot.OffsetY = val;
            if (_offYTxt != null && _offYTxt.Text != val.ToString())
                _offYTxt.Text = val.ToString();
        }

        private Border BuildRow()
        {
            var border = new Border
            {
                Background      = MeToolsTheme.BrBtnBg,
                BorderBrush     = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(4),
                Padding         = new Thickness(6, 6, 6, 6),
                Margin          = new Thickness(0, 0, 0, 4),
            };

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });   // 0 handle
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });   // 1 index
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 2 family+type
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });    // 3 gap
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });   // 4 niveau
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });    // 5 gap
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });   // 6 off X
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });    // 7 gap
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(75) });   // 8 off Y + Y=Frame (wider)
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });    // 9 gap
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });   // 10 gear
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });    // 11 gap
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });   // 12 del

            // Drag handle
            Handle = new TextBlock
            {
                Text = "\u283F", FontSize = 13,
                Foreground = MeToolsTheme.BrMuted,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.SizeNS,
            };
            Grid.SetColumn(Handle, 0); g.Children.Add(Handle);

            // Index badge
            _idxBadge = new TextBlock
            {
                Text      = _index.ToString(),
                FontSize  = 10, FontWeight = FontWeights.Bold,
                Foreground = MeToolsTheme.BrActiveFg,
                VerticalAlignment   = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var badge = new Border
            {
                Background      = MeToolsTheme.BrActiveBg,
                BorderBrush     = new SolidColorBrush(Color.FromArgb(60, 0x18, 0x5f, 0x5f)),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(3),
                Width = 18, Height = 18,
                Child = _idxBadge,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(badge, 1); g.Children.Add(badge);

            // Family + Type stacked vertically
            var famType = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
            BuildFamilyPicker(famType);

            _typeCmb = new ComboBox { Height = 22, FontSize = 10, Margin = new Thickness(0, 2, 0, 0), Background = METools.MeToolsTheme.BrInput, Foreground = METools.MeToolsTheme.BrText, BorderBrush = METools.MeToolsTheme.BrBorder, BorderThickness = new Thickness(1) };
            RefreshTypes();
            if (!string.IsNullOrEmpty(Slot.TypeName))
            {
                int ti = _typeCmb.Items.IndexOf(Slot.TypeName);
                if (ti >= 0) _typeCmb.SelectedIndex = ti;
            }
            _typeCmb.Template = METools.MeToolsWindowBase.MakeComboBoxTemplate();
            METools.MeToolsWindowBase.ApplyComboStyle(_typeCmb);
            _typeCmb.SelectionChanged += TypeChanged;
            famType.Children.Add(_typeCmb);

            Grid.SetColumn(famType, 2); g.Children.Add(famType);

            // Height
            var hSp = new StackPanel { Margin = new Thickness(0) };
            hSp.Children.Add(Lbl("Niveau"));
            _heightTxt = new TextBox
            {
                Text   = Slot.Height.ToString("F0"),
                Height = 28, FontSize = 12,
                TextAlignment = TextAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = METools.MeToolsTheme.BrInput,
                Foreground = METools.MeToolsTheme.BrText,
                BorderBrush = METools.MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1),
            };
            _heightTxt.TextChanged += HeightChanged;
            hSp.Children.Add(_heightTxt);
            Grid.SetColumn(hSp, 4); g.Children.Add(hSp);

            // Offset X factor (horizontal)
            var oxSp = new StackPanel();
            oxSp.Children.Add(Lbl("Off X"));
            _offXTxt = new TextBox
            {
                Text   = Slot.OffsetX.ToString(),
                Height = 28, FontSize = 12,
                TextAlignment = TextAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = METools.MeToolsTheme.BrInput,
                Foreground = METools.MeToolsTheme.BrText,
                BorderBrush = METools.MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1),
            };
            _offXTxt.TextChanged += OffsetXChanged;
            oxSp.Children.Add(_offXTxt);
            Grid.SetColumn(oxSp, 6); g.Children.Add(oxSp);

            // Offset Y factor (vertical)
            var oySp = new StackPanel();
            oySp.Children.Add(Lbl("Off Y"));
            _offYTxt = new TextBox
            {
                Text   = Slot.OffsetY.ToString(),
                Height = 28, FontSize = 12,
                TextAlignment = TextAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = METools.MeToolsTheme.BrInput,
                Foreground = METools.MeToolsTheme.BrText,
                BorderBrush = METools.MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1),
            };
            _offYTxt.TextChanged += OffsetYChanged;
            oySp.Children.Add(_offYTxt);

            // Y = Frame checkbox -- default ON (ticked), full text visible
            bool yFrameDef = true; // default: Y offset follows frame
            bool yFrameCur = Slot.ParamOverrides.TryGetValue("Y_Versatz_gleich_Rahmen", out var yFrameOv)
                ? yFrameOv == "1"
                : yFrameDef;
            var yFrameCb = new CheckBox
            {
                Content   = "Y=Frame",
                IsChecked = yFrameCur,
                FontSize  = 9,
                Foreground = METools.MeToolsTheme.BrPetrol,
                Margin    = new Thickness(0, 2, 0, 0),
                ToolTip   = "Y Offset = Frame (Y_Versatz_gleich_Rahmen): aligns element to the frame edge",
            };
            // Set the default in ParamOverrides if not already set
            if (!Slot.ParamOverrides.ContainsKey("Y_Versatz_gleich_Rahmen") && yFrameDef)
                Slot.ParamOverrides["Y_Versatz_gleich_Rahmen"] = "1";
            yFrameCb.Checked   += (s, e) => { Slot.ParamOverrides["Y_Versatz_gleich_Rahmen"] = "1"; UpdateGearDot(); OnChanged?.Invoke(); };
            yFrameCb.Unchecked += (s, e) => { Slot.ParamOverrides["Y_Versatz_gleich_Rahmen"] = "0"; UpdateGearDot(); OnChanged?.Invoke(); };
            oySp.Children.Add(yFrameCb);
            Grid.SetColumn(oySp, 8); g.Children.Add(oySp);

            // Gear (per-family parameters) + override dot
            _gearBtn = new Button
            {
                Content         = "\u2699",
                Width           = 26, Height = 26,
                FontSize        = 13,
                Background      = METools.MeToolsTheme.BrBtnBg,
                BorderBrush     = METools.MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1),
                Foreground      = METools.MeToolsTheme.BrMuted,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor          = Cursors.Hand,
                ToolTip         = "Family parameters",
                Template        = METools.MeToolsWindowBase.RoundedBtnTemplate(),
            };
            _gearBtn.Click += (s, e) => ToggleParamsPopup();

            var gearGrid = new Grid { VerticalAlignment = VerticalAlignment.Center };
            gearGrid.Children.Add(_gearBtn);
            _gearDot = new TextBlock
            {
                Text                = "\u25CF",
                FontSize            = 8,
                Foreground          = MeToolsTheme.BrPetrol,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment   = VerticalAlignment.Top,
                Margin              = new Thickness(0, -1, 1, 0),
                IsHitTestVisible    = false,
                Visibility          = Visibility.Collapsed,
            };
            gearGrid.Children.Add(_gearDot);
            Grid.SetColumn(gearGrid, 10); g.Children.Add(gearGrid);

            // Remove button
            var delBtn = new Button
            {
                Content         = "\u00D7",
                Width           = 26, Height = 26,
                FontSize        = 14,
                Background      = METools.MeToolsTheme.BrBtnBg,
                BorderBrush     = METools.MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1),
                Foreground      = METools.MeToolsTheme.BrMuted,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor          = Cursors.Hand,
                Template        = METools.MeToolsWindowBase.RoundedBtnTemplate(),
            };
            delBtn.Click      += (s, e) => OnRemove?.Invoke();
            delBtn.MouseEnter += (s, e) => {
                delBtn.Foreground  = new SolidColorBrush(Color.FromRgb(0xf4, 0x47, 0x47));
                delBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(0xf4, 0x47, 0x47));
            };
            delBtn.MouseLeave += (s, e) => {
                delBtn.Foreground  = MeToolsTheme.BrMuted;
                delBtn.BorderBrush = MeToolsTheme.BrBorder;
            };
            Grid.SetColumn(delBtn, 12); g.Children.Add(delBtn);

            border.Child = g;
            return border;
        }

        // --- Parameter popup ---
        private void ToggleParamsPopup()
        {
            if (_popup == null) BuildPopupShell();
            _popup.IsOpen = !_popup.IsOpen;
            if (_popup.IsOpen) EnsureParamsLoaded();
        }

        private void BuildPopupShell()
        {
            _paramFieldsPanel = new StackPanel();

            var header = new TextBlock
            {
                Text       = "Family Parameters",
                FontSize   = 11, FontWeight = FontWeights.Bold,
                Foreground = MeToolsTheme.BrText,
                Margin     = new Thickness(0, 0, 0, 6),
            };

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 320,
                Content   = _paramFieldsPanel,
            };

            var inner = new StackPanel { Margin = new Thickness(10, 8, 10, 8) };
            inner.Children.Add(header);
            inner.Children.Add(scroll);

            var shell = new Border
            {
                Background      = MeToolsTheme.BrSurface,
                BorderBrush     = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(5),
                Width           = 240,
                Child           = inner,
                Effect          = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 10, ShadowDepth = 2, Opacity = 0.25, Color = Colors.Black,
                },
            };

            _popup = new System.Windows.Controls.Primitives.Popup
            {
                PlacementTarget    = _gearBtn,
                Placement          = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                StaysOpen          = false,
                AllowsTransparency = true,
                Child              = shell,
            };
        }

        private void EnsureParamsLoaded()
        {
            string key = (Slot.FamilyName ?? "") + "|" + (Slot.TypeName ?? "");
            if (_paramsBuiltKey == key) return;

            _paramFieldsPanel.Children.Clear();
            if (string.IsNullOrEmpty(Slot.FamilyName))
            {
                _paramFieldsPanel.Children.Add(InfoLine("Select a family first."));
                _paramsBuiltKey = null;
                return;
            }

            _paramFieldsPanel.Children.Add(InfoLine("Loading..."));
            _requestParams?.Invoke(Slot.FamilyName, Slot.TypeName, infos =>
            {
                _paramsBuiltKey = key;
                PopulateFields(infos);
            });
        }

        private void PopulateFields(List<FamilyParamInfo> infos)
        {
            _paramFieldsPanel.Children.Clear();
            if (infos == null || infos.Count == 0)
            {
                _paramFieldsPanel.Children.Add(InfoLine("No editable instance parameters."));
                return;
            }
            int shown = 0;
            foreach (var p in infos)
            {
                if (p.Inline) continue; // Niveau -> handled by the inline height field
                if (p.Name == "Y_Versatz_gleich_Rahmen") continue; // handled inline below Off Y
                if (p.Kind == "yesno") _paramFieldsPanel.Children.Add(BuildBoolField(p));
                else                   _paramFieldsPanel.Children.Add(BuildNumField(p));
                shown++;
            }
            if (shown == 0)
                _paramFieldsPanel.Children.Add(InfoLine("No editable instance parameters."));
            UpdateGearDot();
        }

        private FrameworkElement BuildNumField(FamilyParamInfo p)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 7) };
            string suffix = p.Kind == "length" ? " (mm)" : "";
            sp.Children.Add(new TextBlock
            {
                Text       = p.Label + suffix,
                FontSize   = 10, FontWeight = FontWeights.Bold,
                Foreground = MeToolsTheme.BrMuted,
                Margin     = new Thickness(0, 0, 0, 2),
            });

            string cur = Slot.ParamOverrides.TryGetValue(p.Name, out var ov) ? ov : p.DefaultValue;
            var tb = new TextBox
            {
                Text   = cur, Height = 26, FontSize = 11,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrText,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                CaretBrush = MeToolsTheme.BrText,
            };
            tb.TextChanged += (s, e) =>
            {
                var t = tb.Text?.Trim() ?? "";
                if (t == (p.DefaultValue ?? "")) Slot.ParamOverrides.Remove(p.Name);
                else                             Slot.ParamOverrides[p.Name] = t;
                UpdateGearDot();
                OnChanged?.Invoke();
            };
            sp.Children.Add(tb);
            return sp;
        }

        private FrameworkElement BuildBoolField(FamilyParamInfo p)
        {
            bool def = p.DefaultValue == "1";
            bool cur = Slot.ParamOverrides.TryGetValue(p.Name, out var ov) ? ov == "1" : def;
            var cb = new CheckBox
            {
                Content    = p.Label,
                IsChecked  = cur,
                FontSize   = 11,
                Foreground = MeToolsTheme.BrText,
                Margin     = new Thickness(0, 2, 0, 7),
            };
            cb.Checked   += (s, e) => ApplyBool(p, true,  def);
            cb.Unchecked += (s, e) => ApplyBool(p, false, def);
            return cb;
        }

        private void ApplyBool(FamilyParamInfo p, bool val, bool def)
        {
            if (val == def) Slot.ParamOverrides.Remove(p.Name);
            else            Slot.ParamOverrides[p.Name] = val ? "1" : "0";
            UpdateGearDot();
            OnChanged?.Invoke();
        }

        private TextBlock InfoLine(string t) => new TextBlock
        {
            Text = t, FontSize = 11, Foreground = MeToolsTheme.BrMuted,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2),
        };

        private void UpdateGearDot()
        {
            if (_gearDot != null)
                _gearDot.Visibility = (Slot.ParamOverrides != null && Slot.ParamOverrides.Count > 0)
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        public void ApplyTheme()
        {
            if (Container != null) { Container.Background = METools.MeToolsTheme.BrSurface; Container.BorderBrush = METools.MeToolsTheme.BrBorder; }
            if (_familyText != null)
            {
                _familyText.Background = METools.MeToolsTheme.BrInput;
                _familyText.BorderBrush = METools.MeToolsTheme.BrBorder;
                _familyText.Foreground = string.IsNullOrEmpty(Slot.FamilyName) ? METools.MeToolsTheme.BrMuted : METools.MeToolsTheme.BrText;
            }
            if (_familyPopupList != null)
            {
                var popupBorder = _familyPopup?.Child as Border;
                if (popupBorder != null)
                {
                    popupBorder.Background = METools.MeToolsTheme.BrSurface;
                    popupBorder.BorderBrush = METools.MeToolsTheme.BrBorder;
                }
            }
            if (_typeCmb   != null) METools.MeToolsWindowBase.ApplyComboStyle(_typeCmb);
            if (_heightTxt != null) { _heightTxt.Background = METools.MeToolsTheme.BrInput; _heightTxt.Foreground = METools.MeToolsTheme.BrText; _heightTxt.BorderBrush = METools.MeToolsTheme.BrBorder; _heightTxt.CaretBrush = METools.MeToolsTheme.BrText; }
            if (_offXTxt != null) { _offXTxt.Background = METools.MeToolsTheme.BrInput; _offXTxt.Foreground = METools.MeToolsTheme.BrText; _offXTxt.BorderBrush = METools.MeToolsTheme.BrBorder; _offXTxt.CaretBrush = METools.MeToolsTheme.BrText; }
            if (_offYTxt != null) { _offYTxt.Background = METools.MeToolsTheme.BrInput; _offYTxt.Foreground = METools.MeToolsTheme.BrText; _offYTxt.BorderBrush = METools.MeToolsTheme.BrBorder; _offYTxt.CaretBrush = METools.MeToolsTheme.BrText; }
            if (_gearBtn   != null) { _gearBtn.Background = METools.MeToolsTheme.BrBtnBg; _gearBtn.Foreground = METools.MeToolsTheme.BrMuted; _gearBtn.BorderBrush = METools.MeToolsTheme.BrBorder; }
        }

        private TextBlock Lbl(string t) => new TextBlock
        {
            Text      = t,
            FontSize  = 9,
            Foreground = METools.MeToolsTheme.BrMuted,
            FontWeight = FontWeights.Bold,
            Margin    = new Thickness(0, 0, 0, 2),
        };

        private void RefreshTypes()
        {
            _typeCmb.Items.Clear();
            var family = Slot.FamilyName;
            if (string.IsNullOrEmpty(family)) return;

            foreach (var t in FamilyLoader.GetTypeNames(_all, family))
                _typeCmb.Items.Add(t);
            if (_typeCmb.Items.Count > 0) _typeCmb.SelectedIndex = 0;
        }

        // Pre-fill the Niveau/height field from the selected family's own default.
        // Fires only on user-driven family/type changes (handlers wired after the
        // programmatic template load), so saved template heights are never clobbered.
        private void AutoFillHeight()
        {
            string fam = Slot.FamilyName;
            if (string.IsNullOrEmpty(fam) || _requestNiveau == null) return;
            _requestNiveau(fam, sampled =>
            {
                double? ov  = METools.FamilyHeightStore.Get(fam);  // user override wins over the project value
                double? eff = ov ?? sampled;
                if (eff.HasValue)
                {
                    Slot.Height = eff.Value;
                    if (_heightTxt != null) _heightTxt.Text = eff.Value.ToString("F0");
                }
            });
        }

        // Builds the family selector: a plain TextBox styled to match the old
        // combo, backed by a Popup containing manually-built clickable rows.
        // Deliberately NOT a ComboBox with IsEditable=true -- that approach
        // (tried twice) kept breaking in ways traceable to WPF's internal
        // Popup/StaysOpen/focus timing for editable combos, which isn't
        // reliably controllable from here without live testing. This gives
        // full explicit control over every step: opening, filtering,
        // committing a pick, and reverting on blur, in a known order.
        private void BuildFamilyPicker(StackPanel famType)
        {
            _familyText = new TextBox
            {
                Height = 24, FontSize = 11, Background = METools.MeToolsTheme.BrInput,
                Foreground = METools.MeToolsTheme.BrText, BorderBrush = METools.MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1), Padding = new Thickness(6, 0, 6, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
            };

            _familyPopupList = new StackPanel();
            var scroller = new ScrollViewer
            {
                MaxHeight = 200, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _familyPopupList,
            };
            var popupBorder = new Border
            {
                Background = METools.MeToolsTheme.BrSurface, BorderBrush = METools.MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1), Child = scroller,
            };
            _familyPopup = new Popup
            {
                PlacementTarget = _familyText, Placement = PlacementMode.Bottom,
                StaysOpen = true, // we close it ourselves (on blur/pick/Escape) -- see why below
                Child = popupBorder,
            };

            SyncFamilyText();
            RenderFamilyPopupList("");

            _familyText.GotFocus += (s, e) =>
            {
                _familyPopup.Width = _familyText.ActualWidth > 0 ? _familyText.ActualWidth : 200;
                RenderFamilyPopupList("");
                _familyPopup.IsOpen = true;
                _familyText.Dispatcher.BeginInvoke(new Action(() => _familyText.SelectAll()),
                    System.Windows.Threading.DispatcherPriority.Input);
            };
            _familyText.TextChanged += (s, e) =>
            {
                if (_suppressFamilyTextChanged) return;
                if (!_familyPopup.IsOpen) _familyPopup.IsOpen = true;
                RenderFamilyPopupList(_familyText.Text ?? "");
            };
            _familyText.LostFocus += (s, e) =>
            {
                // Deferred to Background priority so a MouseLeftButtonDown on
                // a popup row (which commits a pick synchronously, at normal
                // priority) always finishes first, regardless of exactly how
                // focus transitions between the two controls.
                _familyText.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _familyPopup.IsOpen = false;
                    SyncFamilyText();
                }), System.Windows.Threading.DispatcherPriority.Background);
            };
            _familyText.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    _familyPopup.IsOpen = false;
                    SyncFamilyText();
                    Keyboard.ClearFocus();
                    e.Handled = true;
                }
            };

            famType.Children.Add(_familyText);
        }

        // Forces the text to match the real current selection -- called on
        // blur (no new pick made) and right after a pick commits, so a
        // half-typed search term never lingers as if it were the selection.
        private void SyncFamilyText()
        {
            _suppressFamilyTextChanged = true;
            try
            {
                _familyText.Text = string.IsNullOrEmpty(Slot.FamilyName) ? "-- No Selection --" : Slot.FamilyName;
                _familyText.Foreground = string.IsNullOrEmpty(Slot.FamilyName)
                    ? METools.MeToolsTheme.BrMuted : METools.MeToolsTheme.BrText;
            }
            finally { _suppressFamilyTextChanged = false; }
        }

        // Rebuilds the popup's clickable rows, optionally filtered by a typed
        // substring (case-insensitive, matched against family name). Category
        // headers only show in the unfiltered view.
        private void RenderFamilyPopupList(string filter)
        {
            _familyPopupList.Children.Clear();
            bool searching = !string.IsNullOrWhiteSpace(filter);
            var matches = searching
                ? _all.Where(f => f.FamilyName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                : _all.AsEnumerable();

            string lastGroup = null;
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var info in matches.OrderBy(f => f.CategoryGroup).ThenBy(f => f.FamilyName))
            {
                if (!searching && info.CategoryGroup != lastGroup)
                {
                    _familyPopupList.Children.Add(new TextBlock
                    {
                        Text = info.CategoryGroup, FontWeight = FontWeights.Bold, FontStyle = FontStyles.Italic,
                        Foreground = MeToolsTheme.BrPetrol, FontSize = 10, Margin = new Thickness(6, 4, 0, 1),
                    });
                    lastGroup = info.CategoryGroup;
                }
                if (seen.Add(info.FamilyName))
                {
                    var row = new Border
                    {
                        Padding = new Thickness(searching ? 6 : 14, 3, 6, 3),
                        Cursor = Cursors.Hand, Background = Brushes.Transparent,
                    };
                    row.Child = new TextBlock { Text = info.FamilyName, FontSize = 11, Foreground = MeToolsTheme.BrText };
                    row.MouseEnter += (s, e) => row.Background = MeToolsTheme.BrActiveBg;
                    row.MouseLeave += (s, e) => row.Background = Brushes.Transparent;
                    var capturedName = info.FamilyName;
                    row.MouseLeftButtonDown += (s, e) => { PickFamily(capturedName); e.Handled = true; };
                    _familyPopupList.Children.Add(row);
                }
            }

            var noSel = new Border { Padding = new Thickness(searching ? 6 : 14, 3, 6, 3), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            noSel.Child = new TextBlock { Text = "-- No Selection --", FontSize = 11, Foreground = MeToolsTheme.BrMuted };
            noSel.MouseEnter += (s, e) => noSel.Background = MeToolsTheme.BrActiveBg;
            noSel.MouseLeave += (s, e) => noSel.Background = Brushes.Transparent;
            noSel.MouseLeftButtonDown += (s, e) => { PickFamily(""); e.Handled = true; };
            _familyPopupList.Children.Insert(0, noSel);

            if (searching && _familyPopupList.Children.Count == 1) // only the "No Selection" row exists
            {
                _familyPopupList.Children.Add(new TextBlock
                {
                    Text = "No matches.", FontSize = 10.5, FontStyle = FontStyles.Italic,
                    Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(8, 4, 8, 4),
                });
            }
        }

        // Commits a pick, synchronously, at normal input priority -- always
        // finishes before the LostFocus-triggered SyncFamilyText (deferred to
        // Background priority) runs, regardless of focus-transition timing.
        private void PickFamily(string familyName)
        {
            Slot.FamilyName = familyName ?? "";
            Slot.ParamOverrides.Clear(); // different family -> different param set
            _paramsBuiltKey = null;
            _familyPopup.IsOpen = false;
            SyncFamilyText();
            UpdateGearDot();
            RefreshTypes();
            AutoFillHeight();
            OnChanged?.Invoke();
        }

        private void TypeChanged(object s, SelectionChangedEventArgs e)
        {
            Slot.TypeName = _typeCmb.SelectedItem as string ?? "";
            _paramsBuiltKey = null;        // defaults may differ per type
            AutoFillHeight();
            OnChanged?.Invoke();
        }

        private void HeightChanged(object s, TextChangedEventArgs e)
        {
            if (double.TryParse(_heightTxt.Text, out double h)) Slot.Height = h;
            OnHeightChanged?.Invoke();
        }

        private void OffsetXChanged(object s, TextChangedEventArgs e)
        {
            if (int.TryParse(_offXTxt.Text, out int o)) Slot.OffsetX = o;
        }

        private void OffsetYChanged(object s, TextChangedEventArgs e)
        {
            if (int.TryParse(_offYTxt.Text, out int o)) Slot.OffsetY = o;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SaveTemplateDialog — simple input dialog
    // ─────────────────────────────────────────────────────────────────────────
    internal class SaveTemplateDialog : Window
    {
        public string TemplateName { get; private set; }

        public SaveTemplateDialog()
        {
            Title  = "Save Template";
            Width  = 320;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var sp = new StackPanel { Margin = new Thickness(14) };
            sp.Children.Add(new TextBlock
            {
                Text = "Template name:", FontSize = 12,
                Margin = new Thickness(0, 0, 0, 6)
            });
            var tb = new TextBox { Height = 28, FontSize = 12 };
            sp.Children.Add(tb);

            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0),
            };
            var cancel = new Button { Content = "Cancel", Height = 28, MinWidth = 80,
                Margin = new Thickness(0, 0, 8, 0),
                Background = MeToolsTheme.BrBtnBg, Foreground = MeToolsTheme.BrText,
                BorderBrush = MeToolsTheme.BrBtnBorder, BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand, Template = METools.MeToolsWindowBase.RoundedBtnTemplate() };
            var ok = new Button { Content = "Save", Height = 28, MinWidth = 80,
                IsDefault = true,
                Background = MeToolsTheme.BrPetrol,
                Foreground = Brushes.White,
                BorderBrush = MeToolsTheme.BrPetrol, BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand, Template = METools.MeToolsWindowBase.RoundedBtnTemplate() };
            cancel.Click += (s, e) => { DialogResult = false; Close(); };
            ok.Click     += (s, e) => { TemplateName = tb.Text; DialogResult = true; Close(); };
            btnRow.Children.Add(cancel);
            btnRow.Children.Add(ok);
            sp.Children.Add(btnRow);
            Content = sp;

            Loaded += (s, e) => tb.Focus();
        }
    }
}
