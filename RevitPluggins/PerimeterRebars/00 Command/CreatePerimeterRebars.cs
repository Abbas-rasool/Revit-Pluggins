using Autodesk.Revit.UI;
using RevitPluggins.PerimeterRebars.RebarPlacementClasses;
using RevitPluggins.PerimeterRebars.Tools;
using System.Linq;
using RevitPluggins.PerimeterRebars.UI;
using RevitPluggins.Common;
using RevitPluggins.GeometryHelpers;
using RevitPluggins.Selection;

namespace RevitPluggins.PerimeterRebars
{
    public class CreatePerimeterRebars
    {
        private const string FloorFillTypeName = "DIAGONAL DOWN - TRANSPARENT";

        private ElementId            _floorFillRegionId;
        private bool                 _floorFillRegionOwned; // false when user supplied the region
        private List<ElementId>      _gridLineIds;
        private ElementId            _gridLineGroupId;
        private CurveLoop            _floorPolygon;
        private List<FamilyInstance> _firstLayerColumns;
        private List<FamilyInstance> _secondLayerColumns;
        private double               _edgeColumnMaxDistance;

        public bool HasDefinedRegions =>
            _floorFillRegionId != null && _floorFillRegionId != ElementId.InvalidElementId;

        public void DefineRegions(PerimeterRebarInputs inputs, UIApplication uiApp)
        {
            UIDocument uidoc = uiApp.ActiveUIDocument;
            Document   doc   = uidoc.Document;

            var uiHelper = new UISelectionHelper(uidoc);

            CurveLoop floorPolygon;
            Reference selectedRegionRef = null;

            if (inputs.SelectionMethod == SelectionMethod.ContinuousFloor)
            {
                var floorRefs = uiHelper.PickFloors();
                if (floorRefs == null || floorRefs.Count == 0)
                {
                    Autodesk.Revit.UI.TaskDialog.Show("Cancelled", "No floor was selected.");
                    return;
                }
                floorPolygon = new FloorBoundryGenerator().GetOuterFloorPerimeter(floorRefs, doc);
            }
            else
            {
                selectedRegionRef = uiHelper.PickFilledRegion();
                if (selectedRegionRef == null)
                {
                    Autodesk.Revit.UI.TaskDialog.Show("Cancelled", "No filled region was selected.");
                    return;
                }
                floorPolygon = FlatCurveLoopFromFilledRegion(selectedRegionRef, uidoc);
            }

            // Resolve fill type before opening the transaction so we can show a warning
            // without risk of it aborting the tx.  Only needed when we create a new region.
            ElementId floorFillTypeId   = null;
            bool      showStandardWarning = false;

            if (selectedRegionRef == null)   // ContinuousFloor path — we create the region
            {
                floorFillTypeId = FindFillTypeByName(doc, FloorFillTypeName);
                if (floorFillTypeId == null)
                {
                    showStandardWarning = true;
                    floorFillTypeId = new FilteredElementCollector(doc)
                        .OfClass(typeof(FilledRegionType))
                        .Cast<FilledRegionType>()
                        .First().Id;
                }
            }

            if (floorPolygon == null)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Error", "Could not extract floor boundary.");
                return;
            }

            Autodesk.Revit.DB.View view = uidoc.ActiveView;

            var finder     = new EdgeColumnFinder();
            var firstLayer = finder.FindEdgeColumns(doc, view, floorPolygon, inputs.EdgeColumnMaxDistance);

            if (firstLayer.Count == 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Info", "No columns found within the edge distance.");
                return;
            }

            var firstLayerIds = firstLayer.Select(c => c.Id).ToHashSet();
            var secondLayer   = finder.FindEdgeColumns(doc, view, floorPolygon, inputs.GridSearchRadiusFt, firstLayerIds);

            var allGridColumns = firstLayer.Concat(secondLayer).ToList();
            var gridLines      = new ColumnGridLineBuilder(inputs.GridClusterTolerance)
                                     .Build(allGridColumns, floorPolygon);

