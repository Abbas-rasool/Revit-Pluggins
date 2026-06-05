using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitPluggins.GeometryHelpers
{
    /// <summary>
    /// Extracts a single flattened outer perimeter (a <see cref="CurveLoop"/> at Z = 0)
    /// from one or more selected floors. Multiple floors are unioned by extruding each
    /// top face into a solid, boolean-unioning them, and reading back the largest loop.
    /// </summary>
    public class FloorBoundryGenerator
    {
        public CurveLoop GetOuterFloorPerimeter(IList<Reference> floorRefs, Document doc)
        {
            if (floorRefs == null || !floorRefs.Any()) return null;

            List<CurveLoop> allOuterLoops = new List<CurveLoop>();
            Options opt = new Options { DetailLevel = ViewDetailLevel.Fine };

            foreach (Reference fRef in floorRefs)
            {
                Element el = doc.GetElement(fRef);
                GeometryElement geomElem = el.get_Geometry(opt);

                foreach (GeometryObject geomObj in geomElem)
                {
                    if (geomObj is Solid solid && solid.Volume > 0)
                    {
                        Face topFace = solid.Faces
                            .OfType<PlanarFace>()
                            .Where(f => f.FaceNormal.Z > 0.9)  // upward-ish
                            .OrderByDescending(f => f.Area)
                            .FirstOrDefault();

                        if (topFace == null) continue;

                        foreach (CurveLoop loop in topFace.GetEdgesAsCurveLoops())
                        {
                            CurveLoop flattened = FlattenLoop(loop);
                            allOuterLoops.Add(flattened);
                        }
                    }
                }
            }

            return UnionCurveLoops(allOuterLoops, doc);
        }

        private CurveLoop UnionCurveLoops(IList<CurveLoop> loops, Document doc)
        {
            if (loops == null || loops.Count == 0) return null;
            if (loops.Count == 1) return loops[0];

            List<Solid> solids = new List<Solid>();
            foreach (CurveLoop loop in loops)
            {
                Solid solid = GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { loop }, XYZ.BasisZ, 1);

                if (solid != null)
                    solids.Add(solid);
            }

            if (solids.Count == 0)
                return null;

            Solid unionSolid = solids[0];
            for (int i = 1; i < solids.Count; i++)
            {
                unionSolid = BooleanOperationsUtils.ExecuteBooleanOperation(unionSolid, solids[i], BooleanOperationsType.Union);
            }

            Face topFace = unionSolid.Faces
                .OfType<PlanarFace>()
                .Where(f => f.FaceNormal.Z > 0.9)
                .OrderByDescending(f => f.Area)
                .FirstOrDefault();

            if (topFace == null)
                return null;

            IList<CurveLoop> resultLoops = topFace.GetEdgesAsCurveLoops();

            var flattenedResult = FlattenLoop(resultLoops
                .OrderByDescending(GetLoopArea)
                .First());

            return flattenedResult;
        }

        private double GetLoopArea(CurveLoop loop)
        {
            double area = 0.0;

            List<XYZ> pts = new List<XYZ>();
            foreach (Curve c in loop)
                pts.Add(c.GetEndPoint(0));

            if (pts.Count < 3)
                return 0;

            for (int i = 0; i < pts.Count; i++)
            {
                XYZ p1 = pts[i];
                XYZ p2 = pts[(i + 1) % pts.Count];

                area += (p1.X * p2.Y) - (p2.X * p1.Y);
            }

            return Math.Abs(area) * 0.5;
        }

        private CurveLoop FlattenLoop(CurveLoop verticalLoop)
        {
            CurveLoop flattened = new CurveLoop();
            foreach (Curve curve in verticalLoop)
            {
                XYZ p1 = curve.GetEndPoint(0);
                XYZ p2 = curve.GetEndPoint(1);

                // Force Z to 0
                flattened.Append(Line.CreateBound(
                    new XYZ(p1.X, p1.Y, 0),
                    new XYZ(p2.X, p2.Y, 0)));
            }
            return flattened;
        }
    }
}
