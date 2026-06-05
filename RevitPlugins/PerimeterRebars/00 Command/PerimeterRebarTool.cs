using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using System.Diagnostics;

namespace RevitPlugins.PerimeterRebars
{
    [Transaction(TransactionMode.Manual)]
    public class PerimeterRebarTool : IExternalCommand
    {
        private static PerimeterRebarsUI _instance;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (_instance != null && _instance.IsLoaded)
            {
                _instance.Activate();
                _instance.Focus();
                return Result.Succeeded;
            }

            _instance = new PerimeterRebarsUI();

            var helper = new System.Windows.Interop.WindowInteropHelper(_instance);
            helper.Owner = Process.GetCurrentProcess().MainWindowHandle;

            _instance.Closed += (s, e) => { _instance = null; };
            _instance.Show();

            return Result.Succeeded;
        }
    }
}
