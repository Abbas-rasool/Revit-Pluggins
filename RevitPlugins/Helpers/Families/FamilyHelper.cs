using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.IO;
using System.Linq;

namespace RevitPlugins.Families
{
    /// <summary>
    /// Thin wrapper around the Revit family-loading API. Finds families already
    /// present in the model, loads them from disk when needed, and handles symbol
    /// activation / interactive placement without blocking dialogs.
    /// </summary>
    public class FamilyHelper
    {
        private readonly UIApplication _uiApp;
        private readonly UIDocument _uiDoc;
        private readonly Document _doc;

        public FamilyHelper(UIApplication app)
        {
            _uiApp = app ?? throw new ArgumentNullException(nameof(app));
            _uiDoc = _uiApp.ActiveUIDocument ?? throw new InvalidOperationException("No active Revit document.");
            _doc = _uiDoc.Document ?? throw new InvalidOperationException("No active Revit document.");
        }

        public Family LoadFamilyIfNeeded(string familyName, string fullPath)
        {
            if (string.IsNullOrWhiteSpace(familyName))
                throw new ArgumentException("Family name is empty.", nameof(familyName));

            if (string.IsNullOrWhiteSpace(fullPath))
                throw new ArgumentException("Family path is empty.", nameof(fullPath));

            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Family file not found.", fullPath);

            Family family = new FilteredElementCollector(_doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .FirstOrDefault(f => f.Name.Equals(familyName, StringComparison.OrdinalIgnoreCase));

            if (family != null)
                return family;

            if (_doc.IsModifiable)
            {
                // Pass SilentFamilyLoadOptions to avoid an overwrite dialog blocking Revit.
                _doc.LoadFamily(fullPath, new SilentFamilyLoadOptions(), out family);
            }
            else
            {
                using (Transaction tx = new Transaction(_doc, "Load Family"))
                {
                    // Wire up a failure preprocessor so warnings are auto-dismissed instead of freezing Revit.
                    var failOpts = tx.GetFailureHandlingOptions();
                    failOpts.SetFailuresPreprocessor(new SilentFailurePreprocessor());
                    tx.SetFailureHandlingOptions(failOpts);

                    tx.Start();
                    _doc.LoadFamily(fullPath, new SilentFamilyLoadOptions(), out family);
                    _doc.Regenerate();
                    tx.Commit();
                }
            }

            if (family == null)
                throw new Exception($"Family '{familyName}' could not be loaded.");

            return family;
        }

        public Family FindLoadedFamily(string familyName)
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .FirstOrDefault(f => f.Name.Equals(familyName, StringComparison.OrdinalIgnoreCase));
        }

        public FamilySymbol GetFirstSymbol(Family family)
        {
            if (family == null)
                throw new ArgumentNullException(nameof(family));

            ElementId id = family.GetFamilySymbolIds().FirstOrDefault();

            if (id == null || id == ElementId.InvalidElementId)
                throw new Exception($"Family '{family.Name}' contains no symbols.");

            FamilySymbol symbol = _doc.GetElement(id) as FamilySymbol;

            if (symbol == null)
                throw new Exception($"Could not get first symbol from family '{family.Name}'.");

            return symbol;
        }

        public void ActivateSymbol(FamilySymbol symbol)
        {
            if (symbol == null)
                throw new ArgumentNullException(nameof(symbol));

            if (symbol.IsActive)
                return;

            if (_doc.IsModifiable)
            {
                symbol.Activate();
                _doc.Regenerate();
            }
            else
            {
                using (Transaction tx = new Transaction(_doc, "Activate Symbol"))
                {
                    tx.Start();
                    symbol.Activate();
                    _doc.Regenerate();
                    tx.Commit();
                }
            }
        }

        public bool PlaceSymbol(FamilySymbol symbol)
        {
            return TryPlaceSymbol(symbol);
        }

