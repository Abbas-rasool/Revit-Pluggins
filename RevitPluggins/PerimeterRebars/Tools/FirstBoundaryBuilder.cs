//using Autodesk.Revit.DB;
//using System;
//using System.Collections.Generic;
//using System.Linq;

//namespace RevitPluggins.PerimeterRebars.Tools
//{
//    // Builds orthogonal spokes from first-layer columns to the floor boundary.
//    // Only "exterior" columns emit spokes — those whose spoke to the floor does not
//    // cross any non-adjacent edge of the group's own hull polygon.
//    public class FirstBoundaryBuilder
//    {
//        private const double AdjacentEpsilon = 0.01;   // ft — endpoint considered touching column
//        private const double ParallelEpsilon = 1e-9;
//        private const double CrossingEpsilon = 1e-6;

//        private readonly double _maxDistanceFt;

//        public FirstBoundaryBuilder(double maxDistanceFt)
//        {
//            _maxDistanceFt = maxDistanceFt;
//        }

//        // Returns the filtered spokes only (not the hull polygon itself).
//        public List<Curve> BuildSpokes(
//            List<FamilyInstance> columns,
//            List<Curve>          floorBoundaryEdges,
//            List<Curve>          hullEdges)
//        {
//            var spokes  = new List<Curve>();
//            var centers = ExtractCenters(columns);

//            foreach (XYZ center in centers)
//            {
//                foreach (Curve edge in floorBoundaryEdges)
//                {
//                    XYZ    a          = Flat(edge.GetEndPoint(0));
//                    XYZ    b          = Flat(edge.GetEndPoint(1));
//                    double edgeLength = a.DistanceTo(b);
//                    if (edgeLength < 0.001) continue;

//                    XYZ    dir      = (b - a).Normalize();
//                    double t        = (center - a).DotProduct(dir);
//                    if (t < 0 || t > edgeLength) continue;

//                    XYZ    foot     = a + dir.Multiply(t);
//                    double perpDist = center.DistanceTo(foot);
//                    if (perpDist > _maxDistanceFt || perpDist < 0.001) continue;

//                    if (SpokePassesThroughHull(center, foot, hullEdges)) continue;

//                    spokes.Add(Line.CreateBound(center, foot));
//                }
//            }

//            return spokes;
//        }

//        // Returns true when the segment center→foot crosses any hull edge that does
//        // not share the column center as an endpoint (non-adjacent edges).
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
