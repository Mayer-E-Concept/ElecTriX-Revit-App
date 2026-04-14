// AutoRoomSeparation/LoopFinder.cs — ME-Tools | Auto Room Separation
// Mayer E-Concept SRL
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace METools.AutoRoomSeparation
{
    /// <summary>
    /// Detects closed polygonal loops from a flat set of curves (all at Z ≈ 0).
    ///
    /// Algorithm: planar half-edge face traversal.
    ///   1. Snap all curve endpoints to a 10 mm grid to build a graph.
    ///   2. Create two directed half-edges for each undirected curve.
    ///   3. At every node, assign each incoming half-edge a "next" half-edge —
    ///      the outgoing edge that requires the minimum counterclockwise rotation
    ///      from the reverse of the incoming direction (= maximum right-turn / most
    ///      clockwise turn). This traces the interior faces of the planar graph.
    ///   4. Follow next-pointers from each unvisited half-edge until a cycle closes.
    ///   5. Filter cycles by area (min/max) and remove near-duplicate loops.
    /// </summary>
    public static class LoopFinder
    {
        // ── Tolerances ──────────────────────────────────────────────────────
        /// <summary>Snap grid size in feet (≈ 10 mm).</summary>
        private const double SNAP_TOL_FT = 0.0328;

        private const double SNAP_SCALE = 1.0 / SNAP_TOL_FT;

        /// <summary>Centroid-deduplication radius in feet (≈ 0.5 m).</summary>
        private const double CENTROID_DEDUP_FT = 1.64;

        // ── Half-edge structure ─────────────────────────────────────────────

        private struct HalfEdge
        {
            public int    CurveIndex;   // index into input curves list
            public bool   Reversed;     // true → traversing the curve backward
            public string FromKey;      // snapped node key at departure
            public string ToKey;        // snapped node key at arrival
            public XYZ    DepartDir;    // unit tangent leaving FromKey
            public XYZ    ArriveDir;    // unit tangent arriving INTO ToKey (forward direction)
            public int    Next;         // index of next half-edge in face (-1 = dead end)
        }

        // ── Public API ─────────────────────────────────────────────────────

        public static List<List<Curve>> FindLoops(
            List<Curve> curves,
            AutoRoomSeparationSettings settings,
            out LoopStats stats)
        {
            stats = new LoopStats();
            if (curves == null || curves.Count == 0)
                return new List<List<Curve>>();

            // ── Step 1: build half-edges ────────────────────────────────────
            var halfEdges = BuildHalfEdges(curves);

            // ── Step 2: build adjacency (node → outgoing half-edge indices) ─
            var adj = BuildAdjacency(halfEdges);

            // ── Step 3: assign next pointers ───────────────────────────────
            AssignNext(halfEdges, adj);

            // ── Step 4: traverse faces ──────────────────────────────────────
            var rawLoops = TraceFaces(halfEdges, curves, settings, stats);

            // ── Step 5: deduplicate loops by centroid ───────────────────────
            var deduped = DeduplicateByCentroid(rawLoops, stats);

            stats.LoopsReturned = deduped.Count;
            return deduped;
        }

        // ── Step 1: Build half-edges ────────────────────────────────────────

        private static List<HalfEdge> BuildHalfEdges(List<Curve> curves)
        {
            var list = new List<HalfEdge>(curves.Count * 2);

            for (int i = 0; i < curves.Count; i++)
            {
                var c  = curves[i];
                var p0 = c.GetEndPoint(0);
                var p1 = c.GetEndPoint(1);

                var d0 = SafeTangentAtParam(c, 0.0);                // leaving p0
                var a0 = SafeTangentAtParam(c, 1.0);                // arriving at p1
                var d1 = a0.Negate();                               // leaving p1 (backward)
                var a1 = d0.Negate();                               // arriving at p0 (backward)

                list.Add(new HalfEdge
                {
                    CurveIndex = i, Reversed = false,
                    FromKey = NodeKey(p0), ToKey = NodeKey(p1),
                    DepartDir = d0, ArriveDir = a0, Next = -1,
                });
                list.Add(new HalfEdge
                {
                    CurveIndex = i, Reversed = true,
                    FromKey = NodeKey(p1), ToKey = NodeKey(p0),
                    DepartDir = d1, ArriveDir = a1, Next = -1,
                });
            }

            return list;
        }

        // ── Step 2: Build adjacency ─────────────────────────────────────────

        private static Dictionary<string, List<int>> BuildAdjacency(List<HalfEdge> halfEdges)
        {
            var adj = new Dictionary<string, List<int>>();
            for (int i = 0; i < halfEdges.Count; i++)
            {
                var key = halfEdges[i].FromKey;
                if (!adj.TryGetValue(key, out var lst))
                    adj[key] = lst = new List<int>();
                lst.Add(i);
            }
            return adj;
        }

        // ── Step 3: Assign next pointers ────────────────────────────────────

        private static void AssignNext(
            List<HalfEdge> halfEdges,
            Dictionary<string, List<int>> adj)
        {
            for (int i = 0; i < halfEdges.Count; i++)
            {
                var he = halfEdges[i];
                if (!adj.TryGetValue(he.ToKey, out var outgoing)) continue;

                // Twin = the other half-edge of the same underlying curve
                // Half-edges are added in pairs (2i, 2i+1), so twin index = i ^ 1
                int twinIdx = i ^ 1;

                // The reverse direction: pointing BACK from he.ToKey
                XYZ reverse = he.ArriveDir.Negate();

                double bestAngle = double.MaxValue;
                int    bestNext  = -1;

                foreach (int j in outgoing)
                {
                    if (j == twinIdx) continue;                     // skip U-turn
                    double angle = CcwAngle(reverse, halfEdges[j].DepartDir);
                    if (angle < 1e-9) angle = 2 * Math.PI;         // treat 0° as full rotation
                    if (angle < bestAngle) { bestAngle = angle; bestNext = j; }
                }

                // If no other outgoing edge, allow U-turn as last resort
                if (bestNext == -1 && twinIdx < halfEdges.Count)
                    bestNext = twinIdx;

                var copy   = halfEdges[i];
                copy.Next  = bestNext;
                halfEdges[i] = copy;
            }
        }

        // ── Step 4: Traverse faces ──────────────────────────────────────────

        private static List<List<Curve>> TraceFaces(
            List<HalfEdge> halfEdges,
            List<Curve> curves,
            AutoRoomSeparationSettings settings,
            LoopStats stats)
        {
            var visited = new bool[halfEdges.Count];
            var result  = new List<List<Curve>>();
            int maxCycles = halfEdges.Count + 2;

            for (int start = 0; start < halfEdges.Count; start++)
            {
                if (visited[start]) continue;
                if (halfEdges[start].Next == -1) { visited[start] = true; continue; }

                // Trace the face starting from 'start'
                var face = new List<int>();
                int cur  = start;
                bool valid = true;

                for (int step = 0; step < maxCycles; step++)
                {
                    if (visited[cur] && cur != start)
                    {
                        // Ran into a face that was already traced → not a new face
                        valid = false;
                        break;
                    }
                    if (cur == start && face.Count > 0)
                        break;          // closed the loop ✓

                    if (halfEdges[cur].Next == -1) { valid = false; break; }

                    visited[cur] = true;
                    face.Add(cur);
                    cur = halfEdges[cur].Next;
                }

                // Must be a closed loop with at least 3 edges
                if (!valid || cur != start || face.Count < 3)
                {
                    stats.OpenOrDegenerate++;
                    continue;
                }

                // Compute area and apply filter
                var faceCurves = face.Select(idx => curves[halfEdges[idx].CurveIndex]).ToList();
                double areaSqFt = ComputeAreaSqFt(faceCurves, halfEdges, face);
                double areaSqM  = areaSqFt * 0.092903;

                if (areaSqM < settings.MinAreaSqM)
                {
                    stats.FilteredTooSmall++;
                    continue;
                }
                if (areaSqM > settings.MaxAreaSqM)
                {
                    stats.FilteredTooLarge++;
                    continue;
                }

                // Re-orient curves so they match the traversal direction
                var oriented = BuildOrientedCurves(faceCurves, halfEdges, face);
                result.Add(oriented);
                stats.LoopsFound++;
            }

            return result;
        }

        /// <summary>
        /// Builds a list of Curve objects oriented to match the traversal direction.
        /// Lines and Arcs are reversed if the half-edge is reversed.
        /// </summary>
        private static List<Curve> BuildOrientedCurves(
            List<Curve> faceCurves,
            List<HalfEdge> halfEdges,
            List<int> face)
        {
            var oriented = new List<Curve>(faceCurves.Count);
            for (int i = 0; i < face.Count; i++)
            {
                var he = halfEdges[face[i]];
                var c  = faceCurves[i];

                if (!he.Reversed)
                {
                    oriented.Add(c);
                }
                else
                {
                    // Reverse the curve
                    try
                    {
                        var p0 = c.GetEndPoint(1);   // swap endpoints
                        var p1 = c.GetEndPoint(0);

                        if (c is Arc arc)
                        {
                            var pm = arc.Evaluate(0.5, true);
                            try { oriented.Add(Arc.Create(p0, p1, pm)); continue; }
                            catch { }
                        }
                        oriented.Add(Line.CreateBound(p0, p1));
                    }
                    catch { oriented.Add(c); }
                }
            }
            return oriented;
        }

        // ── Step 5: De-duplicate by centroid ────────────────────────────────

        private static List<List<Curve>> DeduplicateByCentroid(
            List<List<Curve>> loops,
            LoopStats stats)
        {
            // Sort smallest area first — we keep the inner / smaller room
            var sorted = loops.OrderBy(l => ComputeAreaSqFtSimple(l)).ToList();
            var kept   = new List<List<Curve>>();
            var keptCentroids = new List<XYZ>();

            foreach (var loop in sorted)
            {
                var c = Centroid(loop);
                bool tooClose = keptCentroids.Any(
                    kc => kc.DistanceTo(c) < CENTROID_DEDUP_FT);

                if (tooClose)
                { stats.Deduplicated++; continue; }

                kept.Add(loop);
                keptCentroids.Add(c);
            }

            return kept;
        }

        // ── Geometry helpers ────────────────────────────────────────────────

        private static double ComputeAreaSqFt(
            List<Curve> curves,
            List<HalfEdge> halfEdges,
            List<int> face)
        {
            // Sample each curve into XY points and apply shoelace formula
            var pts = new List<XYZ>();
            for (int i = 0; i < face.Count; i++)
            {
                var he = halfEdges[face[i]];
                var c  = curves[he.CurveIndex];
                int samples = (c is Line) ? 1 : 8;

                for (int k = 0; k < samples; k++)
                {
                    double t = he.Reversed
                        ? 1.0 - (double)k / samples
                        : (double)k / samples;
                    try { pts.Add(c.Evaluate(t, true)); }
                    catch { pts.Add(c.GetEndPoint(he.Reversed ? 1 : 0)); }
                }
            }

            return Math.Abs(ShoelaceArea(pts));
        }

        private static double ComputeAreaSqFtSimple(List<Curve> curves)
        {
            var pts = new List<XYZ>();
            foreach (var c in curves)
            {
                int samples = (c is Line) ? 1 : 8;
                for (int k = 0; k < samples; k++)
                {
                    double t = (double)k / samples;
                    try { pts.Add(c.Evaluate(t, true)); }
                    catch { pts.Add(c.GetEndPoint(0)); }
                }
            }
            return Math.Abs(ShoelaceArea(pts));
        }

        private static double ShoelaceArea(List<XYZ> pts)
        {
            double area = 0;
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                var p1 = pts[i];
                var p2 = pts[(i + 1) % n];
                area += p1.X * p2.Y - p2.X * p1.Y;
            }
            return area / 2.0;
        }

        private static XYZ Centroid(List<Curve> curves)
        {
            double sx = 0, sy = 0;
            int n = 0;
            foreach (var c in curves)
            {
                var p = c.Evaluate(0.5, true);
                sx += p.X; sy += p.Y; n++;
            }
            return n > 0 ? new XYZ(sx / n, sy / n, 0) : XYZ.Zero;
        }

        // ── Math helpers ────────────────────────────────────────────────────

        private static string NodeKey(XYZ p)
        {
            long ix = (long)Math.Round(p.X * SNAP_SCALE);
            long iy = (long)Math.Round(p.Y * SNAP_SCALE);
            return $"{ix}_{iy}";
        }

        /// <summary>
        /// Counterclockwise angle in XY from 'from' direction to 'to' direction, in [0, 2π).
        /// </summary>
        private static double CcwAngle(XYZ from, XYZ to)
        {
            double a1   = Math.Atan2(from.Y, from.X);
            double a2   = Math.Atan2(to.Y, to.X);
            double diff = a2 - a1;
            while (diff <  0)           diff += 2 * Math.PI;
            while (diff >= 2 * Math.PI) diff -= 2 * Math.PI;
            return diff;
        }

        private static XYZ SafeTangentAtParam(Curve c, double param)
        {
            try
            {
                var deriv = c.ComputeDerivatives(param, true);
                var d = deriv.BasisX;
                if (d.GetLength() > 1e-9) return d.Normalize();
            }
            catch { }

            // Fallback: chord direction (correct for Lines, approx for Arcs)
            try
            {
                var dir = (c.GetEndPoint(1) - c.GetEndPoint(0));
                if (dir.GetLength() > 1e-9) return dir.Normalize();
            }
            catch { }

            return XYZ.BasisX;
        }
    }

    // ── Loop statistics ─────────────────────────────────────────────────────

    public class LoopStats
    {
        public int LoopsFound        { get; set; }
        public int LoopsReturned     { get; set; }
        public int OpenOrDegenerate  { get; set; }
        public int FilteredTooSmall  { get; set; }
        public int FilteredTooLarge  { get; set; }
        public int Deduplicated      { get; set; }

        public int TotalFiltered =>
            OpenOrDegenerate + FilteredTooSmall + FilteredTooLarge + Deduplicated;
    }
}
