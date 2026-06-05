using Autodesk.Revit.UI;
using RevitPlugins.Families;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RevitPlugins.TurnDownSlab
{
    public class TurnDownFamilyProvider
    {
        public FamilySymbol ProfileFamilySimple { get; }
        public FamilySymbol ProfileFamilyBrick { get; }


        public TurnDownFamilyProvider(UIApplication app) 
        {
            var familyHelper = new FamilyHelper(app);
            var path = $@"C:\ProgramData\Autodesk\Revit\Addins\{app.Application.VersionNumber}\PluginData\ToolData.json";
            var service = new FamilyCatalogService(path);

            ProfileFamilySimple = familyHelper.GetOrLoadFamilySymbol(service, "SlabEdgeProfile");

            ProfileFamilyBrick = familyHelper.GetOrLoadFamilySymbol(service, "SlabEdgeProfileBrick");

        }
    }
}