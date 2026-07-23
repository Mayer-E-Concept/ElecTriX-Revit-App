// CommentPopupWindow.cs -- ME-Tools | Project Comments notification toast
// Mayer E-Concept SRL
//
// A lightweight, standalone Window (not MeToolsWindowBase -- this doesn't
// need a title bar, app-switcher, or status bar, same reasoning as Dialog.cs's
// DistDialog) that appears in the bottom-right corner of the screen when
// CommentsWatcher finds a new comment relevant to the current user.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace METools.Comments
{
    public class CommentPopupWindow : Window
    {
        private readonly ProjectComment _comment;

        public CommentPopupWindow(ProjectComment comment)
        {
            _comment = comment;

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

            // Bottom-right corner of the primary screen's work area, with a
            // small margin -- the classic "toast notification" position.
            var wa = SystemParameters.WorkArea;
            Loaded += (s, e) =>
            {
                Left = wa.Right - Width - 16;
                Top = wa.Bottom - ActualHeight - 16;
            };
        }

        private TextBlock _errorText;

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
                Text = "New comment",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = MeToolsTheme.BrAccent,
                Margin = new Thickness(0, 0, 0, 6),
            };
            Grid.SetColumn(header, 0);
            headerRow.Children.Add(header);

            // Deliberately separate from the action buttons below and does
            // nothing but Close() -- no ExternalEvent, no Comments logic --
            // so this always works as a way out even if something else here
            // is broken.
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

            var meta = new TextBlock
            {
                Text = $"{_comment.Author}  •  " +
                       (string.IsNullOrWhiteSpace(_comment.ScopeBoxName)
                           ? _comment.LevelName
                           : $"{_comment.LevelName} ({_comment.ScopeBoxName})"),
                FontSize = 11.5,
                Foreground = MeToolsTheme.BrMuted,
                Margin = new Thickness(0, 0, 0, 8),
            };
            root.Children.Add(meta);

            var text = new TextBlock
            {
                Text = _comment.Text,
                FontSize = 12.5,
                Foreground = MeToolsTheme.BrText,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14),
            };
            root.Children.Add(text);

            if (!string.IsNullOrEmpty(_comment.AssignedTo))
            {
                root.Children.Add(new TextBlock
                {
                    Text = "Assigned to " + _comment.AssignedTo,
                    FontSize = 11, FontWeight = FontWeights.SemiBold,
                    Foreground = MeToolsTheme.BrPetrol,
                    Margin = new Thickness(0, -8, 0, 14),
                });
            }

            // Hidden unless something actually throws -- if it appears, the
            // text is the real exception message, not a guess.
            _errorText = new TextBlock
            {
                FontSize = 10.5, Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed,
            };
            root.Children.Add(_errorText);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            root.Children.Add(btnRow);

            var goBtn = MakeBtn("Go There", isOutline: true, () =>
            {
                CommentsHandler.JumpToLevel(_comment.LevelName, _comment.ScopeBoxName);
                Close();
            });
            goBtn.Margin = new Thickness(0, 0, 8, 0);
            btnRow.Children.Add(goBtn);

            if (!string.IsNullOrEmpty(_comment.ReferencedElementId))
            {
                var goItemBtn = MakeBtn("Go to Item", isOutline: true, () =>
                {
                    CommentsHandler.GoToElement(_comment.ReferencedElementId);
                    Close();
                });
                goItemBtn.Margin = new Thickness(0, 0, 8, 0);
                goItemBtn.ToolTip = _comment.ReferencedSummary;
                btnRow.Children.Add(goItemBtn);
            }

            var ignoreBtn = MakeBtn("Ignore", isOutline: true, () =>
            {
                CommentsHandler.MarkStatus(_comment.Id, CommentStatus.Ignored);
                Close();
            });
            ignoreBtn.Margin = new Thickness(0, 0, 8, 0);
            btnRow.Children.Add(ignoreBtn);

            var doneBtn = MakeBtn("Mark Done", isOutline: false, () =>
            {
                CommentsHandler.MarkStatus(_comment.Id, CommentStatus.Done);
                Close();
            });
            btnRow.Children.Add(doneBtn);
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
                    // Surface the real error instead of leaving the person
                    // staring at a button that appears to do nothing.
                    _errorText.Text = $"{ex.GetType().Name}: {ex.Message}";
                    _errorText.Visibility = Visibility.Visible;
                }
            };
            return btn;
        }
    }
}
