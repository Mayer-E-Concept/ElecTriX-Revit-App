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
        private readonly UIApplication                 _uiApp;
        private StackPanel _body;
        private ScrollViewer _scroll;

        protected override string AppKey => "ProjectHealthCheck";

        public ProjectHealthCheckWindow(HealthCheckResult result, ExternalEvent evt, ProjectHealthCheckHandler handler, UIApplication uiApp)
        {
            S.SetLanguage(SettingsStore.Language ?? "en");
            _evt     = evt;
            _handler = handler;
            _uiApp   = uiApp;
            _handler.OnResult = r => Dispatcher.Invoke(() => Render(r));
            _handler.OnFixMessages = msgs => Dispatcher.Invoke(() =>
            {
                if (msgs != null && msgs.Count > 0)
                    StatusLeft.Text = string.Join("  |  ", msgs);
            });

            InitWindow(S._("healthcheck.title"), 520);
            Build();
            Render(result);
        }

        private void OnBackClicked()
        {
            Close();
            _handler.GoBackToDiagnostics = true;
            _evt.Raise();
        }

        private void Build()
        {
            BuildStatusBar(S._("healthcheck.subtitle"));

            // Footer FIRST (Dock.Bottom must be added before the fill element).
            var footer = new Border
            {
                Background = MeToolsTheme.BrFooter,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(14, 10, 14, 10),
            };
            DockPanel.SetDock(footer, Dock.Bottom);
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var fixBtn = FooterBtn(S._("healthcheck.fix_all"), primary: false, onClick: () =>
            {
                StatusLeft.Text = S._("healthcheck.fixing");
                _handler.DoFix = true;
                _evt.Raise();
            });
            fixBtn.Margin = new Thickness(0, 0, 8, 0);
            fixBtn.ToolTip = S._("healthcheck.fix_tip");
            var refreshBtn = FooterBtn(S._("healthcheck.refresh"), primary: true, onClick: () =>
            {
                StatusLeft.Text = S._("healthcheck.checking");
                _evt.Raise();
            });
            row.Children.Add(fixBtn);
            row.Children.Add(refreshBtn);
            footer.Child = row;
            RootDock.Children.Add(footer);

            _scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight  = 620,
                Background = MeToolsTheme.BrBg,
            };
            // Wrapper so the Back button stays put across re-renders --
            // Render() below clears _body.Children on every Refresh/Fix
            // All, which would silently remove anything added directly
            // inside _body itself.
            var wrapper = new StackPanel();
            var backBtn = ActionBtn("\u2190  " + S._("diagnostics.back"), true, OnBackClicked);
            backBtn.HorizontalAlignment = HorizontalAlignment.Left;
            backBtn.Margin = new Thickness(14, 12, 14, 0);
            wrapper.Children.Add(backBtn);
            _body = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
            wrapper.Children.Add(_body);
            _scroll.Content = wrapper;
            RootDock.Children.Add(_scroll);
        }

        private void Render(HealthCheckResult result)
        {
            _body.Children.Clear();

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                StatusLeft.Text = result.ErrorMessage;
                return;
            }

            SettingsStore.SaveScanHistory("health", result.AllHealthy
                ? S._("diagnostics.hub_history_clean")
                : S._("healthcheck.hub_history_issues"));

            _body.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(result.ProjectTitle) ? "" : result.ProjectTitle,
                FontSize = 11, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 12),
            });

            _body.Children.Add(Sec(S._("healthcheck.tag_family")));
            _body.Children.Add(StatusRow(
                "ME-Tools_CircuitTag",
                result.TagFamilyLoaded,
                result.TagFamilyLoaded
                    ? S._("healthcheck.loaded")
                    : S._("healthcheck.not_loaded")));

            _body.Children.Add(Sec(S._("healthcheck.shared_params")));
            foreach (var row in result.ParamRows)
            {
                string detail;
                if (row.IsHealthy)
                    detail = S._("healthcheck.bound_all");
                else if (!row.BoundAtAll)
                    detail = S._("healthcheck.not_bound_any");
                else
                    detail = S._("healthcheck.missing_from") + string.Join(", ", row.MissingCategories);

                _body.Children.Add(StatusRow(row.ParamName, row.IsHealthy, detail));
            }

            _body.Children.Add(Sec(S._("healthcheck.environment")));

            string folderDetail;
            if (!result.SharedFolderConfigured)
                folderDetail = S._("healthcheck.folder_not_configured");
            else if (!result.SharedFolderReachable)
                folderDetail = string.Format(S._("healthcheck.folder_unreachable"), result.SharedFolderPath);
            else
                folderDetail = string.Format(S._("healthcheck.folder_reachable"), result.SharedFolderPath);
            _body.Children.Add(StatusRow(
                S._("healthcheck.shared_folder_title"),
                result.SharedFolderConfigured && result.SharedFolderReachable,
                folderDetail));

            _body.Children.Add(StatusRow(
                S._("healthcheck.tag_rfa_title"),
                result.TagFamilyResourcePresent,
                result.TagFamilyResourcePresent
                    ? S._("healthcheck.resource_present")
                    : S._("healthcheck.resource_missing_tag")));

            _body.Children.Add(StatusRow(
                S._("healthcheck.shared_params_txt_title"),
                result.SharedParamResourcePresent,
                result.SharedParamResourcePresent
                    ? S._("healthcheck.resource_present")
                    : S._("healthcheck.resource_missing_params")));

            _body.Children.Add(Sec(S._("healthcheck.circuit_tagging")));
            _body.Children.Add(StatusRow(
                S._("healthcheck.untagged_title"),
                result.UntaggedCount == 0 ? MeToolsTheme.CGreen : MeToolsTheme.COrange,
                result.UntaggedCount == 0
                    ? S._("healthcheck.untagged_none")
                    : string.Format(S._(result.UntaggedCount == 1 ? "healthcheck.untagged_found_1" : "healthcheck.untagged_found_n"), result.UntaggedCount)));

            StatusLeft.Text = result.AllHealthy
                ? S._("healthcheck.all_passed")
                : S._("healthcheck.some_failed");
        }

        private Border StatusRow(string title, bool healthy, string detail)
            => StatusRow(title, healthy ? MeToolsTheme.CGreen : MeToolsTheme.CRed, detail);

        // Color-parameterized core -- lets the untagged-elements row below use
        // orange for "some found" rather than red, since that's informational
        // (a normal state for an in-progress project), not a failure the way
        // every other row here is.
        private Border StatusRow(string title, Color color, string detail)
        {
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
