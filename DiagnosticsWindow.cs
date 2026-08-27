// DiagnosticsWindow.cs -- ME-Tools | Diagnostics hub
// Mayer E-Concept SRL
//
// A permanent home for model-health / cleanup tools that don't belong
// under Settings (the app's own configuration -- appearance, language,
// license, worksets) and don't belong among the day-to-day design tools
// either: Find Stray Elements, Project Health Check, Imported Objects, and
// Duplicate Family Finder. Deliberately mirrors Settings' own "phone home
// screen" tile-grid style (BuildHomeTile below is intentionally the same
// visual language as SettingsWindow.BuildHomeTile) for consistency, but is
// its own separate ribbon entry point, not a tab bolted onto Settings.
//
// Each tile closes this hub before opening its target -- deliberately,
// rather than trying to keep this modal dialog open alongside whatever
// gets launched: most of these are their own modeless windows with their
// own ExternalEvent, and a modal dialog left open on top of a modeless one
// in the same application would leave the modeless one unable to receive
// input until the modal one closes anyway. Closing first keeps the
// handoff simple and identical for every tile, rather than one special
// case per tool.
//
// BUG FIXED HERE: each tile used to call its target tool's Open() method
// (or ShowDialog() for Imported Objects) directly from its own click
// handler. See DiagnosticsHandler for the full explanation -- in short,
// this window is modeless now and its click handlers have no valid Revit
// API context on their own, so every tile routes through this window's
// own ExternalEvent instead, guaranteeing they run with the same valid
// context an IExternalCommand's Execute() would provide.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.UI;

namespace METools
{
    public class DiagnosticsWindow : MeToolsWindowBase
    {
        private readonly UIApplication _uiApp;
        private readonly ExternalEvent _evt;
        private readonly DiagnosticsHandler _handler;

        // HistoryKey matches the key each tool's own Window passes to
        // SettingsStore.SaveScanHistory -- "stray"/"health"/"imports"/
        // "dupfam" -- so this hub can read back exactly what each tool's
        // own last scan (individual OR via Run All Checks below) found.
        private static readonly (string Key, string Glyph, DiagnosticsTileAction Action, string HistoryKey)[] _tiles =
        {
            ("diagnostics.tile.stray",      "\uE71C", DiagnosticsTileAction.OpenStray,      "stray"),   // Filter/funnel-ish
            ("diagnostics.tile.health",     "\uE9D9", DiagnosticsTileAction.OpenHealth,     "health"),  // Heart/pulse-ish
            ("diagnostics.tile.imports",    "\uE8B5", DiagnosticsTileAction.OpenImports,    "imports"), // Import
            ("diagnostics.tile.duplicates", "\uE8C8", DiagnosticsTileAction.OpenDuplicates, "dupfam"),  // Copy/duplicate-ish
        };

        // One subtitle TextBlock per tile, kept around so a Run All Checks
        // result can update them in place afterward without rebuilding the
        // whole window (which would also lose scroll position, etc.).
        private readonly Dictionary<string, TextBlock> _historyLabels = new Dictionary<string, TextBlock>();
        private TextBlock _runAllStatusLabel;

        public DiagnosticsWindow(UIApplication uiApp, ExternalEvent evt, DiagnosticsHandler handler)
        {
            _uiApp   = uiApp;
            _evt     = evt;
            _handler = handler;
            _handler.OnRunAllDone = summary => Dispatcher.Invoke(() => HandleRunAllDone(summary));

            S.SetLanguage(SettingsStore.Language ?? "en");
            InitWindow(S._("diagnostics.title"), width: 460, isDialog: false);
            BuildStatusBar("", $"v{SplashGate.GetVersion()}");
            BuildContent();
        }

        private void OnTileClicked(DiagnosticsTileAction action)
        {
            Close();
            _handler.Action = action;
            _evt.Raise();
        }

        private void OnRunAllClicked()
        {
            if (_runAllStatusLabel != null) _runAllStatusLabel.Text = S._("diagnostics.runall_running");
            _handler.Action = DiagnosticsTileAction.RunAllChecks;
            _evt.Raise();
        }

