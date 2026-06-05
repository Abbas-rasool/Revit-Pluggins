using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitPluggins.TurnDownSlab.UI;
using System.Collections.Generic;
using System.Linq;

namespace RevitPluggins.TurnDownSlab.Command
{
    [Transaction(TransactionMode.Manual)]
    public class TurnDownSlabCommand : IExternalCommand
    {
        private static TurnDownSlabUI _window;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;

            if (_window != null && _window.IsVisible)
            {
                _window.Activate();
                return Result.Succeeded;
            }

            Document doc = uiApp.ActiveUIDocument.Document;
            List<string> existingTypeNames = new FilteredElementCollector(doc)
                .OfClass(typeof(SlabEdgeType))
                .Cast<SlabEdgeType>()
                .Select(t => t.Name)
                .ToList();

            var handler = new TurnDownSlabCommandHandler
            {
                Inputs = new TurnDownSlabInputs(commandData)
            };

            var externalEvent = ExternalEvent.Create(handler);

            _window = new TurnDownSlabUI(handler, externalEvent, existingTypeNames);

            _window.Closed += (s, e) => { _window = null; };
            
            _window.Show();

            return Result.Succeeded;
        }
    }
}
