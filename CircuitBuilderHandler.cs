using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;

namespace METools.FamilyPlacer
{
    public class CircuitBuilderResult
    {
        public bool  Abgebrochen             { get; set; }
        public int   RaeumeGescannt          { get; set; }
        public int   BauteileGefunden        { get; set; }
        public int   BauteileBereitsAufKreis { get; set; }
        public int   BauteileOhneRaum        { get; set; }
        public int   BauteileOhneKonfig      { get; set; }
        public int   KreiseNormal            { get; set; }
        public int   KreiseSonder            { get; set; }
        public int   BauteileZugewiesen      { get; set; }
        public int   BestehendeKreiseUpdated { get; set; }
        public int   SchemeParamsGesetzt     { get; set; }
        public int   LoadNameGesetzt         { get; set; }
        public int   PanelsNamingSchemeGesetzt { get; set; }
        public List<string>    Warnungen     { get; set; } = new List<string>();
        public List<string>    Fehler        { get; set; } = new List<string>();
        public List<ElementId> FehlerIds     { get; set; } = new List<ElementId>();

        public int KreiseErstellt { get { return KreiseNormal + KreiseSonder; } }

        public string BuildSummary()
        {
            var sb = new StringBuilder();
            if (Abgebrochen) sb.AppendLine("Abgebrochen.\n");
            sb.AppendLine("Scan:");
            sb.AppendLine($"  • Räume in Config:          {RaeumeGescannt}");
            sb.AppendLine($"  • Bauteile gefunden:        {BauteileGefunden}");
            sb.AppendLine($"  • Bereits auf Stromkreis:   {BauteileBereitsAufKreis}");
            sb.AppendLine($"  • Ohne Raumzuordnung:       {BauteileOhneRaum}");
            sb.AppendLine($"  • Ohne Config-Eintrag:      {BauteileOhneKonfig}");
            sb.AppendLine();
            sb.AppendLine("Ergebnis:");
            sb.AppendLine($"  ✓ Neue Normal-Kreise:       {KreiseNormal}");
            sb.AppendLine($"  ✓ Neue Sonder-Kreise:       {KreiseSonder}");
            sb.AppendLine($"  ✓ Bestehende aktualisiert:  {BestehendeKreiseUpdated}");
            sb.AppendLine($"  ✓ Bauteile zugewiesen:      {BauteileZugewiesen}");
            sb.AppendLine($"  ✓ Scheme-Parameter gesetzt: {SchemeParamsGesetzt}");
            sb.AppendLine($"  ✓ Load Name gesetzt:        {LoadNameGesetzt}");
            if (PanelsNamingSchemeGesetzt > 0)
                sb.AppendLine($"  ✓ Panel-Naming angepasst:   {PanelsNamingSchemeGesetzt}");

            if (Warnungen.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Warnungen:");
                var grouped = Warnungen.GroupBy(w => w).Select(g => new { Msg = g.Key, Count = g.Count() });
                foreach (var g in grouped.Take(10))
                    sb.AppendLine(g.Count > 1 ? $"  – ({g.Count}×) {g.Msg}" : "  – " + g.Msg);
                if (grouped.Count() > 10) sb.AppendLine($"  … und {grouped.Count() - 10} weitere");
            }

            if (Fehler.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Fehler (erste 10):");
                foreach (var f in Fehler.Take(10)) sb.AppendLine("  ✗ " + f);
                if (Fehler.Count > 10) sb.AppendLine($"  … und {Fehler.Count - 10} weitere");
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Schluckt harmlose Warnings (unbenutzte Elemente, etc.) damit sie keine
    /// modalen Dialoge öffnen. Errors werden nicht versteckt — Revit rollt bei
    /// echten Errors die Transaction wie gewohnt zurück, aber wir fangen das
    /// mit SubTransactions pro Kreis ab.
    /// </summary>
    public class ElecTriXFailuresPreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            var failures = accessor.GetFailureMessages();
            foreach (var f in failures)
            {
                var sev = f.GetSeverity();
                // Warnings wegklicken — sie würden sonst den User stören
                if (sev == FailureSeverity.Warning)
                    accessor.DeleteWarning(f);
            }
            return FailureProcessingResult.Continue;
        }
    }

    public class CircuitBuilderHandler
    {
        private static readonly BuiltInCategory[] ELEC_KATEGORIEN =
        {
            BuiltInCategory.OST_ElectricalFixtures,
            BuiltInCategory.OST_LightingFixtures,
        };

        private const string PARAM_SONDERSTECKDOSE = "Sondersteckdose";

