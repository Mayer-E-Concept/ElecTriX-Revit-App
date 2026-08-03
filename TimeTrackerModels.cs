// TimeTrackerModels.cs -- ME-Tools | Time Tracker
// Mayer E-Concept SRL
using System;

namespace METools.TimeTracker
{
    // One finished session: a project was open, from StartUtc to EndUtc, for
    // one user. Measurement is deliberately simple wall time (document open
    // -> document close), not an idle-aware "active time" heuristic -- see
    // TimeTrackerWatcher for the one piece of nuance this still needs: a
    // session that never got a clean DocumentClosing (Revit or Windows
    // crashed) is recovered from its last heartbeat instead of being lost
    // outright, and is marked Recovered so that's transparent rather than
    // silently presented as a normal clean session.
    public class TimeSessionEntry
    {
        public DateTime StartUtc { get; set; }
        public DateTime EndUtc   { get; set; }
        public string   User     { get; set; } = "";
        public bool     Recovered { get; set; } // finalized from a crash-recovery heartbeat, not a clean close

        public double DurationSeconds => Math.Max(0, (EndUtc - StartUtc).TotalSeconds);

        // Local-time convenience for display; storage always keeps UTC.
        public DateTime StartLocal => StartUtc.ToLocalTime();
        public DateTime EndLocal   => EndUtc.ToLocalTime();
    }

    // Wire format for the shared JSON-Lines file -- see ActivityLogFileLine
    // for why JSON Lines rather than one JSON array (write-heavy, read-rarely,
    // one bad line shouldn't invalidate the whole log).
    internal class TimeTrackerFileLine
    {
        public string StartUtc  { get; set; }
        public string EndUtc    { get; set; }
        public string User      { get; set; }
        public bool   Recovered { get; set; }
    }

    // A small per-machine (%APPDATA%) marker for a session that's still in
    // progress, written when the document opens and refreshed periodically
    // (see TimeTrackerWatcher's Idling-driven heartbeat). If Revit or Windows
    // crashes before a clean DocumentClosing, this marker is what lets the
    // *next* Revit startup recover that lost time instead of silently
    // discarding it -- finalized using LastHeartbeatUtc as the best available
    // end time. Deliberately local, not shared: it only ever describes a
    // session on this one machine, and is deleted the moment that session
    // ends cleanly.
    internal class ActiveSessionMarker
    {
        public string ProjectId        { get; set; } = "";
        public string User             { get; set; } = "";
        public string StartUtc         { get; set; } = "";
        public string LastHeartbeatUtc { get; set; } = "";
    }
}
