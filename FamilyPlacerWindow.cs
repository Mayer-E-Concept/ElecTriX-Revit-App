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
using Visibility = System.Windows.Visibility;

namespace METools.FamilyPlacer
{
    public class FamilyPlacerWindow : METools.MeToolsWindowBase
    {
        // ── Colors ────────────────────────────────────────────────────────────
        private static readonly Color CPetrol     = Color.FromRgb(0x18, 0x5f, 0x5f);
        private static readonly Color CPetrolDark = Color.FromRgb(0x12, 0x4d, 0x4d); // petrol, slightly darker than accent
        private static readonly Color CPetrolDim  = Color.FromRgb(0xcc, 0xe5, 0xe5);
        private static readonly Color CPetrolText = Color.FromRgb(0x14, 0x4d, 0x4d);
        private static readonly Color CStatusBar  = Color.FromRgb(0x15, 0x58, 0x58);
        private static readonly Color CBg         = Color.FromRgb(0xf4, 0xf5, 0xf6);
        private static readonly Color CSurface    = Colors.White;
        private static readonly Color CBorder     = Color.FromRgb(0xd0, 0xd5, 0xd9);
        private static readonly Color CText       = Color.FromRgb(0x1e, 0x25, 0x28);
        private static readonly Color CMuted      = Color.FromRgb(0x6b, 0x78, 0x80);
        private static readonly Color CDim        = Color.FromRgb(0xa8, 0xb4, 0xbb);

        // ── State ─────────────────────────────────────────────────────────────
        private readonly ExternalEvent         _extEvent;
        private readonly FamilyPlacerHandler   _handler;
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

        private FrameworkElement _levelSection;
        private readonly List<SlotRow> _rows = new List<SlotRow>();

        public FamilyPlacerWindow(ExternalEvent extEvent,
                                  FamilyPlacerHandler handler,
                                  List<FamilyTypeInfo> families,
                                  List<LevelInfo> levels,
                                  ElementId defaultLevelId)
        {
            _extEvent        = extEvent;
            _handler         = handler;
            _allFamilies     = families;
            _templates       = TemplateManager.Load();
            _levels          = levels;
            _selectedLevelId = defaultLevelId;

            // Wire handler callbacks
            _handler.OnStatus = msg => Dispatcher.Invoke(() => SetStatus(msg));
            _handler.OnPlaced = n  => Dispatcher.Invoke(UpdateCount);

            // Window setup
            InitWindow("Family Placer", 520);
            BuildUI();
            AddInitialSlot();
        }

        protected override void OnThemeChanged() { Background = MeToolsTheme.BrBg; }

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

            var btnSave = MakeIconBtn("💾", "Save current config as template", SaveTemplate);
            btnSave.Margin = new Thickness(5, 0, 0, 0);
            Grid.SetColumn(btnSave, 1);

