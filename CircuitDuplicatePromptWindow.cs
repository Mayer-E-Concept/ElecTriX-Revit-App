// CircuitDuplicatePromptWindow.cs -- ME-Tools | Circuit Tagger duplicate-apartment reassign
// Mayer E-Concept SRL
//
// A small, one-off prompt -- not a persistent tool window, so this doesn't
// use the full MeToolsWindowBase chrome/app-switcher machinery, the same way
// Dialog.cs's DistDialog doesn't either. Native window chrome, just themed.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace METools.CircuitDuplicate
{
    public class CircuitDuplicatePromptWindow : Window
    {
        public Action<string, string> OnApply;

        private TextBox _tbBuilding, _tbApartment;

        public CircuitDuplicatePromptWindow(string oldBuilding, string oldApartment, int elementCount)
        {
            S.SetLanguage(SettingsStore.Language ?? "en");
            Title = S._("circdup.title");
            Width = 380; SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = MeToolsTheme.BrBg;
            FontFamily = new FontFamily("Segoe UI");
            Topmost = true; // triggered from a background event -- must not get lost behind Revit

            if (MeToolsWindowBase.RevitHandle != IntPtr.Zero)
                try { new WindowInteropHelper(this).Owner = MeToolsWindowBase.RevitHandle; } catch { }

            BuildUi(oldBuilding, oldApartment, elementCount);
        }

        private void BuildUi(string oldBuilding, string oldApartment, int count)
        {
            var sp = new StackPanel { Margin = new Thickness(16) };
            Content = sp;

            sp.Children.Add(new TextBlock
            {
                Text = string.Format(S._(count == 1 ? "circdup.detected_1" : "circdup.detected_n"), count, oldBuilding, oldApartment),
                FontSize = 12, Foreground = MeToolsTheme.BrText, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4),
            });
            sp.Children.Add(new TextBlock
            {
                Text = S._("circdup.hint"),
                FontSize = 11, Foreground = MeToolsTheme.BrMuted, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14),
            });

            sp.Children.Add(Label(S._("circdup.house")));
            _tbBuilding = Field(oldBuilding);
            sp.Children.Add(_tbBuilding);

            sp.Children.Add(Label(S._("circdup.apartment")));
            _tbApartment = Field(oldApartment);
            sp.Children.Add(_tbApartment);

            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0),
            };

            var skipBtn = new Button
            {
                Content = S._("circdup.skip"), Height = 30, Padding = new Thickness(14, 0, 14, 0),
                Background = MeToolsTheme.BrBtnBg, Foreground = MeToolsTheme.BrText,
                BorderBrush = MeToolsTheme.BrBtnBorder, BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
            };
            skipBtn.Click += (s, e) => Close();

            var applyBtn = new Button
            {
                Content = S._("circdup.apply"), Height = 30, Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(8, 0, 0, 0), FontWeight = FontWeights.SemiBold,
                Background = MeToolsTheme.BrAccent, Foreground = MeToolsTheme.BrOnAccent,
                BorderBrush = MeToolsTheme.BrAccent, BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
            };
            applyBtn.Click += (s, e) =>
            {
                OnApply?.Invoke(_tbBuilding.Text?.Trim() ?? "", _tbApartment.Text?.Trim() ?? "");
                Close();
            };

            btnRow.Children.Add(skipBtn);
            btnRow.Children.Add(applyBtn);
            sp.Children.Add(btnRow);
        }

        private TextBlock Label(string t) => new TextBlock
        {
            Text = t, FontSize = 11, Foreground = MeToolsTheme.BrMuted, Margin = new Thickness(0, 0, 0, 3),
        };

        private TextBox Field(string text) => new TextBox
        {
            Text = text ?? "", Height = 30, FontSize = 13, Margin = new Thickness(0, 0, 0, 12),
            Background = MeToolsTheme.BrInput, Foreground = MeToolsTheme.BrText, CaretBrush = MeToolsTheme.BrText,
            BorderBrush = MeToolsTheme.BrBorder, BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center,
        };
    }
}
