// CircuitTaggerHandler.cs -- ME-Tools | Circuit Tagger
// Mayer E-Concept SRL
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace METools.FamilyPlacer
{
    public class CircuitTagStyle
    {
        public double GapMm      { get; set; } = 50.0;
        public double OffsetYMm  { get; set; } = 0.0;
        public double StackGapMm { get; set; } = 8.0;
    }

    public class CircuitTaggerHandler : IExternalEventHandler
    {
        public const string PARAM_VORSICHERUNG      = "Vorsicherung";
        public const string PARAM_FI                = "FI-Kreis";
        public const string PARAM_STROMKREIS        = "Stromkreis Tag";
        public const string PARAM_BELEUCHTUNGSKREIS = "Schaltkreis";
        public const string PARAM_APARTMENT         = "CAx_Apartment";
        public const string PARAM_BUILDING          = "CAx_Building";

        private const string TAG_FAMILY_NAME = "ME-Tools_CircuitTag";

        public CircuitTaggerRequest         Request           { get; set; } = new CircuitTaggerRequest();
        public CircuitTagStyle              TagStyle          { get; set; } = new CircuitTagStyle();
        public CircuitTaggerSettingsData    Settings          { get; set; } = new CircuitTaggerSettingsData();
        public Action<string>               OnStatus          { get; set; }
        public Action<List<string>>         OnApartmentValues { get; set; }
        public Action<List<string>>         OnBuildingValues  { get; set; }
        public Action                       OnDone            { get; set; }
        public Action<string>               OnError           { get; set; }
        public Action<CircuitTaggerRequest> OnParamsLoaded    { get; set; }

        public string GetName() => "ME-Tools Circuit Tagger";

        public void Execute(UIApplication app)
        {
            var req   = Request;
            var doc   = app.ActiveUIDocument?.Document;
            var uiDoc = app.ActiveUIDocument;
            if (doc == null || req == null || req.Action == CircuitTaggerAction.None) return;

            switch (req.Action)
            {
                case CircuitTaggerAction.ReadApartmentValues:
                    ExecuteReadDropdownValues(doc); break;
                case CircuitTaggerAction.WriteParamsAndPlaceTags:
                    ExecuteWriteAndTag(doc, uiDoc, req); break;
                case CircuitTaggerAction.LoadParamsFromSelection:
                    ExecuteLoadParams(doc, uiDoc); break;
                case CircuitTaggerAction.ClearCircuitData:
                    ExecuteClearCircuitData(doc, req.CircuitLabelToClear); break;
            }
        }

        // -- Read dropdown values for Apartment and Building ---------------
        private void ExecuteReadDropdownValues(Document doc)
        {
            var apts   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var builds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cat in GetElectricalCategories())
            {
                try
                {
                    foreach (var fi in new FilteredElementCollector(doc)
                        .OfCategory(cat).WhereElementIsNotElementType()
                        .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>())
                    {
                        var a = fi.LookupParameter(PARAM_APARTMENT)?.AsString()?.Trim();
                        var b = fi.LookupParameter(PARAM_BUILDING)?.AsString()?.Trim();
                        if (!string.IsNullOrEmpty(a)) apts.Add(a);
                        if (!string.IsNullOrEmpty(b)) builds.Add(b);
                    }
                }
                catch { }
            }
            OnApartmentValues?.Invoke(apts.OrderBy(v => v).ToList());
            OnBuildingValues?.Invoke(builds.OrderBy(v => v).ToList());
        }

        // -- Load params from selected element for editing -----------------
        private void ExecuteLoadParams(Document doc, UIDocument uiDoc)
        {
            try
            {
                var sel = uiDoc.Selection.GetElementIds();
                if (!sel.Any()) { Report("Select one element first, then click Load."); return; }
                var el = doc.GetElement(sel.First());
                if (el == null) { Report("Element not found."); return; }

                var sk     = el.LookupParameter(PARAM_STROMKREIS)?.AsString() ?? "";
                string fiVal = "", skVal = sk, subVal = "";

                // Parse "1F1_2" -> FI="1", Stromkreis="F1", SubIndex="2"
                // Parse "1F1"   -> FI="1", Stromkreis="F1", SubIndex=""
                if (!string.IsNullOrEmpty(sk))
                {
                    var underIdx = sk.LastIndexOf('_');
                    if (underIdx > 0 && underIdx < sk.Length - 1)
                    {
                        var suffix = sk.Substring(underIdx + 1);
                        if (suffix.All(char.IsDigit))
                        {
                            subVal = suffix;
                            sk     = sk.Substring(0, underIdx);
                        }
                    }
                    int i = 0;
                    while (i < sk.Length && char.IsDigit(sk[i])) i++;
                    if (i > 0 && i < sk.Length) { fiVal = sk.Substring(0, i); skVal = sk.Substring(i); }
                    else skVal = sk;
                }

                var loaded = new CircuitTaggerRequest
                {
                    Action            = CircuitTaggerAction.None,
                    Vorsicherung      = el.LookupParameter(PARAM_VORSICHERUNG)?.AsString()      ?? "",
                    FI                = fiVal,
                    Stromkreis        = skVal,
                    SubIndex          = subVal,
                    Beleuchtungskreis = el.LookupParameter(PARAM_BELEUCHTUNGSKREIS)?.AsString() ?? "",
                    Apartment         = el.LookupParameter(PARAM_APARTMENT)?.AsString()         ?? "",
                    Building          = el.LookupParameter(PARAM_BUILDING)?.AsString()          ?? "",
                };
                OnParamsLoaded?.Invoke(loaded);
                Report("Loaded. Edit the fields and click Apply & Tag to update.");
            }
            catch (Exception ex) { Report("Load failed: " + ex.Message); }
        }

        // -- Write params + place tags ------------------------------------
        private void ExecuteWriteAndTag(Document doc, UIDocument uiDoc, CircuitTaggerRequest req)
        {
            if (req.ElementIds == null || req.ElementIds.Count == 0)
            { Report("No elements selected."); return; }

            var view = uiDoc.ActiveView;
            if (view == null) { Report("No active view."); return; }

            FamilySymbol tagSymbol = FindTagSymbol(doc);
            bool canTag = tagSymbol != null;
            if (!canTag)
                Report("Tag family 'ME-Tools_CircuitTag' not loaded -- params written without tags.");

            // Build full label: FI + Stromkreis [+ "_" + SubIndex]
            string baseLabel = BuildCircuitLabel(req.FI, req.Stromkreis);
            string fullLabel = string.IsNullOrEmpty(req.SubIndex)
                ? baseLabel
                : baseLabel + "_" + req.SubIndex.Trim();

            int written = 0, tagged = 0, errors = 0;
            var errorMsgs = new List<string>();
            var groups = new Dictionary<string, List<TagPlacementInfo>>();
            var missingParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (var tx = new Transaction(doc, "ME-Tools: Write Circuit Params + Tags"))
            {
                tx.Start();
                var fopt = tx.GetFailureHandlingOptions();
                fopt.SetFailuresPreprocessor(new SilentPreprocessor());
                tx.SetFailureHandlingOptions(fopt);

                if (canTag && !tagSymbol.IsActive)
                {
                    try { tagSymbol.Activate(); doc.Regenerate(); }
                    catch { canTag = false; }
                }

                foreach (var id in req.ElementIds)
                {
                    try
                    {
                        var el = doc.GetElement(id);
                        if (el == null) continue;

                        if (!WriteParam(el, PARAM_VORSICHERUNG,      req.Vorsicherung))      missingParams.Add(PARAM_VORSICHERUNG);
                        if (!WriteParam(el, PARAM_FI,                req.FI))                missingParams.Add(PARAM_FI);
                        if (!WriteParam(el, PARAM_STROMKREIS,        fullLabel))              missingParams.Add(PARAM_STROMKREIS);
                        if (!WriteParam(el, PARAM_BELEUCHTUNGSKREIS, req.Beleuchtungskreis))  missingParams.Add(PARAM_BELEUCHTUNGSKREIS);
                        if (!WriteParam(el, PARAM_APARTMENT,         req.Apartment))          missingParams.Add(PARAM_APARTMENT);
                        if (!WriteParam(el, PARAM_BUILDING,          req.Building))           missingParams.Add(PARAM_BUILDING);
                        written++;

                        if (canTag && (view is ViewPlan || view is ViewSection))
                        {
                            var fi     = el as FamilyInstance;
                            var center = GetElementCenter(el);
                            var bb     = el.get_BoundingBox(view) ?? el.get_BoundingBox(null);
                            if (center == null) continue;

                            XYZ facing  = GetFacingDirection(fi);
                            string dir  = GetDirectionKey(facing);
                            double wallPos = Math.Abs(facing.X) > 0.5
                                ? Math.Round(center.X, 0)
                                : Math.Round(center.Y, 0);
                            string groupKey = $"{dir}_{wallPos}";

                            if (!groups.ContainsKey(groupKey))
                                groups[groupKey] = new List<TagPlacementInfo>();
                            groups[groupKey].Add(new TagPlacementInfo
                            {
                                ElementId   = id,
                                Center      = center,
                                BoundingBox = bb,
                                Facing      = facing,
                                DirKey      = dir,
                            });
                        }
                    }
                    catch (Exception ex) { errors++; errorMsgs.Add(ex.Message); }
                }

                if (canTag)
                {
                    double gapFt   = TagStyle.GapMm    / 304.8;
                    double offYFt  = TagStyle.OffsetYMm / 304.8;
                    double stackFt = TagStyle.StackGapMm / 304.8;
                    double tagHFt  = 0.033;

                    // Build lookup of existing circuit tags per element in this view
                    var existingTagsByElement = new HashSet<ElementId>();
                    try
                    {
                        foreach (var tag in new FilteredElementCollector(doc, view.Id)
                            .OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
                        {
                            try
                            {
                                foreach (var linkEl in tag.GetTaggedElementIds())
                                    existingTagsByElement.Add(linkEl.HostElementId);
                            }
                            catch { }
                        }
                    }
                    catch { }

                    // Find or create a TextNoteType for sub-labels
                    TextNoteType subLabelType = null;
                    bool hasSubLabel = !string.IsNullOrEmpty(req.SubLabel);
                    if (hasSubLabel)
                    {
                        try
                        {
                            subLabelType = new FilteredElementCollector(doc)
                                .OfClass(typeof(TextNoteType)).Cast<TextNoteType>()
                                .FirstOrDefault(t => t.Name == "ME-Tools SubLabel")
                                ?? DuplicateTextNoteType(doc,
                                    new FilteredElementCollector(doc)
                                        .OfClass(typeof(TextNoteType)).Cast<TextNoteType>()
                                        .FirstOrDefault());
                        }
                        catch { }
                    }

                    foreach (var grp in groups.Values)
                    {
                        bool isNS  = grp[0].DirKey == "N" || grp[0].DirKey == "S";
                        var orient = isNS ? TagOrientation.Horizontal : TagOrientation.Vertical;
                        var sorted = isNS
                            ? grp.OrderBy(p => p.Center.X).ToList()
                            : grp.OrderBy(p => p.Center.Y).ToList();

                        for (int i = 0; i < sorted.Count; i++)
                        {
                            try
                            {
                                var p  = sorted[i];
                                var el = doc.GetElement(p.ElementId);

                                // -- Place circuit tag (skip if already tagged) ---
                                if (!existingTagsByElement.Contains(p.ElementId))
                                {
                                    // Initial placement at element center + gap
                                    double tagX = p.Center.X + gapFt;
                                    double tagY = p.Center.Y + offYFt + i * (tagHFt + stackFt);
                                    var tagPos  = new XYZ(tagX, tagY, p.Center.Z);

                                    var newTag = IndependentTag.Create(doc, tagSymbol.Id, view.Id,
                                        new Reference(el), false, orient, tagPos);
                                    tagged++;

                                    // -- Smart alignment: read tag bounding box and reposition --
                                    // After creation, regenerate so bbox is valid, then shift
                                    // so the tag text sits cleanly to the right of the element.
                                    try
                                    {
                                        doc.Regenerate();
                                        var tagBB = newTag.get_BoundingBox(view);
                                        if (tagBB != null)
                                        {
                                            // For horizontal tags: tag insertion is at bb.Max.X (right edge)
                                            // We want bb.Min.X (left edge of text) = element right edge + gap
                                            double elRight = p.BoundingBox != null ? p.BoundingBox.Max.X : p.Center.X;
                                            double tagWidth = tagBB.Max.X - tagBB.Min.X;
                                            double tagHeight = tagBB.Max.Y - tagBB.Min.Y;

                                            double newTagX, newTagY;
                                            if (isNS)
                                            {
                                                // Horizontal tag: shift so left edge of tag = element right + gap
                                                double currentLeftX = tagBB.Min.X;
                                                double targetLeftX  = elRight + gapFt;
                                                double deltaX = targetLeftX - currentLeftX;
                                                newTagX = tagX + deltaX;
                                                newTagY = tagY;
                                            }
                                            else
                                            {
                                                // Vertical tag: align center Y with element center
                                                double tagCenterY = (tagBB.Min.Y + tagBB.Max.Y) * 0.5;
                                                double elRight2   = p.BoundingBox != null ? p.BoundingBox.Max.X : p.Center.X;
                                                newTagX = elRight2 + gapFt + tagWidth * 0.5;
                                                newTagY = p.Center.Y + offYFt + i * (tagHFt + stackFt);
                                            }

                                            newTag.TagHeadPosition = new XYZ(newTagX, newTagY, p.Center.Z);
                                        }
                                    }
                                    catch { } // alignment failed -- tag still placed at initial position
                                }

                                // -- Place SubLabel TextNote if provided ----------
                                if (hasSubLabel && (view is ViewPlan || view is ViewSection))
                                {
                                    try
                                    {
                                        // Place sub-label above the element (or offset by user Y + some extra)
                                        double elRight = p.BoundingBox != null ? p.BoundingBox.Max.X : p.Center.X;
                                        double subX = p.Center.X + gapFt;
                                        double subY = p.Center.Y + offYFt + i * (tagHFt + stackFt)
                                                    + tagHFt + (5.0 / 304.8); // just above circuit tag

                                        var opts = new TextNoteOptions
                                        {
                                            HorizontalAlignment = HorizontalTextAlignment.Center,
                                        };
                                        if (subLabelType != null)
                                            opts = new TextNoteOptions(subLabelType.Id)
                                            {
                                                HorizontalAlignment = HorizontalTextAlignment.Center,
                                            };

                                        var tn = TextNote.Create(doc, view.Id,
                                            new XYZ(subX, subY, p.Center.Z),
                                            req.SubLabel, opts);

                                        // Apply color override via graphic overrides
                                        try
                                        {
                                            var sc = Settings ?? new CircuitTaggerSettingsData();
                                            if (!string.IsNullOrEmpty(sc.SubLabelColorHex) && sc.SubLabelColorHex != "#000000")
                                            {
                                                var col = HexToRevitColor(sc.SubLabelColorHex);
                                                if (col != null)
                                                {
                                                    var ogs = new OverrideGraphicSettings();
                                                    ogs.SetProjectionLineColor(col);
                                                    view.SetElementOverrides(tn.Id, ogs);
                                                }
                                            }
                                        }
                                        catch { }
                                    }
                                    catch { }
                                }
                            }
                            catch (Exception ex)
                            {
                                // Previously a bare `catch { }` here -- a tag-placement
                                // failure for one circuit was invisible in the summary,
                                // even though the identical failure mode for parameter
                                // writing (the outer loop above) was already tracked.
                                // Recording it the same way keeps "honest reporting"
                                // consistent for both, not just one of them.
                                errors++;
                                errorMsgs.Add(ex.Message);
                            }
                        }
                    }
                }

                if (tx.GetStatus() == TransactionStatus.Started)
                    tx.Commit();
            }

            var summary = $"Done. {written} elements updated";
            if (tagged > 0) summary += $", {tagged} tags placed";
            if (!canTag)    summary += $" -- tag family '{TAG_FAMILY_NAME}' not loaded, no tags placed";
            if (missingParams.Count > 0)
                summary += $" -- NOT bound to this category, values not written: {string.Join(", ", missingParams)}";
            if (errors > 0) summary += $", {errors} errors: " + errorMsgs.FirstOrDefault();
            Report(summary);
            OnDone?.Invoke();
        }

        // -- Helpers -------------------------------------------------------
        public static string BuildCircuitLabel(string fi, string stromkreis)
            => (fi ?? "").Trim() + (stromkreis ?? "").Trim();

        // Strip sub-index suffix to get base circuit: "1F1_2" -> "1F1"
        public static string GetCircuitBase(string label)
        {
            if (string.IsNullOrEmpty(label)) return label;
            var idx = label.LastIndexOf('_');
            if (idx > 0 && idx < label.Length - 1)
            {
                var suffix = label.Substring(idx + 1);
                if (suffix.All(char.IsDigit)) return label.Substring(0, idx);
            }
            return label;
        }

        private static XYZ GetFacingDirection(FamilyInstance fi)
        {
            if (fi == null) return XYZ.BasisX;
            try { var f = fi.FacingOrientation; if (f != null && f.GetLength() > 0.01) return f.Normalize(); } catch { }
            try { var h = fi.HandOrientation;   if (h != null && h.GetLength() > 0.01) return h.Normalize(); } catch { }
            return XYZ.BasisX;
        }

        private static string GetDirectionKey(XYZ dir)
        {
            if (dir == null) return "E";
            return Math.Abs(dir.Y) >= Math.Abs(dir.X)
                ? (dir.Y >= 0 ? "N" : "S")
                : (dir.X >= 0 ? "E" : "W");
        }

        private static bool WriteParam(Element el, string paramName, string value)
        {
            if (string.IsNullOrEmpty(value)) return true; // nothing requested for this field -- not a failure
            try
            {
                var p = el.LookupParameter(paramName);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                {
                    p.Set(value);
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static XYZ GetElementCenter(Element el)
        {
            try
            {
                if (el.Location is LocationPoint lp) return lp.Point;
                var bb = el.get_BoundingBox(null);
                if (bb != null) return (bb.Min + bb.Max) * 0.5;
            }
            catch { }
            return null;
        }

        private static FamilySymbol FindTagSymbol(Document doc)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_MultiCategoryTags)
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(fs =>
                        string.Equals(fs.Family?.Name, TAG_FAMILY_NAME,
                            StringComparison.OrdinalIgnoreCase));
            }
            catch { return null; }
        }

        private static Autodesk.Revit.DB.Color HexToRevitColor(string hex)
        {
            try
            {
                hex = hex.TrimStart('#');
                if (hex.Length == 6)
                {
                    byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                    byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                    byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                    return new Autodesk.Revit.DB.Color(r, g, b);
                }
            }
            catch { }
            return null;
        }

        private void Report(string msg) => OnStatus?.Invoke(msg);

        private TextNoteType DuplicateTextNoteType(Document doc, TextNoteType baseType)
        {
            if (baseType == null) return null;
            try
            {
                var existing = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType)).Cast<TextNoteType>()
                    .FirstOrDefault(t => t.Name == "ME-Tools SubLabel");
                TextNoteType newType = existing ?? baseType.Duplicate("ME-Tools SubLabel") as TextNoteType;
                if (newType == null) return baseType;

                var s = Settings ?? new CircuitTaggerSettingsData();
                var ic = System.Globalization.CultureInfo.InvariantCulture;

                // TEXT_SIZE: mm -> feet
                SetParam(newType, BuiltInParameter.TEXT_SIZE, (s.SubLabelFontSizeMm / 25.4) / 12.0);

                // TEXT_FONT
                var fontP = newType.LookupParameter("Text Font");
                if (fontP != null && !fontP.IsReadOnly && !string.IsNullOrEmpty(s.SubLabelFontName))
                    fontP.Set(s.SubLabelFontName);

                // TEXT_IS_BOLD, TEXT_IS_ITALIC, TEXT_IS_UNDERLINE
                var boldP = newType.LookupParameter("Bold")      ?? newType.LookupParameter("Text Bold");
                var italP = newType.LookupParameter("Italic")    ?? newType.LookupParameter("Text Italic");
                var undlP = newType.LookupParameter("Underline") ?? newType.LookupParameter("Text Underline");
                if (boldP != null && !boldP.IsReadOnly) boldP.Set(s.SubLabelBold      ? 1 : 0);
                if (italP != null && !italP.IsReadOnly) italP.Set(s.SubLabelItalic    ? 1 : 0);
                if (undlP != null && !undlP.IsReadOnly) undlP.Set(s.SubLabelUnderline ? 1 : 0);

                // Width factor
                var wfP = newType.LookupParameter("Width Factor");
                if (wfP != null && !wfP.IsReadOnly) wfP.Set(s.SubLabelWidthFactor);

                // Tab size: mm -> feet
                var tabP = newType.LookupParameter("Tab Size");
                if (tabP != null && !tabP.IsReadOnly)
                    tabP.Set((s.SubLabelTabSizeMm / 25.4) / 12.0);

                // Background: TEXT_BACKGROUND 1=opaque, 0=transparent
                SetParam(newType, BuiltInParameter.TEXT_BACKGROUND, s.SubLabelOpaque ? 1 : 0);

                // Show border
                var borderP = newType.LookupParameter("Show Border") ?? newType.LookupParameter("Border");
                if (borderP != null && !borderP.IsReadOnly) borderP.Set(s.SubLabelShowBorder ? 1 : 0);

                // Line weight
                var lwP = newType.LookupParameter("Line Weight");
                if (lwP != null && !lwP.IsReadOnly && lwP.StorageType == StorageType.Integer)
                    lwP.Set(s.SubLabelLineWeight);

                // Leader/Border offset: mm -> feet
                var loP = newType.LookupParameter("Leader/Border Offset") ?? newType.LookupParameter("Border Offset");
                if (loP != null && !loP.IsReadOnly)
                    loP.Set((s.SubLabelLeaderOffsetMm / 25.4) / 12.0);

                return newType;
            }
            catch { return baseType; }
        }

        private static void SetParam(Element el, BuiltInParameter bip, double val)
        {
            try { var p = el.get_Parameter(bip); if (p != null && !p.IsReadOnly) p.Set(val); } catch { }
        }

        private static void SetParam(Element el, BuiltInParameter bip, int val)
        {
            try { var p = el.get_Parameter(bip); if (p != null && !p.IsReadOnly) p.Set(val); } catch { }
        }

        private static IEnumerable<BuiltInCategory> GetElectricalCategories() => new[]
        {
            BuiltInCategory.OST_ElectricalFixtures,
            BuiltInCategory.OST_LightingFixtures,
            BuiltInCategory.OST_LightingDevices,
            BuiltInCategory.OST_ElectricalEquipment,
            BuiltInCategory.OST_DataDevices,
            BuiltInCategory.OST_FireAlarmDevices,
            BuiltInCategory.OST_CommunicationDevices,
            BuiltInCategory.OST_SecurityDevices,
        };

        public static List<ExportRow> ReadAllTaggedElements(Document doc)
        {
            var rows = new List<ExportRow>();
            Phase phase = null;
            try { phase = new FilteredElementCollector(doc).OfClass(typeof(Phase)).Cast<Phase>().LastOrDefault(); } catch { }

            foreach (var cat in new[]
            {
                BuiltInCategory.OST_ElectricalFixtures, BuiltInCategory.OST_LightingFixtures,
                BuiltInCategory.OST_LightingDevices,    BuiltInCategory.OST_ElectricalEquipment,
                BuiltInCategory.OST_DataDevices,        BuiltInCategory.OST_FireAlarmDevices,
                BuiltInCategory.OST_CommunicationDevices, BuiltInCategory.OST_SecurityDevices,
            })
            {
                try
                {
                    foreach (var fi in new FilteredElementCollector(doc)
                        .OfCategory(cat).WhereElementIsNotElementType()
                        .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>())
                    {
                        var apt  = fi.LookupParameter(PARAM_APARTMENT)?.AsString()         ?? "";
                        var bld  = fi.LookupParameter(PARAM_BUILDING)?.AsString()          ?? "";
                        var vs   = fi.LookupParameter(PARAM_VORSICHERUNG)?.AsString()      ?? "";
                        var fiP  = fi.LookupParameter(PARAM_FI)?.AsString()                ?? "";
                        var sk   = fi.LookupParameter(PARAM_STROMKREIS)?.AsString()        ?? "";
                        var bk   = fi.LookupParameter(PARAM_BELEUCHTUNGSKREIS)?.AsString() ?? "";

                        if (string.IsNullOrEmpty(apt) && string.IsNullOrEmpty(bld) &&
                            string.IsNullOrEmpty(vs)  && string.IsNullOrEmpty(sk)  &&
                            string.IsNullOrEmpty(fiP) && string.IsNullOrEmpty(bk))
                            continue;

                        rows.Add(new ExportRow
                        {
                            Building          = bld,
                            Apartment         = apt,
                            CircuitLabel      = sk,
                            Vorsicherung      = vs,
                            FI                = fiP,
                            Stromkreis        = sk,
                            Beleuchtungskreis = bk,
                            Category          = fi.Category?.Name ?? "",
                            CategoryId        = fi.Category?.Id?.IntegerValue ?? 0,
                            FamilyName        = fi.Symbol?.Family?.Name ?? fi.Name ?? "",
                            Room              = GetRoomName(doc, fi, phase),
                            ElementId         = fi.Id.IntegerValue.ToString(),
                        });
                    }
                }
                catch { }
            }
            return rows;
        }

        private static string GetRoomName(Document doc, FamilyInstance fi, Phase phase)
        {
            try
            {
                if (fi.Room  != null) return fi.Room.Name  ?? "";
                if (fi.Space != null) return fi.Space.Name ?? "";
                var lp = fi.Location as LocationPoint;
                if (lp != null)
                {
                    var r = phase != null ? doc.GetRoomAtPoint(lp.Point, phase) : doc.GetRoomAtPoint(lp.Point);
                    if (r != null) return r.Name ?? "";
                }
            }
            catch { }
            return "";
        }

        // -- Clear all circuit params from elements matching a circuit label --
        private void ExecuteClearCircuitData(Document doc, string circuitLabel)
        {
            if (string.IsNullOrEmpty(circuitLabel)) { Report("No circuit label to clear."); return; }
            int cleared = 0;
            try
            {
                var rows = ReadAllTaggedElements(doc);
                var toClear = rows
                    .Where(r => r.CircuitLabel == circuitLabel || r.Stromkreis == circuitLabel)
                    .ToList();

                if (toClear.Count == 0) { Report($"No elements found with circuit '{circuitLabel}'."); OnDone?.Invoke(); return; }

                using (var tx = new Transaction(doc, $"ME-Tools: Clear circuit data '{circuitLabel}'"))
                {
                    tx.Start();
                    var fopt = tx.GetFailureHandlingOptions();
                    fopt.SetFailuresPreprocessor(new SilentPreprocessor());
                    tx.SetFailureHandlingOptions(fopt);

                    foreach (var r in toClear)
                    {
                        try
                        {
                            var el = doc.GetElement(new ElementId(int.Parse(r.ElementId)));
                            if (el == null) continue;
                            ClearParam(el, PARAM_STROMKREIS);
                            ClearParam(el, PARAM_FI);
                            ClearParam(el, PARAM_VORSICHERUNG);
                            ClearParam(el, PARAM_BELEUCHTUNGSKREIS);
                            ClearParam(el, PARAM_APARTMENT);
                            ClearParam(el, PARAM_BUILDING);
                            cleared++;
                        }
                        catch { }
                    }
                    if (tx.GetStatus() == TransactionStatus.Started) tx.Commit();
                }
            }
            catch (Exception ex) { Report("Clear failed: " + ex.Message); return; }

            Report($"Cleared circuit data from {cleared} elements.");
            OnDone?.Invoke();
        }

        private static void ClearParam(Element el, string paramName)
        {
            try
            {
                var p = el.LookupParameter(paramName);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                    p.Set("");
            }
            catch { }
        }

        private class SilentPreprocessor : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
            {
                foreach (var f in a.GetFailureMessages())
                    if (f.GetSeverity() == FailureSeverity.Warning) a.DeleteWarning(f);
                return FailureProcessingResult.Continue;
            }
        }

        private class TagPlacementInfo
        {
            public ElementId      ElementId   { get; set; }
            public XYZ            Center      { get; set; }
            public BoundingBoxXYZ BoundingBox { get; set; }
            public XYZ            Facing      { get; set; }
            public string         DirKey      { get; set; }
        }
    }
}
