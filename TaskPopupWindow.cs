// TaskPopupWindow.cs -- ME-Tools | Project Tasks notification toast
// Mayer E-Concept SRL
//
// Mirrors CommentPopupWindow.cs directly -- same lightweight standalone
// Window (no MeToolsWindowBase chrome needed), same bottom-right toast
// position, same visual language. Shows for both a brand-new task and a
// stale/unassigned reminder (isStale just changes the header text and
// accent tone -- everything else about the window is identical).
//
// currentUser is captured once by TasksWatcher (which has live Revit API
// access) and passed straight through here, rather than this window
// needing any API access of its own just to know who's clicking
// "Assign to me" -- a user's own Revit username doesn't change between
// the popup appearing and being clicked, so there's nothing to gain by
// re-fetching it live.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace METools.Tasks
{
    public class TaskPopupWindow : Window
    {
        private readonly ProjectTask _task;
        private readonly bool _isStale;
        private readonly string _currentUser;
        private TextBlock _errorText;
        private StackPanel _btnRow;

        public TaskPopupWindow(ProjectTask task, bool isStale, string currentUser)
        {
            S.SetLanguage(SettingsStore.Language ?? "en");
            _task = task;
            _isStale = isStale;
            _currentUser = currentUser;

            Title = "ME-Tools";
            Width = 320;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            AllowsTransparency = false;
            Background = MeToolsTheme.BrSurface;
            BorderBrush = MeToolsTheme.BrBorder;
            BorderThickness = new Thickness(1);

            BuildUi();

            var wa = SystemParameters.WorkArea;
            Loaded += (s, e) =>
            {
                Left = wa.Right - Width - 16;
                Top = wa.Bottom - ActualHeight - 16;
            };
        }

        private void BuildUi()
        {
            var root = new StackPanel { Margin = new Thickness(14) };
            Content = root;

            var accentBar = new Border
            {
                Height = 3,
                Background = MeToolsTheme.BrAccent,
                Margin = new Thickness(-14, -14, -14, 12),
            };
            root.Children.Add(accentBar);

            var headerRow = new Grid();
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            root.Children.Add(headerRow);

            var header = new TextBlock
            {
                Text = S._(_isStale ? "taskpopup.reminder" : "taskpopup.new_task"),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = MeToolsTheme.BrAccent,
                Margin = new Thickness(0, 0, 0, 6),
            };
            Grid.SetColumn(header, 0);
            headerRow.Children.Add(header);

            // Deliberately separate from the action buttons, same as
            // CommentPopupWindow's own close button -- does nothing but
            // Close(), so it always works even if something else here
            // throws.
            var closeBtn = new Button
            {
                Content = "\u2715", Width = 22, Height = 22, Padding = new Thickness(0),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Foreground = MeToolsTheme.BrMuted, Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            };
            closeBtn.Click += (s, e) => Close();
            Grid.SetColumn(closeBtn, 1);
            headerRow.Children.Add(closeBtn);

            var meta = new TextBlock
            {
                Text = $"{(string.IsNullOrWhiteSpace(_task.SenderName) ? _task.SenderEmail : _task.SenderName)} \u2022 " +
                       _task.ReceivedAtUtc.ToLocalTime().ToString("MMM d"),
                FontSize = 11.5,
                Foreground = MeToolsTheme.BrMuted,
                Margin = new Thickness(0, 0, 0, 8),
            };
            root.Children.Add(meta);

            var subject = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(_task.TranslatedSubject) ? _task.OriginalSubject : _task.TranslatedSubject,
                FontSize = 12.5, FontWeight = FontWeights.SemiBold,
                Foreground = MeToolsTheme.BrText,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4),
            };
            root.Children.Add(subject);

            var summary = new TextBlock
            {
                Text = _task.Summary,
                FontSize = 12.5,
                Foreground = MeToolsTheme.BrText,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14),
            };
            root.Children.Add(summary);

            if (!string.IsNullOrEmpty(_task.AssignedTo))
            {
                root.Children.Add(new TextBlock
                {
                    Text = S._("commentpopup.assigned_to") + _task.AssignedTo,
                    FontSize = 11, FontWeight = FontWeights.SemiBold,
                    Foreground = MeToolsTheme.BrAccent,
                    Margin = new Thickness(0, -8, 0, 14),
                });
            }

            // Hidden unless something actually throws -- same reasoning
            // as CommentPopupWindow: the real exception message, not a
            // guess, if a button's action fails.
            _errorText = new TextBlock
            {
                FontSize = 10.5, Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed,
            };
            root.Children.Add(_errorText);

            _btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            root.Children.Add(_btnRow);

            if (!string.IsNullOrEmpty(_task.ReferencedElementId))
            {
                var goBtn = MakeBtn(S._("commentpopup.go_to_item"), isOutline: true, () =>
                {
                    METools.Comments.CommentsHandler.GoToElement(_task.ReferencedElementId);
                    Close();
                });
                goBtn.Margin = new Thickness(0, 0, 8, 0);
                goBtn.ToolTip = _task.ReferencedSummary;
                _btnRow.Children.Add(goBtn);
            }

            // Only offered while genuinely unassigned -- once someone
            // else has claimed it, this popup's "assign to me" would
            // just race TryClaim's own AlreadyClaimed result, so it's
            // simpler and clearer not to show the button at all.
            if (string.IsNullOrWhiteSpace(_task.AssignedTo))
            {
                var claimBtn = MakeBtn(S._("taskpopup.assign_to_me"), isOutline: true, () =>
                {
                    var result = TasksStorage.TryClaim(_task.ProjectId, _task.Id, _currentUser, out var claimedBy, out var error);
                    if (result == METools.Tasks.ClaimResult.StorageError)
                        throw new InvalidOperationException(error);
                    if (result == METools.Tasks.ClaimResult.AlreadyClaimed)
                        throw new InvalidOperationException($"Already assigned to {claimedBy}.");
                    Close();
                });
                claimBtn.Margin = new Thickness(0, 0, 8, 0);
                _btnRow.Children.Add(claimBtn);
            }

            var doneBtn = MakeBtn(S._("commentpopup.mark_done"), isOutline: false, () =>
            {
                if (!TasksStorage.MarkDone(_task.ProjectId, _task.Id, out var error))
                    throw new InvalidOperationException(error);
                Close();
            });
            _btnRow.Children.Add(doneBtn);
        }

        private Button MakeBtn(string label, bool isOutline, Action onClick)
        {
            var btn = new Button
            {
                Content = label,
                Height = 28,
                Padding = new Thickness(10, 0, 10, 0),
                FontSize = 11.5,
                Cursor = Cursors.Hand,
                Background = isOutline ? MeToolsTheme.BrBtnBg : MeToolsTheme.BrAccent,
                BorderBrush = isOutline ? MeToolsTheme.BrBtnBorder : MeToolsTheme.BrAccent,
                BorderThickness = new Thickness(1),
                Foreground = isOutline ? MeToolsTheme.BrText : MeToolsTheme.BrOnAccent,
            };
            btn.Template = MeToolsWindowBase.RoundedBtnTemplate();
            btn.Click += (s, e) =>
            {
                try
                {
                    onClick();
                }
                catch (Exception ex)
                {
                    _errorText.Text = $"{ex.GetType().Name}: {ex.Message}";
                    _errorText.Visibility = Visibility.Visible;
                }
            };
            return btn;
        }
    }
}
