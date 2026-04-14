// AutoRoomSeparation/AutoRoomSeparationWindow.cs — ME-Tools | Auto Room Separation
// Mayer E-Concept SRL
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace METools.AutoRoomSeparation
{
    /// <summary>
    /// Modal settings dialog shown before running AutoRoomSeparation.
    /// The user configures sources, area limits, and layer filters,
    /// then clicks "Generate" to confirm. Actual computation runs
    /// after this dialog closes, inside IExternalCommand.Execute().
    /// </summary>
    public class AutoRoomSeparationWindow : METools.MeToolsWindowBase
    {
        // ── UI controls ─────────────────────────────────────────────────────
        private CheckBox _cbDwg;
        private CheckBox _cbDirectShape;
        private CheckBox _cbWalls;

        private TextBox _tbMinArea;
        private TextBox _tbMaxArea;
        private TextBox _tbMinLength;
        private TextBox _tbLayers;

        // ── Result ──────────────────────────────────────────────────────────
        public bool Confirmed { get; private set; }
        public AutoRoomSeparationSettings Settings { get; private set; }

        // ── Constructor ─────────────────────────────────────────────────────
        public AutoRoomSeparationWindow()
        {
            Settings = new AutoRoomSeparationSettings();
            InitWindow("Auto Room Separation", width: 440, isDialog: true);
            BuildContent();
            BuildStatusBar("Configure sources and area limits", "Revit 2025");
        }

        // ── Build UI ────────────────────────────────────────────────────────
        private void BuildContent()
        {
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };

            var root = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };
            scroll.Content = root;

            DockPanel.SetDock(scroll, Dock.Top);
            RootDock.Children.Insert(RootDock.Children.Count - 1, scroll); // before status bar

            // ── Info box ────────────────────────────────────────────────────
            root.Children.Add(InfoBox(
                "Reads geometry from the selected sources in the active floor plan view, " +
                "finds closed room polygons and places Room Separation Lines. " +
                "Prepare DWG files beforehand: remove furniture, text, hatches."));

            // ── Source selection ─────────────────────────────────────────────
            root.Children.Add(Sec("Geometry Sources"));

            _cbDwg         = MakeCheckBox("DWG / Linked CAD instances",       true);
            _cbDirectShape = MakeCheckBox("DirectShape elements (IFC import)", true);
            _cbWalls       = MakeCheckBox("Native Revit Walls",                true);

            root.Children.Add(_cbDwg);
            root.Children.Add(_cbDirectShape);
            root.Children.Add(_cbWalls);

            root.Children.Add(new Border { Height = 10 });

            // ── Area filter ──────────────────────────────────────────────────
            root.Children.Add(Sec("Area Filter"));

            var areaGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            areaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            areaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            areaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            areaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            areaGrid.RowDefinitions.Add(new RowDefinition());

            _tbMinArea = Num("2.0");
            _tbMaxArea = Num("500.0");

            var lblMin  = MakeLabel("Min. area (m²):", rightAlign: false);
            var lblMax  = MakeLabel("Max. area (m²):", rightAlign: false);

            Grid.SetColumn(lblMin,    0); Grid.SetColumn(_tbMinArea, 1);
            Grid.SetColumn(lblMax,    2); Grid.SetColumn(_tbMaxArea, 3);
            Grid.SetRow(lblMin, 0);       Grid.SetRow(_tbMinArea, 0);
            Grid.SetRow(lblMax, 0);       Grid.SetRow(_tbMaxArea, 0);

            _tbMinArea.Width = 80; _tbMinArea.Margin = new Thickness(6, 0, 16, 0);
            _tbMaxArea.Width = 80; _tbMaxArea.Margin = new Thickness(6, 0, 0, 0);

            areaGrid.Children.Add(lblMin);
            areaGrid.Children.Add(_tbMinArea);
            areaGrid.Children.Add(lblMax);
            areaGrid.Children.Add(_tbMaxArea);
            root.Children.Add(areaGrid);

            // Min curve length
            var lenRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin      = new Thickness(0, 0, 0, 10),
            };
            lenRow.Children.Add(MakeLabel("Min. curve length (m):", rightAlign: false));
            _tbMinLength = Num("0.30");
            _tbMinLength.Width  = 80;
            _tbMinLength.Margin = new Thickness(6, 0, 0, 0);
            lenRow.Children.Add(_tbMinLength);
            root.Children.Add(lenRow);

            // ── Layer exclusion ──────────────────────────────────────────────
            root.Children.Add(Sec("DWG Layer Exclusion"));
            root.Children.Add(MakeLabel(
                "Layers containing these tokens are excluded (comma-separated, case-insensitive):",
                rightAlign: false));
            _tbLayers = new TextBox
            {
                Text              = "HATCH,SCHR,FILL,TEXT,DIM,MASS,BEM,ANNO",
                Height            = 28,
                FontSize          = 11,
                Background        = MeToolsTheme.BrInput,
                Foreground        = MeToolsTheme.BrText,
                BorderBrush       = MeToolsTheme.BrBorder,
                BorderThickness   = new Thickness(1),
                Padding           = new Thickness(6, 0, 6, 0),
                Margin            = new Thickness(0, 4, 0, 12),
                CaretBrush        = MeToolsTheme.BrText,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            root.Children.Add(_tbLayers);

            // ── Buttons ──────────────────────────────────────────────────────
            var btnRow = new Grid();
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var btnCancel  = FooterBtn("Cancel",   false, () => { Confirmed = false; Close(); });
            var btnGenerate = FooterBtn("Generate Separation Lines", true,  OnGenerate);

            Grid.SetColumn(btnCancel,   0);
            Grid.SetColumn(btnGenerate, 2);
            btnRow.Children.Add(btnCancel);
            btnRow.Children.Add(btnGenerate);

            root.Children.Add(btnRow);
        }

        // ── Generate clicked ─────────────────────────────────────────────────
        private void OnGenerate()
        {
            if (!ValidateInputs(out string error))
            {
                MessageBox.Show(error, "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Settings = new AutoRoomSeparationSettings
            {
                UseDwgInstances   = _cbDwg.IsChecked         == true,
                UseDirectShapes   = _cbDirectShape.IsChecked == true,
                UseNativeWalls    = _cbWalls.IsChecked        == true,
                MinAreaSqM        = double.Parse(_tbMinArea.Text.Replace(',', '.')),
                MaxAreaSqM        = double.Parse(_tbMaxArea.Text.Replace(',', '.')),
                MinLengthM        = double.Parse(_tbMinLength.Text.Replace(',', '.')),
                ExcludeLayerTokens = _tbLayers.Text.Trim(),
            };

            Confirmed = true;
            Close();
        }

        // ── Validation ───────────────────────────────────────────────────────
        private bool ValidateInputs(out string error)
        {
            error = null;

            if (!TryParsePositive(_tbMinArea.Text, out double minA))
            { error = "Min. area must be a positive number."; return false; }
            if (!TryParsePositive(_tbMaxArea.Text, out double maxA))
            { error = "Max. area must be a positive number."; return false; }
            if (minA >= maxA)
            { error = "Min. area must be smaller than Max. area."; return false; }
            if (!TryParsePositive(_tbMinLength.Text, out _))
            { error = "Min. curve length must be a positive number."; return false; }
            if (_cbDwg.IsChecked != true &&
                _cbDirectShape.IsChecked != true &&
                _cbWalls.IsChecked != true)
            { error = "Select at least one geometry source."; return false; }

            return true;
        }

        private static bool TryParsePositive(string text, out double value)
        {
            return double.TryParse(text.Replace(',', '.'),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out value) && value > 0;
        }

        // ── UI factory helpers ───────────────────────────────────────────────
        private CheckBox MakeCheckBox(string label, bool isChecked)
        {
            return new CheckBox
            {
                Content             = label,
                IsChecked           = isChecked,
                Foreground          = MeToolsTheme.BrText,
                Margin              = new Thickness(0, 0, 0, 6),
                FontSize            = 12,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
        }

        private static TextBlock MakeLabel(string text, bool rightAlign)
        {
            return new TextBlock
            {
                Text              = text,
                FontSize          = 11,
                Foreground        = MeToolsTheme.BrMuted,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment     = rightAlign ? TextAlignment.Right : TextAlignment.Left,
                Margin            = new Thickness(0, 0, 0, 4),
            };
        }

        protected override void OnThemeChanged()
        {
            base.OnThemeChanged();
            // Re-apply theme-sensitive styles if needed
            if (_tbLayers != null)
            {
                _tbLayers.Background = MeToolsTheme.BrInput;
                _tbLayers.Foreground = MeToolsTheme.BrText;
                _tbLayers.BorderBrush = MeToolsTheme.BrBorder;
            }
        }
    }
}
