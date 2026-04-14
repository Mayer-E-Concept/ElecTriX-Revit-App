// AutoRoomSeparation/AutoRoomSeparationSettings.cs — ME-Tools | Auto Room Separation
// Mayer E-Concept SRL
using System.Collections.Generic;

namespace METools.AutoRoomSeparation
{
    /// <summary>
    /// User-configurable settings for the AutoRoomSeparation command.
    /// All area values are in square metres; length values in metres.
    /// </summary>
    public class AutoRoomSeparationSettings
    {
        // ── Source Selection ────────────────────────────────────────────────
        public bool UseDwgInstances   { get; set; } = true;
        public bool UseDirectShapes   { get; set; } = true;
        public bool UseNativeWalls    { get; set; } = true;

        // ── Area Filter ─────────────────────────────────────────────────────
        public double MinAreaSqM { get; set; } = 2.0;
        public double MaxAreaSqM { get; set; } = 500.0;

        // ── Curve Filter ────────────────────────────────────────────────────
        /// <summary>Minimum curve length in metres; shorter curves are discarded.</summary>
        public double MinLengthM { get; set; } = 0.30;

        // ── Layer Exclusion ─────────────────────────────────────────────────
        /// <summary>
        /// Comma-separated substrings. Any DWG curve whose layer name contains
        /// one of these tokens (case-insensitive) is ignored.
        /// </summary>
        public string ExcludeLayerTokens { get; set; } =
            "HATCH,SCHR,FILL,TEXT,DIM,MASS,BEM,ANNO";

        // ── Derived helpers ─────────────────────────────────────────────────

        public List<string> GetExcludeTokens()
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(ExcludeLayerTokens)) return list;
            foreach (var tok in ExcludeLayerTokens.Split(','))
            {
                var t = tok.Trim().ToUpperInvariant();
                if (t.Length > 0) list.Add(t);
            }
            return list;
        }

        /// <summary>MinAreaSqM converted to square feet.</summary>
        public double MinAreaSqFt => MinAreaSqM / 0.092903;

        /// <summary>MaxAreaSqM converted to square feet.</summary>
        public double MaxAreaSqFt => MaxAreaSqM / 0.092903;

        /// <summary>MinLengthM converted to feet.</summary>
        public double MinLengthFt => MinLengthM / 0.3048;
    }
}
