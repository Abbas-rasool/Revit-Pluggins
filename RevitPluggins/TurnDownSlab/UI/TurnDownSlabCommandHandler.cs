using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitPluggins.TurnDownSlab.UI;
using System;

namespace RevitPluggins.TurnDownSlab.UI
{
    public class TurnDownSlabCommandHandler : IExternalEventHandler
    {
        public TurnDownSlabInputs Inputs { get; set; }
        public string GetName() => "Turn-Down Slab Edge Event Handler";

        public void Execute(UIApplication app)
        {
            if (Inputs == null)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Turn-Down Slab Edge", "Inputs were not initialized.");
                return;
            }

            UIDocument uidoc = app.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                SlabEdgeType edgeType = ResolveSlabEdgeType(doc, app);

                if (edgeType == null)
                    return;

                int created = SlabEdgePlacer.Place(uidoc, edgeType, Inputs.PlacementMode);

                if (created > 0)
                    Autodesk.Revit.UI.TaskDialog.Show("Turn-Down Slab Edge", $"{created} slab edge(s) created.");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // User pressed Esc during picking — nothing to report.
            }
            catch (Exception ex)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Turn-Down Slab Edge — Error", ex.Message);
            }
        }

        private SlabEdgeType ResolveSlabEdgeType(Document doc, UIApplication app)
        {
            if (Inputs.UseExistingType)
            {
                SlabEdgeType existing = SlabEdgeTypeFactory.FindByName(doc, Inputs.ExistingTypeName);
                if (existing == null)
                    Autodesk.Revit.UI.TaskDialog.Show("Turn-Down Slab Edge",
                        $"Slab Edge type '{Inputs.ExistingTypeName}' was not found in the project.");

                return existing;
            }

            var provider = new TurnDownFamilyProvider(app);
            FamilySymbol baseProfile = Inputs.ProfileType == TurnDownProfileType.Brick
                ? provider.ProfileFamilyBrick
                : provider.ProfileFamilySimple;

            SlabEdgeType edgeType;
            using (Transaction tx = new Transaction(doc, "Create Turn-Down Slab Edge Type"))
            {
                tx.Start();

                FamilySymbol profileSymbol = SlabEdgeProfileTypeFactory.GetOrCreate(
                    doc, Inputs.ProfileType, baseProfile,
                    Inputs.WidthInches, Inputs.Depth1Inches, Inputs.Depth2Inches, Inputs.SlopeDegrees);
                edgeType = SlabEdgeTypeFactory.GetOrCreate(doc, profileSymbol);

                tx.Commit();
            }

            return edgeType;
        }
    }
}
