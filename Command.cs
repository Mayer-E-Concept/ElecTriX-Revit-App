// Command.cs — nur Revit, keine WPF.Shapes imports
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace METools
{
    [Transaction(TransactionMode.Manual)]
    public class DistributeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document   doc   = uidoc.Document;

            var selected = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id)).OfType<FamilyInstance>().ToList();

            if (selected.Count == 0)
            {
                TaskDialog.Show("Symmetrische Verteilung", "Bitte eine Family Instance auswählen.");
                return Result.Cancelled;
            }

            FamilyInstance tmpl   = selected.First();
            XYZ            origin = GetOrigin(tmpl);

            string rName=""; double rW=0,rD=0; bool rFound=false; XYZ rCenter=origin;
            try
            {
                foreach (Room rm in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).OfClass(typeof(SpatialElement)).Cast<Room>())
                {
                    try
                    {
                        if (rm==null || !rm.IsPointInRoom(origin)) continue;
                        var bb=rm.get_BoundingBox(null); if(bb==null) continue;
                        rW=UnitUtils.ConvertFromInternalUnits(bb.Max.X-bb.Min.X,UnitTypeId.Meters);
                        rD=UnitUtils.ConvertFromInternalUnits(bb.Max.Y-bb.Min.Y,UnitTypeId.Meters);
                        rCenter=new XYZ((bb.Min.X+bb.Max.X)/2,(bb.Min.Y+bb.Max.Y)/2,bb.Min.Z);
                        rName=rm.Name??""; rFound=true; break;
                    }
                    catch{}
                }
            }
            catch{}

            var dlg = new DistDialog(rName, rW, rD, rFound);
            if (dlg.ShowDialog() != true) return Result.Cancelled;
            var s = dlg.Result;

            try
            {
                using (var tx = new Transaction(doc, "Symmetrische Objektverteilung"))
                {
                    tx.Start();
                    XYZ center = (s.Center && rFound) ? new XYZ(rCenter.X,rCenter.Y,origin.Z) : origin;
                    var pts = CalcPoints(s, center);

                    var sym = tmpl.Symbol;
                    if (!sym.IsActive) { sym.Activate(); doc.Regenerate(); }

                    Level level=null;
                    try { if(tmpl.LevelId!=ElementId.InvalidElementId) level=doc.GetElement(tmpl.LevelId) as Level; } catch{}
                    if(level==null) try { level=new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l=>Math.Abs(l.Elevation-origin.Z)).FirstOrDefault(); } catch{}

                    foreach(var pt in pts)
                        try
                        {
                            if(level!=null) doc.Create.NewFamilyInstance(pt,sym,level,Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                            else            doc.Create.NewFamilyInstance(pt,sym,Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                        }
                        catch{}

                    if(s.Replace) foreach(var fi in selected) try{doc.Delete(fi.Id);}catch{}
                    tx.Commit();
                }
                return Result.Succeeded;
            }
            catch(Exception ex) { message=ex.Message; return Result.Failed; }
        }

        private XYZ GetOrigin(FamilyInstance fi)
        {
            try { if(fi.Location is LocationPoint lp) return lp.Point; } catch{}
            try { if(fi.Location is LocationCurve lc) return lc.Curve.Evaluate(0.5,true); } catch{}
            return XYZ.Zero;
        }

        private List<XYZ> CalcPoints(DistSettings s, XYZ origin)
        {
            double spX=UnitUtils.ConvertToInternalUnits(s.SpX,UnitTypeId.Meters);
            double spY=UnitUtils.ConvertToInternalUnits(s.SpY,UnitTypeId.Meters);
            double rot=s.Rot*Math.PI/180.0;
            var raw=new List<(double x,double y)>();
            switch(s.Pattern)
            {
                case "Circle": int n=(s.Rows+s.Cols)*2; double r=Math.Min(s.Rows*spX,s.Cols*spY)/2; for(int i=0;i<n;i++) raw.Add((Math.Cos(2*Math.PI*i/n)*r,Math.Sin(2*Math.PI*i/n)*r)); break;
                case "Hex":    double hox=s.Cols*spX/2; for(int i=0;i<s.Rows;i++) for(int j=0;j<s.Cols;j++) raw.Add((j*spX+(i%2)*spX*0.5-hox,i*spY*0.866-s.Rows*spY*0.866/2)); break;
                case "Diag":   double dox=(s.Cols-1)*spX/2; for(int i=0;i<s.Rows;i++) for(int j=0;j<s.Cols;j++) raw.Add((j*spX-dox+i*spX*0.3,i*spY-(s.Rows-1)*spY/2)); break;
                default:       double gox=(s.Cols-1)*spX/2,goy=(s.Rows-1)*spY/2; for(int i=0;i<s.Rows;i++) for(int j=0;j<s.Cols;j++) raw.Add((j*spX-gox,i*spY-goy)); break;
            }
            return raw.Select(p=>new XYZ(origin.X+p.x*Math.Cos(rot)-p.y*Math.Sin(rot), origin.Y+p.x*Math.Sin(rot)+p.y*Math.Cos(rot), origin.Z)).ToList();
        }
    }
}