        // Panel-Parameter-IDs (verifiziert via Nonica)
        private const int PARAM_PANEL_CIRCUIT_NAMING = -1140087;

        private static readonly double[] Z_PROBE_OFFSETS_FT_DEFAULT =
            { 1.0, 3.0, 5.0, 0.1, 0.0, -0.5, 6.0, 8.0, 10.0 };

        public CircuitBuilderResult Run(UIDocument uiDoc)
        {
            var result = new CircuitBuilderResult();
            var doc    = uiDoc.Document;
            var settings = ElecTriXSettingsStorage.Laden();

            var konfigJson = KonfigStorage.Laden(doc);
            if (string.IsNullOrWhiteSpace(konfigJson))
            {
                result.Abgebrochen = true;
                result.Fehler.Add("Keine Konfiguration gefunden — zuerst speichern.");
                return result;
            }

            ProjektKonfiguration konfig;
            try { konfig = JsonSerializer.Deserialize<ProjektKonfiguration>(konfigJson) ?? new ProjektKonfiguration(); }
            catch (Exception ex) { result.Abgebrochen = true; result.Fehler.Add("Config: " + ex.Message); return result; }

            if (konfig.RaumVerteiler == null || konfig.RaumVerteiler.Count == 0)
            {
                result.Abgebrochen = true;
                result.Fehler.Add("Keine Raum→Verteiler-Zuordnung.");
                return result;
            }
            result.RaeumeGescannt = konfig.RaumVerteiler.Count(kv => !string.IsNullOrWhiteSpace(kv.Value));

            Phase phase = new FilteredElementCollector(doc).OfClass(typeof(Phase)).Cast<Phase>().LastOrDefault();
            var panelByName = BuildPanelCatalog(doc);
            var levelCache  = BuildLevelCache(doc);
            var panelPrefixCache = BuildPanelPrefixCache(doc);   // name → (prefix, separator)
            var raumNameCache = BuildRaumNameCache(doc);         // raumNr → raumName

            // Global gewählten Naming-Scheme-Element suchen
            ElementId globalSchemeId = ElementId.InvalidElementId;
            if (!string.IsNullOrWhiteSpace(konfig.GewaehltesNamingScheme))
            {
                try
                {
                    var scheme = RevitDatenHelper.LeseVerfuegbareNamingSchemes(doc)
                        .FirstOrDefault(s => string.Equals(s.Name, konfig.GewaehltesNamingScheme, StringComparison.OrdinalIgnoreCase));
                    if (scheme != null) globalSchemeId = scheme.Id;
                }
                catch { }
            }

            var elements = new List<FamilyInstance>();
            foreach (var cat in ELEC_KATEGORIEN)
            {
                try
                {
                    elements.AddRange(new FilteredElementCollector(doc)
                        .OfCategory(cat).WhereElementIsNotElementType()
                        .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>());
                }
                catch { }
            }
            result.BauteileGefunden = elements.Count;

            var normaleGruppen = new Dictionary<string, GruppeInfo>(StringComparer.OrdinalIgnoreCase);
            var sonderGruppen  = new Dictionary<string, GruppeInfo>(StringComparer.OrdinalIgnoreCase);
            var bestehendeKreise = new HashSet<ElementId>();
            var betroffenePanels = new HashSet<ElementId>();

            foreach (var fi in elements)
            {
                if (IstBereitsAufPowerCircuit(fi, out var existingCircuit))
                {
                    result.BauteileBereitsAufKreis++;
                    var nr = FindeRaumFuerBauteil(doc, fi, phase, levelCache, settings);
                    if (!string.IsNullOrWhiteSpace(nr)
                        && konfig.RaumVerteiler.TryGetValue(nr, out var p0)
                        && !string.IsNullOrWhiteSpace(p0)
                        && existingCircuit != null)
                    {
                        bestehendeKreise.Add(existingCircuit.Id);
                    }
                    continue;
                }

                var raumNummer = FindeRaumFuerBauteil(doc, fi, phase, levelCache, settings);
                if (string.IsNullOrWhiteSpace(raumNummer)) { result.BauteileOhneRaum++; continue; }

                if (!konfig.RaumVerteiler.TryGetValue(raumNummer, out var panelName)
                    || string.IsNullOrWhiteSpace(panelName))
                { result.BauteileOhneKonfig++; continue; }

                var g = BuildGruppeFromConfig(konfig, raumNummer, panelName, fi,
                    panelPrefixCache, raumNameCache, settings);
                if (g == null) { result.BauteileOhneKonfig++; continue; }

                if (g.IstSonder)
                {
                    var skey = panelName + "||S|" + g.SonderKuerzel;
                    if (!sonderGruppen.TryGetValue(skey, out var sg)) { sonderGruppen[skey] = g; sg = g; }
                    sg.Elemente.Add(fi);
                }
                else
                {
                    var key = panelName + "||N|" + raumNummer;
                    if (!normaleGruppen.TryGetValue(key, out var g2)) { normaleGruppen[key] = g; g2 = g; }
                    g2.Elemente.Add(fi);
                }

                if (panelByName.TryGetValue(panelName, out var p)) betroffenePanels.Add(p.Id);
            }

            if (normaleGruppen.Count == 0 && sonderGruppen.Count == 0 && bestehendeKreise.Count == 0)
            {
                result.Warnungen.Add("Keine passenden Bauteile.");
                return result;
            }

            using (var tx = new Transaction(doc, "ElecTriX: Stromkreise erstellen/aktualisieren"))
            {
                tx.Start();
                var fo = tx.GetFailureHandlingOptions();
                fo.SetClearAfterRollback(true);
                fo.SetForcedModalHandling(false);
                // WICHTIG: Preprocessor der nur Warnings ignoriert, Errors aber
                // NICHT das gesamte Commit rollback zwingen lässt
                fo.SetFailuresPreprocessor(new ElecTriXFailuresPreprocessor());
                tx.SetFailureHandlingOptions(fo);

                // Erst: Naming Scheme global auf alle betroffenen Panels setzen
                if (globalSchemeId != null && globalSchemeId.IntegerValue > 0)
                {
                    foreach (var pid in betroffenePanels)
                    {
                        using (var subTx = new SubTransaction(doc))
                        {
                            try
                            {
                                subTx.Start();
                                var panel = doc.GetElement(pid) as FamilyInstance;
                                if (panel == null) { subTx.RollBack(); continue; }
                                Parameter p = null;
                                try { p = panel.get_Parameter((BuiltInParameter)PARAM_PANEL_CIRCUIT_NAMING); } catch { }
                                if (p == null) p = panel.LookupParameter("Circuit Naming");
                                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.ElementId)
                                {
                                    var cur = p.AsElementId();
                                    if (cur != globalSchemeId)
                                    {
                                        p.Set(globalSchemeId);
                                        result.PanelsNamingSchemeGesetzt++;
                                    }
                                }
                                subTx.Commit();
                            }
                            catch
                            {
                                try { if (subTx.HasStarted() && !subTx.HasEnded()) subTx.RollBack(); } catch { }
                            }
                        }
                    }
                }

                foreach (var g in normaleGruppen.Values) ErstelleKreis(doc, g, panelByName, result, false, settings);
                foreach (var g in sonderGruppen.Values)  ErstelleKreis(doc, g, panelByName, result, true,  settings);

                // Bestehende Kreise mit Parametern nachziehen
                foreach (var cid in bestehendeKreise)
                {
                    using (var subTx = new SubTransaction(doc))
                    {
                        try
                        {
                            subTx.Start();
                            var sys = doc.GetElement(cid) as ElectricalSystem;
                            if (sys == null) { subTx.RollBack(); continue; }
                            var fi0 = ErstesElementAusKreis(sys);
                            if (fi0 == null) { subTx.RollBack(); continue; }
                            var nr = FindeRaumFuerBauteil(doc, fi0, phase, levelCache, settings);
                            if (string.IsNullOrWhiteSpace(nr)) { subTx.RollBack(); continue; }
                            if (!konfig.RaumVerteiler.TryGetValue(nr, out var pn)) { subTx.RollBack(); continue; }
                            var g = BuildGruppeFromConfig(konfig, nr, pn, fi0,
                                panelPrefixCache, raumNameCache, settings);
                            if (g == null) { subTx.RollBack(); continue; }
                            foreach (Element el in sys.Elements)
                                if (el is FamilyInstance fi && !g.Elemente.Contains(fi))
                                    g.Elemente.Add(fi);
                            g.Geraet       = string.IsNullOrEmpty(g.Geraet)
                                              ? BestimmeGeraeteart(g.Elemente, settings) : g.Geraet;
                            g.LoadNameHint = BaueLoadName(g, raumNameCache, settings);
                            if (SetzeSchemeParameter(sys, g, result, settings)) result.BestehendeKreiseUpdated++;
                            subTx.Commit();
                        }
                        catch
                        {
                            try { if (subTx.HasStarted() && !subTx.HasEnded()) subTx.RollBack(); } catch { }
                        }
                    }
                }

                // Transaction abschließen — Status prüfen!
                var commitStatus = tx.Commit();
                if (commitStatus != TransactionStatus.Committed)
                {
                    result.Abgebrochen = true;
                    result.Fehler.Insert(0,
                        $"KRITISCH: Transaction wurde von Revit abgelehnt (Status: {commitStatus}). " +
                        "Keine Änderungen wurden gespeichert. Die vermeintlich erstellten Kreise " +
                        "sind NICHT im Modell. Ursache meist: kritischer Revit-Failure " +
                        "(z.B. bei Panel-Voltage-Mismatch).");
                    // Counter zurücksetzen damit der Report ehrlich ist
                    result.KreiseNormal = 0;
                    result.KreiseSonder = 0;
                    result.BauteileZugewiesen = 0;
                    result.SchemeParamsGesetzt = 0;
                    result.LoadNameGesetzt = 0;
                    result.BestehendeKreiseUpdated = 0;
                    result.PanelsNamingSchemeGesetzt = 0;
                }
            }

