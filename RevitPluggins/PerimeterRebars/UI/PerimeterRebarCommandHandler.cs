using Autodesk.Revit.UI;

namespace RevitPluggins.PerimeterRebars.UI
{
    public enum PerimeterCommandMode { Define, AddRebars }

    public class PerimeterRebarCommandHandler : IExternalEventHandler
    {
        public Action<bool>          OnExecutionChanged { get; set; }
        public PerimeterRebarInputs  CurrentInputs      { get; set; }
        public PerimeterCommandMode  Mode               { get; set; }
        public bool                  IsRunning          { get; private set; }

        public bool HasDefinedRegions => _command.HasDefinedRegions;

        private readonly CreatePerimeterRebars _command = new CreatePerimeterRebars();

        public void Execute(UIApplication app)
        {
            if (IsRunning) return;

            try
            {
                IsRunning = true;
                OnExecutionChanged?.Invoke(true);

                if (Mode == PerimeterCommandMode.Define)
                    _command.DefineRegions(CurrentInputs, app);
                else
                    _command.AddRebars(CurrentInputs, app);
            }
            finally
            {
                IsRunning = false;
                OnExecutionChanged?.Invoke(false);
            }
        }

        public string GetName() => "Perimeter Rebar Handler";
    }
}
