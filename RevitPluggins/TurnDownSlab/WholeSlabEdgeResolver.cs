using Autodesk.Revit.DB;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitPluggins.TurnDownSlab
{
    public static class WholeSlabEdgeResolver
    {
        public static IList<Reference> GetEdgeReferences(Document doc, IList<Reference> floorRefs)
        {
            var result = new List<Reference>();
            if (floorRefs == null || floorRefs.Count == 0)
                return result;

            var gf = new GeometryFactory();
            var allEdges = new List<(Reference Reference, XYZ Mid)>();
            var footprints = new List<Geometry>();

            foreach (Reference floorRef in floorRefs)
            {
                Element el = doc.GetElement(floorRef);
                if (el == null) continue;

                var opt = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };

                foreach (GeometryObject go in el.get_Geometry(opt))
                {
                    if (!(go is Solid solid) || solid.Volume <= 0) continue;

                    PlanarFace topFace = TopFace(solid);
                    if (topFace == null) continue;

                    foreach (EdgeArray loop in topFace.EdgeLoops)
                    {
                        foreach (Edge edge in loop)
                        {
                            Reference r = edge.Reference;
                            if (r == null) continue;

                            Curve c;
                            try { c = edge.AsCurve(); }
                            catch { continue; }
                            if (c == null) continue;

                            allEdges.Add((r, c.Evaluate(0.5, true)));
                        }
                    }

                    Polygon poly = ToPolygon(gf, topFace.GetEdgesAsCurveLoops().OrderByDescending(LoopArea).FirstOrDefault());

                    if (poly != null)
                        footprints.Add(poly);
                }
            }

            Geometry region = UnionAll(footprints);


            if (region == null)
                return allEdges.Select(e => e.Reference).ToList();

            foreach (var e in allEdges)
            {
                var p = gf.CreatePoint(new Coordinate(e.Mid.X, e.Mid.Y));

                if (!region.Contains(p))
                    result.Add(e.Reference);
            }

            return result;
        }

        private static PlanarFace TopFace(Solid solid)
        {
            return solid.Faces
                .OfType<PlanarFace>()
                .Where(f => f.FaceNormal.Z > 0.9)   // upward-facing
                .OrderByDescending(f => f.Area)
                .FirstOrDefault();
        }

        private static Polygon ToPolygon(GeometryFactory gf, CurveLoop loop)
        {
            if (loop == null) return null;

            var coords = new List<Coordinate>();
            foreach (Curve c in loop)
            {
                XYZ p = c.GetEndPoint(0);
                coords.Add(new Coordinate(p.X, p.Y));
            }

            if (coords.Count < 3) return null;
            coords.Add(coords[0]); // close the ring

            try { return gf.CreatePolygon(gf.CreateLinearRing(coords.ToArray())); }
            catch { return null; }
        }

        private static double LoopArea(CurveLoop loop)
        {
            var pts = new List<XYZ>();
            foreach (Curve c in loop)
                pts.Add(c.GetEndPoint(0));

            if (pts.Count < 3) return 0;

            double area = 0.0;
            for (int i = 0; i < pts.Count; i++)
            {
                XYZ p1 = pts[i];
                XYZ p2 = pts[(i + 1) % pts.Count];
                area += (p1.X * p2.Y) - (p2.X * p1.Y);
            }
            return Math.Abs(area) * 0.5;
        }

        private static Geometry UnionAll(IList<Geometry> polys)
        {
            if (polys == null || polys.Count == 0) return null;

            Geometry union = polys[0];
            for (int i = 1; i < polys.Count; i++)
            {
                try 
                { 
                    union = union.Union(polys[i]); 
                }
                catch { /* skip a polygon that won't union; keep the rest */ }
            }
            return union;
        }
    }
}