            if (result.FehlerIds.Count > 0)
            {
                try { uiDoc.Selection.SetElementIds(result.FehlerIds); } catch { }
            }
            return result;
        }

        private static GruppeInfo BuildGruppeFromConfig(ProjektKonfiguration konfig,
            string raumNummer, string panelName, FamilyInstance fi,
            Dictionary<string, (string Prefix, string Sep)> panelPrefixCache,
            Dictionary<string, string> raumNameCache,
            ElecTriXSettings settings)
        {
            string pfx = "", sep = "-";
            if (panelPrefixCache != null && panelPrefixCache.TryGetValue(panelName, out var pp))
            {
                pfx = pp.Prefix ?? "";
                sep = string.IsNullOrEmpty(pp.Sep) ? "-" : pp.Sep;
            }

            string raumName = "";
            if (raumNameCache != null && !string.IsNullOrEmpty(raumNummer)
                && raumNameCache.TryGetValue(raumNummer, out var rn))
                raumName = rn;

            // Template-Werte für die Formatierung
            var werte = new Dictionary<string, string>
            {
                { "prefix",   pfx },
                { "sep",      sep },
                { "matchpfx", KonfigViewModel.AbleiteMatchPrefix(pfx, panelName, settings) },
                { "raumnr",   raumNummer ?? "" },
                { "raumname", raumName },
            };

            // Defaults aus Templates
            string defaultVS = TemplateEngine.Fuelle(settings.VorsicherungTemplate, werte);
            string defaultFI = TemplateEngine.Fuelle(settings.FIKreisTemplate,      werte);
            string defaultSK = TemplateEngine.Fuelle(settings.SchaltkreisTemplate,  werte);
            string defaultSI = TemplateEngine.Fuelle(settings.SicherungTemplate,    werte);

            var sonderKuerzel = LiesSonderkuerzel(fi);
            if (!string.IsNullOrEmpty(sonderKuerzel))
            {
                string sich = konfig.SonderStromkreise != null
                           && konfig.SonderStromkreise.TryGetValue(sonderKuerzel, out var sc)
                           && !string.IsNullOrWhiteSpace(sc) && sc != "??"
                    ? sc : sonderKuerzel;

                var g = new GruppeInfo
                {
                    PanelName     = panelName,
                    RaumNummer    = raumNummer,
                    Sicherung     = sich,
                    Vorsicherung  = Get(konfig.RaumVorsicherung, raumNummer, defaultVS),
                    FIKreis       = Get(konfig.RaumFIKreis,      raumNummer, defaultFI),
                    Geraet        = sonderKuerzel,
                    Schaltkreis   = Get(konfig.RaumSchaltkreis,  raumNummer,
                                         string.IsNullOrEmpty(defaultSK) ? sich : defaultSK),
                    IstSonder     = true,
                    SonderKuerzel = sonderKuerzel,
                };
                g.LoadNameHint = BaueLoadName(g, raumNameCache, settings);
                return g;
            }

            string sichN = Get(konfig.RaumStromkreisPro, raumNummer,
                               string.IsNullOrEmpty(defaultSI) ? "" : defaultSI);
            if (string.IsNullOrWhiteSpace(sichN)) sichN = "R" + raumNummer;

            var gn = new GruppeInfo
            {
                PanelName    = panelName,
                RaumNummer   = raumNummer,
                Sicherung    = sichN,
                Vorsicherung = Get(konfig.RaumVorsicherung, raumNummer, defaultVS),
                FIKreis      = Get(konfig.RaumFIKreis,      raumNummer, defaultFI),
                Geraet       = Get(konfig.RaumGeraet,       raumNummer, ""),  // wird per BestimmeGeraeteart() nachgezogen
                Schaltkreis  = Get(konfig.RaumSchaltkreis,  raumNummer,
                                    string.IsNullOrEmpty(defaultSK) ? sichN : defaultSK),
                IstSonder    = false,
            };
            gn.LoadNameHint = BaueLoadName(gn, raumNameCache, settings);
            return gn;
        }

