// LicenseManager.cs — ME-Tools | License System
// Mayer E-Concept SRL
// No external NuGet packages — uses only built-in Windows/registry APIs.
//
// License types:
//   Trial      — 30 days from first run (automatic)
//   Extend30   — 30-day extension from activation date
//   Year1      — 1 year from activation date
//   Permanent  — unlimited
//
// Code generation:  HMAC-SHA256(secret, machineId + licenseType)
// Machine ID:       SHA256(MachineGuid + MachineName + ProcessorCount + OsInstallDate)

using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace METools.Licensing
{
    public enum LicenseStatus
    {
        TrialActive,    // Within trial period
        TrialExpired,   // Trial over, no valid license
        Licensed,       // Valid license (30d / 1y / permanent)
        LicenseExpired  // Time-limited license has expired
    }

    public enum LicenseType
    {
        None,
        Extend30,   // 30-day extension
        Year1,      // 1-year license
        Permanent   // Unlimited
    }

    public static class LicenseManager
    {
        // ── Configuration ────────────────────────────────────────────────────
        private const int    TrialDays   = 30;
        private const string RegPath     = @"SOFTWARE\METools\Revit";
        private const string RegInstall  = "InstallRef";    // trial start date
        private const string RegLicType  = "LicTypeRef";   // license type
        private const string RegLicExp   = "LicExpRef";    // expiry date
        private const string RegLicCode  = "LicCodeRef";   // stored code

        // IMPORTANT: Change before distributing. Keep private, never share.
        private const string HmacSecret  = "ME-TOOLS-2025-PRIVATE-SECRET-CHANGE-THIS";

        // ── Public API ───────────────────────────────────────────────────────

        public static LicenseStatus GetStatus()
        {
            // 1. Check stored license
            var (type, expiry) = ReadStoredLicense();
            if (type == LicenseType.Permanent) return LicenseStatus.Licensed;
            if (type == LicenseType.Year1 || type == LicenseType.Extend30)
            {
                if (expiry.HasValue && expiry.Value > DateTime.UtcNow)
                    return LicenseStatus.Licensed;
                if (expiry.HasValue)
                    return LicenseStatus.LicenseExpired;
            }

            // 2. Check trial
            DateTime installDate = GetOrCreateInstallDate();
            int daysUsed = (int)(DateTime.UtcNow - installDate).TotalDays;
            return daysUsed < TrialDays ? LicenseStatus.TrialActive : LicenseStatus.TrialExpired;
        }

        public static int TrialDaysRemaining()
        {
            var status = GetStatus();
            if (status == LicenseStatus.Licensed) return 0;
            DateTime installDate = GetOrCreateInstallDate();
            int used = (int)(DateTime.UtcNow - installDate).TotalDays;
            return Math.Max(0, TrialDays - used);
        }

        /// <summary>Days remaining on a time-limited license (0 = permanent or expired)</summary>
        public static int LicenseDaysRemaining()
        {
            var (type, expiry) = ReadStoredLicense();
            if (type == LicenseType.Permanent) return 9999;
            if (expiry.HasValue && expiry.Value > DateTime.UtcNow)
                return (int)(expiry.Value - DateTime.UtcNow).TotalDays;
            return 0;
        }

        public static LicenseType CurrentLicenseType()
        {
            var (type, _) = ReadStoredLicense();
            return type;
        }

        /// <summary>Machine ID shown to customer (format: XXXXX-XXXXX-XXXXX-XXXXX)</summary>
        public static string GetMachineId()
        {
            string raw = GetMachineGuid()
                       + "|" + Environment.MachineName
                       + "|" + Environment.ProcessorCount
                       + "|" + GetOsInstallDate();

            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
                string hex  = BitConverter.ToString(hash).Replace("-", "").Substring(0, 20).ToUpper();
                return $"{hex.Substring(0,5)}-{hex.Substring(5,5)}-{hex.Substring(10,5)}-{hex.Substring(15,5)}";
            }
        }

        /// <summary>
        /// Validate and activate a license code.
        /// Returns the LicenseType if valid, None if invalid.
        /// </summary>
        public static LicenseType Activate(string code)
        {
            string clean = code.Trim().ToUpper();
            string machineId = GetMachineId();

            // Try all license types
            foreach (LicenseType type in new[] { LicenseType.Permanent, LicenseType.Year1, LicenseType.Extend30 })
            {
                if (string.Equals(clean, GenerateCode(machineId, type), StringComparison.Ordinal))
                {
                    SaveLicense(type, clean);
                    return type;
                }
            }
            return LicenseType.None;
        }

        /// <summary>
        /// Generate an activation code for a machine ID and license type.
        /// Call this in KeyGenerator.html (your private tool).
        /// </summary>
        public static string GenerateCode(string machineId, LicenseType type)
        {
            string suffix = type switch
            {
                LicenseType.Permanent => "PERM",
                LicenseType.Year1     => "1YR",
                LicenseType.Extend30  => "30D",
                _                     => "NONE"
            };

            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(HmacSecret)))
            {
                byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(machineId + suffix));
                string hex  = BitConverter.ToString(hash).Replace("-", "").Substring(0, 25).ToUpper();
                return $"{hex.Substring(0,5)}-{hex.Substring(5,5)}-{hex.Substring(10,5)}-{hex.Substring(15,5)}-{hex.Substring(20,5)}";
            }
        }

        // ── Internal ─────────────────────────────────────────────────────────

        private static void SaveLicense(LicenseType type, string code)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RegPath))
                {
                    if (key == null) return;
                    key.SetValue(RegLicType, Obfuscate(type.ToString()));
                    key.SetValue(RegLicCode, Obfuscate(code));

                    DateTime expiry = type switch
                    {
                        LicenseType.Year1    => DateTime.UtcNow.AddYears(1),
                        LicenseType.Extend30 => DateTime.UtcNow.AddDays(30),
                        _                    => DateTime.MaxValue
                    };
                    key.SetValue(RegLicExp, Obfuscate(expiry.ToString("o")));
                }
            }
            catch { }
        }

        private static (LicenseType type, DateTime? expiry) ReadStoredLicense()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegPath))
                {
                    if (key == null) return (LicenseType.None, null);

                    string typeStr = Deobfuscate(key.GetValue(RegLicType) as string ?? "");
                    string codeStr = Deobfuscate(key.GetValue(RegLicCode) as string ?? "");
                    string expStr  = Deobfuscate(key.GetValue(RegLicExp) as string ?? "");

                    if (!Enum.TryParse<LicenseType>(typeStr, out var type) || type == LicenseType.None)
                        return (LicenseType.None, null);

                    // Verify code still matches this machine
                    string expected = GenerateCode(GetMachineId(), type);
                    if (!string.Equals(codeStr, expected, StringComparison.Ordinal))
                        return (LicenseType.None, null);

                    DateTime? expiry = null;
                    if (DateTime.TryParse(expStr, out DateTime exp))
                        expiry = exp;

                    return (type, expiry);
                }
            }
            catch { return (LicenseType.None, null); }
        }

        private static DateTime GetOrCreateInstallDate()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RegPath))
                {
                    string stored = key?.GetValue(RegInstall) as string;
                    if (!string.IsNullOrEmpty(stored))
                    {
                        string raw = Deobfuscate(stored);
                        if (DateTime.TryParse(raw, out DateTime dt)) return dt;
                    }
                    DateTime now = DateTime.UtcNow;
                    key?.SetValue(RegInstall, Obfuscate(now.ToString("o")));
                    return now;
                }
            }
            catch { return DateTime.UtcNow; }
        }

        // ── Hardware ID ───────────────────────────────────────────────────────
        private static string GetMachineGuid()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                    return key?.GetValue("MachineGuid") as string ?? "GUID";
            }
            catch { return "GUID"; }
        }

        private static string GetOsInstallDate()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    return key?.GetValue("InstallDate")?.ToString() ?? "OS";
            }
            catch { return "OS"; }
        }

        // ── Obfuscation ───────────────────────────────────────────────────────
        private static string Obfuscate(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            for (int i = 0; i < bytes.Length; i++) bytes[i] ^= 0x5A;
            return Convert.ToBase64String(bytes);
        }

        private static string Deobfuscate(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            try
            {
                byte[] bytes = Convert.FromBase64String(value);
                for (int i = 0; i < bytes.Length; i++) bytes[i] ^= 0x5A;
                return Encoding.UTF8.GetString(bytes);
            }
            catch { return ""; }
        }
    }
}