            var btnDel = MakeIconBtn("🗑", "Delete selected template", DeleteTemplate);
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
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });   // handle
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });   // #
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // family
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });   // height
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });   // offset
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });   // del
            AddHdr(hdr, "Family", 2); AddHdr(hdr, "Niveau(mm)", 3); AddHdr(hdr, "Offset ×", 4);
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

            // Info box
            var info = new Border
            {
                Background      = new SolidColorBrush(Color.FromRgb(0xe8, 0xf4, 0xf4)),
                BorderBrush     = new SolidColorBrush(Color.FromArgb(80, 0x18, 0x5f, 0x5f)),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(4),
                Padding         = new Thickness(10, 7, 10, 7),
                Margin          = new Thickness(0, 0, 0, 12),
            };
            var infoSp = new StackPanel { Orientation = Orientation.Horizontal };
            infoSp.Children.Add(new TextBlock
            {
                Text      = "ℹ  ",
                FontSize  = 13,
                Foreground = MeToolsTheme.BrPetrol,
            });
            infoSp.Children.Add(new TextBlock
            {
                Text       = "SPACEBAR to rotate (90°) before placing · Wall detection active · Free workplane supported",
                FontSize   = 11,
                Foreground = MeToolsTheme.BrActiveFg,
                TextWrapping = TextWrapping.Wrap,
            });
            info.Child = infoSp;
            body.Children.Add(info);

            // Place buttons row
            var btnRow = new Grid();
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });

            var btnPlace = MakePlaceBtn("▶  Place", false, PlaceSingle); btnPlace.Template = RoundBtnTemplate(btnPlace);
            Grid.SetColumn(btnPlace, 0);

            var btnMulti = MakePlaceBtn("⊕  Multi-Place", true, PlaceMulti); btnMulti.Template = RoundBtnTemplate(btnMulti);
            Grid.SetColumn(btnMulti, 2);

            btnRow.Children.Add(btnPlace);
            btnRow.Children.Add(btnMulti);
            body.Children.Add(btnRow);
        }

        // ─────────────────────────────────────────────────────────────────────
        // SLOT MANAGEMENT
        // ─────────────────────────────────────────────────────────────────────
        private void AddInitialSlot() => AddSlot();

        private void AddSlot(FamilySlot data = null)
        {
            var row = new SlotRow(_allFamilies, data, _rows.Count + 1);
            row.OnRemove  = () => RemoveSlot(row);
            row.OnChanged = UpdateCount;
            _rows.Add(row);
            _slotPanel.Children.Add(row.Container);
            UpdateCount();
        }

        private void RemoveSlot(SlotRow row)
        {
            _rows.Remove(row);
            _slotPanel.Children.Remove(row.Container);
            // Renumber
            for (int i = 0; i < _rows.Count; i++)
                _rows[i].SetIndex(i + 1);
            UpdateCount();
        }

        private void UpdateCount()
        {
            int valid = _rows.Count(r => !string.IsNullOrEmpty(r.Slot.FamilyName));
            _statusCount.Text = $"{valid} famil{(valid != 1 ? "ies" : "y")} configured";
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
            SetStatus("SPACEBAR = rotate · ESC = cancel");
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
            SetStatus("Click positions · SPACEBAR = rotate · ESC = done");
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
            if (_tplCombo.SelectedIndex <= 0) return;
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
            var dlg = new SaveTemplateDialog();
            if (dlg.ShowDialog() != true || string.IsNullOrEmpty(dlg.TemplateName)) return;

            var name = dlg.TemplateName.Trim();
            var existing = _templates.FirstOrDefault(t => t.Name == name);
            if (existing != null) _templates.Remove(existing);

            _templates.Add(new PlacerTemplate
            {
                Name        = name,
                Orientation = _orientation,
                Slots       = _rows.Select(r => r.Slot).ToList(),
            });
            TemplateManager.Save(_templates);
            RefreshTemplateCombo();

            // Select the newly saved template
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
                FontSize        = 13,
                ToolTip         = tip,
                Background      = MeToolsTheme.BrBtnBg,
                BorderBrush     = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1),
                Cursor          = Cursors.Hand,
            };
            btn.Template = RoundBtnTemplate(btn);
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
                FontSize        = 12,
                Background      = active ? MeToolsTheme.BrActiveBg : MeToolsTheme.BrBtnBg,
                BorderBrush     = active ? MeToolsTheme.BrPetrol    : MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1),
                Foreground      = active ? MeToolsTheme.BrActiveFg  : MeToolsTheme.BrMuted,
                Cursor          = Cursors.Hand,
            };
            btn.Template = RoundBtnTemplate(btn);
            btn.Click += (s, e) => SetOrientation(mode);
            return btn;
        }

        private void SetOrientation(string mode)
        {
            _orientation = mode;
            bool isV = mode == "Vertical";
            UpdateOriBtn(_btnOri1, isV);
            UpdateOriBtn(_btnOri2, !isV);
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
                FontWeight      = FontWeights.SemiBold,
                Background      = isOutline ? MeToolsTheme.BrBtnBg : MeToolsTheme.BrPetrol,
                BorderBrush     = MeToolsTheme.BrPetrol,
                BorderThickness = new Thickness(1.5),
                Foreground      = isOutline ? MeToolsTheme.BrPetrol : Brushes.White,
                Cursor          = Cursors.Hand,
            };
            btn.Click      += (s, e) => onClick();
            btn.MouseEnter += (s, e) => btn.Background = isOutline
                ? MeToolsTheme.BrActiveBg
                : new SolidColorBrush(Color.FromRgb(0x12, 0x4d, 0x4d));
            btn.MouseLeave += (s, e) => btn.Background = isOutline
                ? MeToolsTheme.BrBtnBg
                : MeToolsTheme.BrPetrol;
            return btn;
        }

        private FrameworkElement MakeWinBtn(string symbol, bool isDanger, Action onClick)
        {
            var btn = new Border
            {
                Width  = 44, Height = 36,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = symbol,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center,
                }
            };
            btn.MouseEnter += (s, e) => {
                btn.Background = isDanger
                    ? new SolidColorBrush(Color.FromRgb(0xc4, 0x2b, 0x1c))
                    : new SolidColorBrush(Color.FromRgb(0x3e, 0x4e, 0x4e));
                ((TextBlock)btn.Child).Foreground = MeToolsTheme.BrBtnBg;
            };
            btn.MouseLeave += (s, e) => {
                btn.Background = Brushes.Transparent;
                ((TextBlock)btn.Child).Foreground = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255));
            };
            btn.MouseLeftButtonDown += (s, e) => onClick();
            return btn;
        }

        private static System.Windows.Controls.ControlTemplate RoundBtnTemplate(Button btn)
        {
            var factory = new System.Windows.FrameworkElementFactory(typeof(Border));
            factory.SetBinding(Border.BackgroundProperty,
                new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            factory.SetBinding(Border.BorderBrushProperty,
                new System.Windows.Data.Binding("BorderBrush") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            factory.SetBinding(Border.BorderThicknessProperty,
                new System.Windows.Data.Binding("BorderThickness") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            var content = new System.Windows.FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty,   VerticalAlignment.Center);
            factory.AppendChild(content);
            return new System.Windows.Controls.ControlTemplate(typeof(Button)) { VisualTree = factory };
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

        public Border    Container { get; }
        public FamilySlot Slot    { get; } = new FamilySlot();

        private readonly List<FamilyTypeInfo> _all;
        private ComboBox   _familyCmb, _typeCmb;
        private TextBox    _heightTxt, _offsetTxt;
        private TextBlock  _idxBadge;
        private int        _index;

// Farben von MeToolsTheme

        public SlotRow(List<FamilyTypeInfo> all, FamilySlot data, int index)
        {
            _all   = all;
            _index = index;

            if (data != null)
            {
                Slot.FamilyName   = data.FamilyName;
                Slot.TypeName     = data.TypeName;
                Slot.Height       = data.Height;
                Slot.OffsetFactor = data.OffsetFactor;
            }

            Container = BuildRow();
        }

        public void SetIndex(int i)
        {
            _index = i;
            if (_idxBadge != null) _idxBadge.Text = i.ToString();
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
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });   // handle
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });   // index
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // family+type
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });    // gap
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });   // height
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });    // gap
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });   // offset
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });    // gap
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });   // del

            // Drag handle
            var handle = new TextBlock
            {
                Text = "⠿", FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0xa8, 0xb4, 0xbb)),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.SizeNS,
            };
            Grid.SetColumn(handle, 0); g.Children.Add(handle);

            // Index badge
            _idxBadge = new TextBlock
            {
                Text      = _index.ToString(),
                FontSize  = 10, FontWeight = FontWeights.Bold,
                Foreground = MeToolsTheme.BrPetrol,
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

            _familyCmb = new ComboBox { Height = 24, FontSize = 11, Background = METools.MeToolsTheme.BrInput, Foreground = METools.MeToolsTheme.BrText, BorderBrush = METools.MeToolsTheme.BrBorder, BorderThickness = new Thickness(1) };
            _familyCmb.Items.Add(new ComboBoxItem { Content = "-- No Selection --", Tag = "" });

            // Deduplicated: show each FamilyName once per CategoryGroup
            string lastGroup = null;
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var info in _all.OrderBy(f => f.CategoryGroup).ThenBy(f => f.FamilyName))
            {
                if (info.CategoryGroup != lastGroup)
                {
                    _familyCmb.Items.Add(new ComboBoxItem
                    {
                        Content   = info.CategoryGroup,
                        IsEnabled = false,
                        FontWeight = FontWeights.Bold,
                        FontStyle  = FontStyles.Italic,
                        Foreground = MeToolsTheme.BrPetrol,
                        FontSize   = 10,
                        Padding    = new Thickness(2, 3, 0, 1),
                    });
                    lastGroup = info.CategoryGroup;
                }
                if (seen.Add(info.FamilyName)) // only first occurrence
                {
                    _familyCmb.Items.Add(new ComboBoxItem
                    {
                        Content = info.FamilyName,
                        Tag     = info.FamilyName,
                        Padding = new Thickness(10, 1, 0, 1),
                    });
                }
            }

            _familyCmb.SelectedIndex = 0;
            if (!string.IsNullOrEmpty(Slot.FamilyName))
            {
                foreach (ComboBoxItem item in _familyCmb.Items)
                    if (item.Tag as string == Slot.FamilyName)
                    { _familyCmb.SelectedItem = item; break; }
            }
            _familyCmb.Template = METools.MeToolsWindowBase.MakeComboBoxTemplate();
            METools.MeToolsWindowBase.ApplyComboStyle(_familyCmb);
            _familyCmb.SelectionChanged += FamilyChanged;
            famType.Children.Add(_familyCmb);

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
            hSp.Children.Add(Lbl("Niveau (mm)"));
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

            // Offset factor
            var oSp = new StackPanel();
            oSp.Children.Add(Lbl("Offset ×"));
            _offsetTxt = new TextBox
            {
                Text   = Slot.OffsetFactor.ToString(),
                Height = 28, FontSize = 12,
                TextAlignment = TextAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = METools.MeToolsTheme.BrInput,
                Foreground = METools.MeToolsTheme.BrText,
                BorderBrush = METools.MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1),
            };
            _offsetTxt.TextChanged += OffsetChanged;
            oSp.Children.Add(_offsetTxt);
            Grid.SetColumn(oSp, 6); g.Children.Add(oSp);

            // Remove button
            var delBtn = new Button
            {
                Content         = "×",
                Width           = 26, Height = 26,
                FontSize        = 14,
                Background      = METools.MeToolsTheme.BrBtnBg,
                BorderBrush     = METools.MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1),
                Foreground      = METools.MeToolsTheme.BrMuted,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor          = Cursors.Hand,
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
            Grid.SetColumn(delBtn, 8); g.Children.Add(delBtn);

            border.Child = g;
            return border;
        }

        public void ApplyTheme()
        {
            if (Container != null) { Container.Background = METools.MeToolsTheme.BrSurface; Container.BorderBrush = METools.MeToolsTheme.BrBorder; }
            if (_familyCmb != null) METools.MeToolsWindowBase.ApplyComboStyle(_familyCmb);
            if (_typeCmb   != null) METools.MeToolsWindowBase.ApplyComboStyle(_typeCmb);
            if (_heightTxt != null) { _heightTxt.Background = METools.MeToolsTheme.BrInput; _heightTxt.Foreground = METools.MeToolsTheme.BrText; _heightTxt.BorderBrush = METools.MeToolsTheme.BrBorder; _heightTxt.CaretBrush = METools.MeToolsTheme.BrText; }
            if (_offsetTxt != null) { _offsetTxt.Background = METools.MeToolsTheme.BrInput; _offsetTxt.Foreground = METools.MeToolsTheme.BrText; _offsetTxt.BorderBrush = METools.MeToolsTheme.BrBorder; _offsetTxt.CaretBrush = METools.MeToolsTheme.BrText; }
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
            // SelectedItem is now a ComboBoxItem — read Tag for family name
            var selectedItem = _familyCmb.SelectedItem as ComboBoxItem;
            var family = selectedItem?.Tag as string;
            if (string.IsNullOrEmpty(family)) return;

            foreach (var t in FamilyLoader.GetTypeNames(_all, family))
                _typeCmb.Items.Add(t);
            if (_typeCmb.Items.Count > 0) _typeCmb.SelectedIndex = 0;
        }

        private void FamilyChanged(object s, SelectionChangedEventArgs e)
        {
            var item = _familyCmb.SelectedItem as ComboBoxItem;
            Slot.FamilyName = item?.Tag as string ?? "";
            RefreshTypes();
            OnChanged?.Invoke();
        }

        private void TypeChanged(object s, SelectionChangedEventArgs e)
        {
            Slot.TypeName = _typeCmb.SelectedItem as string ?? "";
            OnChanged?.Invoke();
        }

        private void HeightChanged(object s, TextChangedEventArgs e)
        {
            if (double.TryParse(_heightTxt.Text, out double h)) Slot.Height = h;
        }

        private void OffsetChanged(object s, TextChangedEventArgs e)
        {
            if (int.TryParse(_offsetTxt.Text, out int o)) Slot.OffsetFactor = o;
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
                Margin = new Thickness(0, 0, 8, 0) };
            var ok = new Button { Content = "Save", Height = 28, MinWidth = 80,
                IsDefault = true,
                Background = new SolidColorBrush(Color.FromRgb(0x18, 0x5f, 0x5f)),
                Foreground = MeToolsTheme.BrBtnBg };
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
