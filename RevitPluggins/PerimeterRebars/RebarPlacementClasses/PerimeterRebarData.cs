using Autodesk.Revit.DB;

namespace RevitPluggins.PerimeterRebars.RebarPlacementClasses
{
    public class PerimeterRebarData
    {
        public XYZ InsertionPoint { get; set; }
        public XYZ OutwardNormal  { get; set; }     // unit vector toward floor edge

        public double ExteriorLength { get; set; }  // center → floor edge (always hooked)
        public double InteriorLength { get; set; }  // center → L2 / L1-far / floor-inward

        public bool InteriorHooked { get; set; }    // true only when interior ray hits floor
        // exterior is always hooked — no flag needed

        // Half-width of the open slot on each side of the insertion point.
        // Used for the "Arrow Tributary Length" parameters in the rebar family.
        public double ArrowTributaryLengthLeft  { get; set; }
        public double ArrowTributaryLengthRight { get; set; }

        public bool IsValid =>
            InsertionPoint != null
            && OutwardNormal  != null
            && ExteriorLength > 0.01
            && InteriorLength > 0.01;
    }
}
