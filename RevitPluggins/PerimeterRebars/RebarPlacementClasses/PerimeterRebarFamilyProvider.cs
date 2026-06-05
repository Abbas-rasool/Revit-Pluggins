using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitPluggins.Families;

namespace RevitPluggins.PerimeterRebars.RebarPlacementClasses
{
    public class PerimeterRebarFamilyProvider
    {
        public FamilySymbol RebarSymbol { get; }
        public FamilySymbol TagSymbol   { get; }

        public PerimeterRebarFamilyProvider(UIApplication app)
        {
            var familyHelper = new FamilyHelper(app);
            var path         = $@"C:\ProgramData\Autodesk\Revit\Addins\{app.Application.VersionNumber}\PluginData\ToolData.json";
            var service      = new FamilyCatalogService(path);

            RebarSymbol = familyHelper.GetOrLoadFamilySymbol(service, "Top Bar SC");
            TagSymbol   = familyHelper.GetOrLoadFamilySymbol(service, "Top Bar Tag with Suffix v1.0 R21");
        }
    }
}
