// LevelManagerModels.cs — ME-Tools | Level Manager
// Mayer E-Concept SRL
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace METools.LevelManager
{
    // One row of the level list: the raw Revit level plus everything the
    // window needs to display, group and sort it.
    public class LevelRow
    {
        public ElementId Id          { get; set; } = ElementId.InvalidElementId;
        public string    Name        { get; set; } = "";
        public double    ElevationFt { get; set; }   // internal units (feet) — authoritative for sorting
        public double    ElevationM  { get; set; }   // for display only

        // Auto-detected from the name — e.g. "UKD", "FFB", "Obergeschoss", or ""
        // when nothing recurs often enough to call it a group.
        public string GroupKey { get; set; } = "";
        // Auto-detected trailing tag — e.g. "H1", "H2", or "" when none.
        public string ZoneKey  { get; set; } = "";
    }

    public enum LevelManagerAction { Refresh, AddLevel }

    // Carries one request from the window to the ExternalEvent handler.
    public class LevelManagerRequest
    {
        public LevelManagerAction Action        { get; set; } = LevelManagerAction.Refresh;
        public string             NewName        { get; set; } = "";
        public double             NewElevationM  { get; set; } = 0.0; // meters, converted to feet in the handler
    }

    // ── Name parsing ────────────────────────────────────────────────────────
    // Project-agnostic on purpose: nothing here is hardcoded to "UKD"/"FFB"/"H1".
    // It looks at ALL level names together and auto-discovers which tokens
    // recur often enough to be a meaningful group (e.g. a shared prefix like
    // "UKD" or "Obergeschoss") or zone tag (e.g. a shared suffix like "H1").
    // A project with a totally different naming convention groups just as well.
    public static class LevelNameParser
    {
        public static void AssignGroups(List<LevelRow> rows)
        {
            var tokensByRow  = new Dictionary<LevelRow, string[]>();
            var firstCounts  = new Dictionary<string, int>();
            var lastCounts   = new Dictionary<string, int>();

            foreach (var row in rows)
            {
                var tokens = (row.Name ?? "")
                    .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                tokensByRow[row] = tokens;

                if (tokens.Length < 2) continue;

                var first = tokens[0];
                if (IsGroupTag(first))
                {
                    firstCounts.TryGetValue(first, out var c1);
                    firstCounts[first] = c1 + 1;
                }

                var last = tokens[tokens.Length - 1];
                if (IsZoneTag(last))
                {
                    lastCounts.TryGetValue(last, out var c2);
                    lastCounts[last] = c2 + 1;
                }
            }

            foreach (var row in rows)
            {
                var tokens = tokensByRow[row];
                row.GroupKey = "";
                row.ZoneKey  = "";
                if (tokens.Length < 2) continue;

                var first = tokens[0];
                if (firstCounts.TryGetValue(first, out var c1) && c1 >= 2)
                    row.GroupKey = first;

                var last = tokens[tokens.Length - 1];
                if (lastCounts.TryGetValue(last, out var c2) && c2 >= 2 && last != row.GroupKey)
                    row.ZoneKey = last;
            }
        }

        // A candidate group prefix: purely alphabetic (letters only), at least
        // 2 characters. Recurrence (>=2 levels) is what actually decides
        // whether it becomes a group — this just filters obvious non-candidates.
        private static bool IsGroupTag(string s) => s.Length >= 2 && s.All(char.IsLetter);

        // A candidate zone/building tag: short, starts with a letter, rest are
        // digits — e.g. "H1", "H2", "A3". Deliberately narrower than the group
        // check since this is meant to catch short building/wing codes, not
        // ordinary words.
        private static bool IsZoneTag(string s)
            => s.Length >= 2 && s.Length <= 4 && char.IsLetter(s[0]) && s.Skip(1).All(char.IsDigit);
    }
}
