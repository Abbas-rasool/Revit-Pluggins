//using Autodesk.Revit.DB;
//using System;
//using System.Collections.Generic;
//using System.Linq;

//namespace RevitPluggins.PerimeterRebars.Tools
//{
//    public class NearestNeighborPolygonBuilder : IPolygonBuilder
//    {
//        private const double DuplicateThreshold = 0.01;  // ft (~3 mm)
//        private const double MinSegmentLength   = 0.001; // ft
//        private const double ParallelEpsilon    = 1e-9;
//        private const double CrossingEpsilon    = 1e-6;

//        public List<Curve> Build(List<FamilyInstance> columns)
//        {
//            var points = ExtractUniquePoints(columns);
//            if (points.Count < 2) return new List<Curve>();

//            var ordered = BuildGreedyChain(points);
//            ordered     = Untangle(ordered);

//            return BuildCurves(ordered);
//        }

//        // ── Point extraction ─────────────────────────────────────────────────

//        private static List<XYZ> ExtractUniquePoints(List<FamilyInstance> columns)
//        {
//            var all = columns
//                .Select(c => c.Location as LocationPoint)
//                .Where(lp => lp != null)
//                .Select(lp => new XYZ(lp.Point.X, lp.Point.Y, 0))
//                .ToList();

//            return Deduplicate(all);
//        }

//        private static List<XYZ> Deduplicate(List<XYZ> points)
//        {
//            var unique = new List<XYZ>();
//            foreach (var p in points)
//            {
//                if (unique.All(u => u.DistanceTo(p) >= DuplicateThreshold))
//                    unique.Add(p);
//            }
//            return unique;
//        }

//        // ── Greedy chain ─────────────────────────────────────────────────────

//        private static List<XYZ> BuildGreedyChain(List<XYZ> points)
//        {
//            var unvisited = new List<XYZ>(points);
//            var chain     = new List<XYZ>();

//            // Deterministic start: bottom-left point
//            var start = unvisited.OrderBy(p => p.X).ThenBy(p => p.Y).First();
//            chain.Add(start);
//            unvisited.Remove(start);

//            while (unvisited.Count > 0)
//            {
//                XYZ current = chain[chain.Count - 1];
//                XYZ nearest = unvisited.OrderBy(p => current.DistanceTo(p)).First();
//                chain.Add(nearest);
//                unvisited.Remove(nearest);
//            }

//            return chain;
//        }

//        // ── 2-opt untangle ───────────────────────────────────────────────────

//        // Repeatedly swaps pairs of crossing edges until the polygon is
//        // intersection-free or the pass limit is reached.
//        private static List<XYZ> Untangle(List<XYZ> tour)
//        {
//            int  n         = tour.Count;
//            int  maxPasses = n * 2;
//            bool improved  = true;

//            while (improved && maxPasses-- > 0)
//            {
//                improved = false;

//                for (int i = 0; i < n - 1; i++)
//                {
//                    for (int j = i + 2; j < n; j++)
//                    {
//                        if (IsWrapAroundEdgePair(i, j, n)) continue;

//                        XYZ a = tour[i],     b = tour[i + 1];
//                        XYZ c = tour[j],     d = tour[(j + 1) % n];

//                        if (!SegmentsCross(a, b, c, d)) continue;

//                        // Reverse the sub-tour between i+1 and j to remove the crossing
//                        tour.Reverse(i + 1, j - i);
//                        improved = true;
//                    }
//                }
//            }

//            return tour;
//        }

//        // The closing edge (last → first) shares endpoints with both adjacent
//        // edges — skip that pair to avoid false crossing detections.
//        private static bool IsWrapAroundEdgePair(int i, int j, int n) =>
//            i == 0 && j == n - 1;

//        // ── Curve construction ───────────────────────────────────────────────

//        private static List<Curve> BuildCurves(List<XYZ> ordered)
//        {
//            var curves = new List<Curve>();
//            int n      = ordered.Count;

//            for (int i = 0; i < n; i++)
//            {
//                XYZ p1 = ordered[i];
//                XYZ p2 = ordered[(i + 1) % n];

//                if (p1.DistanceTo(p2) < MinSegmentLength) continue;

//                curves.Add(Line.CreateBound(p1, p2));
//            }

//            return curves;
//        }

//        // ── Geometry ─────────────────────────────────────────────────────────

//        // Returns true only when two segments properly cross (not at endpoints).
//        private static bool SegmentsCross(XYZ a, XYZ b, XYZ c, XYZ d)
//        {
//            double d1x   = b.X - a.X,  d1y = b.Y - a.Y;
//            double d2x   = d.X - c.X,  d2y = d.Y - c.Y;
//            double cross = d1x * d2y - d1y * d2x;

//            if (Math.Abs(cross) < ParallelEpsilon) return false; // parallel / collinear

//            double dx = c.X - a.X, dy = c.Y - a.Y;
//            double t  = (dx * d2y - dy * d2x) / cross;
//            double u  = (dx * d1y - dy * d1x) / cross;

//            return t > CrossingEpsilon && t < 1 - CrossingEpsilon
//                && u > CrossingEpsilon && u < 1 - CrossingEpsilon;
//        }
//    }
//}
