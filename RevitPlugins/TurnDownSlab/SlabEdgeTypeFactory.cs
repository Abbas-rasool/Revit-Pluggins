using Autodesk.Revit.DB;
using System;
using System.Linq;

namespace RevitPlugins.TurnDownSlab
{
    public static class SlabEdgeTypeFactory
    {
        public static SlabEdgeType GetOrCreate(Document doc, FamilySymbol profileSymbol)
        {

            if (profileSymbol == null)
                throw new ArgumentNullException(nameof(profileSymbol));

            var typeName = profileSymbol.Name;

            SlabEdgeType existing = FindByName(doc, typeName);

            if (existing != null)
            {
                SetProfile(doc, existing, profileSymbol);
                return existing;
            }

            SlabEdgeType baseType = new FilteredElementCollector(doc)
                .OfClass(typeof(SlabEdgeType))
                .Cast<SlabEdgeType>()
                .FirstOrDefault();

            if (baseType == null)
                throw new Exception(
                    "No Slab Edge type exists in this project to duplicate.");

            var newType = baseType.Duplicate(typeName) as SlabEdgeType;

            if (newType == null)
                throw new Exception($"Could not duplicate Slab Edge type '{typeName}'.");

            SetProfile(doc, newType, profileSymbol);

            return newType;
        }

        public static SlabEdgeType FindByName(Document doc, string typeName)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(SlabEdgeType))
                .Cast<SlabEdgeType>()
                .FirstOrDefault(t => t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
        }

        private static void SetProfile(Document doc, SlabEdgeType edgeType, FamilySymbol profileSymbol)
        {
            Parameter profileParam = null;
            Parameter namedFallback = null;

            foreach (Parameter parameter in edgeType.Parameters)
            {
                if (parameter.StorageType != StorageType.ElementId || parameter.IsReadOnly)
                    continue;

                string name = parameter.Definition?.Name ?? string.Empty;

                if (name.IndexOf("Profile", StringComparison.OrdinalIgnoreCase) >= 0)
                    namedFallback = parameter;


                if (doc.GetElement(parameter.AsElementId()) is FamilySymbol fs && fs.Category != null && fs.Category.Id == new ElementId(BuiltInCategory.OST_ProfileFamilies))
                {
                    profileParam = parameter;
                    break;
                }
            }

            profileParam ??= namedFallback;

            if (profileParam == null)
                throw new Exception(
                    "Could not locate the profile parameter on the Slab Edge type. " +
                    "Set the profile manually on one slab-edge type and re-run, or report your " +
                    "Revit version so the parameter can be pinned.");

            profileParam.Set(profileSymbol.Id);
        }
    }
}
