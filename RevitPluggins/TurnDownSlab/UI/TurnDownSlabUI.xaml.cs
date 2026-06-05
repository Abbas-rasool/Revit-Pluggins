using Autodesk.Revit.UI;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace RevitPluggins.TurnDownSlab.UI
{
    public partial class TurnDownSlabUI : Window
    {
        // Sentinel item that means "build a new type from the dimension fields".
        private const string NewTypeSentinel = "✚ New from dimensions";

        private readonly TurnDownSlabCommandHandler _handler;
        private readonly ExternalEvent _externalEvent;

        public TurnDownSlabUI(
            TurnDownSlabCommandHandler handler,
            ExternalEvent externalEvent,
            IEnumerable<string> existingTypeNames)
        {
            InitializeComponent();

            _handler = handler;
            _externalEvent = externalEvent;

            CbType.Items.Add(NewTypeSentinel);
            foreach (string name in (existingTypeNames ?? Enumerable.Empty<string>()).OrderBy(n => n))
                CbType.Items.Add(name);

            CbType.SelectedIndex = 0;

            // Sync preview/fields to the default (Simple) selection now that all elements exist.
            ProfileKind_Changed(null, null);
        }

        private bool IsNewType => Equals(CbType.SelectedItem, NewTypeSentinel);

        private bool IsBrick => RbProfileBrick?.IsChecked == true;

        private void CbType_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // Profile shape and dimension fields only matter when building a new type.
            bool isNew = IsNewType;
            if (GbProfile != null) GbProfile.IsEnabled = isNew;
            if (GbDimensions != null) GbDimensions.IsEnabled = isNew;
        }

        private void ProfileKind_Changed(object sender, RoutedEventArgs e)
        {
            bool brick = IsBrick;

            // Swap the preview drawing.
            if (CanvasSimple != null) CanvasSimple.Visibility = brick ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
            if (CanvasBrick != null) CanvasBrick.Visibility = brick ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

            // The second depth (D2) only applies to the brick profile.
            if (LblDepth2 != null) LblDepth2.Visibility = brick ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            if (TbDepth2 != null) TbDepth2.Visibility = brick ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

            // First depth reads as "D" for simple, "D1" for brick.
            if (LblDepth1 != null) LblDepth1.Text = brick ? "D1 (in)" : "D (in)";
        }

        private TurnDownPlacementMode GetPlacementMode()
        {
            if (RbWholeSlab.IsChecked == true) return TurnDownPlacementMode.WholeSlab;
            return TurnDownPlacementMode.SelectEdges;
        }

        private void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            var inputs = _handler.Inputs;
            inputs.PlacementMode = GetPlacementMode();

            if (IsNewType)
            {
                bool brick = IsBrick;

                if (!TryReadField(TbWidth.Text, "W", out double width) ||
                    !TryReadField(TbDepth.Text, brick ? "D1" : "D", out double depth1) ||
                    !TryReadField(TbSlope.Text, "Slope", out double slope))
                    return;

                double depth2 = 0;
                if (brick && !TryReadField(TbDepth2.Text, "D2", out depth2))
                    return;

                if (slope <= 0 || slope > 90)
                {
                    System.Windows.MessageBox.Show("Slope must be greater than 0 and at most 90 degrees (90° = vertical face).",
                        "Turn-Down Slab Edge", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                inputs.UseExistingType = false;
                inputs.ProfileType = brick ? TurnDownProfileType.Brick : TurnDownProfileType.Simple;
                inputs.WidthInches = width;
                inputs.Depth1Inches = depth1;
                inputs.Depth2Inches = depth2;
                inputs.SlopeDegrees = slope;
            }
            else
            {
                inputs.UseExistingType = true;
                inputs.ExistingTypeName = CbType.SelectedItem as string;
            }

            _externalEvent.Raise();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private static bool TryReadField(string text, string label, out double value)
        {
            if (double.TryParse(text, out value) && value > 0)
                return true;

            System.Windows.MessageBox.Show($"Enter a valid positive number for {label}.",
                "Turn-Down Slab Edge", MessageBoxButton.OK, MessageBoxImage.Warning);
            value = 0;
            return false;
        }

        private void RbWholeSlab_Checked(object sender, RoutedEventArgs e)
        {

        }
    }
}