            using (var tx = new Transaction(doc, "Define Perimeter Regions"))
            {
                tx.Start();

                // Remove any previous output
                if (_floorFillRegionOwned) SafeDelete(doc, _floorFillRegionId);

                if (_gridLineGroupId != null && _gridLineGroupId != ElementId.InvalidElementId
                    && doc.GetElement(_gridLineGroupId) != null)
                    SafeDelete(doc, _gridLineGroupId);
                else if (_gridLineIds != null)
                    foreach (var id in _gridLineIds) SafeDelete(doc, id);

                ApplyColumnColor(view, firstLayer,  new Autodesk.Revit.DB.Color(0,   0,   255));
                ApplyColumnColor(view, secondLayer, new Autodesk.Revit.DB.Color(0,   180, 0  ));

                if (selectedRegionRef != null)
                {
                    // User selected an existing fill region — borrow it as-is
                    _floorFillRegionId    = doc.GetElement(selectedRegionRef).Id;
                    _floorFillRegionOwned = false;
                }
                else
                {
                    var floorReg = FilledRegion.Create(doc, floorFillTypeId, view.Id,
                        new List<CurveLoop> { floorPolygon });
                    _floorFillRegionId    = floorReg.Id;
                    _floorFillRegionOwned = true;
                }

                // Draw grid lines in orange
                var ogs = new OverrideGraphicSettings();
                ogs.SetProjectionLineColor(new Autodesk.Revit.DB.Color(255, 140, 0));
                ogs.SetProjectionLineWeight(3);

                _gridLineIds = new List<ElementId>();
                foreach (var curve in gridLines)
                {
                    var dl = doc.Create.NewDetailCurve(view, curve);
                    view.SetElementOverrides(dl.Id, ogs);
                    _gridLineIds.Add(dl.Id);
                }

                if (_gridLineIds.Count > 0)
                {
                    var grp = doc.Create.NewGroup(_gridLineIds);
                    view.SetElementOverrides(grp.Id, ogs);
                    _gridLineGroupId = grp.Id;
                }

                tx.Commit();
            }

            _floorPolygon          = floorPolygon;
            _firstLayerColumns     = firstLayer;
            _secondLayerColumns    = secondLayer;
            _edgeColumnMaxDistance = inputs.EdgeColumnMaxDistance;

            if (showStandardWarning)
                Autodesk.Revit.UI.TaskDialog.Show("Missing Project Standard",
                    "Please make sure to load the required project standards into your project!");

            Autodesk.Revit.UI.TaskDialog.Show("Regions Defined",
                "Floor boundary and column grid lines (orange) are shown.\n\n" +
                "Edit the grid group if needed — add, move, or delete lines — then click ADD REBARS.");
        }

        // ── Add Rebars ───────────────────────────────────────────────────────

        public void AddRebars(PerimeterRebarInputs inputs, UIApplication uiApp)
        {
            if (!HasDefinedRegions)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Error", "Run Define Regions first.");
                return;
            }

            UIDocument uidoc = uiApp.ActiveUIDocument;
            Document   doc   = uidoc.Document;
            Autodesk.Revit.DB.View view = uidoc.ActiveView;

            var gridLines   = ReadGroupOrLines(doc, _gridLineGroupId, _gridLineIds);

            var floorRegion = doc.GetElement(_floorFillRegionId) as FilledRegion;
            var floorCurves = floorRegion != null
                ? BoundaryToCurves(floorRegion)
                : _floorPolygon.ToList();

            var familyProvider = new PerimeterRebarFamilyProvider(uiApp);
            var rebarSymbol    = familyProvider.RebarSymbol;
            var tagSymbol      = familyProvider.TagSymbol;

