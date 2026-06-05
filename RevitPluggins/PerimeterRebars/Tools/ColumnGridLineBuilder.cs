using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitPluggins.PerimeterRebars.Tools
{
    // Builds a flat H+V grid of lines through column cluster positions,
    // clipped so no line extends beyond the floor boundary polygon.
    // Concave floors may produce multiple segments per grid line — all are returned.
    public class ColumnGridLineBuilder
    {
        private const double LineMarginFt   = 5.0;   // overshoot used before clipping
        private const double DuplicateEps   = 1e-4;  // merge near-coincident intersection params
        private const double MinSegmentFt   = 0.01;  // discard degenerate segments
        private const double CrossEps       = 1e-10;

        private readonly double _clusterToleranceFt;

        public ColumnGridLineBuilder(double clusterToleranceFt)
        {
            _clusterToleranceFt = clusterToleranceFt;
        }

        public List<Curve> Build(List<FamilyInstance> columns, CurveLoop floorBoundary)
        {
            var centers = ExtractCenters(columns);
            if (centers.Count == 0) return new List<Curve>();

            GetBounds(floorBoundary, out double minX, out double maxX, out double minY, out double maxY);
            double extMinX = minX - LineMarginFt;
            double extMaxX = maxX + LineMarginFt;
            double extMinY = minY - LineMarginFt;
            double extMaxY = maxY + LineMarginFt;

            var lines = new List<Curve>();

            foreach (double y in Cluster(centers.Select(c => c.Y), minY, maxY))
                lines.AddRange(ClipLineToPolygon(
                    new XYZ(extMinX, y, 0), new XYZ(extMaxX, y, 0), floorBoundary));

            foreach (double x in Cluster(centers.Select(c => c.X), minX, maxX))
                lines.AddRange(ClipLineToPolygon(
                    new XYZ(x, extMinY, 0), new XYZ(x, extMaxY, 0), floorBoundary));

            return lines;
        }

        // ── Polygon clipping ─────────────────────────────────────────────────

        // Clips the segment lineStart→lineEnd against the polygon.
        // Returns the portions of the segment that lie inside the polygon.
        // The segment starts outside (margins guarantee this), so intersection
        // parameters alternate: enter, exit, enter, exit, …
        private static List<Curve> ClipLineToPolygon(XYZ lineStart, XYZ lineEnd, CurveLoop polygon)
        {
            XYZ    dir        = lineEnd - lineStart;
            double lineLength = dir.GetLength();
            if (lineLength < MinSegmentFt) return new List<Curve>();

            // Collect all t-values (0…1 along the segment) where the segment
            // crosses a polygon edge.
            var ts = new List<double>();

            foreach (var edge in polygon)
            {
                XYZ a = new XYZ(edge.GetEndPoint(0).X, edge.GetEndPoint(0).Y, 0);
                XYZ b = new XYZ(edge.GetEndPoint(1).X, edge.GetEndPoint(1).Y, 0);

                double t = SegmentIntersectionT(lineStart, lineEnd, a, b);
                if (t >= 0) ts.Add(t);
            }

            if (ts.Count == 0) return new List<Curve>();

            ts.Sort();
            ts = MergeDuplicates(ts);

            // Segment starts outside → first crossing enters, second exits, …
            var result = new List<Curve>();
            bool inside = false;
            double prev = 0;

            foreach (double t in ts)
            {
                if (inside && t - prev > DuplicateEps)
                {
                    XYZ p1 = lineStart + dir.Multiply(prev);
                    XYZ p2 = lineStart + dir.Multiply(t);
                    if (p1.DistanceTo(p2) >= MinSegmentFt)
                        result.Add(Line.CreateBound(p1, p2));
                }
                inside = !inside;
                prev = t;
            }

            if (inside)
            {
                XYZ p1 = lineStart + dir.Multiply(prev);
                if (p1.DistanceTo(lineEnd) >= MinSegmentFt)
                    result.Add(Line.CreateBound(p1, lineEnd));
            }

            return result;
        }

        // Returns t ∈ [0, 1] along segment p1→p2 where it crosses q1→q2, or -1.
        private static double SegmentIntersectionT(XYZ p1, XYZ p2, XYZ q1, XYZ q2)
        {
            double d1x = p2.X - p1.X, d1y = p2.Y - p1.Y;
            double d2x = q2.X - q1.X, d2y = q2.Y - q1.Y;
            double cross = d1x * d2y - d1y * d2x;
            if (Math.Abs(cross) < CrossEps) return -1;

            double dx = q1.X - p1.X, dy = q1.Y - p1.Y;
            double t  = (dx * d2y - dy * d2x) / cross;
            double u  = (dx * d1y - dy * d1x) / cross;

            if (t >= -DuplicateEps && t <= 1 + DuplicateEps
             && u >= -DuplicateEps && u <= 1 + DuplicateEps)
                return Math.Max(0, Math.Min(1, t));

            return -1;
        }

        // Collapses t-values that are within DuplicateEps of each other (corner hits).
        private static List<double> MergeDuplicates(List<double> sorted)
        {
            var result = new List<double> { sorted[0] };
            for (int i = 1; i < sorted.Count; i++)
                if (sorted[i] - result[result.Count - 1] > DuplicateEps)
                    result.Add(sorted[i]);
            return result;
        }

        // ── Clustering ───────────────────────────────────────────────────────

        // Groups values within tolerance and returns the furthest-from-boundary
        // representative for each group.  axisMin/axisMax are the floor extents
        // on this axis — clusters in the lower half snap to Max, upper half to Min,
        // so the line always lands on the column deepest into the slab.
        private List<double> Cluster(IEnumerable<double> values, double axisMin, double axisMax)
        {
            var sorted = values.OrderBy(v => v).ToList();
            if (sorted.Count == 0) return new List<double>();

            var result = new List<double>();
            var group  = new List<double> { sorted[0] };

            for (int i = 1; i < sorted.Count; i++)
            {
                if (sorted[i] - group[0] <= _clusterToleranceFt)
                    group.Add(sorted[i]);
                else
                {
                    result.Add(FurthestFromBoundary(group, axisMin, axisMax));
                    group = new List<double> { sorted[i] };
                }
            }

            result.Add(FurthestFromBoundary(group, axisMin, axisMax));
            return result;
        }

        // Cluster centre in the lower half → take Max (column deepest from the near edge).
        // Cluster centre in the upper half → take Min (column deepest from the far edge).
        private static double FurthestFromBoundary(List<double> group, double axisMin, double axisMax)
        {
            double axisMid      = (axisMin + axisMax) * 0.5;
            double clusterCentre = group.Average();
            return clusterCentre < axisMid ? group.Max() : group.Min();
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static List<XYZ> ExtractCenters(List<FamilyInstance> columns) =>
            columns
                .Select(c => c.Location as LocationPoint)
                .Where(lp => lp != null)
                .Select(lp => new XYZ(lp.Point.X, lp.Point.Y, 0))
                .ToList();

        private static void GetBounds(CurveLoop loop,
            out double minX, out double maxX, out double minY, out double maxY)
        {
            minX = double.MaxValue; maxX = double.MinValue;
            minY = double.MaxValue; maxY = double.MinValue;

            foreach (var curve in loop)
            {
                foreach (var p in new[] { curve.GetEndPoint(0), curve.GetEndPoint(1) })
                {
                    if (p.X < minX) minX = p.X;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.Y > maxY) maxY = p.Y;
                }
            }
        }
    }
}
