// LicenseManager.cs — ME-Tools | License System
// Mayer E-Concept SRL
// No external NuGet packages required — uses only built-in Windows/registry APIs.

using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace METools.Licensing
{
    public enum LicenseStatus
    {
        TrialActive,
        TrialExpired,
        Licensed
    }

    public static class LicenseManager
    {
        private const int    BetaDays    = 30;
        private const string RegPath    = @"SOFTWARE\METools\Revit";
        private const string RegKeyDate = "InstallRef";
        private const string RegKeyCode = "LicenseRef";

        // IMPORTANT: Change this before distributing. Keep it private — never share.
        private const string HmacSecret = "ME-TOOLS-2025-PRIVATE-SECRET-CHANGE-THIS";

        public static LicenseStatus GetStatus()
        {
            if (IsActivated()) return LicenseStatus.Licensed;
            DateTime installDate = GetOrCreateInstallDate();
            int daysUsed = (int)(DateTime.UtcNow - installDate).TotalDays;
            return daysUsed < BetaDays ? LicenseStatus.TrialActive : LicenseStatus.TrialExpired;
        }

        public static int TrialDaysRemaining()
        {
            if (IsActivated()) return 0;
            DateTime installDate = GetOrCreateInstallDate();
            int used = (int)(DateTime.UtcNow - installDate).TotalDays;
            return Math.Max(0, BetaDays - used);
        }

        /// <summary>
        /// Unique machine ID — uses only registry and environment (no WMI/System.Management).
        /// Format: XXXXX-XXXXX-XXXXX-XXXXX
        /// </summary>
        public static string GetMachineId()
        {
            string raw = GetMachineGuid()
                       + "|" + Environment.MachineName
                       + "|" + Environment.ProcessorCount
                       + "|" + GetOsInstallId();

            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
                string hex  = BitConverter.ToString(hash).Replace("-", "").Substring(0, 20).ToUpper();
                return $"{hex.Substring(0,5)}-{hex.Substring(5,5)}-{hex.Substring(10,5)}-{hex.Substring(15,5)}";
            }
        }

        public static bool Activate(string code)
        {
            string expected = GenerateCode(GetMachineId());
            if (!string.Equals(code.Trim().ToUpper(), expected, StringComparison.Ordinal))
                return false;
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RegPath))
                    key?.SetValue(RegKeyCode, Obfuscate(code.Trim().ToUpper()));
                return true;
            }
            catch { return false; }
        }

        public static string GenerateCode(string machineId)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(HmacSecret)))
            {
                byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(machineId + "LICENSED"));
                string hex  = BitConverter.ToString(hash).Replace("-", "").Substring(0, 25).ToUpper();
                return $"{hex.Substring(0,5)}-{hex.Substring(5,5)}-{hex.Substring(10,5)}-{hex.Substring(15,5)}-{hex.Substring(20,5)}";
            }
        }

        // ── Internal ──────────────────────────────────────────────────────────

        private static bool IsActivated()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegPath))
                {
                    if (key == null) return false;
                    string stored = key.GetValue(RegKeyCode) as string;
                    if (string.IsNullOrEmpty(stored)) return false;
                    string code     = Deobfuscate(stored);
                    string expected = GenerateCode(GetMachineId());
                    return string.Equals(code, expected, StringComparison.Ordinal);
                }
            }
            catch { return false; }
        }

        private static DateTime GetOrCreateInstallDate()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RegPath))
                {
                    string stored = key?.GetValue(RegKeyDate) as string;
                    if (!string.IsNullOrEmpty(stored))
                    {
                        string raw = Deobfuscate(stored);
                        if (DateTime.TryParse(raw, out DateTime dt)) return dt;
                    }
                    DateTime now = DateTime.UtcNow;
                    key?.SetValue(RegKeyDate, Obfuscate(now.ToString("o")));
                    return now;
                }
            }
            catch { return DateTime.UtcNow; }
        }

        // ── Hardware ID — registry + environment only (no WMI) ───────────────

        private static string GetMachineGuid()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                    return key?.GetValue("MachineGuid") as string ?? "GUID";
            }
            catch { return "GUID"; }
        }

        private static string GetOsInstallId()
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
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            for (int i = 0; i < bytes.Length; i++) bytes[i] ^= 0x5A;
            return Convert.ToBase64String(bytes);
        }

        private static string Deobfuscate(string value)
        {
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
