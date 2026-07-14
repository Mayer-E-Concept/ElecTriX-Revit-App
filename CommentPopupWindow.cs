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

            var header = new TextBlock
            {
                Text = "New comment",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = MeToolsTheme.BrAccent,
                Margin = new Thickness(0, 0, 0, 6),
            };
            root.Children.Add(header);

            var meta = new TextBlock
            {
                Text = $"{_comment.Author}  •  {_comment.LevelName}",
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

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            root.Children.Add(btnRow);

            var goBtn = MakeBtn("Go There", isOutline: true, () =>
            {
                CommentsHandler.JumpToLevel(_comment.LevelName);
                Close();
            });
            goBtn.Margin = new Thickness(0, 0, 8, 0);
            btnRow.Children.Add(goBtn);

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
            btn.Click += (s, e) => onClick();
            return btn;
        }
    }
}
