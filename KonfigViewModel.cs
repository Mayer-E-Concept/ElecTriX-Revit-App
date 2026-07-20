using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Autodesk.Revit.DB;

namespace METools.FamilyPlacer
{
    public class RaumZeile : INotifyPropertyChanged
    {
        private string _sicherung, _verteiler, _vorsicherung, _fiKreis, _geraet;

        public string RaumNummer       { get; set; }
        public string RaumName         { get; set; }
        public string KanonischerName  { get; set; }
        public string SynonymAnzeige   { get; set; }
        public List<string> SynonymListe { get; set; } = new List<string>();
        public bool   IstSonder        { get; set; }
        public string Kuerzel          { get; set; }
        public string GeraetName       { get; set; }

        public string Stromkreis
        {
            get { return _sicherung; }
            set { _sicherung = value; OnChanged(); OnChanged("IstOffen"); OnChanged("StatusFarbe"); }
        }
        public string Verteiler
        {
            get { return _verteiler; }
            set { _verteiler = value; OnChanged(); OnChanged("IstOffen"); OnChanged("StatusFarbe"); }
        }
        public string Vorsicherung { get { return _vorsicherung; } set { _vorsicherung = value; OnChanged(); } }
        public string FIKreis      { get { return _fiKreis; }      set { _fiKreis = value; OnChanged(); } }
        public string Geraet       { get { return _geraet; }       set { _geraet = value; OnChanged(); } }

