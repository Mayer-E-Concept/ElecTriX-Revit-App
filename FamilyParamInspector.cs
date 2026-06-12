// FamilyParamInspector.cs -- ME-Tools | Family Placer
// Mayer E-Concept SRL -- reads editable instance parameters of a family on demand
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace METools.FamilyPlacer
{
    public class FamilyParamInspectorHandler : IExternalEventHandler
    {
        public string FamilyName { get; set; } = "";
        public string TypeName   { get; set; } = "";
        public Action<List<FamilyParamInfo>> OnResult { get; set; }

        // Inline-handled params (own UI columns) -- excluded from the popup
        private static readonly HashSet<string> _inlineParams =
            new HashSet<string> { "Niveau", "2DX_Versatzfaktor", "2DY_Versatzfaktor" };

        // Display order, matching the Revit "Dimensions" group
        private static readonly string[] _dimOrder =
        {
            "Box Diameter", "Corner Offset", "Level / Height",
            "2D X Offset Factor", "2D Y Offset Factor", "3D Z Level Offset Factor",
            "3D X Offset Factor", "3D X Delta Correction", "Center on 3D X",
            "Y Offset = Frame", "Set Combo Frame", "Show 2D Frame",
            "Frame Count X", "Frame Count Z", "Frame Width", "Frame Height", "Box Depth",
        };

        private static int OrderIndex(string label)
        {
            for (int i = 0; i < _dimOrder.Length; i++)
                if (_dimOrder[i] == label) return i;
            return int.MaxValue;
        }

        public void Execute(UIApplication app)
        {
            var result = new List<FamilyParamInfo>();
            try
            {
                var doc = app.ActiveUIDocument.Document;
                var family = FindFamily(doc, FamilyName);
                if (family != null && family.IsEditable)
                    result = ReadParams(doc, family, TypeName);
            }
            catch { }
            OnResult?.Invoke(result);
        }

        public string GetName() => "ME-Tools FamilyParam Inspector";

        private static Family FindFamily(Document doc, string familyName)
        {
            if (string.IsNullOrEmpty(familyName)) return null;
            foreach (var e in new FilteredElementCollector(doc).OfClass(typeof(Family)))
                if (e is Family f && f.Name == familyName) return f;
            return null;
        }

        private static List<FamilyParamInfo> ReadParams(Document doc, Family family, string typeName)
        {
            var list = new List<FamilyParamInfo>();
            Document fdoc = null;
            try
            {
                fdoc = doc.EditFamily(family);
                var fm = fdoc.FamilyManager;

                FamilyType ftype = null;
                foreach (FamilyType ft in fm.Types)
                    if (ft.Name == typeName) { ftype = ft; break; }
                if (ftype == null) ftype = fm.CurrentType;

                foreach (FamilyParameter fp in fm.Parameters)
                {
                    if (!fp.IsInstance || fp.IsReadOnly) continue;

                    var st = fp.StorageType;
                    if (st != StorageType.Double && st != StorageType.Integer) continue;

                    var def = fp.Definition;
                    // only family-defined (custom) params; skip built-ins
                    if (def is InternalDefinition idef &&
                        idef.BuiltInParameter != BuiltInParameter.INVALID) continue;

                    // only the "Dimensions" group (PG_GEOMETRY) -- matches the Properties palette
                    ForgeTypeId grp = null;
                    try { grp = def.GetGroupTypeId(); } catch { }
                    if (grp == null || grp.TypeId != GroupTypeId.Geometry.TypeId) continue;

                    string name = def.Name;
                    if (_inlineParams.Contains(name)) continue;

                    ForgeTypeId spec = null;
                    try { spec = def.GetDataType(); } catch { }
                    bool isYesNo  = spec != null && spec.TypeId == SpecTypeId.Boolean.YesNo.TypeId;
                    bool isLength = spec != null && spec.TypeId == SpecTypeId.Length.TypeId;

                    string kind = isYesNo ? "yesno"
                                : st == StorageType.Integer ? "int"
                                : isLength ? "length" : "double";

                    string val = "";
                    try
                    {
                        if (ftype != null && ftype.HasValue(fp))
                        {
                            if (st == StorageType.Integer)
                                val = (ftype.AsInteger(fp) ?? 0).ToString();
                            else
                            {
                                double iv = ftype.AsDouble(fp) ?? 0.0;
                                double dv = isLength
                                    ? UnitUtils.ConvertFromInternalUnits(iv, UnitTypeId.Millimeters)
                                    : iv;
                                val = dv.ToString("0.###");
                            }
                        }
                    }
                    catch { }

                    list.Add(new FamilyParamInfo
                    {
                        Name         = name,
                        Label        = ParamLabels.Translate(name),
                        Kind         = kind,
                        DefaultValue = val,
                    });
                }
            }
            catch { }
            finally { try { fdoc?.Close(false); } catch { } }
            list = list.OrderBy(p => OrderIndex(p.Label)).ToList();
            return list;
        }
    }

    // German Revit parameter name -> English display label.
    // Unknown names fall back to the raw name (true dynamic support).
    public static class ParamLabels
    {
        private static readonly Dictionary<string, string> _map =
            new Dictionary<string, string>
        {
            { "Dosendurchmesser",            "Box Diameter" },
            { "Eckenversatz",                "Corner Offset" },
            { "Eckversatz",                  "Corner Offset" },
            { "Niveau",                      "Level / Height" },
            { "2DX_Versatzfaktor",           "2D X Offset Factor" },
            { "2DY_Versatzfaktor",           "2D Y Offset Factor" },
            { "3DZ_Niveau_Versatzfaktor",    "3D Z Level Offset Factor" },
            { "3DX_Versatzfaktor",           "3D X Offset Factor" },
            { "3DX_Delta_Korrekturversatz",  "3D X Delta Correction" },
            { "3DX_Mittig_setzen",           "Center on 3D X" },
            { "Y_Versatz_gleich_Rahmen",     "Y Offset = Frame" },
            { "Kombirahmen setzen",          "Set Combo Frame" },
            { "2D Rahmen anzeigen",          "Show 2D Frame" },
            { "Rahmen X Anzahl",             "Frame Count X" },
            { "Rahmen Z Anzahl",             "Frame Count Z" },
            { "Rahmenbreite",                "Frame Width" },
            { "Rahmenh\u00F6he",             "Frame Height" }, // Rahmenhoehe
            { "Dosentiefe",                  "Box Depth" },
        };

        public static string Translate(string name)
            => (!string.IsNullOrEmpty(name) && _map.TryGetValue(name, out var en)) ? en : name;
    }
}
