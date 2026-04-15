// RasterRoomDetector.cs - ME-Tools | Raster-based Room Detection
// Mayer E-Concept SRL
// Algorithm: rasterize all DWG lines onto a grid, flood-fill exterior,
// find closed interior regions, return centroids of regions >= minArea.
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace METools
{
    public class DetectedRoom
    {
        public UV     Centroid   { get; set; }
        public double AreaM2     { get; set; }
        public int    CellCount  { get; set; }
    }

    public static class RasterRoomDetector
    {
        // Grid resolution and limits
        private const double CELL_M    = 0.10;              // 10 cm per cell
        private const double CELL_FT   = CELL_M / 0.3048;
        private const double MIN_AREA  = 2.0;               // sqm
        private const double CELL_AREA = CELL_M * CELL_M;   // sqm per cell
        private const int    MAX_CELLS = 4000;              // max grid dimension

        public static List<DetectedRoom> Detect(
            List<Curve> curves,
            double minLengthFt = 0,
            double minAreaM2   = MIN_AREA)
        {
            var rooms = new List<DetectedRoom>();
            if (curves == null || curves.Count == 0) return rooms;

            // 1. Bounding box of all curves
            double x0 =  1e9, y0 =  1e9;
            double x1 = -1e9, y1 = -1e9;

            foreach (var c in curves)
            {
                try
                {
                    var pa = c.GetEndPoint(0);
                    var pb = c.GetEndPoint(1);
                    x0 = Math.Min(x0, Math.Min(pa.X, pb.X));
                    y0 = Math.Min(y0, Math.Min(pa.Y, pb.Y));
                    x1 = Math.Max(x1, Math.Max(pa.X, pb.X));
                    y1 = Math.Max(y1, Math.Max(pa.Y, pb.Y));
                }
                catch { }
            }

            // Safety margins (2 cells on each side)
            x0 -= CELL_FT * 2; y0 -= CELL_FT * 2;
            x1 += CELL_FT * 2; y1 += CELL_FT * 2;

            int cols = (int)Math.Ceiling((x1 - x0) / CELL_FT) + 2;
            int rows = (int)Math.Ceiling((y1 - y0) / CELL_FT) + 2;

            // Sanity check - do not allocate too much
            if (cols > MAX_CELLS || rows > MAX_CELLS || cols < 3 || rows < 3)
                return rooms;

            // 2. Rasterize curves onto boolean wall grid (Bresenham)
            var wall = new bool[cols, rows];

            foreach (var c in curves)
            {
                try
                {
                    if (minLengthFt > 0 && c.Length < minLengthFt) continue;

                    var pa = c.GetEndPoint(0);
                    var pb = c.GetEndPoint(1);

                    int ax = Clamp((int)((pa.X - x0) / CELL_FT), cols);
                    int ay = Clamp((int)((pa.Y - y0) / CELL_FT), rows);
                    int bx = Clamp((int)((pb.X - x0) / CELL_FT), cols);
                    int by = Clamp((int)((pb.Y - y0) / CELL_FT), rows);

                    Bresenham(wall, cols, rows, ax, ay, bx, by);
                }
                catch { }
            }

            // 3. Flood-fill exterior from all 4 border edges
            var ext = new bool[cols, rows];
            var q   = new Queue<int>();

            void Seed(int cx, int cy)
            {
                if (cx >= 0 && cx < cols && cy >= 0 && cy < rows && !wall[cx, cy] && !ext[cx, cy])
                {
                    ext[cx, cy] = true;
                    q.Enqueue(cx * rows + cy);
                }
            }

            for (int c = 0; c < cols; c++) { Seed(c, 0); Seed(c, rows - 1); }
            for (int r = 0; r < rows; r++) { Seed(0, r); Seed(cols - 1, r); }

            while (q.Count > 0)
            {
                int v = q.Dequeue();
                int cx = v / rows, cy = v % rows;
                Seed(cx + 1, cy); Seed(cx - 1, cy);
                Seed(cx, cy + 1); Seed(cx, cy - 1);
            }

            // 4. Find connected interior components (not wall, not exterior)
            var vis     = new bool[cols, rows];
            int minCells = (int)Math.Max(1.0, minAreaM2 / CELL_AREA);

            for (int sx = 1; sx < cols - 1; sx++)
            for (int sy = 1; sy < rows - 1; sy++)
            {
                if (vis[sx, sy] || wall[sx, sy] || ext[sx, sy]) continue;

                // BFS to collect this component
                var comp = new List<int>();
                var bq   = new Queue<int>();
                bq.Enqueue(sx * rows + sy);
                vis[sx, sy] = true;

                while (bq.Count > 0)
                {
                    int v  = bq.Dequeue();
                    int cx = v / rows, cy = v % rows;
                    comp.Add(v);

                    void Try(int nx, int ny)
                    {
                        if (nx >= 0 && nx < cols && ny >= 0 && ny < rows &&
                            !vis[nx, ny] && !wall[nx, ny] && !ext[nx, ny])
                        {
                            vis[nx, ny] = true;
                            bq.Enqueue(nx * rows + ny);
                        }
                    }
                    Try(cx + 1, cy); Try(cx - 1, cy);
                    Try(cx, cy + 1); Try(cx, cy - 1);
                }

                if (comp.Count < minCells) continue;

                // 5. Calculate centroid and area
                double sumX = 0, sumY = 0;
                foreach (int v in comp)
                {
                    int cx = v / rows, cy = v % rows;
                    sumX += x0 + cx * CELL_FT;
                    sumY += y0 + cy * CELL_FT;
                }

                rooms.Add(new DetectedRoom
                {
                    Centroid  = new UV(sumX / comp.Count, sumY / comp.Count),
                    AreaM2    = comp.Count * CELL_AREA,
                    CellCount = comp.Count,
                });
            }

            return rooms;
        }

        // ---- Helpers --------------------------------------------------------

        private static int Clamp(int v, int max) => Math.Max(0, Math.Min(v, max - 1));

        private static void Bresenham(bool[,] g, int cols, int rows, int x0, int y0, int x1, int y1)
        {
            int dx =  Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int er = dx + dy;

            while (true)
            {
                if (x0 >= 0 && x0 < cols && y0 >= 0 && y0 < rows)
                    g[x0, y0] = true;

                if (x0 == x1 && y0 == y1) break;

                int e2 = 2 * er;
                if (e2 >= dy) { er += dy; x0 += sx; }
                if (e2 <= dx) { er += dx; y0 += sy; }
            }
        }
    }
}
