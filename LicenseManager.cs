// LicenseManager.cs — ME-Tools Trial License Management
// Mayer E-Concept SRL
// -----------------------------------------------------------------
// Handles 14-day beta trial enforcement and holds the stub API used
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
using Autodesk.Revit.UI;

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
        private const int TrialDays = 14;

        private static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "METools");

        private static readonly string LicFile = Path.Combine(DataDir, "lic.dat");
        private static readonly string KeyFile = Path.Combine(DataDir, "key.dat");

        // Simple XOR key — enough to prevent casual text-editor tampering.
        private static readonly byte[] XorKey = { 0x4D, 0x45, 0x54, 0x6C }; // "METl"

        /// <summary>Info about the currently active full license, or null if none (trial mode).</summary>
        public class ActiveLicenseInfo
        {
            public LicenseType Type   { get; set; }
            public DateTime    Expiry { get; set; } // DateTime.MaxValue = permanent
        }

        /// <summary>
        /// Returns the currently active license's type and expiry, or null when
        /// running on the trial (no valid key on disk). Used by Settings/License
        /// UI to show how long the license is valid for.
        /// </summary>
        public static ActiveLicenseInfo GetActiveLicense()
        {
            try
            {
                if (!File.Exists(KeyFile)) return null;
                var code = (File.ReadAllText(KeyFile) ?? "").Trim();
                if (!VerifyCode(code, out var type, out var expiry)) return null;
                return new ActiveLicenseInfo { Type = type, Expiry = expiry };
            }
            catch { return null; }
        }

        /// <summary>
        /// Gate called at the top of every tool command's Open()/Execute(),
        /// EXCEPT Settings — which must always stay reachable so the user can
        /// activate a key. Shows a blocking message and returns false once the
        /// trial has expired with no license; returns true otherwise.
        /// </summary>
        public static bool CheckAccessOrExplain()
        {
            if (!IsTrialExpired) return true;
            try
            {
                var td = new TaskDialog("ME-Tools — Trial Expired")
                {
                    MainInstruction = "Your ME-Tools trial has expired",
                    MainContent     = "Your 14-day trial has ended, so this tool is locked.\n\n" +
                                      "Open ME-Tools \u2192 Settings \u2192 License to activate a key and keep using ME-Tools.\n\n" +
                                      "Need a license? Contact office@mayer-econcept.ro (include your Machine ID, shown in Settings).",
                    CommonButtons   = TaskDialogCommonButtons.Ok,
                };
                td.Show();
            }
            catch { }
            return false;
        }

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
                if (IsLicensed())
                {
                    var lic = GetActiveLicense();
                    if (lic != null)
                    {
                        if (lic.Type == LicenseType.Permanent) return "Licensed — Permanent";
                        int daysLeft = Math.Max(0, (lic.Expiry.Date - DateTime.Today).Days);
                        return $"Licensed — {daysLeft} day{(daysLeft == 1 ? "" : "s")} left";
                    }
                    return "Licensed";
                }
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

        /// <summary>
        /// Short machine identifier for activation-code binding. Based on the
        /// registry's MachineGuid (HKLM\SOFTWARE\Microsoft\Cryptography), which
        /// only changes on an OS reinstall -- unlike the previous basis
        /// (MachineName + UserDomainName), which changes the moment a customer
        /// renames their PC or changes domain/workgroup membership, silently
        /// invalidating an already-activated license with no obvious cause.
        /// Falls back to the old scheme if the registry read fails for any
        /// reason (e.g. a locked-down machine), so activation is never worse
        /// than it was before.
        /// </summary>
        public static string GetMachineId()
        {
            try
            {
                string guid = TryGetRegistryMachineGuid();
                if (!string.IsNullOrEmpty(guid)) return HashId(guid);
            }
            catch { }
            return GetLegacyMachineId();
        }

        // Pre-this-fix machine ID. Kept (not removed) so VerifyCode below can
        // still accept any code already issued against it -- switching
        // GetMachineId()'s basis must not retroactively invalidate a license
        // someone already activated under the old scheme.
        private static string GetLegacyMachineId()
        {
            try { return HashId(Environment.MachineName + "|" + Environment.UserDomainName); }
            catch { return "ME-TOOLS"; }
        }

        private static string HashId(string seed)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(seed));
                return BitConverter.ToString(hash, 0, 6).Replace("-", "");
            }
        }

        private static string TryGetRegistryMachineGuid()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                    return key?.GetValue("MachineGuid") as string;
            }
            catch { return null; }
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

                // Bind to this machine -- accepts either the current, durable
                // MachineGuid-based ID or the pre-fix MachineName/UserDomainName
                // one, so a code issued before this change keeps working.
                if (!string.Equals(mid, GetMachineId(), StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(mid, GetLegacyMachineId(), StringComparison.OrdinalIgnoreCase))
                    return false;

                // Verify the signature with the embedded public key.
                using (var ecdsa = ECDsa.Create())
                {
#if NETFRAMEWORK
                    // .NET Framework 4.8 has neither ImportSubjectPublicKeyInfo nor
                    // DSASignatureFormat -- both were added in .NET Core 3.0+. The key
                    // above is a standard uncompressed P-256 SubjectPublicKeyInfo blob;
                    // for that fixed structure, the raw EC point (0x04 marker + 32-byte
                    // X + 32-byte Y) is always exactly the LAST 64 bytes, so it can be
                    // pulled out directly instead of using the newer SPKI importer.
                    // Signature format: .NET Framework's ECDsa (ECDsaCng, backed by
                    // Windows CNG) has always produced/verified IEEE P1363 (raw R||S)
                    // signatures natively -- that's exactly the format
                    // DSASignatureFormat.IeeeP1363FixedFieldConcatenation requests
                    // explicitly below, so omitting the format parameter here is not
                    // an approximation, it's what this platform's VerifyData already does.
                    byte[] spki = Convert.FromBase64String(PublicKeyB64);
                    byte[] qx = new byte[32], qy = new byte[32];
                    Array.Copy(spki, spki.Length - 64, qx, 0, 32);
                    Array.Copy(spki, spki.Length - 32, qy, 0, 32);
                    ecdsa.ImportParameters(new ECParameters
                    {
                        Curve = ECCurve.NamedCurves.nistP256,
                        Q     = new ECPoint { X = qx, Y = qy },
                    });
                    bool ok = ecdsa.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256);
#else
                    ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKeyB64), out _);
                    bool ok = ecdsa.VerifyData(payloadBytes, signature,
                        HashAlgorithmName.SHA256,
                        DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
#endif
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
