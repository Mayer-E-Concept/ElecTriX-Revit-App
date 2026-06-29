// LicenseManager.cs — ME-Tools Trial License Management
// Mayer E-Concept SRL
// -----------------------------------------------------------------
// Handles 30-day beta trial enforcement and holds the stub API used
// by LicenseWindow / LicenseCheck. The real activation logic is a
// clean seam: Activate(code) currently rejects all codes — replace
// the body with real validation once the licensing backend is ready.
// -----------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
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
            try
            {
                if (!File.Exists(KeyFile)) return false;
                var code = (File.ReadAllText(KeyFile) ?? "").Trim();
                return VerifyCode(code, out _, out _);
            }
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
            if (!VerifyCode(code, out LicenseType type, out _)) return LicenseType.None;

            // Valid: persist the code so it survives restarts. IsLicensed()
            // re-verifies it on every check (machine + signature + expiry).
            try
            {
                Directory.CreateDirectory(DataDir);
                File.WriteAllText(KeyFile, code.Trim());
            }
            catch { return LicenseType.None; }

            return type;
        }

        // --- Offline signed-code validation (ECDSA P-256 / SHA-256) ---
        // The app holds ONLY the public key below, which can verify a code but
        // cannot create one. Codes are minted by the KeyGenerator tool, which
        // holds the matching private key (kept off all distributed binaries).
        // Paste the public key printed by the KeyGenerator on first run here:
        private const string PublicKeyB64 = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEfBHkyEUG5YanO0U5o9HAPbQhFwGWi1zwx8Yo83Xny4hErUrSDipnNTcorMaYQUh/18ptEJffYJadMZmullQjMA==";

        // Payload signed by the generator: "MACHINEID|TYPE|EXPIRY"
        //   TYPE   : P = permanent, Y = 1 year, E = 30-day extension
        //   EXPIRY : yyyyMMdd, or "0" for permanent
        // Code on the wire: Base32(payload) + "." + Base32(signature)  (case-insensitive)
        private static bool VerifyCode(string code, out LicenseType type, out DateTime expiry)
        {
            type   = LicenseType.None;
            expiry = DateTime.MinValue;
            try
            {
                if (string.IsNullOrWhiteSpace(code)) return false;
                if (PublicKeyB64 == "PASTE_PUBLIC_KEY_HERE") return false; // not configured yet

                string clean = code.Trim().ToUpperInvariant();
                int dot = clean.IndexOf('.');
                if (dot <= 0 || dot >= clean.Length - 1) return false;

                byte[] payloadBytes = Base32Decode(clean.Substring(0, dot));
                byte[] signature    = Base32Decode(clean.Substring(dot + 1));
                if (payloadBytes.Length == 0 || signature.Length == 0) return false;

                string payload = Encoding.UTF8.GetString(payloadBytes);
                var parts = payload.Split('|');
                if (parts.Length != 3) return false;

                string mid = parts[0], t = parts[1], exp = parts[2];

                // Bind to this machine.
                if (!string.Equals(mid, GetMachineId(), StringComparison.OrdinalIgnoreCase)) return false;

                // Verify the signature with the embedded public key.
                using (var ecdsa = ECDsa.Create())
                {
                    ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKeyB64), out _);
                    bool ok = ecdsa.VerifyData(payloadBytes, signature,
                        HashAlgorithmName.SHA256,
                        DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
                    if (!ok) return false;
                }

                switch (t)
                {
                    case "P": type = LicenseType.Permanent; break;
                    case "Y": type = LicenseType.Year1;     break;
                    case "E": type = LicenseType.Extend30;  break;
                    default:  return false;
                }

                if (exp == "0")
                {
                    expiry = DateTime.MaxValue;
                }
                else
                {
                    if (!DateTime.TryParseExact(exp, "yyyyMMdd",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out expiry))
                        return false;
                    if (expiry < DateTime.Today) return false; // expired
                }

                return true;
            }
            catch { return false; }
        }

        private static byte[] Base32Decode(string s)
        {
            const string A = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            int bits = 0, val = 0;
            var bytes = new List<byte>();
            foreach (char c in s)
            {
                int idx = A.IndexOf(c);
                if (idx < 0) continue; // skip separators / whitespace
                val = (val << 5) | idx;
                bits += 5;
                if (bits >= 8)
                {
                    bytes.Add((byte)((val >> (bits - 8)) & 0xFF));
                    bits -= 8;
                }
            }
            return bytes.ToArray();
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
