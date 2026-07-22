// ProjectHealthCheckWindow.cs -- ME-Tools | Project Health Check
// Mayer E-Concept SRL
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.UI;
using Grid = System.Windows.Controls.Grid;

namespace METools
{
    public class ProjectHealthCheckWindow : MeToolsWindowBase
    {
        private readonly ExternalEvent                _evt;
        private readonly ProjectHealthCheckHandler     _handler;
        private StackPanel _body;
        private ScrollViewer _scroll;

        protected override string AppKey => "ProjectHealthCheck";

        public ProjectHealthCheckWindow(HealthCheckResult result, ExternalEvent evt, ProjectHealthCheckHandler handler)
        {
            _evt     = evt;
            _handler = handler;
            _handler.OnResult = r => Dispatcher.Invoke(() => Render(r));

            InitWindow("ElectriX -- Project Health Check", 520);
            Build();
            Render(result);
        }

        private void Build()
        {
            BuildStatusBar("Checks the tag family and shared-parameter bindings ElecTriX depends on.");

            // Footer FIRST (Dock.Bottom must be added before the fill element).
            var footer = new Border
            {
                Background = MeToolsTheme.BrFooter,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(14, 10, 14, 10),
            };
            DockPanel.SetDock(footer, Dock.Bottom);
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var refreshBtn = FooterBtn("Refresh", primary: true, onClick: () =>
            {
                StatusLeft.Text = "Checking...";
                _evt.Raise();
            });
            row.Children.Add(refreshBtn);
            footer.Child = row;
            RootDock.Children.Add(footer);

            _scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight  = 620,
                Background = MeToolsTheme.BrBg,
            };
            _body = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
            _scroll.Content = _body;
            RootDock.Children.Add(_scroll);
        }

        private void Render(HealthCheckResult result)
        {
            _body.Children.Clear();

            _body.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(result.ProjectTitle) ? "" : result.ProjectTitle,
                FontSize = 11, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 12),
            });

            _body.Children.Add(Sec("Tag Family"));
            _body.Children.Add(StatusRow(
                "ME-Tools_CircuitTag",
                result.TagFamilyLoaded,
                result.TagFamilyLoaded
                    ? "Loaded in this project."
                    : "Not loaded -- Circuit Tagger will write parameters but place no tags."));

            _body.Children.Add(Sec("Shared Parameters (Circuit Tagger)"));
            foreach (var row in result.ParamRows)
            {
                string detail;
                if (row.IsHealthy)
                    detail = "Bound to all 8 categories.";
                else if (!row.BoundAtAll)
                    detail = "Not bound to any category in this project.";
                else
                    detail = "Missing from: " + string.Join(", ", row.MissingCategories);

                _body.Children.Add(StatusRow(row.ParamName, row.IsHealthy, detail));
            }

            StatusLeft.Text = result.AllHealthy
                ? "All checks passed."
                : "Some checks failed -- see details above.";
        }

        private Border StatusRow(string title, bool healthy, string detail)
        {
            var color = healthy ? MeToolsTheme.CGreen : MeToolsTheme.CRed;

            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var badge = new Border
            {
                Width = 10, Height = 10, CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(color),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(2, 5, 10, 0),
            };
            Grid.SetColumn(badge, 0);
            grid.Children.Add(badge);

            var textSp = new StackPanel();
            textSp.Children.Add(new TextBlock
            {
                Text = title, FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrText,
            });
            textSp.Children.Add(new TextBlock
            {
                Text = detail, FontSize = 11, Foreground = MeToolsTheme.BrMuted,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0),
            });
            Grid.SetColumn(textSp, 1);
            grid.Children.Add(textSp);

            return new Border
            {
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 8, 0, 8),
                Child = grid,
            };
        }
    }
}
