using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitPlugins.PerimeterRebars.RebarPlacementClasses
{
    public class PerimeterRebarPlacer
    {
        private readonly Document                _doc;
        private readonly Autodesk.Revit.DB.View  _view;
        private readonly FamilySymbol            _symbol;
        private readonly FamilySymbol            _tagSymbol;

        public PerimeterRebarPlacer(Document doc, Autodesk.Revit.DB.View view, FamilySymbol symbol, FamilySymbol tagSymbol)
        {
            _doc       = doc;
            _view      = view;
            _symbol    = symbol;
            _tagSymbol = tagSymbol;
        }

        public List<FamilyInstance> Place(List<PerimeterRebarData> rebars)
        {
            var placed = new List<FamilyInstance>();
            if (rebars == null || rebars.Count == 0) return placed;

            if (!_symbol.IsActive)
            {
                _symbol.Activate();
                _doc.Regenerate();
            }

            foreach (var r in rebars.Where(x => x != null && x.IsValid))
            {
                var inst = PlaceOne(r);
                if (inst != null) placed.Add(inst);
            }

            return placed;
        }

        private FamilyInstance PlaceOne(PerimeterRebarData r)
        {
            XYZ pt = new XYZ(r.InsertionPoint.X, r.InsertionPoint.Y, 0);

            FamilyInstance bar = _doc.Create.NewFamilyInstance(pt, _symbol, _view);
            if (bar == null) return null;

            double angle     = Math.Atan2(r.OutwardNormal.Y, r.OutwardNormal.X);
            bool   needsSwap = angle <= -Math.PI / 2 + 1e-9 || angle > Math.PI / 2 + 1e-9;

            XYZ inward = r.OutwardNormal.Negate();
            XYZ edgeDir = new XYZ(-r.OutwardNormal.Y, r.OutwardNormal.X, 0); // 90° CCW of outward
            XYZ tagPos = pt + inward.Multiply(r.InteriorLength) * 0.25 + edgeDir.Multiply(1.0);

            if (needsSwap)
            {
                // L1 arm (−X in family space) will point toward OutwardNormal after adjusted rotation.
                SetD(bar, "L1", Math.Max(0, r.ExteriorLength));  // L1 = exterior
                SetD(bar, "L2", Math.Max(0, r.InteriorLength));  // L2 = interior
                SetI(bar, "HOOK - 1", 1);                         // exterior always hooked
                SetI(bar, "HOOK - 2", r.InteriorHooked ? 1 : 0);
            }
            else
            {
                // L2 arm (+X in family space) points toward OutwardNormal — standard case.
                SetD(bar, "L1", Math.Max(0, r.InteriorLength));
                SetD(bar, "L2", Math.Max(0, r.ExteriorLength));
                SetI(bar, "HOOK - 1", r.InteriorHooked ? 1 : 0);
                SetI(bar, "HOOK - 2", 1);                         // exterior always hooked
                tagPos = pt + inward.Multiply(r.InteriorLength * 0.25 - 6.0) + edgeDir.Multiply(1.0);
            }

            SetI(bar, "UpHookLeft",               0);
            SetI(bar, "UpHookRight",              0);
            SetI(bar, "Double Arrow - Right",     0);
            SetI(bar, "Double Arrow - Left",      0);
            SetI(bar, "End Circle Visible",       0);
            SetI(bar, "ARROW VISIBILITY - LEFT",  1);
            SetI(bar, "ARROW VISIBILITY - RIGHT", 1);
            SetI(bar, "Bar Size",                 5);

            SetD(bar, "Arrow Tributary Length - Left",  r.ArrowTributaryLengthLeft);
            SetD(bar, "Arrow Tributary Length - Right", r.ArrowTributaryLengthRight);
            // Adjust rotation: for the swap case rotate by (angle − π) so the
            // family's −X arm (L1) ends up pointing toward OutwardNormal.
            double rotAngle = needsSwap ? angle - Math.PI : angle;
            var    axis     = Line.CreateBound(pt, pt + XYZ.BasisZ);
            ElementTransformUtils.RotateElement(_doc, bar.Id, axis, rotAngle);

            string prefix = "TH";
            string lengthName = "Top Bar Length";
            double armLength  = bar.LookupParameter(lengthName)?.AsDouble() ?? r.InteriorLength;
            SetS(bar, "Suffix", $"{prefix}{Math.Ceiling(armLength)}");


            if (_tagSymbol != null)
            {
                var tag = IndependentTag.Create(
                    _doc, _view.Id,
                    new Reference(bar), false,
                    TagMode.TM_ADDBY_CATEGORY,
                    TagOrientation.Horizontal,
                    tagPos);

                tag.ChangeTypeId(_tagSymbol.Id);
            }

            return bar;
        }

        private void SetD(FamilyInstance inst, string name, double val)
        {
            var p = inst.LookupParameter(name);
            if (p != null && !p.IsReadOnly) p.Set(val);
        }

        private void SetI(FamilyInstance inst, string name, int val)
        {
            var p = inst.LookupParameter(name);
            if (p != null && !p.IsReadOnly) p.Set(val);
        }

        private void SetS(FamilyInstance inst, string name, string val)
        {
            var p = inst.LookupParameter(name);
            if (p != null && !p.IsReadOnly) p.Set(val);
        }

    }
}
