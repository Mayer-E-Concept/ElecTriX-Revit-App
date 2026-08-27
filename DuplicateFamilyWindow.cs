// DuplicateFamilyWindow.cs -- ME-Tools | Duplicate Family Finder
// Mayer E-Concept SRL
//
// Modeless (.Show()) + ExternalEvent, same pattern as Find Stray Elements --
// see DuplicateFamilyHandler for the detection approach (category + exact
// type-name-set match, not naming-convention guessing).
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
    public class DuplicateFamilyWindow : MeToolsWindowBase
    {
        private readonly UIApplication _uiApp;
        private readonly ExternalEvent _evt;
        private readonly DuplicateFamilyHandler _handler;
        private StackPanel _resultList;
        private List<DuplicateFamilyGroup> _groups = new List<DuplicateFamilyGroup>();

        public DuplicateFamilyWindow(UIApplication uiApp, ExternalEvent evt, DuplicateFamilyHandler handler)
        {
            _uiApp   = uiApp;
            _evt     = evt;
            _handler = handler;
            _handler.OnScanDone   = results => Dispatcher.Invoke(() => HandleScanDone(results));
            _handler.OnDeleteDone = id      => Dispatcher.Invoke(() => HandleDeleteDone(id));
            _handler.OnStatus     = msg     => Dispatcher.Invoke(() => { if (StatusLeft != null) StatusLeft.Text = msg; });

            S.SetLanguage(SettingsStore.Language ?? "en");
            InitWindow(S._("dupfam.title"), width: 560, isDialog: false);
            BuildStatusBar("", $"v{SplashGate.GetVersion()}");
            BuildContent();
        }

        private void BuildContent()
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var body = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };

            var backBtn = ActionBtn("\u2190  " + S._("diagnostics.back"), true, OnBackClicked);
            backBtn.HorizontalAlignment = HorizontalAlignment.Left;
            backBtn.Margin = new Thickness(0, 0, 0, 12);
            body.Children.Add(backBtn);

            body.Children.Add(InfoBox(S._("dupfam.intro_hint")));

            var btnScan = FooterBtn(S._("dupfam.scan"), true, OnScanClicked);
            btnScan.Margin = new Thickness(0, 12, 0, 0);
            body.Children.Add(btnScan);

            var resSec = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            resSec.Children.Add(Sec(S._("dupfam.results")));
            var box = new Border
            {
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 4, 0, 0),
            };
            var innerScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 420 };
            _resultList = new StackPanel { Margin = new Thickness(6) };
            innerScroll.Content = _resultList;
            box.Child = innerScroll;
            resSec.Children.Add(box);
            body.Children.Add(resSec);
            RenderResults();

            scroll.Content = body;
            RootDock.Children.Add(scroll);
        }

        private void OnBackClicked()
        {
            Close();
            _handler.Request = new DuplicateFamilyRequest { Action = DuplicateFamilyAction.BackToDiagnostics };
            _evt.Raise();
        }

        private void OnScanClicked()
        {
            StatusLeft.Text = S._("dupfam.scanning");
            _handler.Request = new DuplicateFamilyRequest { Action = DuplicateFamilyAction.Scan };
            _evt.Raise();
        }

        private void HandleScanDone(List<DuplicateFamilyGroup> groups)
        {
            _groups = groups ?? new List<DuplicateFamilyGroup>();
            int totalFamilies = _groups.Sum(g => g.Members.Count);
            StatusLeft.Text = _groups.Count == 0
                ? S._("dupfam.scan_done_clean")
                : string.Format(S._("dupfam.scan_done_found"), _groups.Count, totalFamilies);
            SettingsStore.SaveScanHistory("dupfam", _groups.Count == 0
                ? S._("diagnostics.hub_history_clean")
                : string.Format(S._("dupfam.hub_history_found_fmt"), _groups.Count));
            RenderResults();
        }

        private void HandleDeleteDone(ElementId deletedId)
        {
            // Remove just the deleted member from wherever it is, rather
            // than forcing a full rescan -- if a group drops to a single
            // remaining member, it's no longer a duplicate group at all.
            foreach (var g in _groups)
                g.Members.RemoveAll(m => m.FamilyId == deletedId);
            _groups.RemoveAll(g => g.Members.Count < 2);
            StatusLeft.Text = S._("dupfam.deleted_one");
            RenderResults();
        }

        private void RenderResults()
        {
            _resultList.Children.Clear();
            if (_groups.Count == 0)
            {
                _resultList.Children.Add(new TextBlock
                {
                    Text = S._("dupfam.no_results_yet"), FontSize = 11,
                    Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(4),
                });
                return;
            }

            foreach (var group in _groups)
            {
                var header = new TextBlock
                {
                    Text = $"{group.CategoryName}  ({group.Members.Count})", FontSize = 12, FontWeight = FontWeights.SemiBold,
                    Foreground = MeToolsTheme.BrAccent, Margin = new Thickness(2, 10, 0, 2),
                };
                _resultList.Children.Add(header);

                var sig = new TextBlock
                {
                    Text = string.Format(S._("dupfam.shared_types_fmt"), group.TypeSignature),
                    FontSize = 10, Foreground = MeToolsTheme.BrMuted, TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(2, 0, 0, 6),
                };
                _resultList.Children.Add(sig);

                foreach (var m in group.Members)
                    _resultList.Children.Add(BuildMemberRow(m));
            }
        }

        private Border BuildMemberRow(DuplicateFamilyMember m)
        {
            var grid = new Grid { Margin = new Thickness(2, 3, 2, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var textStack = new StackPanel();
            textStack.Children.Add(new TextBlock
            {
                Text = m.FamilyName, FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrText, TextWrapping = TextWrapping.Wrap,
            });
            bool unused = m.InstanceCount == 0;
            textStack.Children.Add(new TextBlock
            {
                Text = unused
                    ? string.Format(S._("dupfam.member_unused_fmt"), m.TypeCount)
                    : string.Format(S._("dupfam.member_used_fmt"), m.TypeCount, m.InstanceCount),
                FontSize = 10.5, Foreground = unused ? MeToolsTheme.BrMuted : MeToolsTheme.BrOrange,
                TextWrapping = TextWrapping.Wrap,
            });
            Grid.SetColumn(textStack, 0);

            // Go To for anything actually placed somewhere; Delete only for
            // the safe, zero-instance case -- matches this app's existing
            // Purge Unused reasoning elsewhere: an unused family is safe to
            // remove regardless of whether it's confirmed to be a true
            // duplicate or just genuinely unused.
            UIElement actionBtn;
            if (!unused)
            {
                var btnGoTo = ActionBtn(S._("dupfam.go_to"), true, () => OnGoToClicked(m));
                btnGoTo.VerticalAlignment = VerticalAlignment.Top;
                actionBtn = btnGoTo;
            }
            else
            {
                var btnDelete = ActionBtn(S._("dupfam.delete"), true, () => OnDeleteClicked(m));
                btnDelete.VerticalAlignment = VerticalAlignment.Top;
                actionBtn = btnDelete;
            }
            actionBtn.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));
            Grid.SetColumn(actionBtn, 1);

            grid.Children.Add(textStack);
            grid.Children.Add(actionBtn);

            return new Border
            {
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 4, 0, 4), Child = grid,
            };
        }

        private void OnGoToClicked(DuplicateFamilyMember m)
        {
            _handler.Request = new DuplicateFamilyRequest { Action = DuplicateFamilyAction.GoTo, TargetInstanceId = m.FirstInstanceId };
            _evt.Raise();
        }

        private void OnDeleteClicked(DuplicateFamilyMember m)
        {
            if (MessageBox.Show(string.Format(S._("dupfam.delete_confirm_fmt"), m.FamilyName), S._("dupfam.delete_confirm_title"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            _handler.Request = new DuplicateFamilyRequest { Action = DuplicateFamilyAction.Delete, TargetFamilyId = m.FamilyId };
            _evt.Raise();
        }
    }
}
