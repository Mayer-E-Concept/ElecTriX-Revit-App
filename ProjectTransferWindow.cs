// ProjectTransferWindow.cs — ME-Tools | Project Transfer
// Mayer E-Concept SRL — Pure C# WPF, no XAML
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using Brushes  = System.Windows.Media.Brushes;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox  = System.Windows.Controls.TextBox;
using Grid     = System.Windows.Controls.Grid;

namespace METools.ProjectTransfer
{
    public class ProjectTransferWindow : METools.MeToolsWindowBase
    {
        private readonly ExternalEvent            _extEvent;
        private readonly ProjectTransferHandler   _handler;

        private List<TransferItem> _all      = new List<TransferItem>();
        private HashSet<ElementId> _selected = new HashSet<ElementId>();
        private string _categoryFilter = "";     // "" = All
        private string _searchText     = "";

        // ── UI refs ──────────────────────────────────────────────────────
        private ComboBox   _targetCombo;
        private StackPanel _categoryBar;
        private TextBox    _searchBox;
        private StackPanel _rowsPanel;
        private TextBlock  _countLabel;
        private Button     _copyBtn;

        protected override string AppKey => "ProjectTransfer";

        public ProjectTransferWindow(ExternalEvent extEvent, ProjectTransferHandler handler)
        {
            _extEvent = extEvent;
            _handler  = handler;

            _handler.OnSourceLoaded  = items => Dispatcher.Invoke(() => { _all = items; RebuildCategoryBar(); RebuildList(); });
            _handler.OnTargetsLoaded = docs  => Dispatcher.Invoke(() => RebuildTargetCombo(docs));
            _handler.OnStatus        = msg   => Dispatcher.Invoke(() => { if (StatusLeft != null) StatusLeft.Text = msg; });
            _handler.OnCopyDone      = res   => Dispatcher.Invoke(() => ShowCopyResult(res));

            InitWindow("Project Transfer", width: 600);
            BuildStatusBar("Loading…", "Revit 2025");
            BuildUi();

            RequestTargetList();
        }

        // ═════════════════════════════════════════════════════════════════
        // LAYOUT
        // ═════════════════════════════════════════════════════════════════
        private void BuildUi()
        {
            var root = new StackPanel { Margin = new Thickness(14, 12, 14, 10) };
            RootDock.Children.Add(root);

            // ── Target project ───────────────────────────────────────────
            root.Children.Add(Sec("Copy To"));
            var targetRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            targetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            targetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            targetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _targetCombo = StyledCombo(32, 12);
            _targetCombo.SelectionChanged += (s, e) => UpdateCopyButtonState();
            Grid.SetColumn(_targetCombo, 0);
            targetRow.Children.Add(_targetCombo);

            var browseBtn = ActionBtn("Browse…", true, OnBrowseForTarget);
            browseBtn.Height = 32; browseBtn.FontSize = 12; browseBtn.Padding = new Thickness(12, 0, 12, 0);
            Grid.SetColumn(browseBtn, 2);
            targetRow.Children.Add(browseBtn);

            root.Children.Add(targetRow);

            root.Children.Add(new TextBlock
            {
                Text = "Only Drafting Views and Legends can be copied reliably — plan/section/elevation/3D " +
                       "views depend on this project's own levels and grids, so they're left out here.",
                FontSize = 10.5, Foreground = MeToolsTheme.BrMuted, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 0, 0, 12),
            });

            // ── Category filter + search ─────────────────────────────────
            root.Children.Add(Sec("What To Copy"));
            _categoryBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var catScroller = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility   = ScrollBarVisibility.Disabled,
                Content = _categoryBar,
                Margin  = new Thickness(0, 0, 0, 8),
            };
            root.Children.Add(catScroller);

            _searchBox = new TextBox
            {
                Height = 30, FontSize = 12, VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(8, 0, 8, 0),
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrText,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                CaretBrush = MeToolsTheme.BrText, Margin = new Thickness(0, 0, 0, 8),
            };
            SetPlaceholder(_searchBox, "Search…");
            _searchBox.TextChanged += (s, e) =>
            {
                var t = _searchBox.Text;
                _searchText = (t == "Search…") ? "" : t;
                RebuildList();
            };
            root.Children.Add(_searchBox);

