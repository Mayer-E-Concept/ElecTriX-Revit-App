// LicenseCheck.cs — ME-Tools | License Check Integration
// Mayer E-Concept SRL
//
// Call LicenseCheck.Verify() at the beginning of every IExternalCommand.Execute()
// or in the ribbon button callback before opening any window.
//
// Example usage in App.cs or any Command:
//
//   public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
//   {
//       if (!LicenseCheck.Verify(data.Application.MainWindowHandle))
//           return Result.Cancelled;
//       // ... rest of command
//   }

using System;
using System.Windows.Interop;
using METools.Licensing;

namespace METools
{
    public static class LicenseCheck
    {
        /// <summary>
        /// Checks license status. Shows activation window if expired.
        /// Returns true if the add-in is allowed to run.
        /// </summary>
        public static bool Verify(IntPtr revitWindowHandle = default)
        {
            var status = LicenseManager.GetStatus();

            switch (status)
            {
                case LicenseStatus.Licensed:
                    return true;

                case LicenseStatus.TrialActive:
                    int days = LicenseManager.TrialDaysRemaining();
                    // Show a one-time reminder when 7 or fewer days remain
                    if (days <= 7)
                        ShowTrialReminder(days, revitWindowHandle);
                    return true;

                case LicenseStatus.TrialExpired:
                    return ShowActivationWindow(revitWindowHandle);

                default:
                    return false;
            }
        }

        private static bool ShowActivationWindow(IntPtr ownerHandle)
        {
            var win = new LicenseWindow();

            // Attach to Revit window if possible
            if (ownerHandle != IntPtr.Zero)
            {
                var helper = new WindowInteropHelper(win) { Owner = ownerHandle };
            }

            win.ShowDialog();
            return win.Activated;
        }

        private static void ShowTrialReminder(int daysLeft, IntPtr ownerHandle)
        {
            // Only show once per Revit session using a static flag
            if (_reminderShown) return;
            _reminderShown = true;

            string msg = daysLeft == 1
                ? "ME-Tools beta expires tomorrow. Contact Mayer E-Concept to get an activation code."
                : $"ME-Tools beta expires in {daysLeft} days. Contact Mayer E-Concept to get an activation code.";

            System.Windows.MessageBox.Show(
                msg,
                "ME-Tools — Beta Reminder",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }

        private static bool _reminderShown = false;
    }
}
