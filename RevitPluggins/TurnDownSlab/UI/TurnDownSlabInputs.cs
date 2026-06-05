using Autodesk.Revit.UI;

namespace RevitPluggins.TurnDownSlab.UI
{
    public enum TurnDownPlacementMode
    {
        WholeSlab,
        SelectEdges
    }

    public class TurnDownSlabInputs
    {
        public TurnDownSlabInputs(ExternalCommandData commandData)
        {
            CommandData = commandData;
        }

        public ExternalCommandData CommandData { get; set; }
        public TurnDownProfileType ProfileType { get; set; } = TurnDownProfileType.Simple;
        public double WidthInches { get; set; }
        public double Depth1Inches { get; set; } 
        public double Depth2Inches { get; set; }   // Brick only: D2.
        public double SlopeDegrees { get; set; }

        // --- Reuse an existing project type instead of building one from the fields ---
        public bool UseExistingType { get; set; }
        public string ExistingTypeName { get; set; }

        public TurnDownPlacementMode PlacementMode { get; set; } = TurnDownPlacementMode.SelectEdges;
    }
}
