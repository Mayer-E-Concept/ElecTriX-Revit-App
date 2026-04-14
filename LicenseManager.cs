// LicenseManager.cs — ME-Tools License Management
// Mayer E-Concept SRL
using System;
using System.IO;
using System.Net.NetworkInformation;
using System.Text;

namespace METools.Licensing
{
    public enum LicenseStatus
    {
        Trial, TrialActive, TrialExpired, Licensed, LicenseExpired, Expired, Invalid
    }

    public enum LicenseType
    {
        None = 0, Trial = 1, Full = 2, Permanent = 3, Year1 = 4, Extend30 = 5, Subscription = 6
    }

    internal static class LicenseManager
    {
        private const int TrialDays = 30;

        private static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "METools");
        private static readonly string LicFile  = Path.Combine(DataDir, "lic.dat");
        private static readonly string KeyFile  = Path.Combine(DataDir, "key.dat");
        private static readonly byte[] XorKey   = { 0x4D, 0x45, 0x54, 0x6C };

        private static bool? _activatedThisSession;

        public static bool IsLicensed()
        {
            if (_activatedThisSession == true) return true;
            var saved = LoadSavedKey();
            if (string.IsNullOrWhiteSpace(saved)) return false;
            return ValidateKey(saved);
        }

        public static bool IsTrialExpired => !IsLicensed() && DaysRemaining <= 0;

        public static int DaysRemaining
        {
            get
            {
                if (IsLicensed()) return int.MaxValue;
                int elapsed = (int)(DateTime.Today - GetOrCreateInstallDate()).TotalDays;
                return Math.Max(0, TrialDays - elapsed);
            }
        }

        public static string StatusText
        {
            get
            {
                if (IsLicensed()) return "Licensed ✓";
                int d = DaysRemaining;
                return d > 0 ? $"Beta — {d} day{(d == 1 ? "" : "s")} remaining"
                             : "Trial expired — please activate";
            }
        }

        // ── API for LicenseCheck.cs ───────────────────────────────────────────
        public static LicenseStatus GetStatus()
        {
            if (IsLicensed())   return LicenseStatus.Licensed;
            if (IsTrialExpired) return LicenseStatus.TrialExpired;
            return LicenseStatus.TrialActive;
        }

        public static int LicenseDaysRemaining() => DaysRemaining;
        public static int TrialDaysRemaining()   => DaysRemaining;

        // ── API for LicenseWindow.cs ──────────────────────────────────────────
        public static LicenseType Activate(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return LicenseType.None;
            var k = key.Trim().ToUpperInvariant();
            if (!ValidateKey(k)) return LicenseType.None;
            SaveKey(k);
            _activatedThisSession = true;
            return LicenseType.Permanent;
        }

        public static bool TryActivate(string key) => Activate(key) != LicenseType.None;

        public static string GetMachineId()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (ni.OperationalStatus    != OperationalStatus.Up)          continue;
                    var mac = ni.GetPhysicalAddress().ToString();
                    if (!string.IsNullOrEmpty(mac) && mac != "000000000000")
                    {
                        if (mac.Length == 12)
                            mac = string.Join("-",
                                System.Text.RegularExpressions.Regex.Matches(mac, ".."));
                        return mac;
                    }
                }
            }
            catch { }
            return "UNKNOWN-MACHINE";
        }

        public static void Deactivate()
        {
            _activatedThisSession = null;
            try { if (File.Exists(KeyFile)) File.Delete(KeyFile); } catch { }
        }

        public static string SavedKey => LoadSavedKey() ?? "";

        private static bool ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            var k = key.Trim().ToUpperInvariant();
            var parts = k.Split('-');
            if (parts.Length != 4 || parts[0] != "METL") return false;
            foreach (var p in parts) if (p.Length != 4) return false;
            int sum = 0;
            foreach (char c in k.Replace("-", "")) sum += c;
            return sum % 7 == 0;
        }

        private static void SaveKey(string key)
        {
            try { Directory.CreateDirectory(DataDir); File.WriteAllText(KeyFile, Obfuscate(key)); } catch { }
        }

        private static string LoadSavedKey()
        {
            try { return File.Exists(KeyFile) ? Deobfuscate(File.ReadAllText(KeyFile).Trim()) : null; }
            catch { return null; }
        }

        private static DateTime GetOrCreateInstallDate()
        {
            try
            {
                if (File.Exists(LicFile))
                {
                    var raw = File.ReadAllText(LicFile).Trim();
                    if (DateTime.TryParse(Deobfuscate(raw), out DateTime stored)) return stored;
                }
            }
            catch { }
            var today = DateTime.Today;
            try { Directory.CreateDirectory(DataDir); File.WriteAllText(LicFile, Obfuscate(today.ToString("yyyy-MM-dd"))); } catch { }
            return today;
        }

        private static string Obfuscate(string plain)
        {
            var b = Encoding.UTF8.GetBytes(plain);
            for (int i = 0; i < b.Length; i++) b[i] ^= XorKey[i % XorKey.Length];
            return Convert.ToBase64String(b);
        }

        private static string Deobfuscate(string encoded)
        {
            var b = Convert.FromBase64String(encoded);
            for (int i = 0; i < b.Length; i++) b[i] ^= XorKey[i % XorKey.Length];
            return Encoding.UTF8.GetString(b);
        }
    }
}
