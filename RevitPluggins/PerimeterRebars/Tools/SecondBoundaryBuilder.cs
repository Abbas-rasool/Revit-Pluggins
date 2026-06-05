//using Autodesk.Revit.DB;
//using System;
//using System.Collections.Generic;
//using System.Linq;

//namespace RevitPluggins.PerimeterRebars.Tools
//{
//    public class SecondBoundaryBuilder
//    {
//        private const double AdjacentEpsilon = 0.01;
//        private const double ParallelEpsilon = 1e-9;
//        private const double CrossingEpsilon = 1e-6;

//        private readonly double _maxDistanceFt;

//        public SecondBoundaryBuilder(double maxDistanceFt)
//        {
//            _maxDistanceFt = maxDistanceFt;
//        }

//        public List<Curve> Build(List<FamilyInstance> secondLayerColumns, List<Curve> floorBoundaryEdges)
//        {
//            var lines = new List<Curve>();

//            var polygonEdges = new NearestNeighborPolygonBuilder().Build(secondLayerColumns);
//            lines.AddRange(polygonEdges);

//            var centers = ExtractCenters(secondLayerColumns);
//            AddFilteredSpokes(centers, floorBoundaryEdges, polygonEdges, lines);

//            return lines;
//        }

//        // ── Spoke building ───────────────────────────────────────────────────

//        private void AddFilteredSpokes(
//            List<XYZ>   centers,
//            List<Curve> floorBoundaryEdges,
//            List<Curve> hullEdges,
//            List<Curve> lines)
//        {
//            foreach (XYZ center in centers)
//            {
//                foreach (Curve edge in floorBoundaryEdges)
//                {
//                    XYZ    edgeStart  = Flat(edge.GetEndPoint(0));
//                    XYZ    edgeEnd    = Flat(edge.GetEndPoint(1));
//                    double edgeLength = edgeStart.DistanceTo(edgeEnd);
//                    if (edgeLength < 0.001) continue;

//                    XYZ    edgeDir  = (edgeEnd - edgeStart).Normalize();
//                    double t        = (center - edgeStart).DotProduct(edgeDir);
//                    if (t < 0 || t > edgeLength) continue;

//                    XYZ    footPoint = edgeStart + edgeDir.Multiply(t);
//                    double perpDist  = center.DistanceTo(footPoint);
//                    if (perpDist > _maxDistanceFt || perpDist < 0.001) continue;

//                    if (SpokePassesThroughHull(center, footPoint, hullEdges)) continue;

//                    lines.Add(Line.CreateBound(center, footPoint));
//                }
//            }
//        }

//        // Returns true when center→foot crosses any hull edge not adjacent to the column.
//        private static bool SpokePassesThroughHull(XYZ center, XYZ foot, List<Curve> hullEdges)
//        {
//            foreach (var edge in hullEdges)
//            {
//                XYZ a = Flat(edge.GetEndPoint(0));
//                XYZ b = Flat(edge.GetEndPoint(1));

//                if (a.DistanceTo(center) < AdjacentEpsilon || b.DistanceTo(center) < AdjacentEpsilon)
//                    continue;

//                if (SegmentsCross(center, foot, a, b)) return true;
//            }
//            return false;
//        }

//        // ── Helpers ──────────────────────────────────────────────────────────

//        private static List<XYZ> ExtractCenters(List<FamilyInstance> columns) =>
//            columns
//                .Select(c => c.Location as LocationPoint)
//                .Where(lp => lp != null)
//                .Select(lp => Flat(lp.Point))
//                .ToList();

//        private static XYZ Flat(XYZ p) => new XYZ(p.X, p.Y, 0);

//        private static bool SegmentsCross(XYZ a, XYZ b, XYZ c, XYZ d)
//        {
//            double d1x = b.X - a.X, d1y = b.Y - a.Y;
//            double d2x = d.X - c.X, d2y = d.Y - c.Y;
//            double cross = d1x * d2y - d1y * d2x;
//            if (Math.Abs(cross) < ParallelEpsilon) return false;

//            double dx = c.X - a.X, dy = c.Y - a.Y;
//            double t  = (dx * d2y - dy * d2x) / cross;
//            double u  = (dx * d1y - dy * d1x) / cross;

//            return t > CrossingEpsilon && t < 1 - CrossingEpsilon
//                && u > CrossingEpsilon && u < 1 - CrossingEpsilon;
//        }
//    }
//}