        public bool TryPlaceSymbol(FamilySymbol symbol)
        {
            if (symbol == null)
                throw new ArgumentNullException(nameof(symbol));

            ActivateSymbol(symbol);

            Autodesk.Revit.DB.View activeView = _doc.ActiveView;

            string reason;
            if (!CanPlaceInteractively(symbol, activeView, out reason))
                return false;

            try
            {
                _uiDoc.PostRequestForElementTypePlacement(symbol);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool CanPlaceInteractively(
            FamilySymbol symbol,
            Autodesk.Revit.DB.View activeView,
            out string reason)
        {
            reason = null;

            if (symbol == null)
            {
                reason = "Family symbol is null.";
                return false;
            }

            if (activeView == null)
            {
                reason = "There is no active Revit view.";
                return false;
            }

            if (activeView.IsTemplate)
            {
                reason = "The active view is a view template.";
                return false;
            }

            Family family = symbol.Family;

            if (family == null)
            {
                reason = "The symbol has no parent family.";
                return false;
            }

            FamilyPlacementType placementType = family.FamilyPlacementType;

            bool isDetailOrAnnotation =
                symbol.Category != null &&
                (
                    symbol.Category.Id.Value == (int)BuiltInCategory.OST_DetailComponents ||
                    symbol.Category.Id.Value == (int)BuiltInCategory.OST_GenericAnnotation
                );

            bool is2DView =
                activeView.ViewType == ViewType.DraftingView ||
                activeView.ViewType == ViewType.Detail ||
                activeView.ViewType == ViewType.FloorPlan ||
                activeView.ViewType == ViewType.CeilingPlan ||
                activeView.ViewType == ViewType.EngineeringPlan ||
                activeView.ViewType == ViewType.Section ||
                activeView.ViewType == ViewType.Elevation;

            bool isModelView =
                activeView.ViewType == ViewType.FloorPlan ||
                activeView.ViewType == ViewType.CeilingPlan ||
                activeView.ViewType == ViewType.EngineeringPlan ||
                activeView.ViewType == ViewType.ThreeD ||
                activeView.ViewType == ViewType.Section ||
                activeView.ViewType == ViewType.Elevation;

            if (isDetailOrAnnotation)
            {
                if (!is2DView)
                {
                    reason = "Detail and annotation families must be placed in a compatible 2D view.";
                    return false;
                }

                return true;
            }

            switch (placementType)
            {
                case FamilyPlacementType.ViewBased:
                    if (!is2DView)
                    {
                        reason = "This is a view-based family and must be placed in a compatible 2D view.";
                        return false;
                    }
                    return true;

                case FamilyPlacementType.OneLevelBased:
                case FamilyPlacementType.TwoLevelsBased:
                    if (!isModelView)
                    {
                        reason = "This is a model family and cannot be placed in the current view.";
                        return false;
                    }
                    return true;

                case FamilyPlacementType.WorkPlaneBased:
                    if (!isModelView)
                    {
                        reason = "This work-plane-based family requires a model view.";
                        return false;
                    }
                    return true;

                case FamilyPlacementType.CurveBased:
                case FamilyPlacementType.CurveBasedDetail:
                    if (!is2DView && !isModelView)
                    {
                        reason = "This curve-based family cannot be placed in the current view.";
                        return false;
                    }
                    return true;

                case FamilyPlacementType.OneLevelBasedHosted:
                    if (!isModelView)
                    {
                        reason = "This host-based family requires a model view (floor plan, section, or 3D).";
                        return false;
                    }
                    return true;

                case FamilyPlacementType.Adaptive:
                    if (!isModelView)
                    {
                        reason = "Adaptive components require a model view.";
                        return false;
                    }
                    return true;

                case FamilyPlacementType.Invalid:
                    reason = "This family has an invalid placement type and cannot be placed.";
                    return false;

                default:
                    return true;
            }
        }

        /// <summary>
        /// Returns the requested family symbol, loading it from the catalog (and the
        /// network family library it points at) when it is not already in the model.
        /// </summary>
        public FamilySymbol GetOrLoadFamilySymbol(FamilyCatalogService service, string familyName, string typeName = "Standard")
        {
            var symbol = new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilySymbol))
                .OfType<FamilySymbol>()
                .FirstOrDefault(f =>
                    f.Family.Name != null &&
                    f.Family.Name.Equals(familyName, StringComparison.OrdinalIgnoreCase) &&
                    f.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

            if (symbol != null)
            {
                ActivateSymbol(symbol);
                return symbol;
            }

            if (service.GetByName(familyName) is not FamilyDocument familyDoc)
                throw new Exception($"Family '{familyName}' not found in the family catalog.");

            familyDoc.App = _uiApp;
            familyDoc.PlaceInteractively = false;
            familyDoc.Open();

            symbol = new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilySymbol))
                .OfType<FamilySymbol>()
                .FirstOrDefault(f =>
                    f.Family.Name != null &&
                    f.Family.Name.Equals(familyName, StringComparison.OrdinalIgnoreCase) &&
                    f.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

            if (symbol == null)
            {
                symbol = new FilteredElementCollector(_doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfType<FamilySymbol>()
                    .FirstOrDefault(f =>
                        f.Family.Name != null &&
                        f.Family.Name.Equals(familyName, StringComparison.OrdinalIgnoreCase));
            }

            if (symbol == null)
                throw new Exception($"Family '{familyName}' could not be loaded.");

            ActivateSymbol(symbol);
            return symbol;
        }
    }

    /// <summary>Auto-answers "yes, overwrite" so an already-loaded family never blocks on a dialog.</summary>
    public class SilentFamilyLoadOptions : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = true;
            return true;
        }

        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
        {
            source = FamilySource.Project;
            overwriteParameterValues = true;
            return true;
        }
    }

    /// <summary>Auto-dismisses warnings during family loading so Revit doesn't queue them and freeze.</summary>
    public class SilentFailurePreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            foreach (FailureMessageAccessor failure in accessor.GetFailureMessages())
            {
                if (failure.GetSeverity() == FailureSeverity.Warning)
                    accessor.DeleteWarning(failure);
            }
            return FailureProcessingResult.Continue;
        }
    }
}
