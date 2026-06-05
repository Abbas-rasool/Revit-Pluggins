//using Autodesk.Revit.DB;
//using System;
//using System.Collections.Generic;
//using System.Linq;

//namespace RevitPluggins.PerimeterRebars.Tools
//{
//    public class EdgeColumnPolygonBuilder : IPolygonBuilder
//    {
//        public List<Curve> Build(List<FamilyInstance> columns)
//        {
//            var points = columns
//                .Select(c => c.Location as LocationPoint)
//                .Where(lp => lp != null)
//                .Select(lp => new XYZ(lp.Point.X, lp.Point.Y, 0))
//                .ToList();

//            if (points.Count < 2) return new List<Curve>();

//            var hull = ConvexHull(points);

//            var result = new List<Curve>();
//            for (int i = 0; i < hull.Count; i++)
//            {
//                XYZ p1 = hull[i];
//                XYZ p2 = hull[(i + 1) % hull.Count];
//                if (p1.DistanceTo(p2) < 0.001) continue;
//                result.Add(Line.CreateBound(p1, p2));
//            }
//            return result;
//        }

//        // Graham scan — returns hull vertices in counter-clockwise order.
//        private static List<XYZ> ConvexHull(List<XYZ> points)
//        {
//            if (points.Count < 3) return points.ToList();

//            // Start from the lowest-then-leftmost point
//            XYZ pivot = points.OrderBy(p => p.Y).ThenBy(p => p.X).First();

//            var sorted = points
//                .Where(p => !p.IsAlmostEqualTo(pivot))
//                .OrderBy(p => Math.Atan2(p.Y - pivot.Y, p.X - pivot.X))
//                .ThenBy(p => p.DistanceTo(pivot))
//                .ToList();

//            sorted.Insert(0, pivot);

//            var hull = new List<XYZ> { sorted[0], sorted[1] };
//            for (int i = 2; i < sorted.Count; i++)
//            {
//                while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], sorted[i]) <= 0)
//                    hull.RemoveAt(hull.Count - 1);
//                hull.Add(sorted[i]);
//            }

//            return hull;
//        }

//        private static double Cross(XYZ O, XYZ A, XYZ B) =>
//            (A.X - O.X) * (B.Y - O.Y) - (A.Y - O.Y) * (B.X - O.X);
//    }
//}
