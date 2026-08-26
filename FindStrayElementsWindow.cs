// FindStrayElementsWindow.cs -- ME-Tools | Find Stray Elements
// Mayer E-Concept SRL
//
// Scans a view (or every view in the project) for elements whose position
// is a statistical outlier relative to everything else in that same view.
// Born directly from a real debugging session: a Drafting View looked
// completely empty because text notes had been pasted ~370 kilometers
// from the view's real content, which silently wrecked Zoom to Fit (it
// zoomed out far enough to include the outlier, shrinking the genuinely
// present content to an imperceptible speck) and cost roughly an hour of
// manual detective work through Nonica to actually find. This turns that
// into a few seconds.
//
// Modeless (.Show()) + ExternalEvent -- see FindStrayElementsHandler for
// why this changed from the original modal design. All actual Revit API
// work lives in the Handler now; this Window only builds UI and raises
// requests.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Grid = System.Windows.Controls.Grid;

namespace METools
{
    public class FindStrayElementsWindow : MeToolsWindowBase
    {
        private readonly UIApplication _uiApp;
        private readonly ExternalEvent _evt;
        private readonly FindStrayElementsHandler _handler;
        private StackPanel _resultList;
        private bool _wholeModel;
        private Button _btnScopeActive, _btnScopeModel;
        private List<StrayElementInfo> _results = new List<StrayElementInfo>();

        public FindStrayElementsWindow(UIApplication uiApp, ExternalEvent evt, FindStrayElementsHandler handler)
        {
            _uiApp   = uiApp;
            _evt     = evt;
            _handler = handler;
            _handler.OnScanDone = (results, viewsScanned, errorKey) => Dispatcher.Invoke(() => HandleScanDone(results, viewsScanned, errorKey));
            _handler.OnPruneDone = survivors => Dispatcher.Invoke(() => HandlePruneDone(survivors));
            _handler.OnStatus = msg => Dispatcher.Invoke(() => { if (StatusLeft != null) StatusLeft.Text = msg; });

            S.SetLanguage(SettingsStore.Language ?? "en");
            InitWindow(S._("straytool.title"), width: 540, isDialog: false);
            BuildStatusBar("", $"v{SplashGate.GetVersion()}");
            BuildContent();

            // Remembers the last scan across a close/reopen (confirmed as a
            // real, reported gap -- closing and reopening this tool used to
            // lose everything). Cached at the Command level (see
            // FindStrayElementsCommand), not written to disk -- this is a
            // session-lifetime convenience, not a persistent record, and a
            // fresh Revit session starting with a clean slate is the right
            // default. If there's a cached list, it's shown immediately and
            // then quietly re-verified (Prune) so anything already deleted
            // since the last scan disappears on its own instead of sitting
            // there stale.
            if (FindStrayElementsCommand.CachedResults != null && FindStrayElementsCommand.CachedResults.Count > 0)
            {
                _results = FindStrayElementsCommand.CachedResults;
                RenderResults();
                _handler.Request = new FindStrayElementsRequest { Action = FindStrayAction.Prune, ToPrune = _results };
                _evt.Raise();
            }
        }

        private void BuildContent()
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var body = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };

            var backBtn = ActionBtn("\u2190  " + S._("diagnostics.back"), true, OnBackClicked);
            backBtn.HorizontalAlignment = HorizontalAlignment.Left;
            backBtn.Margin = new Thickness(0, 0, 0, 12);
            body.Children.Add(backBtn);

            body.Children.Add(InfoBox(S._("straytool.intro_hint")));

            body.Children.Add(Sec(S._("straytool.scope")));
            var scopeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            _btnScopeActive = ToggleBtn(S._("straytool.scope_active_view"), true,  () => SetScope(false));
            _btnScopeModel  = ToggleBtn(S._("straytool.scope_whole_model"), false, () => SetScope(true));
            _btnScopeActive.Margin = new Thickness(0, 0, 5, 0);
            scopeRow.Children.Add(_btnScopeActive);
            scopeRow.Children.Add(_btnScopeModel);
            body.Children.Add(scopeRow);

