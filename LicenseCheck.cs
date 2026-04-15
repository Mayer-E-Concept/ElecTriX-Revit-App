// LicenseCheck.cs - ME-Tools License Gate for Commands
// Mayer E-Concept SRL
using Autodesk.Revit.UI;

namespace METools
{
    public static class LicenseCheck
    {
        public static bool IsAllowed()  => LicenseManager.IsLicensed() || !LicenseManager.IsTrialExpired;
        public static bool IsExpired()  => LicenseManager.IsTrialExpired;
        public static bool IsValid()    => IsAllowed();
        public static bool CanRun()     => IsAllowed();

        public static bool Verify(string featureName = "ME-Tools")
        {
            if (IsAllowed()) return true;
            TaskDialog.Show(featureName,
                "The trial period has expired.\n\n" +
                "Please activate ME-Tools via the Settings button in the ribbon\n" +
                "or contact office@mayer-econcept.ro for a license key.");
            return false;
        }

        // nint overloads - Revit 2025: ElementId.IntegerValue returns nint
        public static bool IsAllowed(nint _)                  => IsAllowed();
        public static bool IsExpired(nint _)                  => IsExpired();
        public static bool IsValid(nint _)                    => IsValid();
        public static bool CanRun(nint _)                     => CanRun();
        public static bool Verify(nint _)                     => Verify();
        public static bool Verify(nint _, string featureName) => Verify(featureName);

        // int overloads - fallback compatibility
        public static bool IsAllowed(int _)                   => IsAllowed();
        public static bool IsExpired(int _)                   => IsExpired();
        public static bool IsValid(int _)                     => IsValid();
        public static bool CanRun(int _)                      => CanRun();
        public static bool Verify(int _)                      => Verify();
        public static bool Verify(int _, string featureName)  => Verify(featureName);

        public static int          DaysRemaining => LicenseManager.DaysRemaining;
        public static LicenseStatus Status       => LicenseManager.GetStatus();
    }
}
