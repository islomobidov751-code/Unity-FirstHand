// GameEventReporter.cs — report game moments WITHOUT writing code, by wiring existing UnityEvents in the Inspector.
// ----------------------------------------------------------------------------------------------------------------
// Example (First Hand, Meta Interaction SDK): on a grabbable glove part, add an "Interactable Unity Event Wrapper"
// (or use the one already there), then in its "When Select" list press +, drag in the object that has this
// component, and pick GameEventReporter → Grab (string), typing the part name. "When Unselect" → ReleaseRight.
// Example (North Star): wherever a script already raises a UnityEvent (rope attached, harpoon fired…), point it at
// GameEventReporter → Report (string) with the event name, e.g. "rope_tied".
// All names must match the event list in games/profiles.js.
using UnityEngine;

public class GameEventReporter : MonoBehaviour
{
    [Tooltip("Speaker name used by ReportDialogue(text)")]
    public string speaker = "Captain";
    [Tooltip("Stat increased by Count()")]
    public string statKey = "";

    // ----- generic -----
    public void Report(string eventName) => TelemetryBridge.Report(eventName);
    public void ReportChapter(string chapter) => TelemetryBridge.ReportChapter(chapter);
    public void ReportDialogue(string text) => TelemetryBridge.ReportDialogue(speaker, text);
    public void EndSession() => TelemetryBridge.ReportSessionEnd();
    public void Count() { if (!string.IsNullOrEmpty(statKey)) TelemetryBridge.AddStat(statKey); }
    public void CountStat(string key) => TelemetryBridge.AddStat(key);

    // ----- hands -----
    public void Grab(string objectName) { TelemetryBridge.SetGrab(false, true); TelemetryBridge.AddStat("grabs"); TelemetryBridge.Report("grab", "object", objectName); }
    public void GrabLeft(string objectName) { TelemetryBridge.SetGrab(true, true); TelemetryBridge.AddStat("grabs"); TelemetryBridge.Report("grab", "object", objectName); }
    public void ReleaseRight() => TelemetryBridge.SetGrab(false, false);
    public void ReleaseLeft() => TelemetryBridge.SetGrab(true, false);
    public void Poke(string buttonName) { TelemetryBridge.AddStat("pokes"); TelemetryBridge.Report("poke", "name", buttonName); }

    // ----- First Hand helpers -----
    int _pieces;
    [Tooltip("Total glove pieces, for \"Glove piece attached, 2 of 4\"")]
    public int gloveTotal = 4;
    public void GlovePieceAttached(string part)
    {
        _pieces++;
        TelemetryBridge.SetStat("pieces", _pieces);
        TelemetryBridge.Report("glove_piece", "n", _pieces, "total", gloveTotal, "part", part);
        if (_pieces >= gloveTotal) TelemetryBridge.Report("gloves_complete");
    }
    public void PalmBlast() { TelemetryBridge.AddStat("blasts"); TelemetryBridge.Report("palm_blast"); }

    // ----- North Star helpers -----
    public void RopeTied() { TelemetryBridge.AddStat("ropes"); TelemetryBridge.Report("rope_tied"); }
    public void HarpoonFired(bool hit) { TelemetryBridge.AddStat("harpoons"); TelemetryBridge.Report("harpoon_fired", "hit", hit); }
    public void CargoRetrieved() { TelemetryBridge.AddStat("cargo"); TelemetryBridge.Report("cargo_retrieved"); }
    public void Weather(string kind) => TelemetryBridge.Report("weather", "kind", kind);   // "storm" or "calm"
}