        /// <summary>
        /// Baut den Load Name aus dem Template der Settings.
        /// Wenn das Ergebnis leer ist (z.B. weil Raumname fehlt), wird
        /// auf Raumnummer + Gerät zurückgefallen.
        /// </summary>
        private static string BaueLoadName(GruppeInfo g, Dictionary<string, string> raumNameCache,
            ElecTriXSettings settings)
        {
            string raumName = "";
            if (raumNameCache != null && !string.IsNullOrEmpty(g.RaumNummer)
                && raumNameCache.TryGetValue(g.RaumNummer, out var rn))
                raumName = rn;

            string geraet = !string.IsNullOrEmpty(g.Geraet) ? g.Geraet
                          : BestimmeGeraeteart(g.Elemente, settings);
            if (string.IsNullOrEmpty(geraet)) geraet = settings.DefaultGeraetLabel ?? "Stromkreis";

            var werte = new Dictionary<string, string>
            {
                { "geraet",   geraet },
                { "raumname", raumName },
                { "raumnr",   g.RaumNummer ?? "" },
                { "prefix",   "" },
                { "sep",      "" },
            };

            var template = string.IsNullOrWhiteSpace(settings.LoadNameTemplate)
                ? "{raumname}"
                : settings.LoadNameTemplate;

            var result = TemplateEngine.Fuelle(template, werte).Trim();

            // Safety-Net: Wenn LoadName nach Template-Fill leer oder nur Leerzeichen
            // ist (weil z.B. Raumname fehlt), nicht leer zurückgeben sondern
            // mit Raumnummer füllen
            if (string.IsNullOrWhiteSpace(result))
            {
                if (!string.IsNullOrWhiteSpace(g.RaumNummer))
                    result = g.RaumNummer;
                else
                    result = geraet;
            }
            return result;
        }

