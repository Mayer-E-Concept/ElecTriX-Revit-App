// ThemeToggleCommand.cs — ME-Tools Theme Toggle
// Mayer E-Concept SRL
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace METools
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ThemeToggleCommand : IExternalCommand
    {
        public static PushButton RibbonButton { get; set; }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            MeToolsTheme.Toggle();
            UpdateButton();
            return Result.Succeeded;
        }

        public static void UpdateButton()
        {
            if (RibbonButton == null) return;
            bool isDark = MeToolsTheme.Current == MeTheme.Dark;

            RibbonButton.ItemText   = isDark ? "Dark\nMode"  : "Light\nMode";
            RibbonButton.ToolTip    = isDark
                ? "Currently: Dark Mode active — click to switch to Light Mode."
                : "Currently: Light Mode active — click to switch to Dark Mode.";
            RibbonButton.Image      = LoadIcon(isDark ? "icon_theme_dark_16.png"  : "icon_theme_light_16.png");
            RibbonButton.LargeImage = LoadIcon(isDark ? "icon_theme_dark_32.png"  : "icon_theme_light_32.png");
        }

        private static System.Windows.Media.ImageSource LoadIcon(string fileName)
        {
            try
            {
                var stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream($"METools.Icons.{fileName}");
                if (stream == null) return null;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = stream;
                bmp.CacheOption  = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }
    }
}
