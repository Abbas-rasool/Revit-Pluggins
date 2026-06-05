//using Autodesk.Revit.DB;
//using System;
//using System.Collections.Generic;
//using System.Linq;

//namespace RevitPluggins.PerimeterRebars.Tools
//{
//    // Connects every column center in angular order from the group centroid.
//    // Produces a polygon that passes through all centers rather than just the outer hull.
//    public class ColumnCenterPolygonBuilder
//    {
//        public List<Curve> Build(List<FamilyInstance> columns)
//        {
//            var points = columns
//                .Select(c => c.Location as LocationPoint)
//                .Where(lp => lp != null)
//                .Select(lp => new XYZ(lp.Point.X, lp.Point.Y, 0))
//                .ToList();

//            if (points.Count < 2) return new List<Curve>();

//            double cx = points.Average(p => p.X);
//            double cy = points.Average(p => p.Y);

//            var sorted = points
//                .OrderBy(p => Math.Atan2(p.Y - cy, p.X - cx))
//                .ToList();

//            var result = new List<Curve>();
//            for (int i = 0; i < sorted.Count; i++)
//            {
//                XYZ p1 = sorted[i];
//                XYZ p2 = sorted[(i + 1) % sorted.Count];
//                if (p1.DistanceTo(p2) < 0.001) continue;
//                result.Add(Line.CreateBound(p1, p2));
//            }
//            return result;
//        }
//    }
//}