        /// <summary>
        /// Bestimmt aus den Bauteil-Kategorien einen Gerätetext anhand der
        /// Kategorie-Labels in Settings. Mehrere Kategorien werden mit dem
        /// konfigurierten Trennzeichen verbunden.
        /// </summary>
        private static string BestimmeGeraeteart(List<FamilyInstance> elemente, ElecTriXSettings settings)
        {
            if (elemente == null || elemente.Count == 0)
                return settings.DefaultGeraetLabel ?? "";

            var labels = new List<string>();
            var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var fi in elemente)
            {
                var cat = fi.Category?.Id?.IntegerValue ?? 0;
                if (cat == 0) continue;
                var key = cat.ToString();
                if (settings.KategorieLabels != null
                    && settings.KategorieLabels.TryGetValue(key, out var lbl)
                    && !string.IsNullOrWhiteSpace(lbl)
                    && seen.Add(lbl))
                {
                    labels.Add(lbl);
                }
            }

            if (labels.Count == 0) return settings.DefaultGeraetLabel ?? "";
            var trenner = string.IsNullOrEmpty(settings.GeraeteMixTrenner) ? " & " : settings.GeraeteMixTrenner;
            // Dekodiere HTML-encoded Trenner (JSON-Defaults enthalten "&amp;")
            trenner = trenner.Replace("&amp;", "&");
            return string.Join(trenner, labels);
        }

        private static string Get(Dictionary<string, string> d, string k, string fb)
        {
            if (d != null && k != null && d.TryGetValue(k, out var v)
                && !string.IsNullOrWhiteSpace(v) && v != "??") return v;
            return fb;
        }

