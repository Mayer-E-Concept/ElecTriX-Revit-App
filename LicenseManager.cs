// LicenseManager.cs — ME-Tools Trial License Management
// Mayer E-Concept SRL
// -----------------------------------------------------------------
// Full API compatible with LicenseCheck.cs, LicenseWindow.cs,
// SplashWindow.cs. Activation stub is ready for a real key-server.
// -----------------------------------------------------------------
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace METools.Licensing
{
    // ── LicenseStatus enum ────────────────────────────────────────────────

    public enum LicenseStatus
    {
        TrialActive,
        TrialExpired,
        Licensed,
        LicenseExpired,
        Invalid
    }

    // ── LicenseType enum ──────────────────────────────────────────────────
    // Must be an enum so that members are compile-time constants,
    // which is required for switch/case statements in LicenseWindow.cs.
    // Activate() returns LicenseType so the switch variable and case labels
    // share the same type — resolving all CS0019, CS0029 and CS9135 errors.

    public enum LicenseType
    {
        None     = 0,
        Trial    = 1,
        Full     = 2,
        Permanent= 3,
        Year1    = 4,
        Extend30 = 5
    }

    // ── LicenseManager ────────────────────────────────────────────────────

    public static class LicenseManager
    {
        // ── Configuration ─────────────────────────────────────────────────
        private const int TrialDays = 30;

        private static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "METools");

        private static readonly string LicFile = Path.Combine(DataDir, "lic.dat");
        private static readonly string KeyFile  = Path.Combine(DataDir, "key.dat");

        // Simple XOR key — prevents casual text-editor tampering
        private static readonly byte[] XorKey = { 0x4D, 0x45, 0x54, 0x6C }; // "METl"

        // ── Public API ────────────────────────────────────────────────────

        /// <summary>Returns the current license status of this installation.</summary>
        public static LicenseStatus GetStatus()
        {
            if (IsActivated())            return LicenseStatus.Licensed;
            if (TrialDaysRemaining() > 0) return LicenseStatus.TrialActive;
            return LicenseStatus.TrialExpired;
        }

        /// <summary>Whether a valid full license key has been activated on this machine.</summary>
        public static bool IsLicensed() => IsActivated();

        /// <summary>True when the trial has expired and no full license is present.</summary>
        public static bool IsTrialExpired => GetStatus() == LicenseStatus.TrialExpired;

        /// <summary>
        /// Current license type (None / Trial / Permanent / Year1 / Extend30).
        /// Called as a method by SplashWindow.cs and LicenseWindow.cs.
        /// </summary>
        public static LicenseType CurrentLicenseType()
        {
            if (!IsActivated()) return LicenseType.Trial;
            return LicenseType.Permanent; // extend once key-server returns finer-grained type
        }

        /// <summary>
        /// Days remaining in the 30-day trial period.
        /// Returns 0 when expired. Returns int.MaxValue when fully licensed.
        /// Called as a method by LicenseCheck.cs and SplashWindow.cs.
        /// </summary>
        public static int TrialDaysRemaining()
        {
            if (IsActivated()) return int.MaxValue;
            var install = GetOrCreateInstallDate();
            int elapsed  = (int)(DateTime.Today - install).TotalDays;
            return Math.Max(0, TrialDays - elapsed);
        }

        /// <summary>
        /// Days remaining on the current license (trial or time-limited full license).
        /// For a perpetual full license this returns int.MaxValue.
        /// Called as a method by SplashWindow.cs.
        /// </summary>
        public static int LicenseDaysRemaining() => TrialDaysRemaining();

        /// <summary>Short human-readable status string for display in UI.</summary>
        public static string StatusText
        {
            get
            {
                switch (GetStatus())
                {
                    case LicenseStatus.Licensed:
                        return "Licensed — Mayer E-Concept SRL";
                    case LicenseStatus.TrialActive:
                        int d = TrialDaysRemaining();
                        return $"Beta — {d} day{(d == 1 ? "" : "s")} remaining";
                    default:
                        return "Trial expired — contact Mayer E-Concept SRL";
                }
            }
        }

        /// <summary>
        /// Returns a unique machine identifier used for license activation.
        /// Based on a hash of machine name, user name, and processor count.
        /// </summary>
        public static string GetMachineId()
        {
            try
            {
                string raw = Environment.MachineName + "|"
                           + Environment.UserName    + "|"
                           + Environment.ProcessorCount;
                using (var md5 = MD5.Create())
                {
                    var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(raw));
                    var sb   = new StringBuilder();
                    for (int i = 0; i < hash.Length; i++)
                    {
                        sb.Append(hash[i].ToString("X2"));
                        if (i == 3 || i == 5 || i == 7 || i == 9) sb.Append("-");
                    }
                    return sb.ToString().Substring(0, 29);
                }
            }
            catch
            {
                return "UNKNOWN-MACHINE-ID";
            }
        }

        /// <summary>
        /// Attempts to activate the add-in with the given license key.
        /// Returns the activated LicenseType on success, LicenseType.None on failure.
        /// The returned LicenseType can be used directly in a switch/case statement.
        /// Stub: detects key prefix — replace inner logic with real key-server call.
        /// </summary>
        public static LicenseType Activate(string licenseKey)
        {
            if (string.IsNullOrWhiteSpace(licenseKey))
                return LicenseType.None;

            string key = licenseKey.Trim();

            // TODO: replace with real key-server validation
            LicenseType granted;
            if      (key.StartsWith("MTOOLS-E30", StringComparison.OrdinalIgnoreCase)) granted = LicenseType.Extend30;
            else if (key.StartsWith("MTOOLS-Y1",  StringComparison.OrdinalIgnoreCase)) granted = LicenseType.Year1;
            else if (key.StartsWith("MTOOLS-P",   StringComparison.OrdinalIgnoreCase)) granted = LicenseType.Permanent;
            else if (key.StartsWith("MTOOLS-",    StringComparison.OrdinalIgnoreCase)) granted = LicenseType.Permanent;
            else                                                                        granted = LicenseType.None;

            if (granted != LicenseType.None)
            {
                try
                {
                    Directory.CreateDirectory(DataDir);
                    File.WriteAllText(KeyFile, Obfuscate(key));
                }
                catch { }
            }

            return granted;
        }

        // ── Internal helpers ──────────────────────────────────────────────

        private static bool IsActivated()
        {
            try
            {
                if (!File.Exists(KeyFile)) return false;
                var raw = File.ReadAllText(KeyFile).Trim();
                var key = Deobfuscate(raw);
                return !string.IsNullOrWhiteSpace(key)
                    && key.StartsWith("MTOOLS-", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static DateTime GetOrCreateInstallDate()
        {
            try
            {
                if (File.Exists(LicFile))
                {
                    var raw     = File.ReadAllText(LicFile).Trim();
                    var decoded = Deobfuscate(raw);
                    if (DateTime.TryParse(decoded, out DateTime stored))
                        return stored;
                }
            }
            catch { }

            var today = DateTime.Today;
            try
            {
                Directory.CreateDirectory(DataDir);
                File.WriteAllText(LicFile, Obfuscate(today.ToString("yyyy-MM-dd")));
            }
            catch { }

            return today;
        }

        private static string Obfuscate(string plain)
        {
            var bytes = Encoding.UTF8.GetBytes(plain);
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] ^= XorKey[i % XorKey.Length];
            return Convert.ToBase64String(bytes);
        }

        private static string Deobfuscate(string encoded)
        {
            var bytes = Convert.FromBase64String(encoded);
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] ^= XorKey[i % XorKey.Length];
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