        private void HandleRunAllDone(string summary)
        {
            if (_runAllStatusLabel != null) _runAllStatusLabel.Text = "";
            RefreshHistoryLabels();
            MessageBox.Show(summary, S._("diagnostics.runall_summary_title"), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // Re-reads each tile's saved history and updates its subtitle in
        // place -- called once when the hub first opens, and again after
        // Run All Checks finishes (its own individual-tool scans just
        // updated the same underlying SettingsStore entries this reads).
        private void RefreshHistoryLabels()
        {
            foreach (var tile in _tiles)
            {
                if (!_historyLabels.TryGetValue(tile.HistoryKey, out var label)) continue;
                var (summary, when) = SettingsStore.GetScanHistory(tile.HistoryKey);
                label.Text = string.IsNullOrEmpty(summary)
                    ? S._("diagnostics.hub_history_never")
                    : string.Format(S._("diagnostics.hub_history_fmt"), summary, FormatWhen(when));
            }
        }

        private static string FormatWhen(DateTime? when)
        {
            if (when == null) return "";
            var span = DateTime.Now - when.Value;
            if (span.TotalMinutes < 1)   return S._("diagnostics.hub_when_just_now");
            if (span.TotalHours < 1)     return string.Format(S._("diagnostics.hub_when_minutes_fmt"), (int)span.TotalMinutes);
            if (span.TotalDays < 1)      return string.Format(S._("diagnostics.hub_when_hours_fmt"), (int)span.TotalHours);
            return string.Format(S._("diagnostics.hub_when_days_fmt"), (int)span.TotalDays);
        }

        private void BuildContent()
        {
            var body = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
            body.Children.Add(InfoBox(S._("diagnostics.intro_hint")));

            var grid = new Grid { Margin = new Thickness(0, 4, 0, 8) };
            for (int c = 0; c < 3; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            int rows = (_tiles.Length + 2) / 3; // matches SettingsWindow.BuildHomeGrid's own wrapping approach
            for (int r = 0; r < rows; r++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            for (int i = 0; i < _tiles.Length; i++)
            {
                var action = _tiles[i].Action;
                var tile = BuildHomeTile(S._(_tiles[i].Key), _tiles[i].Glyph, () => OnTileClicked(action), out var historyLabel);
                _historyLabels[_tiles[i].HistoryKey] = historyLabel;
                Grid.SetRow(tile, i / 3);
                Grid.SetColumn(tile, i % 3);
                grid.Children.Add(tile);
            }
            body.Children.Add(grid);

            var runAllBtn = FooterBtn(S._("diagnostics.run_all"), true, OnRunAllClicked);
            body.Children.Add(runAllBtn);
            _runAllStatusLabel = new TextBlock
            {
                FontSize = 10.5, Foreground = MeToolsTheme.BrMuted, TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0),
            };
            body.Children.Add(_runAllStatusLabel);

            RootDock.Children.Add(body);
            RefreshHistoryLabels();
        }

        // Same visual language as SettingsWindow.BuildHomeTile, deliberately
        // -- this hub and Settings should read as the same family of
        // window even though they're separate classes. Extended with a
        // small history subtitle line under the label, whose TextBlock is
        // handed back via historyLabel so RefreshHistoryLabels can update
        // it later without rebuilding the tile.
        private Border BuildHomeTile(string label, string glyph, Action onClick, out TextBlock historyLabel)
        {
            var iconTb = new TextBlock
            {
                Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 24,
                Foreground = MeToolsTheme.BrAccent, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 6),
            };
            var labelTb = new TextBlock
            {
                Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = MeToolsTheme.BrText,
                HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            };
            historyLabel = new TextBlock
            {
                FontSize = 9.5, Foreground = MeToolsTheme.BrMuted, TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0), MaxWidth = 130,
            };
            var inner = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            inner.Children.Add(iconTb);
            inner.Children.Add(labelTb);
            inner.Children.Add(historyLabel);

            var tile = new Border
            {
                Background      = MeToolsTheme.BrSurface,
                BorderBrush      = MeToolsTheme.BrBorder,
                BorderThickness  = new Thickness(1),
                CornerRadius     = new CornerRadius(10),
                Margin           = new Thickness(6),
                Padding          = new Thickness(8, 14, 8, 14),
                Height           = 112,
                Cursor           = Cursors.Hand,
                Child            = inner,
            };
            tile.MouseEnter += (s, e) => tile.Background = MeToolsTheme.BrActiveBg;
            tile.MouseLeave += (s, e) => tile.Background = MeToolsTheme.BrSurface;
            tile.MouseLeftButtonDown += (s, e) => onClick();
            return tile;
        }
    }
}
