using RevitPluggins.Common;

namespace RevitPluggins.PerimeterRebars.UI
{
    public class PerimeterRebarInputs
    {
        public SelectionMethod SelectionMethod       { get; set; }
        public double          EdgeColumnMaxDistance { get; set; }  // ft — L1 exclusion zone columns
        public double          GridSearchRadiusFt    { get; set; }  // ft — columns included in grid (L1 + L2)
        public double          GridClusterTolerance  { get; set; }  // ft — snap tolerance when grouping columns into rows
        public double          SlabThicknessInFeet   { get; set; }
    }
}
