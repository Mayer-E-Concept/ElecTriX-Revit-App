// CollisionCheckerWindow.Duplicates.cs -- ME-Tools | Collision Checker: Duplicate Devices tab
// Mayer E-Concept SRL
//
// A partial-class extension of CollisionCheckerWindow, kept in its own
// file on purpose. The real CollisionCheckerWindow.cs is large (1500+
// lines) with specific, already-debugged height/sizing behavior (see its
// own comments about MaxHeight/SizeToContent) -- rather than risk that by
// editing deep into a file this large, this file adds a second tab
// entirely alongside it. The only changes needed in the original file are
// two small, precise edits -- see the accompanying instructions.
//
// Same visual tab-pill pattern as ActivityLogWindow's Activity/Team
// Totals/My Session tabs (MakeTab/ShowTab there), reimplemented here with
// its own field names (all "_dup"-prefixed) since this is a different
// class and there's no guarantee the original file's private field names
// don't already collide with something simpler.
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.UI;
using Color      = System.Windows.Media.Color;
using Grid       = System.Windows.Controls.Grid;
using Visibility = System.Windows.Visibility;

namespace METools.CollisionChecker
{
    public partial class CollisionCheckerWindow
    {
        private DuplicateElementHandler _dupHandler;
        private ExternalEvent _dupEvent;

        private Border _dupTabCollisions, _dupTabDuplicates, _dupActiveTab;
        private FrameworkElement _dupCollisionsContent, _dupPanelDuplicates;

        private StackPanel _dupResultsPanel;
        private TextBlock _dupSummaryLabel;
        private Button _dupDeleteBtn;
        private DuplicateScanResult _dupLastScanResult;

        // Called once from Build()'s tail (see wiring instructions) with
        // the already-built Collisions tab content, and returns the
        // combined tabbed container to actually add to RootDock.
        private FrameworkElement BuildTabbedRoot(FrameworkElement collisionsContent)
        {
            EnsureDupHandler();

            _dupCollisionsContent = collisionsContent;
            _dupPanelDuplicates = BuildDuplicatesPanel();
            _dupPanelDuplicates.Visibility = Visibility.Collapsed;

            _dupTabCollisions = MakeDupTab("Collisions", MeToolsTheme.CPetrol,
                () => ShowDupTab(_dupTabCollisions, _dupCollisionsContent));
            _dupTabDuplicates = MakeDupTab("Duplicate Devices", MeToolsTheme.COrange,
                () => ShowDupTab(_dupTabDuplicates, _dupPanelDuplicates));

            var tabSp = new StackPanel { Orientation = Orientation.Horizontal };
            tabSp.Children.Add(_dupTabCollisions);
            tabSp.Children.Add(_dupTabDuplicates);
            var tabBar = new Border
            {
                Background = MeToolsTheme.BrHeader,
                BorderBrush = MeToolsTheme.BrBorder,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(4, 0, 0, 0),
                Child = tabSp,
            };
            DockPanel.SetDock(tabBar, Dock.Top);
            RootDock.Children.Add(tabBar);

            var container = new Grid();
            container.Children.Add(_dupCollisionsContent);
            container.Children.Add(_dupPanelDuplicates);

            ShowDupTab(_dupTabCollisions, _dupCollisionsContent);

            return container;
        }

        private void EnsureDupHandler()
        {
            if (_dupHandler != null) return;
            _dupHandler = new DuplicateElementHandler();
            _dupEvent = ExternalEvent.Create(_dupHandler);
            _dupHandler.OnScanComplete = result => Dispatcher.Invoke(() => RenderDuplicateScanResult(result));
            _dupHandler.OnDeleteComplete = result => Dispatcher.Invoke(() => HandleDuplicateDeleteResult(result));
        }

        // ── Tab pill helpers (mirrors ActivityLogWindow's MakeTab/ShowTab) ──
        private Border MakeDupTab(string label, Color tc, Action onClick)
        {
            var pill = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 2, 10, 2),
                Background = new SolidColorBrush(Color.FromArgb(35, tc.R, tc.G, tc.B)),
                Child = new TextBlock
                {
                    Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold,
                    Foreground = MeToolsTheme.BrMuted, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            var tab = new Border
            {
                Padding = new Thickness(8, 6, 8, 6),
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = MeToolsTheme.BrHeader,
                BorderThickness = new Thickness(0, 0, 0, 2),
                BorderBrush = Brushes.Transparent,
                Child = pill,
                Tag = tc,
            };
            tab.MouseEnter += (s, e) => { if (tab != _dupActiveTab) tab.Background = MeToolsTheme.BrBg; };
            tab.MouseLeave += (s, e) => { if (tab != _dupActiveTab) tab.Background = MeToolsTheme.BrHeader; };
            tab.MouseLeftButtonDown += (s, e) => onClick();
            return tab;
        }