            if (rebarSymbol == null)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Error", "Perimeter rebar family could not be loaded. Check ToolData.json.");
                return;
            }

            var resolver  = new PerimeterRebarResolver(
                gridLines, floorCurves, _firstLayerColumns,
                _edgeColumnMaxDistance, inputs.SlabThicknessInFeet);

            var rebarData = resolver.Resolve();

            if (rebarData.Count == 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Info", "No rebar positions could be resolved from the current grid.");
                return;
            }

            var exclusionZoneSegments = resolver.GetExclusionZoneSegments();
            var inclusionZoneSegments = resolver.GetInclusionZoneSegments();

            using (var tx = new Transaction(doc, "Add Perimeter Rebars"))
            {
                tx.Start();

                DrawDebugSegments(doc, view, exclusionZoneSegments, new Autodesk.Revit.DB.Color(255, 0,   0));
                DrawDebugSegments(doc, view, inclusionZoneSegments, new Autodesk.Revit.DB.Color(0,   180, 0));
                new PerimeterRebarPlacer(doc, view, rebarSymbol, tagSymbol).Place(rebarData);

                var mb1Zones = resolver.GetMB1EdgeZones();
                new MB1RebarCreator(doc, view).Place(mb1Zones);

                if (_floorFillRegionOwned) SafeDelete(doc, _floorFillRegionId);

                if (_gridLineGroupId != null && _gridLineGroupId != ElementId.InvalidElementId
                    && doc.GetElement(_gridLineGroupId) != null)
                    SafeDelete(doc, _gridLineGroupId);
                else if (_gridLineIds != null)
                    foreach (var id in _gridLineIds) SafeDelete(doc, id);

                ResetColumnColors(view, _firstLayerColumns);
                ResetColumnColors(view, _secondLayerColumns);

                tx.Commit();
            }

            _floorFillRegionId     = null;
            _floorFillRegionOwned  = false;
            _gridLineIds           = null;
            _gridLineGroupId       = null;
            _floorPolygon          = null;
            _firstLayerColumns     = null;
            _secondLayerColumns    = null;
            _edgeColumnMaxDistance = 0;

            Autodesk.Revit.UI.TaskDialog.Show("Done", $"{rebarData.Count} perimeter rebar(s) placed.");
        }

        // ── Fill region type helpers ─────────────────────────────────────────

        private static ElementId FindFillTypeByName(Document doc, string name) =>
            new FilteredElementCollector(doc)
                .OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>()
                .FirstOrDefault(t => t.Name == name)
                ?.Id;

        // ── Curve / geometry helpers ─────────────────────────────────────────

        // Reads back spoke lines from the group (includes any lines the user added
        // inside group-edit mode), falling back to stored IDs if the group was ungrouped.
        private static List<Curve> ReadGroupOrLines(Document doc, ElementId groupId, List<ElementId> fallbackIds)
        {
            if (groupId != null && groupId != ElementId.InvalidElementId)
            {
                var grp = doc.GetElement(groupId) as Group;
                if (grp != null)
                {
                    return grp.GetMemberIds()
                        .Select(id => doc.GetElement(id) as DetailCurve)
                        .Where(dc => dc != null)
                        .Select(dc => (Curve)Line.CreateBound(
                            new XYZ(dc.GeometryCurve.GetEndPoint(0).X, dc.GeometryCurve.GetEndPoint(0).Y, 0),
                            new XYZ(dc.GeometryCurve.GetEndPoint(1).X, dc.GeometryCurve.GetEndPoint(1).Y, 0)))
                        .ToList();
                }
            }

            return ReadBoundaryLines(doc, fallbackIds);
        }

        private static List<Curve> ReadBoundaryLines(Document doc, List<ElementId> ids)
        {
            if (ids == null) return new List<Curve>();

            return ids
                .Select(id => doc.GetElement(id) as DetailCurve)
                .Where(dc => dc != null)
                .Select(dc => (Curve)Line.CreateBound(
                    new XYZ(dc.GeometryCurve.GetEndPoint(0).X, dc.GeometryCurve.GetEndPoint(0).Y, 0),
                    new XYZ(dc.GeometryCurve.GetEndPoint(1).X, dc.GeometryCurve.GetEndPoint(1).Y, 0)))
                .ToList();
        }

        private static List<Curve> BoundaryToCurves(FilledRegion region)
        {
            var bounds = region.GetBoundaries();
            if (bounds == null || bounds.Count == 0) return new List<Curve>();
            return bounds[0].ToList();
        }

        private static CurveLoop FlatCurveLoopFromFilledRegion(Reference regionRef, UIDocument uidoc)
        {
            var region = uidoc.Document.GetElement(regionRef) as FilledRegion;
            if (region == null) return null;

            var boundaries = region.GetBoundaries();
            if (boundaries == null || boundaries.Count == 0) return null;

            var loop = new CurveLoop();
            foreach (Curve c in boundaries[0])
            {
                XYZ s = new XYZ(c.GetEndPoint(0).X, c.GetEndPoint(0).Y, 0);
                XYZ e = new XYZ(c.GetEndPoint(1).X, c.GetEndPoint(1).Y, 0);
                loop.Append(Line.CreateBound(s, e));
            }
            return loop;
        }

        // ── View / element helpers ───────────────────────────────────────────

        private static void ApplyColumnColor(Autodesk.Revit.DB.View view, List<FamilyInstance> columns, Autodesk.Revit.DB.Color color)
        {
            var ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(color);
            ogs.SetCutLineColor(color);

            foreach (var col in columns)
                view.SetElementOverrides(col.Id, ogs);
        }

        private static void ResetColumnColors(Autodesk.Revit.DB.View view, List<FamilyInstance> columns)
        {
            if (columns == null) return;

            var empty = new OverrideGraphicSettings();

            foreach (var col in columns)
                view.SetElementOverrides(col.Id, empty);
        }

        private static void SafeDelete(Document doc, ElementId id)
        {
            if (id == null || id == ElementId.InvalidElementId) return;

            if (doc.GetElement(id) != null)
                doc.Delete(id);
        }

        private static void DrawDebugSegments(
            Document                   doc,
            Autodesk.Revit.DB.View     view,
            List<(XYZ Start, XYZ End)> segments,
            Autodesk.Revit.DB.Color    color)
        {
            var ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(color);
            ogs.SetProjectionLineWeight(6);

            foreach (var (start, end) in segments)
            {
                if (start.DistanceTo(end) < 0.001) continue;

                var line       = Line.CreateBound(start, end);
                var detailLine = doc.Create.NewDetailCurve(view, line);
                view.SetElementOverrides(detailLine.Id, ogs);
            }
        }
    }
}
