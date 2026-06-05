using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitPluggins.PerimeterRebars.RebarPlacementClasses
{
    public class PerimeterRebarResolver
    {
        private const double ColumnExclusionMarginFt = 1.5;  // clear extension added each side of a column
        private const double MinRebarZoneFt          = 3.0;  // gaps narrower than this become part of exclusion
        private const double MinEdgeLengthFt         = 5.0;  // edges shorter than this are skipped entirely
        private const double FallbackInsetFt         = 3.0;  // fixed inset when no grid line is hit
        private const double RayOriginOffsetFt       = 1e-4;
        private const double ParallelEpsilon         = 1e-10;
        private const double MB1MinEdgeLengthFt = 8.0;

        private readonly List<Curve>          _gridLines;
        private readonly List<Curve>          _floorBoundaryEdges;
        private readonly List<FamilyInstance> _firstLayerColumns;
        private readonly double               _edgeColumnMaxDistance;
        private readonly double               _slabThicknessInFeet;

        public PerimeterRebarResolver(
            List<Curve>          gridLines,
            List<Curve>          floorBoundaryEdges,
            List<FamilyInstance> firstLayerColumns,
            double               edgeColumnMaxDistance,
            double               slabThicknessInFeet)
        {
            _gridLines             = gridLines;
            _floorBoundaryEdges    = floorBoundaryEdges;
            _firstLayerColumns     = firstLayerColumns;
            _edgeColumnMaxDistance = edgeColumnMaxDistance;
            _slabThicknessInFeet   = slabThicknessInFeet;
        }

        // ── Public entry points ──────────────────────────────────────────────

        public List<PerimeterRebarData> Resolve()
        {
            var rebars = new List<PerimeterRebarData>();

            foreach (var floorEdge in _floorBoundaryEdges)
                ResolveRebarZonesAlongEdge(floorEdge, rebars);

            return rebars;
        }

        public List<MB1ZoneData> GetMB1EdgeZones()
        {
            var result = new List<MB1ZoneData>();

            foreach (var floorEdge in _floorBoundaryEdges)
            {
                XYZ    edgeStart  = FlattenToXY(floorEdge.GetEndPoint(0));
                XYZ    edgeEnd    = FlattenToXY(floorEdge.GetEndPoint(1));
                double edgeLength = edgeStart.DistanceTo(edgeEnd);

                if (edgeLength < MB1MinEdgeLengthFt) continue;

                XYZ edgeDir      = (edgeEnd - edgeStart).Normalize();
                XYZ inwardNormal = ComputeOutwardNormal(edgeStart, edgeEnd).Negate();

                result.Add(new MB1ZoneData
                {
                    EdgeStart    = edgeStart,
                    EdgeDir      = edgeDir,
                    InwardNormal = inwardNormal,
                    EdgeLength   = edgeLength,
                });
            }

            return result;
        }

        // Returns world-space exclusion zone segments for debug drawing (red).
        // Uses the same tightened zones as Resolve() so the lines match exactly.
        public List<(XYZ Start, XYZ End)> GetExclusionZoneSegments()
            => CollectZoneSegments(useOpenSlots: false);

        // Returns world-space inclusion (rebar) zone segments for debug drawing (green).
        public List<(XYZ Start, XYZ End)> GetInclusionZoneSegments()
            => CollectZoneSegments(useOpenSlots: true);

        private List<(XYZ Start, XYZ End)> CollectZoneSegments(bool useOpenSlots)
        {
            var segments = new List<(XYZ Start, XYZ End)>();

            foreach (var floorEdge in _floorBoundaryEdges)
            {
                XYZ    edgeStart  = FlattenToXY(floorEdge.GetEndPoint(0));
                XYZ    edgeEnd    = FlattenToXY(floorEdge.GetEndPoint(1));
                double edgeLength = edgeStart.DistanceTo(edgeEnd);
               
                if (edgeLength < MinEdgeLengthFt) continue;

                XYZ edgeDirection = (edgeEnd - edgeStart).Normalize();

                var rawZones  = BuildExclusionZonesForEdge(edgeStart, edgeDirection, edgeLength);
                var merged    = MergeOverlappingZones(rawZones);
                var tightened = CloseSmallGaps(merged, edgeLength);
                var zones     = useOpenSlots ? FindOpenSlots(tightened, edgeLength) : tightened;

                foreach (var (zoneStart, zoneEnd) in zones)
                {
                    XYZ p1 = edgeStart + edgeDirection.Multiply(zoneStart);
                    XYZ p2 = edgeStart + edgeDirection.Multiply(zoneEnd);
                    segments.Add((p1, p2));
                }
            }

            return segments;
        }

        // ── Rebar zone resolution ────────────────────────────────────────────

        // For one floor edge: builds exclusion zones, closes small gaps,
        // then places one rebar at the midpoint of each remaining open slot.
        private void ResolveRebarZonesAlongEdge(Curve floorEdge, List<PerimeterRebarData> rebars)
        {
            XYZ    edgeStart  = FlattenToXY(floorEdge.GetEndPoint(0));
            XYZ    edgeEnd    = FlattenToXY(floorEdge.GetEndPoint(1));
            double edgeLength = edgeStart.DistanceTo(edgeEnd);

            if (edgeLength < MinEdgeLengthFt) return;

            XYZ edgeDirection = (edgeEnd - edgeStart).Normalize();
            XYZ outwardNormal = ComputeOutwardNormal(edgeStart, edgeEnd);

            var rawZones  = BuildExclusionZonesForEdge(edgeStart, edgeDirection, edgeLength);
            var merged    = MergeOverlappingZones(rawZones);
            var tightened = CloseSmallGaps(merged, edgeLength);
            var rebarZones = FindOpenSlots(tightened, edgeLength);

            foreach (var (slotStart, slotEnd) in rebarZones)
            {
                double halfSlot        = 0.5 * (slotEnd - slotStart);
                double midT            = slotStart + halfSlot;
                XYZ    originOnEdge    = edgeStart + edgeDirection.Multiply(midT);

                var rebar = TryResolveRebarAt(originOnEdge, outwardNormal, halfSlot);

                if (rebar != null) rebars.Add(rebar);
            }
        }

        // ── Rebar arm resolution ─────────────────────────────────────────────

        // Resolves one rebar from a point on the floor edge.
        // Fires a ray inward and collects the first two grid line hits:
        //   first hit  → InsertionPoint (ExteriorLength from edge)
        //   second hit → interior arm end (InteriorLength from InsertionPoint), not hooked
        //   no second hit → falls back to floor boundary, hooked
        // Returns null when no grid line is hit at all.
        private PerimeterRebarData TryResolveRebarAt(
            XYZ originOnEdge, XYZ outwardNormal, double halfSlot)
        {
            XYZ inwardDirection = outwardNormal.Negate();

            var (d1, d2) = CastRayTwoHits(originOnEdge, inwardDirection, _gridLines);
            if (d1 <= 0) return null;

            XYZ insertionPoint = originOnEdge + inwardDirection.Multiply(d1);

            // From the insertion point: compare next grid line vs floor boundary.
            // Floor boundary is a hard cap — whichever comes first wins.
            double toNextGrid = d2 > 0 ? d2 - d1 : double.MaxValue;
            double toFloor    = CastRay(insertionPoint, inwardDirection, _floorBoundaryEdges);

            double interiorLength;
            bool   isHooked;

            if (toFloor > 0 && toFloor <= toNextGrid)
            {
                interiorLength = toFloor;
                isHooked       = true;
            }
            else if (toNextGrid < double.MaxValue)
            {
                interiorLength = toNextGrid;
                isHooked       = false;
            }
            else
            {
                interiorLength = FallbackInsetFt;
                isHooked       = true;
            }

            if (interiorLength <= 0) return null;

            return new PerimeterRebarData
            {
                InsertionPoint            = insertionPoint,
                OutwardNormal             = outwardNormal,
                ExteriorLength            = d1,
                InteriorLength            = interiorLength,
                InteriorHooked            = isHooked,
                ArrowTributaryLengthLeft  = halfSlot,
                ArrowTributaryLengthRight = halfSlot,
            };
        }

        // ── Ray casting ──────────────────────────────────────────────────────

        // Returns the distances to the nearest and second-nearest forward hits, or -1 for each miss.
        private static (double first, double second) CastRayTwoHits(XYZ origin, XYZ direction, List<Curve> curves)
        {
            var hits = new List<double>();

            foreach (var curve in curves)
            {
                double dist = RaySegmentIntersectionDistance(
                    origin, direction,
                    curve.GetEndPoint(0), curve.GetEndPoint(1));

                if (dist > RayOriginOffsetFt) hits.Add(dist);
            }

            hits.Sort();
            return (hits.Count > 0 ? hits[0] : -1,
                    hits.Count > 1 ? hits[1] : -1);
        }

        // Fires a ray from origin in the given direction against all curve segments
        // and returns the distance to the nearest forward hit, or -1 if none.
        // Hits within RayOriginOffsetFt of the origin are ignored (prevents self-hits
        // when the origin lies exactly on a hull boundary).
        private static double CastRay(XYZ origin, XYZ direction, List<Curve> curves)
        {
            double nearestHit = double.MaxValue;
            bool   didHit     = false;

            foreach (var curve in curves)
            {
                double dist = RaySegmentIntersectionDistance(
                    origin, direction,
                    curve.GetEndPoint(0), curve.GetEndPoint(1));

                if (dist > RayOriginOffsetFt && dist < nearestHit)
                {
                    nearestHit = dist;
                    didHit     = true;
                }
            }

            return didHit ? nearestHit : -1;
        }

        // Returns the distance t ≥ 0 along the ray at which it intersects the segment
        // [segStart, segEnd], or -1 if the ray misses, is parallel, or hits a back-face.
        private static double RaySegmentIntersectionDistance(
            XYZ rayOrigin, XYZ rayDirection, XYZ segStart, XYZ segEnd)
        {
            double rayDirX = rayDirection.X, rayDirY = rayDirection.Y;
            double segDirX = segEnd.X - segStart.X;
            double segDirY = segEnd.Y - segStart.Y;

            double denominator = rayDirX * segDirY - rayDirY * segDirX;
            if (Math.Abs(denominator) < ParallelEpsilon) return -1; // parallel / collinear

            double offsetX = segStart.X - rayOrigin.X;
            double offsetY = segStart.Y - rayOrigin.Y;

            double t = (offsetX * segDirY - offsetY * segDirX) / denominator; // along ray
            double u = (offsetX * rayDirY - offsetY * rayDirX) / denominator; // along segment

            bool hitsSegment = u >= -1e-6 && u <= 1 + 1e-6;
            return (t > 0 && hitsSegment) ? t : -1;
        }

        // ── Exclusion zone building ──────────────────────────────────────────

        // Projects every first-layer column onto this edge and returns one
        // exclusion zone per qualifying column (unsorted, may overlap).
        private List<(double start, double end)> BuildExclusionZonesForEdge(
            XYZ edgeStart, XYZ edgeDirection, double edgeLength)
        {
            var zones = new List<(double start, double end)>();

            foreach (var column in _firstLayerColumns)
            {
                if (column.Location is not LocationPoint lp) continue;

                XYZ    center = FlattenToXY(lp.Point);
                double t      = (center - edgeStart).DotProduct(edgeDirection);

                // Accept columns slightly beyond the endpoints — captures step-corner
                // columns whose projection falls in the gap between adjacent edge segments.
                if (t < -ColumnExclusionMarginFt || t > edgeLength + ColumnExclusionMarginFt) continue;
                t = Math.Max(0, Math.Min(edgeLength, t));

                double perpDist = PerpendicularDistanceToEdge(center, edgeStart, edgeDirection);
                if (perpDist > _edgeColumnMaxDistance) continue;

                double halfWidth = ComputeColumnHalfWidthAlongDirection(column, edgeDirection);
                double zoneStart = Math.Max(0,          t - ColumnExclusionMarginFt * _slabThicknessInFeet - halfWidth);
                double zoneEnd   = Math.Min(edgeLength, t + ColumnExclusionMarginFt * _slabThicknessInFeet + halfWidth);

                if (zoneEnd > zoneStart) zones.Add((zoneStart, zoneEnd));
            }

            return zones;
        }

        // ── Gap / slot helpers ───────────────────────────────────────────────

        // Sorts and merges any overlapping exclusion zones into a minimal set.
        private static List<(double start, double end)> MergeOverlappingZones(
            List<(double start, double end)> rawZones)
        {
            rawZones.Sort((a, b) => a.start.CompareTo(b.start));
            var merged = new List<(double start, double end)>();

            foreach (var zone in rawZones)
            {
                if (merged.Count == 0 || zone.start > merged[merged.Count - 1].end)
                    merged.Add(zone);
                else
                {
                    var last = merged[merged.Count - 1];
                    merged[merged.Count - 1] = (last.start, Math.Max(last.end, zone.end));
                }
            }

            return merged;
        }

        // Absorbs any gap narrower than MinRebarZoneFt into its surrounding exclusion
        // zones, including the leading gap [0 → first zone] and the trailing gap
        // [last zone → edgeLength], so tiny corner slivers never produce stray rebars.
        private static List<(double start, double end)> CloseSmallGaps(
            List<(double start, double end)> sortedMergedZones, double edgeLength)
        {
            if (sortedMergedZones.Count == 0) return sortedMergedZones;

            var zones = new List<(double start, double end)>(sortedMergedZones);

            // Leading gap
            if (zones[0].start < MinRebarZoneFt)
                zones[0] = (0, zones[0].end);

            // Inter-zone gaps: restart after each merge because indices shift
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < zones.Count - 1; i++)
                {
                    double gap = zones[i + 1].start - zones[i].end;
                    if (gap < MinRebarZoneFt)
                    {
                        zones[i] = (zones[i].start, zones[i + 1].end);
                        zones.RemoveAt(i + 1);
                        changed = true;
                        break;
                    }
                }
            }

            // Trailing gap
            int last = zones.Count - 1;
            if (edgeLength - zones[last].end < MinRebarZoneFt)
                zones[last] = (zones[last].start, edgeLength);

            return zones;
        }

        // Returns the open (non-excluded) spans along the edge.
        private static List<(double start, double end)> FindOpenSlots(
            List<(double start, double end)> exclusionZones, double edgeLength)
        {
            var    openSlots   = new List<(double start, double end)>();
            double coveredUpTo = 0;

            foreach (var (zoneStart, zoneEnd) in exclusionZones)
            {
                if (zoneStart > coveredUpTo) openSlots.Add((coveredUpTo, zoneStart));
                coveredUpTo = Math.Max(coveredUpTo, zoneEnd);
            }

            if (coveredUpTo < edgeLength) openSlots.Add((coveredUpTo, edgeLength));
            return openSlots;
        }

        // ── Geometry utilities ───────────────────────────────────────────────

        // Returns the unit normal that points AWAY from the floor polygon interior.
        // Uses the polygon winding (signed area via the shoelace theorem) so it is
        // correct for both convex and concave floor shapes, regardless of edge-length
        // distribution.
        private XYZ ComputeOutwardNormal(XYZ edgeStart, XYZ edgeEnd)
        {
            XYZ    edgeDirection = (edgeEnd - edgeStart).Normalize();
            double signedArea    = ComputeSignedPolygonArea(_floorBoundaryEdges);

            // For a CCW polygon (positive signed area viewed from +Z) the
            // right-hand perpendicular of the edge direction points outward.
            // For a CW polygon the left-hand perpendicular is outward.
            return signedArea >= 0
                ? new XYZ( edgeDirection.Y, -edgeDirection.X, 0)  // right perp — outward for CCW
                : new XYZ(-edgeDirection.Y,  edgeDirection.X, 0); // left  perp — outward for CW
        }

        // Shoelace (surveyor's) formula — positive result means CCW winding in the XY plane.
        private static double ComputeSignedPolygonArea(List<Curve> curves)
        {
            double area = 0;
            foreach (var curve in curves)
            {
                XYZ p1 = curve.GetEndPoint(0);
                XYZ p2 = curve.GetEndPoint(1);
                area += p1.X * p2.Y - p2.X * p1.Y;
            }
            return area * 0.5;
        }

        // 2D perpendicular distance from a point to an infinite line
        // defined by a point and a unit direction (cross product magnitude).
        private static double PerpendicularDistanceToEdge(
            XYZ point, XYZ edgeStart, XYZ edgeDirection)
        {
            XYZ offset = point - edgeStart;
            return Math.Abs(offset.X * edgeDirection.Y - offset.Y * edgeDirection.X);
        }

        // Projects the column's bounding box corners onto the given direction
        // and returns half the total projected width.
        private static double ComputeColumnHalfWidthAlongDirection(
            FamilyInstance column, XYZ direction)
        {
            BoundingBoxXYZ bb = column.get_BoundingBox(null);
            if (bb == null) return 0;

            double[] cornerProjections =
            {
                bb.Min.X * direction.X + bb.Min.Y * direction.Y,
                bb.Max.X * direction.X + bb.Min.Y * direction.Y,
                bb.Max.X * direction.X + bb.Max.Y * direction.Y,
                bb.Min.X * direction.X + bb.Max.Y * direction.Y
            };

            return 0.5 * (cornerProjections.Max() - cornerProjections.Min());
        }

        private static XYZ FlattenToXY(XYZ point) => new XYZ(point.X, point.Y, 0);
    }
}
