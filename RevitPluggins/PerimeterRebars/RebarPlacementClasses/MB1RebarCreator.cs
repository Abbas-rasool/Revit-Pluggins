using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitPluggins.PerimeterRebars.RebarPlacementClasses
{
    public class MB1ZoneData
    {
        public XYZ    EdgeStart    { get; set; }
        public XYZ    EdgeDir      { get; set; }
        public XYZ    InwardNormal { get; set; }  // always points toward slab interior
        public double EdgeLength   { get; set; }  // total length of the floor edge segment
    }

    public class MB1RebarCreator
    {
        private const double BarLengthFt  = 6.0;
        private const double EdgeOffsetFt = 1.0; // bar center inward from slab boundary
        private const double TagOffsetFt  = 1.0; // additional inward clearance from bar center to tag
        private const double SHookRadius  = 0.35; // radius of each S-hook semicircle

        private readonly Document               _doc;
        private readonly Autodesk.Revit.DB.View _view;

        public MB1RebarCreator(Document doc, Autodesk.Revit.DB.View view)
        {
            _doc  = doc;
            _view = view;
        }

        public void Place(List<MB1ZoneData> zones)
        {
            if (zones == null || zones.Count == 0) return;

            var lineStyle = GetLineStyle("REBAR TOP");
            var textType  = GetOrCreateTextType();

            foreach (var zone in zones)
                PlaceOne(zone, lineStyle, textType);
        }

        private void PlaceOne(MB1ZoneData zone, GraphicsStyle lineStyle, TextNoteType textType)
        {
            double halfBar = BarLengthFt / 2.0;
            double t       = zone.EdgeLength * 0.5; // always place at edge midpoint

            XYZ onEdge = zone.EdgeStart + zone.EdgeDir.Multiply(t);
            XYZ inward = zone.InwardNormal;
            XYZ center = new XYZ(
                onEdge.X + inward.X * EdgeOffsetFt,
                onEdge.Y + inward.Y * EdgeOffsetFt,
                0);

            XYZ half      = zone.EdgeDir.Multiply(halfBar);
            XYZ lineStart = new XYZ(center.X - half.X, center.Y - half.Y, 0);
            XYZ lineEnd   = new XYZ(center.X + half.X, center.Y + half.Y, 0);

            DetailLine detailLine = _doc.Create.NewDetailCurve(_view, Line.CreateBound(lineStart, lineEnd)) as DetailLine;
            if (detailLine == null) return;

            if (lineStyle != null)
                detailLine.LineStyle = lineStyle;

            var ids = new List<ElementId> { detailLine.Id };

            // S-hook symbols at both bar ends — same line style, same group
            ids.AddRange(CreateSHook(lineStart, zone.EdgeDir.Negate(), inward, lineStyle));
            ids.AddRange(CreateSHook(lineEnd,   zone.EdgeDir,          inward, lineStyle));

            if (textType != null)
            {
                bool   textRendersOutward = (inward.Y > 0.5) || (inward.X < -0.5);
                double tagClearance       = TagOffsetFt * (textRendersOutward ? 2.0 : 1.0);
                XYZ    tagPos             = new XYZ(
                    center.X + inward.X * tagClearance,
                    center.Y + inward.Y * tagClearance,
                    0);

                // Rotate tag to align with rebar for vertical edges
                bool   isVertical  = Math.Abs(zone.EdgeDir.Y) > Math.Abs(zone.EdgeDir.X);
                double tagRotation = isVertical ? Math.PI / 2.0 : 0.0;

                TextNote tag = TextNote.Create(_doc, _view.Id, tagPos, "MB1",
                    new TextNoteOptions(textType.Id)
                    {
                        HorizontalAlignment = HorizontalTextAlignment.Center,
                        Rotation            = tagRotation
                    });

                if (tag != null)
                    ids.Add(tag.Id);
            }

            _doc.Create.NewGroup(ids);
        }

        // Creates two connected semicircular arcs forming an S at tipPoint, extending in the
        // perp direction.  Arc 1 curves toward +barDir, Arc 2 toward -barDir, giving a true S.
        private List<ElementId> CreateSHook(XYZ tipPoint, XYZ barDir, XYZ perp, GraphicsStyle lineStyle)
        {
            var    ids = new List<ElementId>();
            double r   = SHookRadius;

            try
            {
                // Arc 1: tipPoint - perp*2r → tipPoint, bulging toward +barDir
                XYZ c1   = new XYZ(tipPoint.X - perp.X * r, tipPoint.Y - perp.Y * r, 0);
                var arc1 = Arc.Create(c1, r, -Math.PI / 2.0, Math.PI / 2.0, barDir, perp);
                var dc1  = _doc.Create.NewDetailCurve(_view, arc1);
                if (dc1 != null)
                {
                    if (lineStyle != null) dc1.LineStyle = lineStyle;
                    ids.Add(dc1.Id);
                }

                // Arc 2: tipPoint → tipPoint + perp*2r, bulging toward -barDir
                XYZ c2   = new XYZ(tipPoint.X + perp.X * r, tipPoint.Y + perp.Y * r, 0);
                var arc2 = Arc.Create(c2, r, -Math.PI / 2.0, Math.PI / 2.0, barDir.Negate(), perp);
                var dc2  = _doc.Create.NewDetailCurve(_view, arc2);
                if (dc2 != null)
                {
                    if (lineStyle != null) dc2.LineStyle = lineStyle;
                    ids.Add(dc2.Id);
                }
            }
            catch { }

            return ids;
        }

        private GraphicsStyle GetLineStyle(string styleName)
        {
            Category lineCat = _doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);

            foreach (Category sub in lineCat.SubCategories)
            {
                if (sub.Name.Equals(styleName, StringComparison.OrdinalIgnoreCase))
                    return sub.GetGraphicsStyle(GraphicsStyleType.Projection);
            }

            return null;
        }

        private TextNoteType GetOrCreateTextType()
        {
            const double modelTextHeightFt = 1;

            int    viewScale  = _view.Scale;
            string typeName   = $"MB1 Tag {viewScale}";
            double sizeInFeet = modelTextHeightFt / viewScale;

            TextNoteType existing = new FilteredElementCollector(_doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .FirstOrDefault(t => t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

            if (existing != null) return existing;

            TextNoteType baseType = new FilteredElementCollector(_doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .FirstOrDefault();

            if (baseType == null) return null;

            TextNoteType newType = baseType.Duplicate(typeName) as TextNoteType;
            if (newType == null) return null;

            newType.get_Parameter(BuiltInParameter.TEXT_SIZE).Set(sizeInFeet);
            newType.get_Parameter(BuiltInParameter.TEXT_STYLE_BOLD).Set(0); // standard, not bold

            return newType;
        }
    }
}
