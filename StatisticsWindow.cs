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
using System.Windows.Input;
using System.Windows.Media;

namespace METools
{
    public class StatisticsWindow : MeToolsWindowBase
    {
        private readonly ExternalEvent     _ev;
        private readonly StatisticsHandler _handler;
        private List<StatRow> _rows;
        private string        _docTitle;
        private readonly string _projectId;
        private StatSnapshot    _snapshot;

        private ScrollViewer _scroll;
        private StackPanel   _body;

        protected override string AppKey => "Statistics";

        // Section render order
        private static readonly string[] _sections =
            { "Electrical", "Sockets by type", "Switches by type",
              "Sockets by workset", "Switches by workset", "Lamps by workset",
              "Per floor", "Cable & Containment", "Mechanical & Plumbing", "Spaces & Levels" };

        public StatisticsWindow(ExternalEvent ev, StatisticsHandler handler, List<StatRow> rows, string docTitle,
            string projectId = null, StatSnapshot snapshot = null)
        {
            _ev        = ev;
            _handler   = handler;
            _rows      = rows ?? new List<StatRow>();
            _docTitle  = docTitle ?? "";
            _projectId = projectId;
            _snapshot  = snapshot;

            _handler.OnResult = (rr, tt) => Dispatcher.Invoke(() =>
            {
                _rows     = rr ?? new List<StatRow>();
                _docTitle = tt ?? "";
                StatusLeft.Text = _docTitle;
                Rebuild();
            });
            _handler.OnSnapshotSaved = pid => Dispatcher.Invoke(() =>
            {
                // Reload from disk rather than reconstructing in-memory --
                // this is the same file LoadSnapshot() would read later, so
                // showing exactly that (not just what Save() was passed)
                // catches any serialization surprise immediately.
                _snapshot = StatisticsSnapshotStorage.Load(pid);
                StatusLeft.Text = S.Get("stats.snapshot_saved");
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

            BuildCompareSection();

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
            export.Margin = new Thickness(0, 0, 6, 0);
            var saveSnapshot = MiniBtn(S.Get("stats.save_snapshot"), false, () =>
            {
                StatusLeft.Text = S.Get("stats.saving_snapshot");
                _handler.SaveSnapshotRequested = true;
                _ev.Raise();
            });
            btnRow.Children.Add(refresh);
            btnRow.Children.Add(export);
            btnRow.Children.Add(saveSnapshot);
            _body.Children.Add(btnRow);

            ResizeToFitContent();
        }

        // Shows what changed since the last saved snapshot -- only the rows
        // that actually differ, not the whole list again. Nothing here ever
        // modifies the snapshot itself; it's purely a read-only comparison
        // until the user explicitly clicks Save Snapshot again.
        private void BuildCompareSection()
        {
            _body.Children.Add(SectionHeader(S.Get("stats.compare_title")));

            if (_snapshot == null)
            {
                _body.Children.Add(new TextBlock
                {
                    Text = S.Get("stats.no_snapshot_yet"), FontSize = 11, FontStyle = FontStyles.Italic,
                    Foreground = MeToolsTheme.BrMuted, TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 8),
                });
                return;
            }

            var diffs = StatisticsSnapshotStorage.ComputeDiff(_rows, _snapshot);

            _body.Children.Add(new TextBlock
            {
                Text = string.Format(S.Get("stats.compared_to"), _snapshot.SavedAtUtc.ToLocalTime().ToString("g")),
                FontSize = 10.5, Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 6),
            });

            if (diffs.Count == 0)
            {
                _body.Children.Add(new TextBlock
                {
                    Text = S.Get("stats.no_changes"), FontSize = 11.5, Foreground = MeToolsTheme.BrMuted,
                    Margin = new Thickness(0, 0, 0, 8),
                });
                return;
            }

            foreach (var d in diffs.OrderBy(x => x.Section).ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase))
                _body.Children.Add(DiffLine(d));
        }

        private Grid DiffLine(StatDiffRow d)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock
            {
                Text = TrLabel(d.Label), FontSize = 12, Foreground = MeToolsTheme.BrText,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(label, 0); g.Children.Add(label);

            string unit = d.IsLength ? " m" : "";
            string deltaText = (d.Delta > 0 ? "+" : "") + (d.IsLength ? d.Delta.ToString("F1") : d.Delta.ToString("0")) + unit;
            var deltaBrush = d.Delta > 0 ? MeToolsTheme.BrGreen : d.Delta < 0 ? MeToolsTheme.BrOrange : MeToolsTheme.BrMuted;

            var valueText = new TextBlock
            {
                FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
                Inlines =
                {
                    new System.Windows.Documents.Run((d.IsLength ? d.OldValue.ToString("F1") : d.OldValue.ToString("0")) + unit)
                        { Foreground = MeToolsTheme.BrMuted },
                    new System.Windows.Documents.Run("  \u2192  ") { Foreground = MeToolsTheme.BrMuted },
                    new System.Windows.Documents.Run((d.IsLength ? d.NewValue.ToString("F1") : d.NewValue.ToString("0")) + unit)
                        { Foreground = MeToolsTheme.BrText, FontWeight = FontWeights.Bold },
                    new System.Windows.Documents.Run("   " + deltaText) { Foreground = deltaBrush, FontWeight = FontWeights.Bold },
                },
            };
            Grid.SetColumn(valueText, 1); g.Children.Add(valueText);

            return g;
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
            "Sockets by workset"   => "Sockets by workset",
            "Switches by workset"  => "Switches by workset",
            "Lamps by workset"     => "Lamps by workset",
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
            var bgN = primary ? MeToolsTheme.BrPetrol : MeToolsTheme.BrInput;
            var bgH = primary ? MeToolsTheme.BrPetrolDark : MeToolsTheme.BrActiveBg;
            var b = new Button
            {
                Content         = text,
                Height          = 28,
                FontSize        = 12,
                Padding         = new Thickness(14, 0, 14, 0),
                Background      = bgN,
                Foreground      = primary ? Brushes.White : MeToolsTheme.BrText,
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
                sb.AppendLine("Section,Name,Value,Unit");

                // Defines the order sections appear in the export (matches on-screen order).
                var sectionOrder = new List<string>
                {
                    "Electrical", "Sockets by type", "Switches by type",
                    "Sockets by workset", "Switches by workset", "Lamps by workset",
                    "Per floor", "Cable & Containment", "Mechanical & Plumbing", "Spaces & Levels",
                };
                int SectionRank(string s)
                {
                    int i = sectionOrder.IndexOf(s);
                    return i < 0 ? sectionOrder.Count : i;
                }

                // Sort by section (in the defined order), then alphabetically by name --
                // e.g. within "Sockets by type" every row is listed A-Z by its type name,
                // rather than by how many were found.
                var sorted = _rows
                    .Where(x => x.Section != "Highlights" && (x.Count > 0 || x.LengthM > 0))
                    .OrderBy(x => SectionRank(x.Section))
                    .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var row in sorted)
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
