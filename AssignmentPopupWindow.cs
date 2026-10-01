// AssignmentPopupWindow.cs -- ME-Tools | "X wants to hand you a request"
// Mayer E-Concept SRL
//
// Same toast look and position as TaskPopupWindow. Two uses:
//  - a pending hand-over to this user: Accept / Decline (optional reason),
//    answered here or in Nexus -- whichever comes first wins, the other
//    side then just says it was already answered;
//  - the answer to one of this user's own hand-overs (accepted/declined).
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace METools.Tasks
{
    public class AssignmentPopupWindow : Window
    {
        private readonly AssignmentRequest _a;
        private readonly bool _isOutcome;
        private TextBlock _errorText;
        private TextBox _reasonBox;
        private Button _declineBtn;

        public Action Answered; // e.g. the Workboard refreshes itself

        public AssignmentPopupWindow(AssignmentRequest a, bool isOutcome, ProjectTask task = null)
        {
            S.SetLanguage(SettingsStore.Language ?? "en");
            _a = a;
            _isOutcome = isOutcome;

            Title = "ME-Tools";
            Width = 340;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Background = MeToolsTheme.BrSurface;
            BorderBrush = MeToolsTheme.BrBorder;
            BorderThickness = new Thickness(1);

            BuildUi(task);

            var wa = SystemParameters.WorkArea;
            Loaded += (s, e) =>
            {
                Left = wa.Right - Width - 16;
                Top = wa.Bottom - ActualHeight - 16;
            };
        }

        private void BuildUi(ProjectTask task)
        {
            var root = new StackPanel { Margin = new Thickness(14) };
            Content = root;
            root.Children.Add(new Border { Height = 3, Background = MeToolsTheme.BrAccent, Margin = new Thickness(-14, -14, -14, 12) });

            var headerRow = new Grid();
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            root.Children.Add(headerRow);
            var header = new TextBlock
            {
                Text = S._(_isOutcome ? (_a.State == "accepted" ? "assignpopup.accepted_title" : "assignpopup.declined_title") : "assignpopup.title"),
                FontSize = 14, FontWeight = FontWeights.Bold, Foreground = MeToolsTheme.BrAccent, Margin = new Thickness(0, 0, 0, 6),
            };
            headerRow.Children.Add(header);
            var closeBtn = new Button
            {
                Content = "✕", Width = 22, Height = 22, Padding = new Thickness(0),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Foreground = MeToolsTheme.BrMuted, Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            };
            closeBtn.Click += (s, e) => Close();
            Grid.SetColumn(closeBtn, 1);
            headerRow.Children.Add(closeBtn);

            string message;
            if (!_isOutcome) message = string.Format(S._("assignpopup.message"), _a.By, _a.Subject);
            else if (_a.State == "accepted") message = string.Format(S._("assignpopup.accepted"), _a.To, _a.Subject);
            else message = string.Format(S._(string.IsNullOrWhiteSpace(_a.Reason) ? "assignpopup.declined" : "assignpopup.declined_reason"), _a.To, _a.Subject, _a.Reason);
            root.Children.Add(new TextBlock
            {
                Text = message, FontSize = 12.5, Foreground = MeToolsTheme.BrText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
            });

            if (task != null && !string.IsNullOrWhiteSpace(task.Summary))
                root.Children.Add(new TextBlock
                {
                    Text = task.Summary, FontSize = 11.5, Foreground = MeToolsTheme.BrMuted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
                });

            _errorText = new TextBlock
            {
                FontSize = 10.5, Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed,
            };
            root.Children.Add(_errorText);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            if (_isOutcome)
            {
                btnRow.Children.Add(MakeBtn("OK", false, Close));
                root.Children.Add(btnRow);
                return;
            }

            _reasonBox = new TextBox
            {
                Height = 54, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, FontSize = 12,
                Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrText, BorderBrush = MeToolsTheme.BrBorder,
                Margin = new Thickness(0, 0, 0, 10), Visibility = Visibility.Collapsed,
                ToolTip = S._("assignpopup.reason_hint"),
            };
            root.Children.Add(_reasonBox);

            _declineBtn = MakeBtn(S._("assignpopup.decline"), true, () =>
            {
                // First click shows the (optional) reason, the second declines.
                if (_reasonBox.Visibility != Visibility.Visible)
                {
                    _reasonBox.Visibility = Visibility.Visible;
                    _declineBtn.Content = S._("assignpopup.decline_send");
                    _reasonBox.Focus();
                    return;
                }
                if (!TaskAssignments.Decline(_a, _reasonBox.Text?.Trim(), out var error)) throw new InvalidOperationException(error);
                Answered?.Invoke();
                Close();
            });
            _declineBtn.Margin = new Thickness(0, 0, 8, 0);
            btnRow.Children.Add(_declineBtn);
            btnRow.Children.Add(MakeBtn(S._("assignpopup.accept"), false, () =>
            {
                if (!TaskAssignments.Accept(_a, out var error)) throw new InvalidOperationException(error);
                Answered?.Invoke();
                Close();
            }));
            root.Children.Add(btnRow);
        }

        // Starts with the reason box open -- the Workboard's "Decline..." button.
        public void ShowDeclineReason()
        {
            if (_reasonBox == null) return;
            _reasonBox.Visibility = Visibility.Visible;
            _declineBtn.Content = S._("assignpopup.decline_send");
        }

        private Button MakeBtn(string label, bool isOutline, Action onClick)
        {
            var btn = new Button
            {
                Content = label, Height = 28, Padding = new Thickness(10, 0, 10, 0), FontSize = 11.5, Cursor = Cursors.Hand,
                Background = isOutline ? MeToolsTheme.BrBtnBg : MeToolsTheme.BrAccent,
                BorderBrush = isOutline ? MeToolsTheme.BrBtnBorder : MeToolsTheme.BrAccent,
                BorderThickness = new Thickness(1),
                Foreground = isOutline ? MeToolsTheme.BrText : MeToolsTheme.BrOnAccent,
            };
            btn.Template = MeToolsWindowBase.RoundedBtnTemplate();
            btn.Click += (s, e) =>
            {
                try { onClick(); }
                catch (Exception ex)
                {
                    _errorText.Text = ex.Message;
                    _errorText.Visibility = Visibility.Visible;
                }
            };
            return btn;
        }
    }
}
