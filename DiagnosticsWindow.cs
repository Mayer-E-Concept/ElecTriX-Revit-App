// DiagnosticsWindow.cs -- ME-Tools | Diagnostics hub
// Mayer E-Concept SRL
//
// A permanent home for model-health / cleanup tools that don't belong
// under Settings (the app's own configuration -- appearance, language,
// license, worksets) and don't belong among the day-to-day design tools
// either: Find Stray Elements, Project Health Check, and Imported Objects.
// Deliberately mirrors Settings' own "phone home screen" tile-grid style
// (BuildHomeTile below is intentionally the same visual language as
// SettingsWindow.BuildHomeTile) for consistency, but is its own separate
// ribbon entry point, not a tab bolted onto Settings.
//
// Each tile closes this hub before opening its target -- deliberately,
// rather than trying to keep this modal dialog open alongside whatever
// gets launched: Project Health Check is its own modeless window with its
// own ExternalEvent, and a modal dialog left open on top of a modeless one
// in the same application would leave the modeless one unable to receive
// input until the modal one closes anyway. Closing first keeps the
// handoff simple and identical for all three tiles, rather than one
// special case per tool.
using System;
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

        private static readonly (string Key, string Glyph, Action<DiagnosticsWindow> Open)[] _tiles =
        {
            ("diagnostics.tile.stray",   "\uE71C", w => { w.Close(); FindStrayElementsCommand.Open(w._uiApp); }), // Filter/funnel-ish
            ("diagnostics.tile.health",  "\uE9D9", w => { w.Close(); ProjectHealthCheckCommand.Open(w._uiApp); }), // Heart/pulse-ish
            ("diagnostics.tile.imports", "\uE8B5", w =>
            {
                // BUG FIXED HERE: SettingsWindow relies internally on the
                // static SettingsCommand.CurrentApp property (e.g. Try
                // Purge Unused reads it directly) rather than taking a
                // UIApplication as a constructor parameter the way the
                // other two tiles' Open() methods do -- this was never
                // being set on this specific path, since Imported Objects
                // is reached through SettingsWindow's own constructor, not
                // through SettingsCommand.Execute() (the only other place
                // that was setting it). Left null, anything inside
                // Imported Objects that reads CurrentApp would have
                // silently done nothing.
                SettingsCommand.SetCurrentAppForDirectLaunch(w._uiApp);
                w.Close();
                new SettingsWindow(SettingsWindow.ImportsTabIndex).ShowDialog();
                SettingsCommand.SetCurrentAppForDirectLaunch(null);
            }),
        };

        public DiagnosticsWindow(UIApplication uiApp)
        {
            _uiApp = uiApp;
            S.SetLanguage(SettingsStore.Language ?? "en");
            InitWindow(S._("diagnostics.title"), width: 460, isDialog: false);
            BuildStatusBar("", $"v{SplashGate.GetVersion()}");
            BuildContent();
        }

        private void BuildContent()
        {
            var body = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
            body.Children.Add(InfoBox(S._("diagnostics.intro_hint")));

            var grid = new Grid { Margin = new Thickness(0, 4, 0, 8) };
            for (int c = 0; c < 3; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            for (int i = 0; i < _tiles.Length; i++)
            {
                var tile = BuildHomeTile(S._(_tiles[i].Key), _tiles[i].Glyph, _tiles[i].Open);
                Grid.SetRow(tile, 0);
                Grid.SetColumn(tile, i);
                grid.Children.Add(tile);
            }
            body.Children.Add(grid);

            RootDock.Children.Add(body);
        }

        // Same visual language as SettingsWindow.BuildHomeTile, deliberately
        // -- this hub and Settings should read as the same family of
        // window even though they're separate classes.
        private Border BuildHomeTile(string label, string glyph, Action<DiagnosticsWindow> onClick)
        {
            var iconTb = new TextBlock
            {
                Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 26,
                Foreground = MeToolsTheme.BrAccent, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 8),
            };
            var labelTb = new TextBlock
            {
                Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = MeToolsTheme.BrText,
                HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            };
            var inner = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            inner.Children.Add(iconTb);
            inner.Children.Add(labelTb);

            var tile = new Border
            {
                Background      = MeToolsTheme.BrSurface,
                BorderBrush      = MeToolsTheme.BrBorder,
                BorderThickness  = new Thickness(1),
                CornerRadius     = new CornerRadius(10),
                Margin           = new Thickness(6),
                Padding          = new Thickness(8, 14, 8, 14),
                Height           = 100,
                Cursor           = Cursors.Hand,
                Child            = inner,
            };
            tile.MouseEnter += (s, e) => tile.Background = MeToolsTheme.BrActiveBg;
            tile.MouseLeave += (s, e) => tile.Background = MeToolsTheme.BrSurface;
            tile.MouseLeftButtonDown += (s, e) => onClick(this);
            return tile;
        }
    }
}
