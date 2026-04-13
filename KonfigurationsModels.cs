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

    public class ProjektKonfiguration
    {
        public int Version { get; set; } = 1;
        public string ErkannteSchema { get; set; }
        public Dictionary<string, string> RaumStromkreise { get; set; } = new();
        public Dictionary<string, string> SonderStromkreise { get; set; } = new();
        public Dictionary<string, string> WohnungVerteiler { get; set; } = new();
    }
}