        public bool IstOffen
        {
            get
            {
                bool skFehlt = string.IsNullOrWhiteSpace(Stromkreis) || Stromkreis == "??";
                if (IstSonder) return skFehlt;
                bool vtFehlt = string.IsNullOrWhiteSpace(Verteiler) || Verteiler == "??";
                return skFehlt || vtFehlt;
            }
        }
        public string StatusFarbe { get { return IstOffen ? "#EF9F27" : (IstSonder ? "#378ADD" : "#1D9E75"); } }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged([CallerMemberName] string n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>Eine Zeile pro Stromkreis, angezeigt unter seinem Verteiler.</summary>
    public class StromkreisZeile
    {
        public string CircuitNumber      { get; set; } = "";
        public string LoadName           { get; set; } = "";
        public string LoadClassification { get; set; } = "";
        public string RaumAnzeige        { get; set; } = "";
        public int    AnzahlBauteile     { get; set; }
    }

    /// <summary>
    /// Erweiterte Verteiler-Zeile mit Circuit-Prefix + Match-Prefix (editierbar)
    /// + Liste der aktuell zugeordneten Stromkreise.
    /// </summary>
    public class VerteilerZeile : INotifyPropertyChanged
    {
        private string _matchPrefix;

        public string WohnungsId            { get; set; }
        public string VerteilerId           { get; set; }
        public string VerteilerName         { get; set; }
        public string RaeumeAnzeige         { get; set; }
        public bool   AutomatischErkannt    { get; set; }

        // Aus Modell gelesen (read-only)
        public string CircuitPrefix         { get; set; }
        public string CircuitPrefixSeparator{ get; set; }
        public string NamingSchemeName      { get; set; }

        /// <summary>Editierbarer Prefix: mit diesem werden Raum-Nummern gematcht.</summary>
        public string MatchPrefix
        {
            get { return _matchPrefix; }
            set { _matchPrefix = value; OnChanged(); }
        }

        /// <summary>Alle Stromkreise die aktuell diesem Verteiler zugeordnet sind.</summary>
        public List<StromkreisZeile> Stromkreise { get; set; } = new List<StromkreisZeile>();

        public int    AnzahlStromkreise  { get { return Stromkreise.Count; } }
        public string AnzeigeName        { get { return string.IsNullOrWhiteSpace(VerteilerName) ? "(ohne Name)" : VerteilerName; } }
        public string PrefixAnzeige      { get { return string.IsNullOrWhiteSpace(CircuitPrefix)  ? "(kein Prefix)" : CircuitPrefix; } }
        public string StatusTooltip { get { return AutomatischErkannt ? "Automatically detected" : "Manually assigned"; } }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged([CallerMemberName] string n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class KonfigViewModel : INotifyPropertyChanged
    {
        private readonly Document _doc;
        private readonly ElecTriXSettings _settings;
        private string _schemaInfo         = "Detecting schema...";
        private string _schemaBeschreibung = "";
        private int    _offeneZuordnungen;
        private string _fehlerMeldung      = "";
        private string _speicherInfo       = "";
        private string _gewaehltesNamingScheme = "";

        public ElecTriXSettings Settings { get { return _settings; } }

        public ObservableCollection<RaumZeile>      RaumZeilen           { get; } = new ObservableCollection<RaumZeile>();
        public ObservableCollection<RaumZeile>      SonderZeilen         { get; } = new ObservableCollection<RaumZeile>();
        public ObservableCollection<VerteilerZeile> VerteilerZeilen      { get; } = new ObservableCollection<VerteilerZeile>();
        public ObservableCollection<string>         VerfuegbareVerteiler { get; } = new ObservableCollection<string>();
        public ObservableCollection<string>         VerfuegbareNamingSchemes { get; } = new ObservableCollection<string>();
        public ObservableCollection<string>         ToteVerteilerNamen   { get; } = new ObservableCollection<string>();

        public string SchemaInfo         { get { return _schemaInfo; }         set { _schemaInfo = value; OnChanged(); } }
        public string SchemaBeschreibung { get { return _schemaBeschreibung; } set { _schemaBeschreibung = value; OnChanged(); } }
        public int    OffeneZuordnungen  { get { return _offeneZuordnungen; }  set { _offeneZuordnungen = value; OnChanged(); } }

        /// <summary>Global gewählter Circuit-Naming-Scheme-Name (Projekt-weit).</summary>
        public string GewaehltesNamingScheme
        {
            get { return _gewaehltesNamingScheme; }
            set { _gewaehltesNamingScheme = value; OnChanged(); }
        }

        public int ZugeordnetGesamt
        {
            get { int n=0; foreach (var z in RaumZeilen) if (!z.IstOffen) n++; foreach (var z in SonderZeilen) if (!z.IstOffen) n++; return n; }
        }

        public string ProjektName { get { try { return _doc.ProjectInformation.Name; } catch { return "Mayer E-Concept SRL"; } } }
        public bool   GespeichertErfolgreich { get; private set; }
        public string FehlerMeldung { get { return _fehlerMeldung; } }
        public string SpeicherInfo  { get { return _speicherInfo; } }

        public ICommand SpeichernCommand { get; }
        public ICommand AbbrechenCommand { get; }
        public ICommand AutoMatchCommand { get; }

        public KonfigViewModel(Document doc)
        {
            _doc = doc;
            _settings = ElecTriXSettingsStorage.Laden();
            SpeichernCommand = new RelayCommand(o => Speichern());
            AbbrechenCommand = new RelayCommand(o => { GespeichertErfolgreich = false; });
            AutoMatchCommand = new RelayCommand(o => AutoMatchRaeumeZuVerteilern());
            LadeDaten();
        }

        private void LadeDaten()
        {
            var gespeichert = LadeGespeicherteKonfig();
            var raeumeRoh    = RevitDatenHelper.LeseAlleRaeume(_doc); // fetched once, reused below (and passed into ErstelleDiagnose)
            var diag = RevitDatenHelper.ErstelleDiagnose(_doc, raeumeRoh);
            var raumnummern  = RevitDatenHelper.LeseAlleRaumnummern(raeumeRoh);
            var schema       = SchemaHelper.ErkenneSchema(raumnummern);
            var verteilerDet = RevitDatenHelper.LeseVerteilerDetails(_doc);
            var zuordnung    = RevitDatenHelper.ErmittleVerteilerzuordnung(_doc, schema);
            var namingSchemes = RevitDatenHelper.LeseVerfuegbareNamingSchemes(_doc);
            var alleKreise   = RevitDatenHelper.LeseAlleStromkreise(_doc);

            // Stale-Detection (NICHT löschen! Nur zählen — User entscheidet selbst):
            // Wenn ein Verteiler umbenannt wurde ('Verteiler(UV Geräte)' → 'UV Wohnung 2'),
            // würde stumm-löschen den User-Workflow komplett zerstören.
            var echteNamen = new HashSet<string>(
                verteilerDet.Select(v => v.Name ?? ""), StringComparer.OrdinalIgnoreCase);
            var toteNamen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int stale = 0;
            if (gespeichert.RaumVerteiler != null)
            {
                foreach (var kv in gespeichert.RaumVerteiler)
                {
                    if (!string.IsNullOrWhiteSpace(kv.Value) && !echteNamen.Contains(kv.Value))
                    {
                        stale++;
                        toteNamen.Add(kv.Value);
                    }
                }
            }
            if (stale > 0)
            {
                var toteListe = string.Join(", ", toteNamen.Take(3));
                _speicherInfo = $"⚠ {stale} rooms point to {toteNamen.Count} dead panel(s) ({toteListe}). " +
                                "Re-route them in the Panels tab to existing panels.";
            }

            // Toter-Verteiler-Liste fürs UI verfügbar machen
            ToteVerteilerNamen.Clear();
            foreach (var n in toteNamen.OrderBy(x => x))
                ToteVerteilerNamen.Add(n);

            // Naming-Scheme-Liste
            foreach (var ns in namingSchemes)
                if (!string.IsNullOrWhiteSpace(ns.Name))
                    VerfuegbareNamingSchemes.Add(ns.Name);

            if (!string.IsNullOrWhiteSpace(gespeichert.GewaehltesNamingScheme)
                && VerfuegbareNamingSchemes.Contains(gespeichert.GewaehltesNamingScheme))
                GewaehltesNamingScheme = gespeichert.GewaehltesNamingScheme;
            else if (VerfuegbareNamingSchemes.Contains("MEC-Stromkreise"))
                GewaehltesNamingScheme = "MEC-Stromkreise";
            else if (VerfuegbareNamingSchemes.Count > 0)
                GewaehltesNamingScheme = VerfuegbareNamingSchemes[0];

            // Dropdown-Liste Verteiler (alle echten)
            foreach (var name in verteilerDet
                .Select(v => v.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                VerfuegbareVerteiler.Add(name);

            // Kreise nach Panel-Name gruppieren
            var kreiseNachPanel = alleKreise
                .GroupBy(k => k.PanelName ?? "", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            // VerteilerZeilen mit vollständiger Info + zugeordneten Kreisen
            foreach (var vd in verteilerDet.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase))
            {
                var zo = zuordnung.FirstOrDefault(z => z.VerteilerName == vd.Name);
                string match = gespeichert.VerteilerMatchPrefix != null
                            && gespeichert.VerteilerMatchPrefix.TryGetValue(vd.Name, out var mp)
                            && !string.IsNullOrWhiteSpace(mp)
                    ? mp : AbleiteMatchPrefix(vd.CircuitPrefix, vd.Name);

                var zeile = new VerteilerZeile
                {
                    WohnungsId             = zo?.WohnungsId ?? "",
                    VerteilerId            = vd.ElementId.IntegerValue.ToString(),
                    VerteilerName          = vd.Name,
                    CircuitPrefix          = vd.CircuitPrefix ?? "",
                    CircuitPrefixSeparator = vd.CircuitPrefixSeparator ?? "-",
                    NamingSchemeName       = vd.NamingSchemeName ?? "",
                    MatchPrefix            = match,
                    RaeumeAnzeige          = zo?.Raeume != null && zo.Raeume.Count > 0
                                              ? string.Join(", ", zo.Raeume.Take(4)) : "-",
                    AutomatischErkannt     = zo?.AutomatischErkannt ?? false,
                };

                if (kreiseNachPanel.TryGetValue(vd.Name ?? "", out var kreise))
                {
                    foreach (var k in kreise)
                    {
                        zeile.Stromkreise.Add(new StromkreisZeile
                        {
                            CircuitNumber      = string.IsNullOrWhiteSpace(k.CircuitNumber)
                                                  ? "—" : k.CircuitNumber,
                            LoadName           = k.LoadName,
                            LoadClassification = k.LoadClassification,
                            RaumAnzeige        = k.Raumnummern.Count > 0
                                                  ? string.Join(", ", k.Raumnummern.Take(3))
                                                  : "—",
                            AnzahlBauteile     = k.AnzahlBauteile,
                        });
                    }
                }

                VerteilerZeilen.Add(zeile);
            }

            // Räume laden
            var raeume = raeumeRoh
                .Where(r => !string.IsNullOrWhiteSpace(r.Name))
                .OrderBy(r => SortKey(r.Nummer), StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Name ?? "", StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var r in raeume)
            {
                string typ = RaumHelper.Normalisiere(r.Name);
                string wid = SchemaHelper.ExtrahiereWohnungsId(r.Nummer ?? "", schema);

                string sk = "??";
                if (!string.IsNullOrWhiteSpace(r.Nummer) && gespeichert.RaumStromkreisPro.ContainsKey(r.Nummer))
                    sk = gespeichert.RaumStromkreisPro[r.Nummer];
                else if (!string.IsNullOrWhiteSpace(typ) && gespeichert.RaumStromkreise.ContainsKey(typ))
                    sk = gespeichert.RaumStromkreise[typ];

                string vt = "";
                if (!string.IsNullOrWhiteSpace(r.Nummer) && gespeichert.RaumVerteiler.ContainsKey(r.Nummer))
                    vt = gespeichert.RaumVerteiler[r.Nummer];

                // Defaults für Scheme-Parameter aus ElecTriX-Settings-Templates ableiten
                string defaultVS = "", defaultFI = "";
                if (!string.IsNullOrEmpty(vt))
                {
                    var vDet = verteilerDet.FirstOrDefault(v => v.Name == vt);
                    var werte = new Dictionary<string, string>
                    {
                        { "prefix",   vDet?.CircuitPrefix ?? "" },
                        { "sep",      vDet?.CircuitPrefixSeparator ?? "-" },
                        { "matchpfx", AbleiteMatchPrefix(vDet?.CircuitPrefix, vDet?.Name, _settings) },
                        { "raumnr",   r.Nummer ?? "" },
                        { "raumname", r.Name ?? "" },
                    };
                    defaultVS = TemplateEngine.Fuelle(_settings.VorsicherungTemplate, werte);
                    defaultFI = TemplateEngine.Fuelle(_settings.FIKreisTemplate,      werte);
                }

                string vs = gespeichert.RaumVorsicherung.ContainsKey(r.Nummer ?? "")
                    ? gespeichert.RaumVorsicherung[r.Nummer] : defaultVS;
                string fi = gespeichert.RaumFIKreis.ContainsKey(r.Nummer ?? "")
                    ? gespeichert.RaumFIKreis[r.Nummer] : defaultFI;
                string ge = gespeichert.RaumGeraet.ContainsKey(r.Nummer ?? "")
                    ? gespeichert.RaumGeraet[r.Nummer] : (_settings.DefaultGeraetLabel ?? "");

                RaumZeilen.Add(new RaumZeile
                {
                    RaumNummer      = r.Nummer ?? "",
                    RaumName        = r.Name ?? "",
                    KanonischerName = typ,
                    SynonymAnzeige  = string.Join(", ", RaumHelper.GetSynonyme(typ)),
                    SynonymListe    = RaumHelper.GetSynonyme(typ),
                    IstSonder       = false,
                    Stromkreis      = sk,
                    Verteiler       = vt,
                    Vorsicherung    = vs,
                    FIKreis         = fi,
                    Geraet          = ge,
                });
            }

            // Sonderkreise
            var kuerzel = RevitDatenHelper.LeseSonderkuerzel(_doc);
            foreach (var k in kuerzel.OrderBy(x => x))
            {
                string bez = RaumHelper.SonderKuerzelBezeichnung.ContainsKey(k)
                    ? RaumHelper.SonderKuerzelBezeichnung[k] : k;
                string ssk = gespeichert.SonderStromkreise.ContainsKey(k)
                    ? gespeichert.SonderStromkreise[k] : "??";

                SonderZeilen.Add(new RaumZeile
                {
                    KanonischerName = k,
                    Kuerzel         = k,
                    GeraetName      = bez,
                    IstSonder       = true,
                    Stromkreis      = ssk,
                    Geraet          = k,
                });
            }

            // Info-Text
            if (diag.GesamtPlatziert == 0)
            {
                SchemaInfo         = "No rooms / MEP spaces found";
                SchemaBeschreibung = diag.DetailText;
            }
            else
            {
                SchemaInfo         = diag.KurzText + "  |  Schema: " + schema.Beschreibung;
                SchemaBeschreibung = "Example: " + schema.Beispiel;
            }

            AktualisiereStatistik();
        }

        /// <summary>
        /// Leitet aus CircuitPrefix und Verteilername einen Match-Prefix ab.
        /// Nutzt die konfigurierbaren Patterns aus ElecTriXSettings.
        /// Leere Rückgabe wenn nichts passt — der User kann dann manuell editieren.
        /// </summary>
        public static string AbleiteMatchPrefix(string circuitPrefix, string verteilerName,
            ElecTriXSettings settings = null)
        {
            settings = settings ?? ElecTriXSettingsStorage.Laden();
            string quelle = (circuitPrefix ?? "") + " " + (verteilerName ?? "");

            // User-definierte Patterns in Reihenfolge durchprobieren
            if (settings.MatchPrefixPatterns != null)
            {
                foreach (var pat in settings.MatchPrefixPatterns)
                {
                    if (string.IsNullOrWhiteSpace(pat.Regex)) continue;
                    try
                    {
                        var m = Regex.Match(quelle, pat.Regex, RegexOptions.IgnoreCase);
                        if (m.Success)
                            return m.Result(pat.Ersetzung ?? "");
                    }
                    catch { /* invalide Regex ignorieren */ }
                }
            }

            // Fallback: CircuitPrefix übernehmen wenn kurz genug
            if (!string.IsNullOrWhiteSpace(circuitPrefix)
                && circuitPrefix.Length <= (settings.FallbackMaxPrefixLaenge > 0
                                            ? settings.FallbackMaxPrefixLaenge : 10))
                return circuitPrefix.Trim();

            return "";
        }

        /// <summary>
        /// Ordnet alle Räume, die aktuell auf <paramref name="alterName"/> verweisen,
        /// dem <paramref name="neuerName"/> zu. Nützlich wenn ein Verteiler umbenannt
        /// wurde und die Config die alte Bezeichnung noch enthält.
        /// </summary>
        public int MigriereVerteiler(string alterName, string neuerName)
        {
            if (string.IsNullOrWhiteSpace(alterName) || string.IsNullOrWhiteSpace(neuerName))
                return 0;

            int count = 0;
            var neueDetailsMap = new Dictionary<string, (string Prefix, string Sep)>(StringComparer.OrdinalIgnoreCase);
            // Hole neue Panel-Details direkt aus dem VerteilerZeilen-Cache
            foreach (var v in VerteilerZeilen)
                neueDetailsMap[v.VerteilerName ?? ""] = (v.CircuitPrefix ?? "", v.CircuitPrefixSeparator ?? "-");

            string neuPfx = "", neuSep = "-";
            if (neueDetailsMap.TryGetValue(neuerName, out var pp))
            {
                neuPfx = pp.Prefix;
                neuSep = string.IsNullOrEmpty(pp.Sep) ? "-" : pp.Sep;
            }

            foreach (var rz in RaumZeilen)
            {
                if (string.Equals(rz.Verteiler, alterName, StringComparison.OrdinalIgnoreCase))
                {
                    rz.Verteiler = neuerName;

                    // Defaults nachziehen wenn bisher leer ODER aus altem Prefix abgeleitet
                    if (!string.IsNullOrEmpty(neuPfx))
                    {
                        var werte = new Dictionary<string, string>
                        {
                            { "prefix",   neuPfx },
                            { "sep",      neuSep },
                            { "matchpfx", AbleiteMatchPrefix(neuPfx, neuerName, _settings) },
                            { "raumnr",   rz.RaumNummer ?? "" },
                            { "raumname", rz.RaumName ?? "" },
                        };
                        var neuVS = TemplateEngine.Fuelle(_settings.VorsicherungTemplate, werte);
                        var neuFI = TemplateEngine.Fuelle(_settings.FIKreisTemplate,      werte);

                        if (string.IsNullOrWhiteSpace(rz.Vorsicherung)
                            || (!string.IsNullOrEmpty(alterName) && rz.Vorsicherung != null
                                && rz.Vorsicherung.StartsWith(alterName, StringComparison.OrdinalIgnoreCase)))
                        {
                            rz.Vorsicherung = neuVS;
                        }
                        if (string.IsNullOrWhiteSpace(rz.FIKreis)
                            || (!string.IsNullOrEmpty(alterName) && rz.FIKreis != null
                                && rz.FIKreis.StartsWith(alterName, StringComparison.OrdinalIgnoreCase)))
                        {
                            rz.FIKreis = neuFI;
                        }
                    }
                    count++;
                }
            }

            // Aus Tote-Liste entfernen damit UI aktualisiert
            ToteVerteilerNamen.Remove(alterName);
            AktualisiereStatistik();
            return count;
        }

        /// <summary>
        /// Durchläuft alle Räume und ordnet sie dem Verteiler zu, dessen MatchPrefix
        /// am Anfang der Raumnummer steht. Pro Raum wird nur gesetzt, wenn noch
        /// kein Verteiler manuell zugewiesen ist.
        /// </summary>
        public int AutoMatchRaeumeZuVerteilern()
        {
            int count = 0;
            foreach (var rz in RaumZeilen)
            {
                if (!string.IsNullOrEmpty(rz.Verteiler)) continue;
                var nr = (rz.RaumNummer ?? "").Trim();
                if (string.IsNullOrWhiteSpace(nr)) continue;

                // Längster passender Match-Prefix gewinnt
                var best = VerteilerZeilen
                    .Where(v => !string.IsNullOrWhiteSpace(v.MatchPrefix)
                             && nr.StartsWith(v.MatchPrefix, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(v => v.MatchPrefix.Length)
                    .FirstOrDefault();

                if (best != null)
                {
                    rz.Verteiler = best.VerteilerName;
                    // Defaults für Scheme-Parameter via Template nachziehen
                    var werte = new Dictionary<string, string>
                    {
                        { "prefix",   best.CircuitPrefix ?? "" },
                        { "sep",      best.CircuitPrefixSeparator ?? "-" },
                        { "matchpfx", best.MatchPrefix ?? "" },
                        { "raumnr",   rz.RaumNummer ?? "" },
                        { "raumname", rz.RaumName ?? "" },
                    };
                    if (string.IsNullOrEmpty(rz.Vorsicherung))
                        rz.Vorsicherung = TemplateEngine.Fuelle(_settings.VorsicherungTemplate, werte);
                    if (string.IsNullOrEmpty(rz.FIKreis))
                        rz.FIKreis = TemplateEngine.Fuelle(_settings.FIKreisTemplate, werte);
                    count++;
                }
            }
            AktualisiereStatistik();
            return count;
        }

        private static string SortKey(string nummer)
        {
            if (string.IsNullOrWhiteSpace(nummer)) return "zzzz";
            var s = nummer.Trim().ToUpperInvariant();
            if (Regex.IsMatch(s, @"^\d+$")) return "0_" + s.PadLeft(6, '0');
            var m = Regex.Match(s, @"^W(\d+)[-_\s]?([UEODK])?(\d*)");
            if (m.Success)
            {
                string wnr    = m.Groups[1].Value.PadLeft(3, '0');
                string et     = m.Groups[2].Success ? m.Groups[2].Value : "Z";
                string etRank = EtagenRank(et);
                string raumnr = m.Groups[3].Success ? m.Groups[3].Value.PadLeft(4, '0') : "0000";
                return "1_W" + wnr + "_" + etRank + et + "_" + raumnr;
            }
            return "9_" + s.PadLeft(12, '0');
        }

        private static string EtagenRank(string et)
        {
            switch (et) { case "U": return "0"; case "E": return "1"; case "O": return "2"; case "D": return "3"; case "K": return "0"; default: return "9"; }
        }

        private ProjektKonfiguration LadeGespeicherteKonfig()
        {
            try
            {
                var json = KonfigStorage.Laden(_doc);
                if (!string.IsNullOrWhiteSpace(json))
                    return JsonSerializer.Deserialize<ProjektKonfiguration>(json) ?? new ProjektKonfiguration();
            }
            catch { }
            return new ProjektKonfiguration();
        }

        private void Speichern()
        {
            _fehlerMeldung = "";
            var konfig = new ProjektKonfiguration { Version = 4, ErkannteSchema = SchemaInfo };
            konfig.GewaehltesNamingScheme = GewaehltesNamingScheme ?? "";

            foreach (var z in RaumZeilen)
            {
                if (string.IsNullOrWhiteSpace(z.KanonischerName)) continue;
                if (string.IsNullOrWhiteSpace(z.Stromkreis) || z.Stromkreis == "??") continue;
                if (!konfig.RaumStromkreise.ContainsKey(z.KanonischerName))
                    konfig.RaumStromkreise[z.KanonischerName] = z.Stromkreis;
            }

            foreach (var z in RaumZeilen)
            {
                if (string.IsNullOrWhiteSpace(z.RaumNummer)) continue;
                konfig.RaumStromkreisPro[z.RaumNummer] = z.Stromkreis   ?? "??";
                konfig.RaumVerteiler   [z.RaumNummer] = z.Verteiler    ?? "";
                konfig.RaumVorsicherung[z.RaumNummer] = z.Vorsicherung ?? "";
                konfig.RaumFIKreis     [z.RaumNummer] = z.FIKreis      ?? "";
                konfig.RaumGeraet      [z.RaumNummer] = z.Geraet       ?? "";
            }

            foreach (var z in SonderZeilen)
                konfig.SonderStromkreise[z.Kuerzel] = z.Stromkreis ?? "??";

            foreach (var v in VerteilerZeilen)
                konfig.VerteilerMatchPrefix[v.VerteilerName] = v.MatchPrefix ?? "";

            string json;
            try { json = JsonSerializer.Serialize(konfig); }
            catch (Exception ex)
            {
                _fehlerMeldung = "JSON: " + ex.Message;
                GespeichertErfolgreich = false;
                return;
            }

            bool ok = KonfigStorage.Speichern(_doc, json);
            GespeichertErfolgreich = ok;
            _speicherInfo = KonfigStorage.LastInfo ?? "";
            if (!ok) _fehlerMeldung = KonfigStorage.LastError;
            AktualisiereStatistik();
        }

        public void AktualisiereStatistik()
        {
            int offen = 0;
            foreach (var z in RaumZeilen)   if (z.IstOffen) offen++;
            foreach (var z in SonderZeilen) if (z.IstOffen) offen++;
            OffeneZuordnungen = offen;
            OnChanged("ZugeordnetGesamt");
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged([CallerMemberName] string n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;
        public RelayCommand(Action<object> e, Func<object, bool> c = null) { _execute = e; _canExecute = c; }
        public bool CanExecute(object p) { return _canExecute == null || _canExecute(p); }
        public void Execute(object p)    { _execute(p); }
        public event EventHandler CanExecuteChanged
        {
            add    { System.Windows.Input.CommandManager.RequerySuggested += value; }
            remove { System.Windows.Input.CommandManager.RequerySuggested -= value; }
        }
    }
}
