using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace RevitPlugins.TurnDownSlab
{
    public class FloorEdgeSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element e) => e is Floor;
        public bool AllowReference(Reference reference, XYZ position) => true;
    }
}
