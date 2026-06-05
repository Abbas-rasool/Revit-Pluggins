using Autodesk.Revit.DB;
using System;
using System.Globalization;
using System.Linq;

namespace RevitPlugins.TurnDownSlab
{
    public static class SlabEdgeProfileTypeFactory
    {
        private const string WidthParam = "Width";
        private const string DepthParam = "Depth";
        private const string ExtensionParam = "Extension";

        // Brick profile
        private const string D1Param = "D1";
        private const string D2Param = "D2";


        public static FamilySymbol GetOrCreate(Document doc, TurnDownProfileType kind, FamilySymbol baseSymbol, double widthInches, double depth1Inches, double depth2Inches, double slopeDegrees)
        {
            if (baseSymbol == null)
                throw new ArgumentNullException(nameof(baseSymbol));

            string familyName = baseSymbol.Family?.Name;
            string typeName = BuildTypeName(kind, widthInches, depth1Inches, depth2Inches, slopeDegrees);

            FamilySymbol existing = FindType(doc, familyName, typeName);
            if (existing != null)
            {
                Activate(doc, existing);
                return existing;
            }

            var symbol = baseSymbol.Duplicate(typeName) as FamilySymbol;
            if (symbol == null)
                throw new Exception($"Could not duplicate profile type '{typeName}'.");

            double widthFeet = UnitUtils.Convert(widthInches, UnitTypeId.Inches, UnitTypeId.Feet);
            double depth1Feet = UnitUtils.Convert(depth1Inches, UnitTypeId.Inches, UnitTypeId.Feet);

            if (kind == TurnDownProfileType.Brick)
            {
                double depth2Feet = UnitUtils.Convert(depth2Inches, UnitTypeId.Inches, UnitTypeId.Feet);

                // The sloped left face spans the full height D1 + D2.
                double extensionFeet = ExtensionFeet(widthFeet, depth1Feet + depth2Feet, slopeDegrees);

                SetIfPresent(symbol, WidthParam, widthFeet);
                SetIfPresent(symbol, D1Param, depth1Feet);
                SetIfPresent(symbol, D2Param, depth2Feet);
                SetIfPresent(symbol, ExtensionParam, extensionFeet);
            }
            else
            {
                double extensionFeet = ExtensionFeet(widthFeet, depth1Feet, slopeDegrees);

                SetIfPresent(symbol, WidthParam, widthFeet);
                SetIfPresent(symbol, DepthParam, depth1Feet);
                SetIfPresent(symbol, ExtensionParam, extensionFeet);
            }

            Activate(doc, symbol);
            return symbol;
        }

        private static double ExtensionFeet(double widthFeet, double slopeHeightFeet, double slopeDegrees)
        {
            if (slopeDegrees >= 90.0)
                return widthFeet;

            double rad = slopeDegrees * Math.PI / 180.0;
            return widthFeet + slopeHeightFeet / Math.Tan(rad);
        }

        private static FamilySymbol FindType(Document doc, string familyName, string typeName)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(s =>
                    s.Family?.Name != null &&
                    s.Family.Name.Equals(familyName, StringComparison.OrdinalIgnoreCase) &&
                    s.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
        }

        private static string BuildTypeName(
            TurnDownProfileType kind, double widthInches, double depth1Inches, double depth2Inches, double slopeDegrees)
        {
            string w = Format(widthInches);
            string s = Format(slopeDegrees);

            if (kind == TurnDownProfileType.Brick)
                return $"TDB {w}x{Format(depth1Inches)}x{Format(depth2Inches)} S{s}";

            return $"TD {w}x{Format(depth1Inches)} S{s}";
        }

        private static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        private static void SetIfPresent(FamilySymbol symbol, string paramName, double valueFt)
        {
            Parameter p = symbol.LookupParameter(paramName);

            if (p == null)
                throw new Exception(
                    $"Profile family '{symbol.Family?.Name}' has no '{paramName}' parameter. " +
                    "It must be a type parameter labelled to the sketch dimension.");

            if (p.IsReadOnly)
                throw new Exception($"Profile parameter '{paramName}' is read-only and cannot be driven.");

            p.Set(valueFt);
        }

        private static void Activate(Document doc, FamilySymbol symbol)
        {
            if (symbol.IsActive)
                return;

            symbol.Activate();
            doc.Regenerate();
        }
    }
}
