// FixLevelWindow.cs -- ME-Tools | Fix Level UI
// Mayer E-Concept SRL
using Autodesk.Revit.UI;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace METools
{
    public class FixLevelWindow : MeToolsWindowBase
    {
        private readonly ExternalEvent   _ev;
        private readonly FixLevelHandler _h;
        private readonly string          _activeLevel;

        private CheckBox    _cbSockets, _cbSwitches, _cbLamps, _cbSkipWall;
        private RadioButton _rbView, _rbStorey, _rbModel;
        private StackPanel  _body;

        public FixLevelWindow(ExternalEvent ev, FixLevelHandler handler, string activeLevel)
        {
            _ev = ev;
            _h  = handler;
            _activeLevel = activeLevel ?? "";

            _h.OnDone = msg => Dispatcher.Invoke(() => StatusLeft.Text = msg);

            S.SetLanguage(SettingsStore.Language ?? "en");
            InitWindow(S.Get("fixlevel.title"), 420);
            Build();
        }

        private void Build()
        {
            BuildStatusBar(string.IsNullOrEmpty(_activeLevel) ? "" : "Active level: " + _activeLevel);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 720, Background = MeToolsTheme.BrBg,
            };
            _body = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
            scroll.Content = _body;
            var _fxGrid = new System.Windows.Controls.Grid();
            _fxGrid.Children.Add(scroll);
            _fxGrid.Children.Add(Watermark());
            RootDock.Children.Add(_fxGrid);

            _body.Children.Add(new TextBlock
            {
                Text = S.Get("fixlevel.heading"), FontSize = 15, FontWeight = FontWeights.Bold,
                Foreground = MeToolsTheme.BrText, Margin = new Thickness(0, 0, 0, 2),
            });
            _body.Children.Add(new TextBlock
            {
                Text = S.Get("fixlevel.description"),
                FontSize = 11, Foreground = MeToolsTheme.BrMuted, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
            });

            // ── Limitation notice ────────────────────────────────────────────
            var noticeBox = new Border
            {
                Background       = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 248, 220)),
                BorderBrush      = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 160, 0)),
                BorderThickness  = new Thickness(1),
                CornerRadius     = new CornerRadius(4),
                Padding          = new Thickness(10, 8, 10, 8),
                Margin           = new Thickness(0, 0, 0, 14),
            };
            noticeBox.Child = new TextBlock
            {
                Text = S.Get("fixlevel.notice"),
                FontSize = 10, TextWrapping = TextWrapping.Wrap,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 70, 0)),
            };
            _body.Children.Add(noticeBox);

            _body.Children.Add(Section(S.Get("fixlevel.categories")));
            _cbSockets  = Check(S.Get("fixlevel.sockets"), true);
            _cbSwitches = Check(S.Get("fixlevel.switches"), true);
            _cbLamps    = Check(S.Get("fixlevel.lamps"), true);
            _body.Children.Add(_cbSockets);
            _body.Children.Add(_cbSwitches);
            _body.Children.Add(_cbLamps);

            _cbSkipWall = Check(S.Get("fixlevel.skip_wall"), true);
            _cbSkipWall.Margin = new Thickness(22, 2, 0, 8);
            _body.Children.Add(_cbSkipWall);

            _body.Children.Add(Section(S.Get("fixlevel.scope")));
            _rbView   = Radio(S.Get("fixlevel.active_view"), true);
            _rbStorey = Radio(S.Get("fixlevel.current_storey"), false);
            _rbModel  = Radio(S.Get("fixlevel.whole_model"), false);
            _body.Children.Add(_rbView);
            _body.Children.Add(_rbStorey);
            _body.Children.Add(_rbModel);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };

            var preview = new Button
            {
                Content = S.Get("fixlevel.preview_btn"), Height = 34, FontSize = 12,
                Padding = new Thickness(16, 0, 16, 0),
                Margin = new Thickness(0, 0, 8, 0),
                Background = MeToolsTheme.BrSurface, Foreground = MeToolsTheme.BrText,
                BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
                ToolTip = "Count how many elements would be moved without making any changes",
            };
            preview.Click += (s, e) => RunFix(dryRun: true);
            btnRow.Children.Add(preview);

            var run = new Button
            {
                Content = S.Get("fixlevel.fix_btn"), Height = 34, FontSize = 13, FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(24, 0, 24, 0),
                Background = MeToolsTheme.BrPetrol, Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
            };
            run.Click += (s, e) => RunFix(dryRun: false);
            btnRow.Children.Add(run);
            _body.Children.Add(btnRow);
        }

        private void RunFix(bool dryRun = false)
        {
            _h.Request = new FixLevelRequest
            {
                DryRun       = dryRun,
                Sockets       = _cbSockets.IsChecked == true,
                Switches      = _cbSwitches.IsChecked == true,
                Lamps         = _cbLamps.IsChecked == true,
                SkipWallLamps = _cbSkipWall.IsChecked == true,
                Scope         = _rbStorey.IsChecked == true ? FixLevelScope.Storey
                              : _rbModel.IsChecked  == true ? FixLevelScope.WholeModel
                                                            : FixLevelScope.ActiveView,
            };
            if (!_h.Request.Sockets && !_h.Request.Switches && !_h.Request.Lamps)
            { StatusLeft.Text = "Select at least one category."; return; }

            StatusLeft.Text = "Fixing levels...";
            _ev.Raise();
        }

        private TextBlock Section(string t) => new TextBlock
        {
            Text = t, FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = MeToolsTheme.BrSecText, Margin = new Thickness(0, 12, 0, 6),
        };

        private CheckBox Check(string label, bool isChecked) => new CheckBox
        {
            Content = label, IsChecked = isChecked, FontSize = 12,
            Foreground = MeToolsTheme.BrText, Margin = new Thickness(0, 3, 0, 3),
        };

        private RadioButton Radio(string label, bool isChecked) => new RadioButton
        {
            Content = label, IsChecked = isChecked, GroupName = "scope", FontSize = 12,
            Foreground = MeToolsTheme.BrText, Margin = new Thickness(0, 3, 0, 3),
        };
    }
}
