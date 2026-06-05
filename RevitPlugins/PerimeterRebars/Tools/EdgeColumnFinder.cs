using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace RevitPlugins.PerimeterRebars.Tools
{
    public class EdgeColumnFinder
    {
        public List<FamilyInstance> FindEdgeColumns(Document doc, Autodesk.Revit.DB.View activeView, CurveLoop floorPolygon, double maxDistance, ICollection<ElementId> exclude = null)
        {
            var result = new List<FamilyInstance>();

            var columns = new FilteredElementCollector(doc, activeView.Id)
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()
                .ToList();

            var polygonCurves = floorPolygon.ToList();

            foreach (var column in columns)
            {
                if (exclude != null && exclude.Contains(column.Id)) continue;

                if (column.Location is not LocationPoint lp) continue;

                var flatPt = new XYZ(lp.Point.X, lp.Point.Y, 0);

                if (!IsPointInsideLoop(flatPt, polygonCurves)) continue;

                double minDist = polygonCurves.Min(c => c.Distance(flatPt));

                if (minDist <= maxDistance)
                    result.Add(column);
            }

            return result;
        }

        private bool IsPointInsideLoop(XYZ point, List<Curve> curves)
        {
            int crossings = 0;
            double px = point.X, py = point.Y;

            foreach (var curve in curves)
            {
                var p0 = curve.GetEndPoint(0);
                var p1 = curve.GetEndPoint(1);
                double x0 = p0.X, y0 = p0.Y, x1 = p1.X, y1 = p1.Y;

                if ((y0 <= py && y1 > py) || (y1 <= py && y0 > py))
                {
                    double t = (py - y0) / (y1 - y0);
                    if (px < x0 + t * (x1 - x0))
                        crossings++;
                }
            }

            return (crossings % 2) == 1;
        }
    }
}
