// LicenseManager.cs — ME-Tools License Management
// Mayer E-Concept SRL
// -----------------------------------------------------------------
// Handles 30-day beta trial enforcement and license activation.
// Architecture is open for a future full license-key system:
// implement IsLicensed() to return true for valid keys.
// -----------------------------------------------------------------
using System;
using System.IO;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;

namespace METools
{
    // ── License status (overall state of the installation) ────────────────
    public enum LicenseStatus
    {
        Active,           // Trial or license is currently valid
        TrialExpired,     // 30-day beta period ended, no license present
        LicenseExpired    // A timed license was activated but has since expired
    }

    // ── License type returned by Activate() ───────────────────────────────
    public enum LicenseType
    {
        None,       // Activation failed — code invalid or already used
        Extend30,   // 30-day extension code
        Year1,      // 1-year license code
        Permanent   // Perpetual license code
    }

    internal static class LicenseManager
    {
        // ── Configuration ─────────────────────────────────────────────────
        private const int TrialDays = 30;

        private static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "METools");

        private static readonly string LicFile = Path.Combine(DataDir, "lic.dat");

        // Simple XOR key — enough to prevent casual text-editor tampering
        private static readonly byte[] XorKey = { 0x4D, 0x45, 0x54, 0x6C }; // "METl"

        // ── Public API ────────────────────────────────────────────────────

        /// <summary>
        /// Returns true when a valid activation code is saved on this machine.
        /// Automatically verified against the code format on every call.
        /// </summary>
        public static bool IsLicensed()
        {
            var key = SavedKey;
            return !string.IsNullOrEmpty(key) && Activate(key) != LicenseType.None;
        }

        /// <summary>True when the trial has expired and no valid license is present.</summary>
        public static bool IsTrialExpired => !IsLicensed() && DaysRemaining <= 0;

        /// <summary>
        /// Days remaining in the trial period.
        /// Returns int.MaxValue for licensed users.
        /// Returns 0 once expired (never negative).
        /// </summary>
        public static int DaysRemaining
        {
            get
            {
                if (IsLicensed()) return int.MaxValue;
                var install = GetOrCreateInstallDate();
                int elapsed = (int)(DateTime.Today - install).TotalDays;
                return Math.Max(0, TrialDays - elapsed);
            }
        }

        /// <summary>Short status string suitable for display in the ribbon or a dialog.</summary>
        public static string StatusText
        {
            get
            {
                if (IsLicensed()) return "Licensed";
                int d = DaysRemaining;
                return d > 0
                    ? $"Beta — {d} day{(d == 1 ? "" : "s")} remaining"
                    : "Trial expired — contact Mayer E-Concept SRL";
            }
        }

        // ── Internal helpers ──────────────────────────────────────────────

        /// <summary>
        /// Returns the stored install date, or writes today's date on first run.
        /// The date is XOR-obfuscated and Base64-encoded to prevent trivial tampering.
        /// </summary>
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
            catch
            {
                // Corrupt or unreadable — fall through to create a fresh record
            }

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

        // ── Public API: status, machine ID, activation ────────────────────

        /// <summary>
        /// Returns the overall license status for display in the UI.
        /// </summary>
        public static LicenseStatus GetStatus()
        {
            if (IsLicensed())       return LicenseStatus.Active;
            if (DaysRemaining > 0)  return LicenseStatus.Active;
            return LicenseStatus.TrialExpired;
        }

        /// <summary>
        /// Returns a stable, hardware-bound machine identifier.
        /// Used to generate activation codes tied to this machine.
        /// </summary>
        public static string GetMachineId()
        {
            try
            {
                // Use the first physical MAC address as the basis
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                        nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    {
                        var mac = nic.GetPhysicalAddress().ToString();
                        if (!string.IsNullOrEmpty(mac) && mac != "000000000000")
                        {
                            // Hash and truncate to a readable 12-char ID
                            using var sha = SHA256.Create();
                            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(mac + "METools"));
                            return BitConverter.ToString(hash, 0, 6).Replace("-", "").ToUpper();
                        }
                    }
                }
            }
            catch { }

            // Fallback: machine name hash
            var fallback = Environment.MachineName + Environment.UserName;
            using var sha2 = SHA256.Create();
            var h = sha2.ComputeHash(Encoding.UTF8.GetBytes(fallback));
            return BitConverter.ToString(h, 0, 6).Replace("-", "").ToUpper();
        }

        /// <summary>
        /// Attempts to activate using the provided code.
        /// Returns the LicenseType on success, LicenseType.None on failure.
        /// Stub implementation — replace with real server validation when ready.
        /// </summary>
        public static LicenseType Activate(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return LicenseType.None;

            // Code format: ME-XXXXXX-TYPE  (e.g. ME-A1B2C3-Y1 / -E30 / -PRM)
            // This is a stub — replace with HMAC server call for production.
            string upper = code.Trim().ToUpper();
            if (upper.StartsWith("ME-") && upper.Length >= 10)
            {
                if (upper.EndsWith("-PRM"))  return LicenseType.Permanent;
                if (upper.EndsWith("-Y1"))   return LicenseType.Year1;
                if (upper.EndsWith("-E30"))  return LicenseType.Extend30;
            }

            return LicenseType.None;
        }

        // ── Key persistence file (separate from trial date file) ──────────
        private static readonly string KeyFile = Path.Combine(DataDir, "key.dat");

        /// <summary>
        /// The activation code currently saved on this machine, or null if none.
        /// Stored obfuscated on disk; decoded on read.
        /// </summary>
        public static string SavedKey
        {
            get
            {
                try
                {
                    if (!File.Exists(KeyFile)) return null;
                    var raw = File.ReadAllText(KeyFile).Trim();
                    return string.IsNullOrEmpty(raw) ? null : Deobfuscate(raw);
                }
                catch { return null; }
            }
        }

        /// <summary>
        /// Validates and, on success, persists the activation code to disk.
        /// Returns true when the code is valid and was saved successfully.
        /// Use instead of Activate() when the UI should also store the result.
        /// </summary>
        public static bool TryActivate(string code)
        {
            var result = Activate(code);
            if (result == LicenseType.None) return false;

            try
            {
                Directory.CreateDirectory(DataDir);
                File.WriteAllText(KeyFile, Obfuscate(code.Trim()), Encoding.UTF8);
            }
            catch { /* activation is still logically valid even if save fails */ }

            return true;
        }

        /// <summary>
        /// Removes the saved activation key from disk, reverting to trial/expired state.
        /// </summary>
        public static void Deactivate()
        {
            try
            {
                if (File.Exists(KeyFile))
                    File.Delete(KeyFile);
            }
            catch { }
        }
    }
}
