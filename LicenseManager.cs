// LicenseManager.cs — ME-Tools Trial License Management
// Mayer E-Concept SRL
// -----------------------------------------------------------------
// Handles 30-day beta trial enforcement and holds the stub API used
// by LicenseWindow / LicenseCheck. The real activation logic is a
// clean seam: Activate(code) currently rejects all codes — replace
// the body with real validation once the licensing backend is ready.
// -----------------------------------------------------------------
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace METools
{
    // Public-facing status used by LicenseWindow header text.
    public enum LicenseStatus
    {
        BetaActive,      // trial running
        BetaExpiring,    // ≤ 5 days left
        BetaExpired,     // trial ran out, no key
        Licensed,        // valid key present
        LicenseExpired,  // was licensed, now expired (future use)
    }

    // Type of license granted by Activate().
    public enum LicenseType
    {
        None,       // activation failed
        Extend30,   // +30 days
        Year1,      // 1-year license
        Permanent,  // permanent license
    }

    internal static class LicenseManager
    {
        // ── Configuration ─────────────────────────────────────────────────
        private const int TrialDays = 30;

        private static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "METools");

        private static readonly string LicFile = Path.Combine(DataDir, "lic.dat");
        private static readonly string KeyFile = Path.Combine(DataDir, "key.dat");

        // Simple XOR key — enough to prevent casual text-editor tampering.
        private static readonly byte[] XorKey = { 0x4D, 0x45, 0x54, 0x6C }; // "METl"

        // ── Public API ────────────────────────────────────────────────────

        /// <summary>
        /// Stub for future full-license key validation.
        /// Returns true only when a previously-activated key is on disk.
        /// </summary>
        public static bool IsLicensed()
        {
            try { return File.Exists(KeyFile) && File.ReadAllText(KeyFile).Trim().Length > 0; }
            catch { return false; }
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

        /// <summary>Status enum used by LicenseWindow header and Settings.</summary>
        public static LicenseStatus GetStatus()
        {
            if (IsLicensed()) return LicenseStatus.Licensed;
            int d = DaysRemaining;
            if (d <= 0) return LicenseStatus.BetaExpired;
            if (d <= 5) return LicenseStatus.BetaExpiring;
            return LicenseStatus.BetaActive;
        }

        /// <summary>Short machine identifier for activation-code binding.</summary>
        public static string GetMachineId()
        {
            try
            {
                string seed = Environment.MachineName + "|" + Environment.UserDomainName;
                using (var sha = SHA256.Create())
                {
                    var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(seed));
                    return BitConverter.ToString(hash, 0, 6).Replace("-", "");
                }
            }
            catch { return "ME-TOOLS"; }
        }

        /// <summary>
        /// STUB — real activation not yet implemented. Returns LicenseType.None
        /// for any input. Replace the body with real server/offline validation
        /// when the licensing backend is ready. The seam on LicenseWindow and
        /// the persistence in KeyFile already work.
        /// </summary>
        public static LicenseType Activate(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return LicenseType.None;

            // TODO: replace with real validation. Example seam:
            //
            //   var granted = MyLicenseServer.Verify(code, GetMachineId());
            //   if (granted == LicenseType.None) return LicenseType.None;
            //   PersistKey(code, granted);
            //   return granted;

            return LicenseType.None;
        }

        // ── Key persistence seam used by SettingsWindow ───────────────────
        // These three members are the contract the Settings UI relies on.
        // Replace the inner logic once the real license server is wired up;
        // the UI code will not need to change.

        /// <summary>The key currently persisted on disk, empty string if none.</summary>
        public static string SavedKey
        {
            get
            {
                try { return File.Exists(KeyFile) ? (File.ReadAllText(KeyFile) ?? "").Trim() : ""; }
                catch { return ""; }
            }
        }

        /// <summary>
        /// Attempts to activate with the given key. Returns true on success.
        /// Current stub: accepts no keys — replace with real validator.
        /// </summary>
        public static bool TryActivate(string key)
        {
            var granted = Activate(key);
            if (granted == LicenseType.None) return false;
            try
            {
                Directory.CreateDirectory(DataDir);
                File.WriteAllText(KeyFile, key.Trim());
            }
            catch { return false; }
            return true;
        }

        /// <summary>Remove any stored key, returning the app to beta-trial mode.</summary>
        public static void Deactivate()
        {
            try { if (File.Exists(KeyFile)) File.Delete(KeyFile); }
            catch { }
        }

        // ── Internal helpers ──────────────────────────────────────────────

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