        private void ShowDupTab(Border tab, FrameworkElement panel)
        {
            foreach (var t in new[] { _dupTabCollisions, _dupTabDuplicates })
            {
                if (t == null) continue;
                t.BorderBrush = Brushes.Transparent;
                t.Background = MeToolsTheme.BrHeader;
                if (t.Child is Border p)
                {
                    var tc2 = (Color)t.Tag;
                    p.Background = new SolidColorBrush(Color.FromArgb(30, tc2.R, tc2.G, tc2.B));
                    if (p.Child is TextBlock tb2) { tb2.Foreground = MeToolsTheme.BrMuted; tb2.FontWeight = FontWeights.SemiBold; }
                }
            }
            foreach (var p in new[] { _dupCollisionsContent, _dupPanelDuplicates })
                if (p != null) p.Visibility = Visibility.Collapsed;

            _dupActiveTab = tab;
            var ac = (Color)tab.Tag;
            tab.BorderBrush = new SolidColorBrush(ac);
            tab.Background = MeToolsTheme.BrSurface;
            if (tab.Child is Border apill)
            {
                apill.Background = new SolidColorBrush(ac);
                if (apill.Child is TextBlock atb) { atb.Foreground = new SolidColorBrush(Color.FromRgb(230, 245, 245)); atb.FontWeight = FontWeights.Bold; }
            }
            panel.Visibility = Visibility.Visible;
        }

        // ── Duplicates tab content ───────────────────────────────────────────
        private StackPanel BuildDuplicatesPanel()
        {
            var panel = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

            panel.Children.Add(InfoBox(
                "Finds electrical devices (sockets, switches, and similar) sitting at the exact same " +
                "location, family, type, and level -- the pattern left by accidentally pasting a room's " +
                "devices twice. Only exact matches are flagged, and nothing is ever deleted without " +
                "confirming first."));

            var scanRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 10) };
            scanRow.Children.Add(ActionBtn("Scan for duplicates", false, RunDuplicateScan));
            _dupSummaryLabel = new TextBlock
            {
                Text = "", FontSize = 12, Foreground = MeToolsTheme.BrMuted,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0),
            };
            scanRow.Children.Add(_dupSummaryLabel);
            panel.Children.Add(scanRow);

            _dupResultsPanel = new StackPanel();
            var scroller = new ScrollViewer
            {
                Content = _dupResultsPanel, MaxHeight = 380,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            panel.Children.Add(scroller);

            panel.Children.Add(new Border { Height = 10 });
            _dupDeleteBtn = ActionBtn("Delete all duplicates found", false, ConfirmAndDeleteDuplicates);
            _dupDeleteBtn.IsEnabled = false;
            panel.Children.Add(_dupDeleteBtn);

            return panel;
        }

        private void RunDuplicateScan()
        {
            _dupSummaryLabel.Text = "Scanning\u2026";
            _dupHandler.Request = new DuplicateCheckRequest { Action = DuplicateCheckAction.Scan };
            _dupEvent.Raise();
        }

        private void RenderDuplicateScanResult(DuplicateScanResult result)
        {
            _dupLastScanResult = result;
            _dupResultsPanel.Children.Clear();

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                _dupSummaryLabel.Text = "";
                _dupResultsPanel.Children.Add(InfoBox("Scan failed: " + result.Error));
                _dupDeleteBtn.IsEnabled = false;
                return;
            }

            if (result.Groups.Count == 0)
            {
                _dupSummaryLabel.Text = "No duplicates found.";
                _dupDeleteBtn.IsEnabled = false;
                return;
            }

            _dupSummaryLabel.Text = $"{result.Groups.Count} duplicate group(s), {result.TotalExtraElements} extra element(s) to delete.";
            _dupDeleteBtn.IsEnabled = true;

            foreach (var group in result.Groups)
            {
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock
                {
                    Text = $"{group.FamilyName} - {group.TypeName}",
                    FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = MeToolsTheme.BrText,
                });
                sp.Children.Add(new TextBlock
                {
                    Text = $"{group.CategoryName} \u00b7 Level: {group.LevelName} \u00b7 " +
                           $"{group.DuplicateInstances.Count} extra at {group.LocationSummary}",
                    FontSize = 10.5, Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 2, 0, 0),
                });

                var goToRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
                goToRow.Children.Add(ActionBtn("Go to", true, () => SendGoToGroup(group)));
                sp.Children.Add(goToRow);

                _dupResultsPanel.Children.Add(new Border
                {
                    Background = MeToolsTheme.BrSurface, BorderBrush = MeToolsTheme.BrBorder,
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 8),
                    Child = sp,
                });
            }
        }

        private void SendGoToGroup(DuplicateGroup group)
        {
            _dupHandler.Request = new DuplicateCheckRequest { Action = DuplicateCheckAction.GoToGroup, TargetGroup = group };
            _dupEvent.Raise();
        }

        private void ConfirmAndDeleteDuplicates()
        {
            if (_dupLastScanResult == null || _dupLastScanResult.Groups.Count == 0) return;

            var confirm = MessageBox.Show(
                $"Delete {_dupLastScanResult.TotalExtraElements} duplicate element(s) across " +
                $"{_dupLastScanResult.Groups.Count} group(s)? One copy of each is kept -- only the " +
                "extras are removed. This can't be undone from here.",
                "Delete duplicate devices", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

            if (confirm != MessageBoxResult.Yes) return;

            _dupHandler.Request = new DuplicateCheckRequest
            {
                Action = DuplicateCheckAction.DeleteDuplicates,
                GroupsToDelete = _dupLastScanResult.Groups,
            };
            _dupEvent.Raise();
        }

        private void HandleDuplicateDeleteResult(DuplicateDeleteResult result)
        {
            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                MessageBox.Show("Delete failed: " + result.Error, "Delete duplicate devices",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _dupSummaryLabel.Text = $"Deleted {result.Deleted} element(s).";
            _dupDeleteBtn.IsEnabled = false;
            _dupResultsPanel.Children.Clear();
            _dupLastScanResult = null;

            // Re-scan so the list reflects reality immediately, rather than
            // leaving a stale count on screen.
            RunDuplicateScan();
        }
    }
}
