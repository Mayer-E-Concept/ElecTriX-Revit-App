// IfcLevelImportWindow.cs -- ME-Tools | IFC Level Importer
// Mayer E-Concept SRL
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Grid = System.Windows.Controls.Grid;
using Color = System.Windows.Media.Color;

namespace METools.IfcImport
{
    public class IfcLevelImportWindow : MeToolsWindowBase
    {
        private readonly ExternalEvent _extEvent;
        private readonly IfcLevelImportHandler _handler;
        private readonly IfcParseResult _parsed;
        private readonly string _filePath;
        private readonly UIApplication _uiApp;

        private readonly List<IfcLevelRow> _rows = new List<IfcLevelRow>();
        private StackPanel _tableList;
        private CheckBox _selectAllCb;
        private Button _importBtn;

        protected override string AppKey => "IfcLevelImport";

        public IfcLevelImportWindow(IfcParseResult parsed, string filePath,
            ExternalEvent extEvent, IfcLevelImportHandler handler, UIApplication uiApp)
        {
            _parsed = parsed; _filePath = filePath; _extEvent = extEvent; _handler = handler; _uiApp = uiApp;

            InitWindow("IFC Level Importer", 620);
            BuildUi();
            BuildStatusBar("Ready.", "Revit 2025/2026");

            _handler.OnDone  = res => Dispatcher.Invoke(() => OnImportDone(res));
            _handler.OnError = msg => Dispatcher.Invoke(() => StatusLeft.Text = msg);
        }

        private void BuildUi()
        {
            var doc = _uiApp.ActiveUIDocument?.Document;
            var scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var root = new Grid();
            var sp = new StackPanel { Margin = new Thickness(16) };
            root.Children.Add(sp);
            root.Children.Add(Watermark());
            scroller.Content = root;
            RootDock.Children.Add(scroller);

            sp.Children.Add(new TextBlock
            {
                Text = Path.GetFileName(_filePath), FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrText, Margin = new Thickness(0, 0, 0, 2), TextWrapping = TextWrapping.Wrap,
            });
            sp.Children.Add(new TextBlock
            {
                Text = $"Schema: {_parsed.SchemaVersion}", FontSize = 10.5, Foreground = MeToolsTheme.BrMuted,
                Margin = new Thickness(0, 0, 0, 12),
            });

            // -- Units -----------------------------------------------------------
            sp.Children.Add(Sec("Units"));
            var (revitLabel, revitKind) = DescribeRevitLengthUnit(doc);
            bool mismatch = _parsed.LengthUnitKind != IfcLengthUnitKind.Unknown
                            && revitKind != IfcLengthUnitKind.Unknown
                            && _parsed.LengthUnitKind != revitKind;

            var unitGrid = new Grid();
            unitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            unitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var ifcUnitBox = UnitTile("This IFC file", _parsed.LengthUnitLabel, mismatch);
            var rvtUnitBox = UnitTile("Your Revit project", revitLabel, mismatch);
            Grid.SetColumn(ifcUnitBox, 0); unitGrid.Children.Add(ifcUnitBox);
            Grid.SetColumn(rvtUnitBox, 1); unitGrid.Children.Add(rvtUnitBox);
            sp.Children.Add(unitGrid);

            if (mismatch)
            {
                double ratio = _parsed.LengthUnitToMeters / RevitUnitToMeters(revitKind);
                sp.Children.Add(InfoBoxWarn(
                    $"Unit mismatch: this IFC file is in {_parsed.LengthUnitLabel}, but your Revit project's units are set to {revitLabel}. " +
                    $"One raw unit in the file equals {ratio:0.####}x what the same number would mean in your project. " +
                    "Level elevations below are converted correctly regardless -- this warning is so you don't get caught out entering or eyeballing other values (like dimensions from this file's drawings) assuming the wrong scale."));
            }
            else
            {
                sp.Children.Add(InfoBox("No unit mismatch detected between this file and your project."));
            }

            // -- Site / location (read-only, informational only) -----------------
            if (_parsed.Site.HasAnyInfo)
            {
                sp.Children.Add(Sec("Site / Location (read-only -- nothing here changes your project)"));
                sp.Children.Add(BuildSitePanel(revitKind));
            }

            // -- Warnings ----------------------------------------------------------
            if (_parsed.Warnings.Count > 0)
            {
                sp.Children.Add(Sec($"Notes ({_parsed.Warnings.Count})"));
                var warnBox = new Border
                {
                    Background = MeToolsTheme.BrSurface, BorderBrush = MeToolsTheme.BrBorder,
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, 12),
                };
                var warnPanel = new StackPanel();
                foreach (var w in _parsed.Warnings)
                    warnPanel.Children.Add(new TextBlock
                    {
                        Text = "\u2022 " + w, FontSize = 10.5, Foreground = MeToolsTheme.BrMuted,
                        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2),
                    });
                warnBox.Child = warnPanel;
                sp.Children.Add(warnBox);
            }