        private void ErstelleKreis(Document doc, GruppeInfo g,
            Dictionary<string, FamilyInstance> panelByName,
            CircuitBuilderResult result, bool istSonder, ElecTriXSettings settings)
        {
            if (!panelByName.TryGetValue(g.PanelName, out var panel))
            {
                result.Warnungen.Add($"Panel '{g.PanelName}' nicht im Modell (Raum {g.RaumNummer})");
                return;
            }

            // Jeder Kreis in seiner eigenen SubTransaction → ein Fehler beim SelectPanel
            // rollt NUR diesen Kreis zurück, nicht die gesamte Operation
            using (var subTx = new SubTransaction(doc))
            {
                ElectricalSystem system = null;
                bool panelAssigned = false;
                try
                {
                    subTx.Start();
                    var ids = g.Elemente.Select(e => e.Id).ToList();

                    // 1) System erstellen
                    try
                    {
                        system = ElectricalSystem.Create(doc, ids, ElectricalSystemType.PowerCircuit);
                    }
                    catch (Exception ex)
                    {
                        var lbl = istSonder ? $"Sonder {g.SonderKuerzel}" : $"Raum {g.RaumNummer}";
                        result.Fehler.Add($"{lbl}: Kreis-Erstellung — {ex.Message}");
                        foreach (var fi in g.Elemente) result.FehlerIds.Add(fi.Id);
                        subTx.RollBack();
                        return;
                    }

                    // 2) Panel zuweisen
                    try
                    {
                        system.SelectPanel(panel);
                        panelAssigned = true;
                    }
                    catch (Exception ex)
                    {
                        var lbl = istSonder ? $"Sonder {g.SonderKuerzel}" : $"Raum {g.RaumNummer}";
                        var msg = ex.Message;
                        if (msg.IndexOf("do not match", StringComparison.OrdinalIgnoreCase) >= 0)
                            msg += " — Distribution-System/Voltage/Phasen prüfen.";
                        result.Fehler.Add($"{lbl}: Panel '{g.PanelName}' — {msg}");
                        foreach (var fi in g.Elemente) result.FehlerIds.Add(fi.Id);

                        // Orphaned Circuit verwerfen → komplett rollback (system wird mit-weggeworfen)
                        subTx.RollBack();
                        return;
                    }

                    // 3) Gerät bestimmen + Parameter setzen
                    if (!g.IstSonder && string.IsNullOrEmpty(g.Geraet))
                        g.Geraet = BestimmeGeraeteart(g.Elemente, settings);
                    g.LoadNameHint = BaueLoadName(g, null, settings);   // Raumname-Lookup passiert woanders

                    SetzeSchemeParameter(system, g, result, settings);

                    // 4) Commit dieser SubTransaction → Kreis bleibt persistent
                    var st = subTx.Commit();
                    if (st == TransactionStatus.Committed)
                    {
                        if (istSonder) result.KreiseSonder++;
                        else           result.KreiseNormal++;
                        result.BauteileZugewiesen += g.Elemente.Count;
                    }
                    else
                    {
                        var lbl = istSonder ? $"Sonder {g.SonderKuerzel}" : $"Raum {g.RaumNummer}";
                        result.Fehler.Add($"{lbl}: SubTransaction status {st} — Kreis nicht gespeichert.");
                        foreach (var fi in g.Elemente) result.FehlerIds.Add(fi.Id);
                    }
                }
                catch (Exception ex)
                {
                    try { if (subTx.HasStarted() && !subTx.HasEnded()) subTx.RollBack(); } catch { }
                    var lbl = istSonder ? $"Sonder {g.SonderKuerzel}" : $"Raum {g.RaumNummer}";
                    result.Fehler.Add($"{lbl}: {ex.GetType().Name}: {ex.Message}");
                    foreach (var fi in g.Elemente) result.FehlerIds.Add(fi.Id);
                }
            }
        }

        private bool SetzeSchemeParameter(ElectricalSystem system, GruppeInfo g,
            CircuitBuilderResult result, ElecTriXSettings settings)
        {
            bool any = false;

            // LoadName = Template aus Settings
            try
            {
                Parameter ln = null;
                try { ln = system.get_Parameter((BuiltInParameter)(-1140089)); } catch { }
                if (ln == null)
                {
                    try { ln = system.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_NAME); } catch { }
                }
                if (ln == null) ln = system.LookupParameter("Load Name");

                if (ln != null && !ln.IsReadOnly && !string.IsNullOrEmpty(g.LoadNameHint))
                {
                    ln.Set(g.LoadNameHint);
                    result.LoadNameGesetzt++;
                    any = true;
                }
            }
            catch { }