            // ── Item list ─────────────────────────────────────────────────
            _rowsPanel = new StackPanel();
            var scroller = new ScrollViewer
            {
                Height = 300,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _rowsPanel,
            };
            var scrollerBorder = new Border
            {
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5), Background = MeToolsTheme.BrSurface,
                Child = scroller, Margin = new Thickness(0, 0, 0, 10),
            };
            root.Children.Add(scrollerBorder);

            // ── Select all / none + count ─────────────────────────────────
            var selRow = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            selRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            selRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            selRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            selRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var selAllBtn  = ActionBtn("Select All",  true, () => SetAllVisibleSelected(true));
            var selNoneBtn = ActionBtn("Select None", true, () => SetAllVisibleSelected(false));
            selAllBtn.Height = 28; selAllBtn.FontSize = 11.5; selAllBtn.Padding = new Thickness(10, 0, 10, 0);
            selNoneBtn.Height = 28; selNoneBtn.FontSize = 11.5; selNoneBtn.Padding = new Thickness(10, 0, 10, 0);
            Grid.SetColumn(selAllBtn, 0); selRow.Children.Add(selAllBtn);
            Grid.SetColumn(selNoneBtn, 2); selRow.Children.Add(selNoneBtn);

            _countLabel = new TextBlock
            {
                FontSize = 11, Foreground = MeToolsTheme.BrMuted,
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right,
            };
            Grid.SetColumn(_countLabel, 3);
            selRow.Children.Add(_countLabel);
            root.Children.Add(selRow);

            // ── Copy button ────────────────────────────────────────────────
            _copyBtn = ActionBtn("Copy Selected to Target Project", false, OnCopyClicked);
            _copyBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
            _copyBtn.IsEnabled = false;
            root.Children.Add(_copyBtn);
        }

        // ═════════════════════════════════════════════════════════════════
        // TARGET PROJECT
        // ═════════════════════════════════════════════════════════════════
        private void RequestTargetList()
        {
            _handler.Request = new TransferRequest { Action = TransferAction.ListTargets };
            _extEvent.Raise();
        }

        private void RebuildTargetCombo(List<OpenDocInfo> docs)
        {
            var previouslySelected = (_targetCombo.SelectedItem as ComboBoxItem)?.Tag as string;

            _targetCombo.Items.Clear();
            foreach (var d in docs)
            {
                if (d.IsActive) continue; // can't copy a project into itself
                _targetCombo.Items.Add(new ComboBoxItem { Content = d.Title, Tag = d.Title });
            }

            if (_targetCombo.Items.Count == 0)
                _targetCombo.Items.Add(new ComboBoxItem { Content = "— open another project first —", Tag = "" });

            var match = _targetCombo.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(i => (i.Tag as string) == previouslySelected);
            _targetCombo.SelectedItem = match ?? _targetCombo.Items[0];
            UpdateCopyButtonState();
        }

        private void OnBrowseForTarget()
        {
            var dlg = new OpenFileDialog
            {
                Title = "Open target project",
                Filter = "Revit Project (*.rvt)|*.rvt",
                CheckFileExists = true,
            };
            if (dlg.ShowDialog() != true) return;

            if (StatusLeft != null) StatusLeft.Text = "Opening project…";
            _handler.Request = new TransferRequest { Action = TransferAction.OpenTargetFile, TargetFilePath = dlg.FileName };
            _extEvent.Raise();
        }

        private string SelectedTargetTitle()
            => (_targetCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

        private void UpdateCopyButtonState()
        {
            if (_copyBtn == null) return;
            bool hasTarget = !string.IsNullOrEmpty(SelectedTargetTitle());
            bool hasSelection = _selected.Count > 0;
            _copyBtn.IsEnabled = hasTarget && hasSelection;
            _copyBtn.Opacity   = _copyBtn.IsEnabled ? 1.0 : 0.5;
        }

        // ═════════════════════════════════════════════════════════════════
        // CATEGORY FILTER + LIST
        // ═════════════════════════════════════════════════════════════════
        private void RebuildCategoryBar()
        {
            _categoryBar.Children.Clear();

            var counts = _all.GroupBy(i => i.Category).ToDictionary(g => g.Key, g => g.Count());
            string CountOf(TransferCategory c) => counts.TryGetValue(c, out var n) ? $" ({n})" : " (0)";

            var allBtn = ToggleBtn($"All ({_all.Count})", _categoryFilter == "", () => SetCategoryFilter(""));
            _categoryBar.Children.Add(allBtn);

            var defs = new (TransferCategory Cat, string Label)[]
            {
                (TransferCategory.Filters,   "Filters"),
                (TransferCategory.Views,     "Views"),
                (TransferCategory.Sheets,    "Sheets"),
                (TransferCategory.Schedules, "Schedules"),
            };
            foreach (var (cat, label) in defs)
            {
                var key = cat.ToString();
                var btn = ToggleBtn(label + CountOf(cat), _categoryFilter == key, () => SetCategoryFilter(key));
                btn.Margin = new Thickness(6, 0, 0, 0);
                _categoryBar.Children.Add(btn);
            }
        }

        private void SetCategoryFilter(string key)
        {
            _categoryFilter = key;
            RebuildCategoryBar();
            RebuildList();
        }

        private bool MatchesFilter(TransferItem i)
        {
            bool catOk = _categoryFilter == "" || i.Category.ToString() == _categoryFilter;
            bool searchOk = string.IsNullOrEmpty(_searchText) ||
                            i.Name.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0;
            return catOk && searchOk;
        }

        private void RebuildList()
        {
            var filtered = _all.Where(MatchesFilter)
                .OrderBy(i => i.Category).ThenBy(i => i.Name)
                .ToList();

            _rowsPanel.Children.Clear();
            foreach (var item in filtered)
                _rowsPanel.Children.Add(BuildRow(item));

            _countLabel.Text = $"{_selected.Count} selected of {_all.Count} total";
        }

        private Border BuildRow(TransferItem item)
        {
            var g = new Grid { Height = 30 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });   // checkbox
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // name
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });      // subinfo
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });   // warning icon

            var cb = new CheckBox
            {
                IsChecked = _selected.Contains(item.Id),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            cb.Checked   += (s, e) => { _selected.Add(item.Id); UpdateCounts(); };
            cb.Unchecked += (s, e) => { _selected.Remove(item.Id); UpdateCounts(); };
            Grid.SetColumn(cb, 0);
            g.Children.Add(cb);

            var name = new TextBlock
            {
                Text = item.Name, FontSize = 12, Foreground = MeToolsTheme.BrText,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(name, 1);
            g.Children.Add(name);

            var sub = new TextBlock
            {
                Text = item.SubInfo, FontSize = 10.5, Foreground = MeToolsTheme.BrMuted,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0),
            };
            Grid.SetColumn(sub, 2);
            g.Children.Add(sub);

            if (item.Warning)
            {
                var warn = new TextBlock
                {
                    Text = "\u26A0", FontSize = 13, Foreground = MeToolsTheme.BrOrange,
                    VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                    ToolTip = item.WarningText,
                };
                Grid.SetColumn(warn, 3);
                g.Children.Add(warn);
            }

            var container = new Border { Child = g, Cursor = Cursors.Hand };
            container.MouseEnter += (s, e) => container.Background = MeToolsTheme.BrRowHov;
            container.MouseLeave += (s, e) => container.Background = Brushes.Transparent;
            // Clicking the name/subinfo area toggles the checkbox too (bigger click
            // target); the checkbox manages its own click natively, so it's not
            // wired here to avoid double-toggling.
            name.MouseLeftButtonUp += (s, e) => cb.IsChecked = !(cb.IsChecked == true);
            sub.MouseLeftButtonUp  += (s, e) => cb.IsChecked = !(cb.IsChecked == true);

            return container;
        }

        private void SetAllVisibleSelected(bool selected)
        {
            var visible = _all.Where(MatchesFilter);
            foreach (var item in visible)
            {
                if (selected) _selected.Add(item.Id);
                else _selected.Remove(item.Id);
            }
            RebuildList();
            UpdateCopyButtonState();
        }

        private void UpdateCounts()
        {
            _countLabel.Text = $"{_selected.Count} selected of {_all.Count} total";
            UpdateCopyButtonState();
        }

        // ═════════════════════════════════════════════════════════════════
        // COPY
        // ═════════════════════════════════════════════════════════════════
        private void OnCopyClicked()
        {
            var target = SelectedTargetTitle();
            if (string.IsNullOrEmpty(target))
            { if (StatusLeft != null) StatusLeft.Text = "Pick or open a target project first."; return; }
            if (_selected.Count == 0)
            { if (StatusLeft != null) StatusLeft.Text = "Nothing selected to copy."; return; }

            if (StatusLeft != null) StatusLeft.Text = "Copying…";
            _handler.Request = new TransferRequest
            {
                Action      = TransferAction.Copy,
                TargetTitle = target,
                ItemIds     = _selected.ToList(),
            };
            _extEvent.Raise();
        }

        private void ShowCopyResult(TransferResult res)
        {
            var summary = string.Join("   ·   ", res.Lines);
            if (StatusLeft != null)
                StatusLeft.Text = string.IsNullOrEmpty(summary) ? "Nothing copied." : summary;
        }

        // ═════════════════════════════════════════════════════════════════
        // HELPERS
        // ═════════════════════════════════════════════════════════════════
        private void SetPlaceholder(TextBox tb, string placeholder)
        {
            tb.Text = placeholder;
            tb.Foreground = MeToolsTheme.BrMuted;
            tb.GotFocus += (s, e) => { if (tb.Text == placeholder) { tb.Text = ""; tb.Foreground = MeToolsTheme.BrText; } };
            tb.LostFocus += (s, e) => { if (string.IsNullOrWhiteSpace(tb.Text)) { tb.Text = placeholder; tb.Foreground = MeToolsTheme.BrMuted; } };
        }

        protected override void OnThemeChanged()
        {
            ApplyComboStyle(_targetCombo);
            RebuildList();
        }
    }
}
