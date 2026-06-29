// Statistics/StatisticsWindow.cs -- ME-Tools | Statistics view
// Mayer E-Concept SRL
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace METools
{
    public class StatisticsWindow : MeToolsWindowBase
    {
        private readonly ExternalEvent     _ev;
        private readonly StatisticsHandler _handler;
        private List<StatRow> _rows;
        private string        _docTitle;

        private ScrollViewer _scroll;
        private StackPanel   _body;

        protected override string AppKey => "Statistics";

        // Section render order
        private static readonly string[] _sections =
            { "Electrical", "Sockets by type", "Switches by type", "Per floor", "Cable & Containment", "Mechanical & Plumbing", "Spaces & Levels" };

        public StatisticsWindow(ExternalEvent ev, StatisticsHandler handler, List<StatRow> rows, string docTitle)
        {
            _ev       = ev;
            _handler  = handler;
            _rows     = rows ?? new List<StatRow>();
            _docTitle = docTitle ?? "";

            _handler.OnResult = (rr, tt) => Dispatcher.Invoke(() =>
            {
                _rows     = rr ?? new List<StatRow>();
                _docTitle = tt ?? "";
                StatusLeft.Text = _docTitle;
                Rebuild();
            });

            S.SetLanguage(SettingsStore.Language ?? "en");
            InitWindow(S.Get("stats.title"), 460);
            Build();
        }

        private void Build()
        {
            BuildStatusBar(_docTitle);

            _scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight  = 820,
                Background = MeToolsTheme.BrBg,
            };
            _body = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
            _scroll.Content = _body;
            var _stGrid = new System.Windows.Controls.Grid();
            _stGrid.Children.Add(_scroll);
            _stGrid.Children.Add(Watermark());
            RootDock.Children.Add(_stGrid);

            Rebuild();
        }

        private void Rebuild()
        {
            if (_body == null) return;
            _body.Children.Clear();

            _body.Children.Add(new TextBlock
            {
                Text = S.Get("stats.subtitle"), FontSize = 15, FontWeight = FontWeights.Bold,
                Foreground = MeToolsTheme.BrText, Margin = new Thickness(0, 0, 0, 2),
            });
            _body.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(_docTitle) ? "(no document)" : _docTitle,
                FontSize = 11, Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 12),
            });

            // Highlight tiles
            var tiles = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 6) };
            foreach (var h in _rows.Where(x => x.Section == "Highlights"))
                tiles.Children.Add(Tile(TrLabel(h.Label), h.Count));
            _body.Children.Add(tiles);

            // Grouped sections (only categories with count > 0)
            // Per-floor section gets a compact grouped layout
            var floorRows = _rows.Where(x => x.Section == "Per floor").ToList();
            if (floorRows.Count > 0)
            {
                _body.Children.Add(SectionHeader(S.Get("stats.per_floor")));
                // Group by level name (strip the " -- Sockets/Switches/Lamps" suffix)
                var levels = floorRows.Select(r =>
                {
                    int dash = r.Label.LastIndexOf(" — ");
                    return dash >= 0 ? r.Label.Substring(0, dash) : r.Label;
                }).Distinct().OrderBy(l => l).ToList();
                foreach (var lvl in levels)
                {
                    var lvlRows = floorRows.Where(r => r.Label.StartsWith(lvl)).ToList();
                    var rowGrid = new System.Windows.Controls.Grid { Margin = new Thickness(0, 1, 0, 1) };
                    rowGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    rowGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
                    rowGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
                    rowGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
                    var lblLevel = new TextBlock { Text = lvl, FontSize = 11, Foreground = MeToolsTheme.BrText,
                        VerticalAlignment = VerticalAlignment.Center };
                    System.Windows.Controls.Grid.SetColumn(lblLevel, 0);
                    rowGrid.Children.Add(lblLevel);
                    int col = 1;
                    foreach (var rv in lvlRows.Take(3))
                    {
                        int dash = rv.Label.LastIndexOf(" — ");
                        string cat = dash >= 0 ? rv.Label.Substring(dash + 3) : rv.Label;
                        var badge = new Border { Background = MeToolsTheme.BrSurface,
                            BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 1, 6, 1),
                            Margin = new Thickness(6, 0, 0, 0) };
                        badge.Child = new TextBlock
                        {
                            Text = $"{TrFloorCat(cat)}: {rv.Count}", FontSize = 10,
                            Foreground = MeToolsTheme.BrMuted,
                        };
                        System.Windows.Controls.Grid.SetColumn(badge, col++);
                        rowGrid.Children.Add(badge);
                    }
                    _body.Children.Add(rowGrid);
                }
            }

            foreach (var sec in _sections.Where(s => s != "Per floor"))
            {
                bool isCable = sec == "Cable & Containment";
                var rows = _rows.Where(x => x.Section == sec && (isCable ? (x.LengthM > 0 || x.Count > 0) : x.Count > 0)).ToList();
                if (rows.Count == 0) continue;
                _body.Children.Add(SectionHeader(TrSection(sec)));
                foreach (var row in rows)
                {
                    if (isCable && row.LengthM > 0)
                        _body.Children.Add(StatLineLength(TrLabel(row.Label), row.LengthM));
                    else
                        _body.Children.Add(StatLine(TrLabel(row.Label), row.Count));
                }
            }

            // Buttons
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
            var refresh = MiniBtn(S.Get("stats.refresh"), true, () => { StatusLeft.Text = S.Get("stats.refreshing"); _ev.Raise(); });
            refresh.Margin = new Thickness(0, 0, 6, 0);
            var export = MiniBtn(S.Get("stats.export"), false, ExportCsv);
            btnRow.Children.Add(refresh);
            btnRow.Children.Add(export);
            _body.Children.Add(btnRow);
        }

        private Border Tile(string label, int count)
        {
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(new TextBlock
            {
                Text = count.ToString(), FontSize = 26, FontWeight = FontWeights.Bold,
                Foreground = MeToolsTheme.BrPetrol, HorizontalAlignment = HorizontalAlignment.Center,
            });
            sp.Children.Add(new TextBlock
            {
                Text = label, FontSize = 11, Foreground = MeToolsTheme.BrMuted,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            return new Border
            {
                Margin = new Thickness(4), Padding = new Thickness(8, 10, 8, 10),
                CornerRadius = new CornerRadius(6),
                Background = MeToolsTheme.BrInput, BorderBrush = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1), Child = sp,
            };
        }

        // Translate a section key / label to the current language for display only.
        // Internal section keys stay English (used for grouping + CSV).
        private static string TrSection(string key) => key switch
        {
            "Per floor"            => S.Get("stats.per_floor"),
            "Sockets by type"      => S.Get("stats.sockets_type"),
            "Switches by type"     => S.Get("stats.switches_type"),
            "Electrical"           => S.Get("stats.electrical"),
            "Cable & Containment"  => S.Get("stats.cable"),
            "Mechanical & Plumbing"=> S.Get("stats.mech"),
            "Spaces & Levels"      => S.Get("stats.spaces"),
            _                      => key,
        };

        private static string TrLabel(string label) => label switch
        {
            "Sockets"  => S.Get("stats.sockets"),
            "Switches" => S.Get("stats.switches"),
            "Lamps"    => S.Get("stats.lamps"),
            "Lamps (Lighting Fixtures)"     => S.Get("stats.cat.lamps"),
            "Sockets (Electrical Fixtures)" => S.Get("stats.cat.sockets"),
            "Switches (Lighting Devices)"   => S.Get("stats.cat.switches"),
            "Electrical Equipment / Panels" => S.Get("stats.cat.panels"),
            "Fire Alarm Devices"            => S.Get("stats.cat.fire"),
            "Data Devices"                  => S.Get("stats.cat.data"),
            "Communication Devices"         => S.Get("stats.cat.comms"),
            _ => label,
        };

        private static string TrFloorCat(string cat) => cat switch
        {
            "Sockets"  => S.Get("stats.sockets"),
            "Switches" => S.Get("stats.switches"),
            "Lamps"    => S.Get("stats.lamps"),
            _ => cat,
        };

        private TextBlock SectionHeader(string text) => new TextBlock
        {
            Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = MeToolsTheme.BrSecText, Margin = new Thickness(0, 14, 0, 4),
        };

        private Grid StatLine(string label, int count)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var l = new TextBlock { Text = label, FontSize = 12, Foreground = MeToolsTheme.BrText, VerticalAlignment = VerticalAlignment.Center };
            var c = new TextBlock { Text = count.ToString(), FontSize = 12, FontWeight = FontWeights.Bold, Foreground = MeToolsTheme.BrText, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(l, 0); Grid.SetColumn(c, 1);
            g.Children.Add(l); g.Children.Add(c);
            return g;
        }

        // For Cable & Containment: show total length in meters instead of count
        private Grid StatLineLength(string label, double lengthM)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var l = new TextBlock { Text = label, FontSize = 12, Foreground = MeToolsTheme.BrText, VerticalAlignment = VerticalAlignment.Center };
            // Format: e.g. "123.4 m"
            var c = new TextBlock
            {
                Text = $"{lengthM:F1} m",
                FontSize = 12, FontWeight = FontWeights.Bold,
                Foreground = MeToolsTheme.BrPetrol, VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(l, 0); Grid.SetColumn(c, 1);
            g.Children.Add(l); g.Children.Add(c);
            return g;
        }

        private Button MiniBtn(string text, bool primary, Action onClick)
        {
            var b = new Button
            {
                Content         = text,
                Height          = 28,
                FontSize        = 12,
                Padding         = new Thickness(14, 0, 14, 0),
                Background      = primary ? MeToolsTheme.BrPetrol : MeToolsTheme.BrInput,
                Foreground      = primary ? Brushes.White : MeToolsTheme.BrText,
                BorderBrush     = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(1),
            };
            b.Click += (s, e) => onClick();
            return b;
        }

        private void ExportCsv()
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "METools");
                Directory.CreateDirectory(dir);
                var safe = new string((_docTitle ?? "model")
                    .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
                if (string.IsNullOrEmpty(safe)) safe = "model";
                var path = Path.Combine(dir,
                    "statistics_" + safe + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");

                var sb = new StringBuilder();
                sb.AppendLine("Section,Category,Value,Unit");
                foreach (var row in _rows.Where(x => x.Section != "Highlights" && (x.Count > 0 || x.LengthM > 0)))
                {
                    string val  = row.Section == "Cable & Containment" && row.LengthM > 0
                        ? row.LengthM.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                        : row.Count.ToString();
                    string unit = row.Section == "Cable & Containment" && row.LengthM > 0 ? "m" : "count";
                    sb.AppendLine(Csv(row.Section) + "," + Csv(row.Label) + "," + val + "," + unit);
                }

                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                StatusLeft.Text = "Exported: Documents\\METools\\" + System.IO.Path.GetFileName(path);
            }
            catch (Exception ex)
            {
                StatusLeft.Text = "Export failed: " + ex.Message;
            }
        }

        private static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Contains(",") ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }
    }
}
