using Autodesk.Revit.UI;
using RevitPluggins.PerimeterRebars.UI;
using RevitPluggins.Common;
using System.Windows;

namespace RevitPluggins.PerimeterRebars
{
    public partial class PerimeterRebarsUI : Window
    {
        private readonly ExternalEvent              _exEvent;
        private readonly PerimeterRebarCommandHandler _handler;

        public PerimeterRebarsUI()
        {
            InitializeComponent();

            _handler = new PerimeterRebarCommandHandler();
            _handler.OnExecutionChanged = (isRunning) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    IsEnabled              = !isRunning;
                    BtnAddRebars.IsEnabled = !isRunning && _handler.HasDefinedRegions;
                }));
            };

            _exEvent = ExternalEvent.Create(_handler);
        }

        private void BtnDefine_Click(object sender, RoutedEventArgs e)
        {
            if (_handler.IsRunning)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Busy", "A command is already running. Please wait until it finishes.");
                return;
            }

            if (!double.TryParse(TxtMaxEdgeDistance.Text, out double maxDist) || maxDist <= 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Invalid Input", "Edge column max distance must be a positive number.");
                return;
            }

            if (!double.TryParse(TxtGridSearchRadius.Text, out double gridRadius) || gridRadius <= 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Invalid Input", "Grid search radius must be a positive number.");
                return;
            }

            if (gridRadius <= maxDist)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Invalid Input", "Grid search radius must be greater than the edge column distance.");
                return;
            }

            if (!double.TryParse(TxtGridClusterTolerance.Text, out double clusterTol) || clusterTol <= 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Invalid Input", "Cluster tolerance must be a positive number.");
                return;
            }

            if (!double.TryParse(TxtSlabThickness.Text, out double slabThicknessIn) || slabThicknessIn <= 0)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Invalid Input", "Slab thickness must be a positive number (in inches).");
                return;
            }

            _handler.CurrentInputs = new PerimeterRebarInputs
            {
                SelectionMethod      = RbFilledRegion.IsChecked == true
                                           ? SelectionMethod.FilledRegion
                                           : SelectionMethod.ContinuousFloor,
                EdgeColumnMaxDistance = maxDist,
                GridSearchRadiusFt   = gridRadius,
                GridClusterTolerance = clusterTol,
                SlabThicknessInFeet  = slabThicknessIn / 12.0
            };

            _handler.Mode  = PerimeterCommandMode.Define;
            IsEnabled      = false;
            _exEvent.Raise();
        }

        private void BtnAddRebars_Click(object sender, RoutedEventArgs e)
        {
            if (_handler.IsRunning)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Busy", "A command is already running. Please wait until it finishes.");
                return;
            }

            _handler.Mode = PerimeterCommandMode.AddRebars;
            IsEnabled     = false;
            _exEvent.Raise();
        }
    }
}