            var scopeHint = new TextBlock
            {
                Text = S._("straytool.scope_model_hint"), FontSize = 10.5, TextWrapping = TextWrapping.Wrap,
                Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 12),
            };
            body.Children.Add(scopeHint);

            var btnScan = FooterBtn(S._("straytool.scan"), true, OnScanClicked);
            body.Children.Add(btnScan);

            var resSec = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            resSec.Children.Add(Sec(S._("straytool.results")));
            var box = new Border
            {
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 4, 0, 0),
            };
            var innerScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 380 };
            _resultList = new StackPanel { Margin = new Thickness(6) };
            innerScroll.Content = _resultList;
            box.Child = innerScroll;
            resSec.Children.Add(box);
            body.Children.Add(resSec);
            RenderResults();

            scroll.Content = body;
            RootDock.Children.Add(scroll);
        }

        private void SetScope(bool wholeModel)
        {
            _wholeModel = wholeModel;
            UpdateToggle(_btnScopeActive, !wholeModel);
            UpdateToggle(_btnScopeModel, wholeModel);
        }

        private void OnBackClicked()
        {
            Close();
            _handler.Request = new FindStrayElementsRequest { Action = FindStrayAction.BackToDiagnostics };
            _evt.Raise();
        }

        // ── Scan ──────────────────────────────────────────────────────────
        private void OnScanClicked()
        {
            StatusLeft.Text = S._("straytool.scanning");
            _handler.Request = new FindStrayElementsRequest { Action = FindStrayAction.Scan, WholeModel = _wholeModel };
            _evt.Raise();
        }

        private void HandleScanDone(List<StrayElementInfo> results, int viewsScanned, string errorKey)
        {
            if (errorKey == "no_views") { StatusLeft.Text = S._("straytool.no_views"); return; }

            _results = results ?? new List<StrayElementInfo>();
            FindStrayElementsCommand.CachedResults = _results;

            StatusLeft.Text = _results.Count == 0
                ? string.Format(S._("straytool.scan_done_clean"), viewsScanned)
                : string.Format(S._("straytool.scan_done_found"), _results.Count, viewsScanned);
            RenderResults();
        }

        private void HandlePruneDone(List<StrayElementInfo> survivors)
        {
            int removed = _results.Count - (survivors?.Count ?? 0);
            _results = survivors ?? new List<StrayElementInfo>();
            FindStrayElementsCommand.CachedResults = _results;
            if (removed > 0)
                StatusLeft.Text = string.Format(S._("straytool.pruned_fmt"), removed);
            RenderResults();
        }

        // ── Results list ──────────────────────────────────────────────────
        private void RenderResults()
        {
            _resultList.Children.Clear();
            if (_results.Count == 0)
            {
                _resultList.Children.Add(new TextBlock
                {
                    Text = S._("straytool.no_results_yet"), FontSize = 11,
                    Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(4),
                });
                return;
            }

            foreach (var group in _results.GroupBy(r => r.ViewName).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                var header = new TextBlock
                {
                    Text = $"{group.Key}  ({group.Count()})", FontSize = 12, FontWeight = FontWeights.SemiBold,
                    Foreground = MeToolsTheme.BrAccent, Margin = new Thickness(2, 8, 0, 4),
                };
                _resultList.Children.Add(header);

                foreach (var r in group)
                    _resultList.Children.Add(BuildStrayRow(r));
            }
        }

        private Border BuildStrayRow(StrayElementInfo r)
        {
            var grid = new Grid { Margin = new Thickness(2, 3, 2, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            double distM = r.DistanceFt * 0.3048;
            string distText = distM >= 1000
                ? string.Format(S._("straytool.distance_km_fmt"), Math.Round(distM / 1000.0, 1))
                : string.Format(S._("straytool.distance_m_fmt"), Math.Round(distM, 1));

            var textStack = new StackPanel();
            textStack.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(r.TypeName) ? r.Category : $"{r.Category} - {r.TypeName}",
                FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = MeToolsTheme.BrText,
                TextWrapping = TextWrapping.Wrap,
            });
            textStack.Children.Add(new TextBlock
            {
                Text = string.Format(S._("straytool.distance_line_fmt"), distText),
                FontSize = 10.5, Foreground = MeToolsTheme.BrOrange, TextWrapping = TextWrapping.Wrap,
            });
            Grid.SetColumn(textStack, 0);

            var btnGoTo = ActionBtn(S._("straytool.go_to"), true, () => OnGoToClicked(r));
            btnGoTo.VerticalAlignment = VerticalAlignment.Top;
            btnGoTo.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(btnGoTo, 1);

            grid.Children.Add(textStack);
            grid.Children.Add(btnGoTo);

            return new Border
            {
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 4, 0, 4), Child = grid,
            };
        }

        private void OnGoToClicked(StrayElementInfo r)
        {
            _handler.Request = new FindStrayElementsRequest
            {
                Action = FindStrayAction.GoTo, TargetViewId = r.ViewId, TargetElementId = r.Id,
            };
            _evt.Raise();
        }
    }
}
