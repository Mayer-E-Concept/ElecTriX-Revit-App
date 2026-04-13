using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using Autodesk.Revit.DB;

namespace METools.FamilyPlacer
{
    public class RaumZeile : INotifyPropertyChanged
    {
        private string _stromkreis;
        public string KanonischerName { get; set; }
        public string SynonymAnzeige  { get; set; }
        public System.Collections.Generic.List<string> SynonymListe { get; set; } = new System.Collections.Generic.List<string>();
        public bool   IstSonder       { get; set; }
        public string Kuerzel         { get; set; }
        public string GeraetName      { get; set; }

        public string Stromkreis
        {
            get { return _stromkreis; }
            set { _stromkreis = value; OnPropertyChanged(); OnPropertyChanged("IstOffen"); OnPropertyChanged("StatusFarbe"); }
        }

        public bool   IstOffen    { get { return string.IsNullOrWhiteSpace(Stromkreis) || Stromkreis == "??"; } }
        public string StatusFarbe { get { return IstOffen ? "#EF9F27" : (IstSonder ? "#378ADD" : "#1D9E75"); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class VerteilerZeile : INotifyPropertyChanged
    {
        private string _verteilerName;
        public string WohnungsId         { get; set; }
        public string VerteilerId        { get; set; }
        public string RaeumeAnzeige      { get; set; }
        public bool   AutomatischErkannt { get; set; }

        public string VerteilerName
        {
            get { return _verteilerName; }
            set { _verteilerName = value; OnPropertyChanged(); OnPropertyChanged("StatusFarbe"); }
        }

        public string StatusFarbe   { get { return AutomatischErkannt ? "#378ADD" : "#EF9F27"; } }
        public string StatusTooltip { get { return AutomatischErkannt ? "Automatisch erkannt" : "Bitte manuell auswählen"; } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class KonfigViewModel : INotifyPropertyChanged
    {
        private readonly Document _doc;
        private string _schemaInfo         = "Schema wird erkannt...";
        private string _schemaBeschreibung = "";
        private int    _offeneZuordnungen;

        public ObservableCollection<RaumZeile>      RaumZeilen           { get; } = new ObservableCollection<RaumZeile>();
        public ObservableCollection<RaumZeile>      SonderZeilen         { get; } = new ObservableCollection<RaumZeile>();
        public ObservableCollection<VerteilerZeile> VerteilerZeilen      { get; } = new ObservableCollection<VerteilerZeile>();
        public ObservableCollection<string>         VerfuegbareVerteiler { get; } = new ObservableCollection<string>();

        public string SchemaInfo
        {
            get { return _schemaInfo; }
            set { _schemaInfo = value; OnPropertyChanged(); }
        }

        public string SchemaBeschreibung
        {
            get { return _schemaBeschreibung; }
            set { _schemaBeschreibung = value; OnPropertyChanged(); }
        }

        public int OffeneZuordnungen
        {
            get { return _offeneZuordnungen; }
            set { _offeneZuordnungen = value; OnPropertyChanged(); }
        }

        public int ZugeordnetGesamt
        {
            get
            {
                int n = 0;
                foreach (var z in RaumZeilen)   if (!z.IstOffen) n++;
                foreach (var z in SonderZeilen) if (!z.IstOffen) n++;
                return n;
            }
        }

        public string ProjektName
        {
            get
            {
                try { return _doc.ProjectInformation.Name; }
                catch { return "Mayer E-Concept SRL"; }
            }
        }

        public bool GespeichertErfolgreich { get; private set; }

        public ICommand SpeichernCommand     { get; }
        public ICommand AbbrechenCommand     { get; }
        public ICommand SchemaAendernCommand { get; }

        public KonfigViewModel(Document doc)
        {
            _doc = doc;
            SpeichernCommand     = new RelayCommand(o => Speichern());
            AbbrechenCommand     = new RelayCommand(o => { GespeichertErfolgreich = false; });
            SchemaAendernCommand = new RelayCommand(o => { });
            LadeDaten();
        }

        private void LadeDaten()
        {
            var gespeichert = LadeGespeicherteKonfig();

            // Räume
            var raumtypen = RevitDatenHelper.LeseEinzigartigeRaumtypen(_doc);
            foreach (var raumtyp in raumtypen)
            {
                string sk = gespeichert.RaumStromkreise.ContainsKey(raumtyp)
                    ? gespeichert.RaumStromkreise[raumtyp] : "??";

                RaumZeilen.Add(new RaumZeile
                {
                    KanonischerName = raumtyp,
                    SynonymAnzeige  = string.Join(", ", RaumHelper.GetSynonyme(raumtyp)),
                    SynonymListe    = RaumHelper.GetSynonyme(raumtyp),
                    IstSonder       = false,
                    Stromkreis      = sk,
                });
            }

            // Sonderkürzel
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
                });
            }

            // Schema + Verteiler
            var raumnummern    = RevitDatenHelper.LeseAlleRaumnummern(_doc);
            var schema         = SchemaHelper.ErkenneSchema(raumnummern);
            SchemaInfo         = "Schema erkannt: " + schema.Beschreibung;
            SchemaBeschreibung = "Beispiel: " + schema.Beispiel;

            var verteilerListe = RevitDatenHelper.ErmittleVerteilerzuordnung(_doc, schema);
            foreach (var v in verteilerListe)
                VerfuegbareVerteiler.Add(v.VerteilerName);

            foreach (var v in verteilerListe)
            {
                string vn = gespeichert.WohnungVerteiler.ContainsKey(v.WohnungsId)
                    ? gespeichert.WohnungVerteiler[v.WohnungsId] : v.VerteilerName;

                VerteilerZeilen.Add(new VerteilerZeile
                {
                    WohnungsId         = v.WohnungsId,
                    VerteilerId        = v.VerteilerId,
                    VerteilerName      = vn,
                    RaeumeAnzeige      = v.Raeume.Count > 0 ? string.Join(", ", v.Raeume.Take(4)) : "-",
                    AutomatischErkannt = v.AutomatischErkannt,
                });
            }

            AktualisiereStatistik();
        }

        private ProjektKonfiguration LadeGespeicherteKonfig()
        {
            try
            {
                var param = _doc.ProjectInformation.LookupParameter("SK_Konfiguration");
                var json  = param?.AsString();
                if (!string.IsNullOrWhiteSpace(json))
                    return JsonSerializer.Deserialize<ProjektKonfiguration>(json) ?? new ProjektKonfiguration();
            }
            catch { }
            return new ProjektKonfiguration();
        }

        private void Speichern()
        {
            var konfig = new ProjektKonfiguration { Version = 1, ErkannteSchema = SchemaInfo };

            foreach (var z in RaumZeilen)
                konfig.RaumStromkreise[z.KanonischerName] = z.Stromkreis ?? "??";
            foreach (var z in SonderZeilen)
                konfig.SonderStromkreise[z.Kuerzel] = z.Stromkreis ?? "??";
            foreach (var v in VerteilerZeilen)
                konfig.WohnungVerteiler[v.WohnungsId] = v.VerteilerName ?? "";

            try
            {
                var json  = JsonSerializer.Serialize(konfig);
                using (var tx = new Transaction(_doc, "SK-Konfiguration speichern"))
                {
                    tx.Start();
                    var param = _doc.ProjectInformation.LookupParameter("SK_Konfiguration");
                    if (param != null && !param.IsReadOnly)
                        param.Set(json);
                    tx.Commit();
                }
                GespeichertErfolgreich = true;
            }
            catch { GespeichertErfolgreich = false; }

            AktualisiereStatistik();
        }

        public void AktualisiereStatistik()
        {
            int offen = 0;
            foreach (var z in RaumZeilen)   if (z.IstOffen) offen++;
            foreach (var z in SonderZeilen) if (z.IstOffen) offen++;
            foreach (var v in VerteilerZeilen)
                if (string.IsNullOrWhiteSpace(v.VerteilerName)) offen++;
            OffeneZuordnungen = offen;
            OnPropertyChanged("ZugeordnetGesamt");
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            _execute    = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object p) { return _canExecute == null || _canExecute(p); }
        public void Execute(object p)    { _execute(p); }

        public event EventHandler CanExecuteChanged
        {
            add    { System.Windows.Input.CommandManager.RequerySuggested += value; }
            remove { System.Windows.Input.CommandManager.RequerySuggested -= value; }
        }
    }
}