            // Shared Parameters — welche geschrieben werden, steht in Settings
            //   Settings-Key → Wert aus GruppeInfo
            var werteMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Vorsicherung", g.Vorsicherung },
                { "FIKreis",      g.FIKreis },
                { "Sicherung",    g.Sicherung },
                { "Geraet",       g.Geraet },
                { "Schaltkreis",  g.Schaltkreis },
            };

            if (settings.CircuitSharedParameters != null)
            {
                foreach (var kv in settings.CircuitSharedParameters)
                {
                    if (string.IsNullOrWhiteSpace(kv.Value)) continue;
                    if (!werteMap.TryGetValue(kv.Key, out var wert)) continue;
                    TrySet(system, kv.Value, wert, result, ref any);
                }
            }

            return any;
        }

        private static void TrySet(ElectricalSystem sys, string name, string value,
            CircuitBuilderResult result, ref bool any)
        {
            try
            {
                var p = sys.LookupParameter(name);
                if (p == null)
                {
                    result.Warnungen.Add("Parameter '" + name + "' nicht an Electrical Circuits gebunden.");
                    return;
                }
                if (p.IsReadOnly) return;
                if (string.IsNullOrWhiteSpace(value)) return;  // keine leeren Strings schreiben
                if (p.StorageType == StorageType.String)
                {
                    p.Set(value); result.SchemeParamsGesetzt++; any = true;
                }
                else if (p.StorageType == StorageType.Integer && int.TryParse(value, out var iv))
                {
                    p.Set(iv); result.SchemeParamsGesetzt++; any = true;
                }
            }
            catch (Exception ex)
            {
                result.Warnungen.Add("Parameter '" + name + "' schreibfehler: " + ex.Message);
            }
        }

        // ─── Raumsuche ────────────────────────────────────────────────
        private static string FindeRaumFuerBauteil(Document doc, FamilyInstance fi,
            Phase phase, Dictionary<ElementId, Level> levelCache, ElecTriXSettings settings)
        {
            // 1) fi.Space direkt abfragen
            try { var sp = fi.Space;
                if (sp != null) { var nr = sp.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString();
                    if (!string.IsNullOrWhiteSpace(nr)) return nr; } } catch { }
            // 2) fi.Room direkt abfragen
            try { var r = fi.Room;
                if (r != null) { var nr = r.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString();
                    if (!string.IsNullOrWhiteSpace(nr)) return nr; } } catch { }

            var pos = GetElementPosition(fi);
            if (pos == null) return null;

            // Basis-Z ermitteln
            double baseZ = 0;
            var lvlId = fi.LevelId;
            if (lvlId == null || lvlId == ElementId.InvalidElementId)
                if (fi.Host != null) lvlId = fi.Host.LevelId;
            if ((lvlId == null || lvlId == ElementId.InvalidElementId)
                && fi.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM) is Parameter lp
                && lp.StorageType == StorageType.ElementId)
                lvlId = lp.AsElementId();
            if (lvlId != null && lvlId != ElementId.InvalidElementId
                && levelCache.TryGetValue(lvlId, out var level))
                baseZ = level.Elevation;
            else baseZ = pos.Z;

            // Z-Offsets aus Settings (oder Defaults)
            var offsets = (settings?.ZProbeOffsetsFt != null && settings.ZProbeOffsetsFt.Count > 0)
                ? settings.ZProbeOffsetsFt
                : new List<double>(Z_PROBE_OFFSETS_FT_DEFAULT);

            // 3) Location-Point + Z-Offsets
            foreach (var off in offsets)
            {
                var tp = new XYZ(pos.X, pos.Y, baseZ + off);
                var nr = TestePunkt(doc, tp, phase);
                if (!string.IsNullOrWhiteSpace(nr)) return nr;
            }

            // 4) BoundingBox-Fallback: wand-gehostete Bauteile haben oft den
            // Location-Point direkt auf der Wand — der Space-Test schlägt dann
            // fehl, weil der Punkt exakt auf der Grenze liegt. Wir testen das
            // BB-Zentrum und kleine Verschiebungen.
            if (settings == null || settings.UseBoundingBoxFallback)
            {
                try
                {
                    var bb = fi.get_BoundingBox(null);
                    if (bb != null)
                    {
                        var cx = (bb.Min.X + bb.Max.X) * 0.5;
                        var cy = (bb.Min.Y + bb.Max.Y) * 0.5;
                        foreach (var off in offsets)
                        {
                            var tp = new XYZ(cx, cy, baseZ + off);
                            var nr = TestePunkt(doc, tp, phase);
                            if (!string.IsNullOrWhiteSpace(nr)) return nr;
                        }
                        // Kleine Verschiebungen (0.5 ft ≈ 15 cm) in alle Richtungen
                        double[] dxs = { 0.5, -0.5, 0.0,  0.0 };
                        double[] dys = { 0.0,  0.0, 0.5, -0.5 };
                        foreach (var off in offsets)
                        {
                            for (int i = 0; i < 4; i++)
                            {
                                var tp = new XYZ(cx + dxs[i], cy + dys[i], baseZ + off);
                                var nr = TestePunkt(doc, tp, phase);
                                if (!string.IsNullOrWhiteSpace(nr)) return nr;
                            }
                        }
                    }
                }
                catch { }
            }

            return null;
        }

        /// <summary>Kapselt GetSpaceAtPoint/GetRoomAtPoint-Logik für einen Punkt.</summary>
        private static string TestePunkt(Document doc, XYZ tp, Phase phase)
        {
            try
            {
                var sp = phase != null ? doc.GetSpaceAtPoint(tp, phase) : doc.GetSpaceAtPoint(tp);
                if (sp != null)
                {
                    var nr = sp.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString();
                    if (!string.IsNullOrWhiteSpace(nr)) return nr;
                }
            }
            catch { }
            try
            {
                var r = phase != null ? doc.GetRoomAtPoint(tp, phase) : doc.GetRoomAtPoint(tp);
                if (r != null)
                {
                    var nr = r.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString();
                    if (!string.IsNullOrWhiteSpace(nr)) return nr;
                }
            }
            catch { }
            return null;
        }

        private static Dictionary<string, FamilyInstance> BuildPanelCatalog(Document doc)
        {
            var d = new Dictionary<string, FamilyInstance>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var fi in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                    .WhereElementIsNotElementType().OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>())
                {
                    if (fi.MEPModel == null || !(fi.MEPModel is ElectricalEquipment)) continue;
                    var n = fi.Name ?? "";
                    if (!string.IsNullOrWhiteSpace(n) && !d.ContainsKey(n)) d[n] = fi;
                }
            }
            catch { }
            return d;
        }

        /// <summary>Panel-Name → (CircuitPrefix, Separator) als Defaults für Scheme-Parameter.</summary>
        private static Dictionary<string, (string Prefix, string Sep)> BuildPanelPrefixCache(Document doc)
        {
            var d = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var v in RevitDatenHelper.LeseVerteilerDetails(doc))
                    if (!string.IsNullOrWhiteSpace(v.Name))
                        d[v.Name] = (v.CircuitPrefix ?? "", v.CircuitPrefixSeparator ?? "-");
            }
            catch { }
            return d;
        }

        /// <summary>Raumnummer → Raumname (aus Räumen + MEP-Spaces).</summary>
        private static Dictionary<string, string> BuildRaumNameCache(Document doc)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var r in RevitDatenHelper.LeseAlleRaeume(doc))
                    if (!string.IsNullOrWhiteSpace(r.Nummer) && !d.ContainsKey(r.Nummer))
                        d[r.Nummer] = r.Name ?? "";
            }
            catch { }
            return d;
        }

        private static Dictionary<ElementId, Level> BuildLevelCache(Document doc)
        {
            var d = new Dictionary<ElementId, Level>();
            try { foreach (var l in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()) d[l.Id] = l; } catch { }
            return d;
        }

        private static bool IstBereitsAufPowerCircuit(FamilyInstance fi, out ElectricalSystem existing)
        {
            existing = null;
            try
            {
                if (fi.MEPModel == null) return false;
                var ess = fi.MEPModel.GetElectricalSystems();
                if (ess == null) return false;
                foreach (ElectricalSystem es in ess)
                    if (es.SystemType == ElectricalSystemType.PowerCircuit) { existing = es; return true; }
            }
            catch { }
            return false;
        }

        private static string LiesSonderkuerzel(FamilyInstance fi)
        {
            try
            {
                var p = fi.LookupParameter(PARAM_SONDERSTECKDOSE);
                var v = p?.AsString()?.Trim().ToUpperInvariant();
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
            catch { }
            return null;
        }

        private static XYZ GetElementPosition(Element el)
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

        private static FamilyInstance ErstesElementAusKreis(ElectricalSystem sys)
        {
            try { foreach (Element el in sys.Elements) if (el is FamilyInstance fi) return fi; } catch { }
            return null;
        }

        private class GruppeInfo
        {
            public string PanelName     { get; set; }
            public string RaumNummer    { get; set; }
            public string Sicherung     { get; set; }
            public string Vorsicherung  { get; set; }
            public string FIKreis       { get; set; }
            public string Geraet        { get; set; }
            public string Schaltkreis   { get; set; }
            public bool   IstSonder     { get; set; }
            public string SonderKuerzel { get; set; }
            public string LoadNameHint  { get; set; }
            public List<FamilyInstance> Elemente { get; } = new List<FamilyInstance>();
        }
    }
}
