using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System.Collections.Generic;

namespace RevitPlugins.Selection
{
    /// <summary>
    /// Small wrapper over Revit's interactive selection that shows a guiding
    /// dialog, applies the right element filter, and swallows the user pressing
    /// ESC (returning null / an empty list instead of throwing).
    /// </summary>
    public class UISelectionHelper
    {
        private readonly UIDocument _uiDoc;
        private readonly Document _doc;

        public UISelectionHelper(UIDocument uidoc)
        {
            _uiDoc = uidoc;
            _doc = uidoc.Document;
        }

        public IList<Reference> PickFloors()
        {
            var dialog = new Autodesk.Revit.UI.TaskDialog("Select Floors")
            {
                MainInstruction = "Select Floors.",
                TitleAutoPrefix = false,
                AllowCancellation = true,
            };
            dialog.Show();

            try
            {
                return _uiDoc.Selection.PickObjects(
                    ObjectType.Element,
                    new FloorFilter(),
                    "Select the floors");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return new List<Reference>();
            }
        }

        public Reference PickFilledRegion()
        {
            var dialog = new Autodesk.Revit.UI.TaskDialog("Select Filled Region")
            {
                MainInstruction = "Select one filled region.",
                TitleAutoPrefix = false,
                AllowCancellation = true,
            };
            dialog.Show();

            try
            {
                return _uiDoc.Selection.PickObject(
                    ObjectType.Element,
                    new FilledRegionFilter(),
                    "Select one filled region");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return null;
            }
        }
    }

    public class FloorFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            if (elem is Floor) return true;

            return elem.Category != null
                && elem.Category.Id.Value == (int)BuiltInCategory.OST_Floors;
        }

        public bool AllowReference(Reference reference, XYZ position) => false;
    }

    public class FilledRegionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem is FilledRegion;

        public bool AllowReference(Reference reference, XYZ position) => true;
    }
}
