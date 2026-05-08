using System.Collections.Generic;

namespace METools.FamilyPlacer
{
    public class RaumZuordnung
    {
        public string KanonischerName { get; set; }
        public List<string> Synonyme { get; set; } = new();
        public string Stromkreis { get; set; } = "??";
        public bool IstSonderanschluss { get; set; } = false;
        public string Kuerzel { get; set; }
        public string GeraetBezeichnung { get; set; }
    }

    public class VerteilerzuordnungEintrag
    {
        public string WohnungsId { get; set; }
        public string VerteilerId { get; set; }
        public string VerteilerName { get; set; }
        public List<string> Raeume { get; set; } = new();
        public bool AutomatischErkannt { get; set; } = false;
    }

    /// <summary>
    /// Gesamte ElecTriX-Konfiguration eines Projekts.
    /// Version 4: Globales Naming-Scheme + Match-Prefix pro Verteiler.
    /// </summary>
    public class ProjektKonfiguration
    {
        public int Version { get; set; } = 4;
        public string ErkannteSchema { get; set; }

        public Dictionary<string, string> RaumStromkreise   { get; set; } = new();
        public Dictionary<string, string> SonderStromkreise { get; set; } = new();
        public Dictionary<string, string> WohnungVerteiler  { get; set; } = new();

        // Pro-Raum Zuordnungen
        public Dictionary<string, string> RaumVerteiler     { get; set; } = new();
        public Dictionary<string, string> RaumStromkreisPro { get; set; } = new();
        public Dictionary<string, string> RaumVorsicherung  { get; set; } = new();
        public Dictionary<string, string> RaumFIKreis       { get; set; } = new();
        public Dictionary<string, string> RaumGeraet        { get; set; } = new();
        public Dictionary<string, string> RaumSchaltkreis   { get; set; } = new();

        // NEU v4: Globales Naming Scheme
        public string GewaehltesNamingScheme { get; set; } = "";

        // NEU v4: Pro Verteiler (Name → Match-Prefix für Raum-Nummern)
        public Dictionary<string, string> VerteilerMatchPrefix { get; set; } = new();
    }
}