            // -- Levels table --------------------------------------------------------
            sp.Children.Add(Sec($"Levels found ({_parsed.Levels.Count})"));

            var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (doc != null)
                    foreach (var l in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>())
                        existingNames.Add(l.Name);
            }
            catch { }

            foreach (var info in _parsed.Levels)
            {
                bool clash = existingNames.Contains((info.Name ?? "").Trim());
                _rows.Add(new IfcLevelRow
                {
                    Info = info,
                    IsSelected = !clash,
                    BlockReason = clash ? "A level with this name already exists -- will be skipped" : null,
                });
            }

            var tableBorder = new Border
            {
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4), ClipToBounds = true, Margin = new Thickness(0, 0, 0, 10),
            };
            var tableOuter = new StackPanel();
            tableOuter.Children.Add(BuildTableHeader());
            _tableList = new StackPanel();
            foreach (var row in _rows) _tableList.Children.Add(BuildTableRow(row, revitKind));
            tableOuter.Children.Add(_tableList);
            tableBorder.Child = tableOuter;
            sp.Children.Add(tableBorder);

            _importBtn = ActionBtn("Import Selected Levels", false, OnImportClicked);
            sp.Children.Add(_importBtn);
        }

        private Border UnitTile(string title, string label, bool warn)
        {
            return new Border
            {
                Background = warn ? new SolidColorBrush(Color.FromArgb(30, MeToolsTheme.CRed.R, MeToolsTheme.CRed.G, MeToolsTheme.CRed.B)) : MeToolsTheme.BrSurface,
                BorderBrush = warn ? new SolidColorBrush(MeToolsTheme.CRed) : MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 6, 10),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = title, FontSize = 10, Foreground = MeToolsTheme.BrMuted },
                        new TextBlock { Text = label, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = MeToolsTheme.BrText, TextWrapping = TextWrapping.Wrap },
                    },
                },
            };
        }

        private FrameworkElement BuildSitePanel(IfcLengthUnitKind revitKind)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            var site = _parsed.Site;

            if (site.LocalX.HasValue)
            {
                double toM = _parsed.LengthUnitToMeters;
                double x = (site.LocalX ?? 0) * toM, y = (site.LocalY ?? 0) * toM, z = (site.LocalZ ?? 0) * toM;
                panel.Children.Add(InfoLine("Site's local placement",
                    $"X = {x:0.###} m,  Y = {y:0.###} m,  Z = {z:0.###} m  (relative to this IFC file's own origin/placement -- not necessarily your project's origin)"));
            }
            if (site.LatitudeDeg.HasValue && site.LongitudeDeg.HasValue)
            {
                panel.Children.Add(InfoLine("Geographic reference",
                    $"Lat {site.LatitudeDeg:0.000000}\u00B0, Lon {site.LongitudeDeg:0.000000}\u00B0" +
                    (site.RefElevationRaw.HasValue ? $", elevation {(site.RefElevationRaw.Value * _parsed.LengthUnitToMeters):0.##} m" : "")));
            }
            if (site.MapEastings.HasValue)
            {
                panel.Children.Add(InfoLine("Survey coordinates (IfcMapConversion)",
                    $"Easting {site.MapEastings:0.###},  Northing {site.MapNorthings:0.###}" +
                    (site.MapOrthogonalHeight.HasValue ? $",  Height {site.MapOrthogonalHeight:0.###}" : "")));
            }
            return panel;
        }

        private FrameworkElement InfoLine(string label, string value)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            sp.Children.Add(new TextBlock { Text = label, FontSize = 10.5, Foreground = MeToolsTheme.BrMuted });
            sp.Children.Add(new TextBlock { Text = value, FontSize = 12, Foreground = MeToolsTheme.BrText, TextWrapping = TextWrapping.Wrap });
            return sp;
        }

        private Grid BuildTableHeader()
        {
            var grid = new Grid { Background = MeToolsTheme.BrHeader, MinHeight = 28 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });

            _selectAllCb = new CheckBox { IsChecked = true, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            _selectAllCb.Click += (s, e) =>
            {
                bool check = _selectAllCb.IsChecked == true;
                foreach (var row in _rows.Where(r => r.Importable)) row.IsSelected = check;
                RebuildRows();
            };
            Grid.SetColumn(_selectAllCb, 0); grid.Children.Add(_selectAllCb);

            var headers = new (int col, string text)[] { (1, "Level name"), (2, "Elevation (file unit)"), (3, "Elevation (your project)") };
            foreach (var (col, text) in headers)
            {
                var tb = new TextBlock
                {
                    Text = text, FontSize = 9.5, FontWeight = FontWeights.SemiBold, Foreground = MeToolsTheme.BrMuted,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0),
                };
                Grid.SetColumn(tb, col); grid.Children.Add(tb);
            }
            return grid;
        }

        private Border BuildTableRow(IfcLevelRow row, IfcLengthUnitKind revitKind)
        {
            var grid = new Grid { MinHeight = 30 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });

            var cb = new CheckBox
            {
                IsChecked = row.IsSelected, IsEnabled = row.Importable,
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
            };
            cb.Checked   += (s, e) => row.IsSelected = true;
            cb.Unchecked += (s, e) => row.IsSelected = false;
            Grid.SetColumn(cb, 0); grid.Children.Add(cb);

            var nameStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            nameStack.Children.Add(new TextBlock
            {
                Text = row.Info.Name, FontSize = 12, Foreground = row.Importable ? MeToolsTheme.BrText : MeToolsTheme.BrMuted,
                Margin = new Thickness(6, 4, 4, 0),
            });
            if (!row.Importable)
                nameStack.Children.Add(new TextBlock
                {
                    Text = row.BlockReason, FontSize = 9.5, Foreground = MeToolsTheme.BrOrange,
                    Margin = new Thickness(6, 0, 4, 4), TextWrapping = TextWrapping.Wrap,
                });
            Grid.SetColumn(nameStack, 1); grid.Children.Add(nameStack);

            var rawTb = new TextBlock
            {
                Text = $"{row.Info.ElevationRaw:0.###}", FontSize = 11.5, Foreground = MeToolsTheme.BrMuted,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0),
            };
            Grid.SetColumn(rawTb, 2); grid.Children.Add(rawTb);

            double meters = row.Info.ElevationRaw * _parsed.LengthUnitToMeters;
            double converted = meters / RevitUnitToMeters(revitKind);
            var convTb = new TextBlock
            {
                Text = $"{converted:0.###}", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = MeToolsTheme.BrText,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0),
            };
            Grid.SetColumn(convTb, 3); grid.Children.Add(convTb);

            return new Border
            {
                Background = MeToolsTheme.BrRow, BorderBrush = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(0, 0, 0, 1), Child = grid,
            };
        }

        private void RebuildRows()
        {
            _tableList.Children.Clear();
            var doc = _uiApp.ActiveUIDocument?.Document;
            var (_, revitKind) = DescribeRevitLengthUnit(doc);
            foreach (var row in _rows) _tableList.Children.Add(BuildTableRow(row, revitKind));
        }

        private void OnImportClicked()
        {
            var selected = _rows.Where(r => r.IsSelected && r.Importable).Select(r => r.Info).ToList();
            if (selected.Count == 0) { StatusLeft.Text = "Nothing selected."; return; }

            _importBtn.IsEnabled = false;
            StatusLeft.Text = $"Creating {selected.Count} level(s)...";
            _handler.Request = new IfcLevelImportRequest
            {
                LevelsToCreate = selected,
                LengthUnitToMeters = _parsed.LengthUnitToMeters,
            };
            _extEvent.Raise();
        }

        private void OnImportDone(IfcLevelImportResultInfo res)
        {
            _importBtn.IsEnabled = true;
            string msg = $"Created {res.Created} level(s).";
            if (res.Skipped > 0) msg += $" Skipped {res.Skipped}: {string.Join(", ", res.SkippedNames)}.";
            StatusLeft.Text = msg;
        }

        // -- Unit helpers --------------------------------------------------------
        private static (string Label, IfcLengthUnitKind Kind) DescribeRevitLengthUnit(Document doc)
        {
            if (doc == null) return ("(no active document)", IfcLengthUnitKind.Unknown);
            try
            {
                var fo = doc.GetUnits().GetFormatOptions(SpecTypeId.Length);
                var uid = fo.GetUnitTypeId();
                if (uid == UnitTypeId.Millimeters) return ("Millimetres (mm)", IfcLengthUnitKind.Millimeter);
                if (uid == UnitTypeId.Centimeters) return ("Centimetres (cm)", IfcLengthUnitKind.Centimeter);
                if (uid == UnitTypeId.Meters) return ("Metres (m)", IfcLengthUnitKind.Meter);
                if (uid == UnitTypeId.Feet) return ("Feet (ft)", IfcLengthUnitKind.Foot);
                if (uid == UnitTypeId.FeetFractionalInches) return ("Feet & fractional inches", IfcLengthUnitKind.Foot);
                if (uid == UnitTypeId.Inches) return ("Inches (in)", IfcLengthUnitKind.Inch);
                if (uid == UnitTypeId.FractionalInches) return ("Fractional inches", IfcLengthUnitKind.Inch);
                return (uid.TypeId, IfcLengthUnitKind.Unknown);
            }
            catch { return ("(unknown)", IfcLengthUnitKind.Unknown); }
        }

        private static double RevitUnitToMeters(IfcLengthUnitKind kind)
        {
            switch (kind)
            {
                case IfcLengthUnitKind.Millimeter: return 0.001;
                case IfcLengthUnitKind.Centimeter: return 0.01;
                case IfcLengthUnitKind.Decimeter:  return 0.1;
                case IfcLengthUnitKind.Meter:      return 1.0;
                case IfcLengthUnitKind.Kilometer:  return 1000.0;
                case IfcLengthUnitKind.Foot:       return 0.3048;
                case IfcLengthUnitKind.Inch:       return 0.0254;
                default: return 1.0;
            }
        }

        // Same visual weight as InfoBox() but red-tinted, for the unit-mismatch
        // warning specifically -- everything else uses the normal InfoBox().
        private Border InfoBoxWarn(string text) => new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(28, MeToolsTheme.CRed.R, MeToolsTheme.CRed.G, MeToolsTheme.CRed.B)),
            BorderBrush = new SolidColorBrush(MeToolsTheme.CRed), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 12),
            Child = new TextBlock { Text = text, FontSize = 11, Foreground = MeToolsTheme.BrText, TextWrapping = TextWrapping.Wrap },
        };
    }
}
