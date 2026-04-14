// LicenseCheck.cs — ME-Tools | License Gate
// Mayer E-Concept SRL
using System;
using System.Windows;
using System.Windows.Interop;
using METools.Licensing;

namespace METools
{
    public static class LicenseCheck
    {
        private static bool _reminderShown = false;

        /// <summary>
        /// Call at the start of every command Execute().
        /// Returns true = allowed to run. False = blocked (trial expired / license expired).
        /// </summary>
        public static bool Verify(IntPtr ownerHandle = default)
        {
            var status = LicenseManager.GetStatus();

            switch (status)
            {
                case LicenseStatus.Licensed:
                    // Show reminder if time-limited license expires soon (≤ 7 days)
                    int licDays = LicenseManager.LicenseDaysRemaining();
                    if (licDays < 9999 && licDays <= 7 && !_reminderShown)
                    {
                        _reminderShown = true;
                        string msg = licDays <= 1
                            ? "Your ME-Tools license expires tomorrow. Contact Mayer E-Concept SRL to renew."
                            : $"Your ME-Tools license expires in {licDays} days. Contact Mayer E-Concept SRL to renew.";
                        MessageBox.Show(msg, "ME-Tools — License Expiring Soon",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    return true;

                case LicenseStatus.TrialActive:
                    int trialDays = LicenseManager.TrialDaysRemaining();
                    if (trialDays <= 7 && !_reminderShown)
                    {
                        _reminderShown = true;
                        string msg = trialDays <= 1
                            ? "ME-Tools beta expires tomorrow. Contact Mayer E-Concept SRL for a license."
                            : $"ME-Tools beta expires in {trialDays} days. Contact Mayer E-Concept SRL for a license.";
                        MessageBox.Show(msg, "ME-Tools — Beta Expiring Soon",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    return true;

                case LicenseStatus.TrialExpired:
                case LicenseStatus.LicenseExpired:
                    return ShowActivationWindow(ownerHandle);

                default:
                    return false;
            }
        }

        private static bool ShowActivationWindow(IntPtr ownerHandle)
        {
            var win = new LicenseWindow();
            if (ownerHandle != IntPtr.Zero)
                new WindowInteropHelper(win) { Owner = ownerHandle };
            win.ShowDialog();
            return win.Activated;
        }
    }
}
