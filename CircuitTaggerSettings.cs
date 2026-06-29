// CircuitTaggerSettings.cs -- ME-Tools | Circuit Tagger
// Persists tag placement and secondary label styling to %APPDATA%\METools\circuit-tagger.json
// Mayer E-Concept SRL
using System;
using System.IO;
using System.Text;

namespace METools.FamilyPlacer
{
    public class CircuitTaggerSettingsData
    {
        // Tag placement
        public double GapMm      { get; set; } = 50.0;
        public double OffsetYMm  { get; set; } = 0.0;
        public double StackGapMm { get; set; } = 8.0;


        // Secondary label -- Graphics
        public string SubLabelFontName      { get; set; } = "Arial Narrow";
        public double SubLabelFontSizeMm    { get; set; } = 2.0;
        public string SubLabelColorHex      { get; set; } = "#000000";
        public int    SubLabelLineWeight    { get; set; } = 2;
        public double SubLabelLeaderOffsetMm { get; set; } = 0.0;
        public bool   SubLabelShowBorder    { get; set; } = false;
        public bool   SubLabelOpaque        { get; set; } = false;
        public string SubLabelHAlign        { get; set; } = "None"; // leader arrowhead

        // Secondary label -- Text
        public bool   SubLabelBold          { get; set; } = false;
        public bool   SubLabelItalic        { get; set; } = false;
        public bool   SubLabelUnderline     { get; set; } = false;
        public double SubLabelTabSizeMm     { get; set; } = 12.7;
        public double SubLabelWidthFactor   { get; set; } = 0.75;
    }

    public static class CircuitTaggerSettings
    {
        private static string FilePath
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "METools");
                try { Directory.CreateDirectory(dir); } catch { }
                return Path.Combine(dir, "circuit-tagger.json");
            }
        }

        public static CircuitTaggerSettingsData Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath, Encoding.UTF8);
                    return SimpleJsonDeserialize(json) ?? new CircuitTaggerSettingsData();
                }
            }
            catch { }
            return new CircuitTaggerSettingsData();
        }

        public static void Save(CircuitTaggerSettingsData data)
        {
            try
            {
                var json = SimpleJsonSerialize(data);
                File.WriteAllText(FilePath, json, Encoding.UTF8);
            }
            catch { }
        }

        // Simple JSON serializer (no external dependency)
        private static string SimpleJsonSerialize(CircuitTaggerSettingsData d)
        {
            var sb = new StringBuilder();
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            sb.AppendLine("{");
            sb.AppendLine($"  \"GapMm\": {d.GapMm.ToString(ic)},");
            sb.AppendLine($"  \"OffsetYMm\": {d.OffsetYMm.ToString(ic)},");
            sb.AppendLine($"  \"StackGapMm\": {d.StackGapMm.ToString(ic)},");
            sb.AppendLine($"  \"SubLabelFontName\": \"{Esc(d.SubLabelFontName)}\",");
            sb.AppendLine($"  \"SubLabelFontSizeMm\": {d.SubLabelFontSizeMm.ToString(ic)},");
            sb.AppendLine($"  \"SubLabelColorHex\": \"{Esc(d.SubLabelColorHex)}\",");
            sb.AppendLine($"  \"SubLabelLineWeight\": {d.SubLabelLineWeight},");
            sb.AppendLine($"  \"SubLabelLeaderOffsetMm\": {d.SubLabelLeaderOffsetMm.ToString(ic)},");
            sb.AppendLine($"  \"SubLabelShowBorder\": {(d.SubLabelShowBorder ? "true" : "false")},");
            sb.AppendLine($"  \"SubLabelOpaque\": {(d.SubLabelOpaque ? "true" : "false")},");
            sb.AppendLine($"  \"SubLabelHAlign\": \"{Esc(d.SubLabelHAlign)}\",");
            sb.AppendLine($"  \"SubLabelBold\": {(d.SubLabelBold ? "true" : "false")},");
            sb.AppendLine($"  \"SubLabelItalic\": {(d.SubLabelItalic ? "true" : "false")},");
            sb.AppendLine($"  \"SubLabelUnderline\": {(d.SubLabelUnderline ? "true" : "false")},");
            sb.AppendLine($"  \"SubLabelTabSizeMm\": {d.SubLabelTabSizeMm.ToString(ic)},");
            sb.AppendLine($"  \"SubLabelWidthFactor\": {d.SubLabelWidthFactor.ToString(ic)}");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static CircuitTaggerSettingsData SimpleJsonDeserialize(string json)
        {
            var d = new CircuitTaggerSettingsData();
            foreach (var line in json.Split('\n'))
            {
                var trim = line.Trim().TrimEnd(',');
                if (TryReadDouble(trim, "GapMm",                out var v))   d.GapMm                = v;
                if (TryReadDouble(trim, "OffsetYMm",            out var v2))  d.OffsetYMm            = v2;
                if (TryReadDouble(trim, "StackGapMm",           out var v3))  d.StackGapMm           = v3;
                if (TryReadDouble(trim, "SubLabelFontSizeMm",   out var v4))  d.SubLabelFontSizeMm   = v4;
                if (TryReadDouble(trim, "SubLabelLeaderOffsetMm",out var v5)) d.SubLabelLeaderOffsetMm = v5;
                if (TryReadDouble(trim, "SubLabelTabSizeMm",    out var v6))  d.SubLabelTabSizeMm    = v6;
                if (TryReadDouble(trim, "SubLabelWidthFactor",  out var v7))  d.SubLabelWidthFactor  = v7;
                if (TryReadString(trim, "SubLabelFontName",     out var s))   d.SubLabelFontName     = s;
                if (TryReadString(trim, "SubLabelColorHex",     out var s2))  d.SubLabelColorHex     = s2;
                if (TryReadString(trim, "SubLabelHAlign",       out var s3))  d.SubLabelHAlign       = s3;
                if (TryReadBool(trim,   "SubLabelBold",         out var b))   d.SubLabelBold         = b;
                if (TryReadBool(trim,   "SubLabelItalic",       out var b2))  d.SubLabelItalic       = b2;
                if (TryReadBool(trim,   "SubLabelUnderline",    out var b3))  d.SubLabelUnderline    = b3;
                if (TryReadBool(trim,   "SubLabelShowBorder",   out var b4))  d.SubLabelShowBorder   = b4;
                if (TryReadBool(trim,   "SubLabelOpaque",       out var b5))  d.SubLabelOpaque       = b5;
                if (TryReadDouble(trim, "SubLabelLineWeight",   out var lw))  d.SubLabelLineWeight   = (int)lw;
            }
            return d;
        }

        private static bool TryReadDouble(string line, string key, out double val)
        {
            val = 0;
            var prefix = $"\"{key}\":";
            var idx = line.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;
            var rest = line.Substring(idx + prefix.Length).Trim().TrimEnd(',');
            return double.TryParse(rest, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out val);
        }

        private static bool TryReadString(string line, string key, out string val)
        {
            val = null;
            var prefix = $"\"{key}\":";
            var idx = line.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;
            var rest = line.Substring(idx + prefix.Length).Trim().TrimEnd(',').Trim('"');
            val = rest.Replace("\\\"", "\"").Replace("\\\\", "\\");
            return true;
        }

        private static bool TryReadBool(string line, string key, out bool val)
        {
            val = false;
            var prefix = $"\"{key}\":";
            var idx = line.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;
            var rest = line.Substring(idx + prefix.Length).Trim().TrimEnd(',').ToLowerInvariant();
            val = rest == "true";
            return true;
        }
    }
}
