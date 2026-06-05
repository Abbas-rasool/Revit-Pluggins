using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using RevitPlugins.TurnDownSlab.UI;
using System.Collections.Generic;

namespace RevitPlugins.TurnDownSlab
{
    public static class SlabEdgePlacer
    {
        public static int Place(UIDocument uidoc, SlabEdgeType edgeType, TurnDownPlacementMode mode)
        {
            Document doc = uidoc.Document;

            switch (mode)
            {
                case TurnDownPlacementMode.WholeSlab:
                    return CreateSlabEdges(doc, edgeType, PickWholeSlabEdges(uidoc));

                case TurnDownPlacementMode.SelectEdges:
                default:
                    return CreateSlabEdges(doc, edgeType, PickEdges(uidoc));
            }
        }

        private static IList<Reference> PickEdges(UIDocument uidoc)
        {
            var filter = new FloorEdgeSelectionFilter();

            return uidoc.Selection.PickObjects(ObjectType.Edge, filter, "Pick slab edge(s), then Finish");
        }

        private static IList<Reference> PickWholeSlabEdges(UIDocument uidoc)
        {
            var filter = new FloorEdgeSelectionFilter();
            IList<Reference> floorRefs = uidoc.Selection.PickObjects(ObjectType.Element, filter, "Select floor(s), then Finish");

            return WholeSlabEdgeResolver.GetEdgeReferences(uidoc.Document, floorRefs);
        }

        private static int CreateSlabEdges(Document doc, SlabEdgeType edgeType, IList<Reference> edges)
        {
            int created = 0;

            using (Transaction t = new Transaction(doc, "Create Turn-Down Slab Edges"))
            {
                t.Start();
                foreach (Reference reference in edges)
                {
                    // Skip edges Revit refuses (non-planar, already edged, etc.) instead of aborting
                    // the whole batch — but count them so we can tell the user nothing landed.
                    try
                    {
                        doc.Create.NewSlabEdge(edgeType, reference);
                        created++;
                    }
                    catch
                    {
                    }
                }
                t.Commit();
            }

            if (created == 0 && edges != null && edges.Count > 0)
                Autodesk.Revit.UI.TaskDialog.Show("Turn-Down Slab Edge",
                    "No slab edge could be created on the selected edge(s). They may be curved/non-planar " +
                    "or already have this slab edge applied.");

            return created;
        }
    }
}
