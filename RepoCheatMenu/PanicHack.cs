using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;

namespace RepoCheatMenu;

// Kill switch for playing a real match without anything active or even reachable - press the
// corner button and every hack turns off as if the plugin weren't loaded, until "]" brings it
// all back exactly as it was.
//
// The only thing that reliably switches every hack off at once is CheatState itself: every
// Harmony patch (AntiKnockbackPatch, GentleDropPatch, ImpactDetectorBreakPatch, ...) and every
// RunSafe tick hack (EspOverlay, TrollHack, VisionHack, ...) reads a CheatState ConfigEntry<bool>
// with no separate enable path of its own - so forcing every one of those to false is equivalent
// to "the mod does nothing", without having to touch 40 hack files individually. Reflects over
// CheatState's public static fields once (cached) rather than hand-listing them, so newly added
// toggles are covered automatically instead of silently leaking through a kill switch that forgot
// about them.
//
// Two things a plain bool-zero doesn't cover, added explicitly:
//  - Fov: VisionHack.cs only ever widens it (deliberately, to avoid fighting the game's own FOV
//    animations), never narrows - zeroing bools alone would leave a raised FOV stuck raised.
//  - One more real ApplyStats() pass right after zeroing: most hacks only undo themselves the
//    next time they run (ESP hides its pooled labels, VisionHack resets ThirdPerson's camera
//    offset, CoreStats pushes DebugEnergy/godMode/etc. back to false on the game's own fields) -
//    skip that and they'd stay stuck at whatever they were mid-effect instead of cleanly off.
internal static class PanicHack
{
    // Backed by config now, not a plain field: setting every toggle to false writes those falses
    // straight to disk (that's just what a ConfigEntry setter does), so if the restore list only
    // lived in memory, quitting while panicked threw the entire toggle layout away permanently -
    // ']' in the next session had nothing to restore and no idea it was even hidden. Both halves
    // of the state have to survive a restart together for the kill switch to be reversible.
    public static bool Hidden
    {
        get => CheatState.PanicHidden.Value;
        private set => CheatState.PanicHidden.Value = value;
    }

    private static readonly Dictionary<string, ConfigEntry<bool>> _toggles = new();
    private static int _fovBackup;

    // The kill switch is meant to stop everything the mod DOES to the game - not to reset
    // preferences or take out the tooling used to recover afterwards:
    //  - PanicHidden is the flag saying a sweep is in effect; zeroing it erases that fact.
    //  - DebugBridgeEnabled/EventLogging are developer plumbing. Neither renders anything nor
    //    touches gameplay, and killing the bridge specifically meant that after a panic + quit
    //    there was no way left to restore anything programmatically (found exactly that way).
    //  - HudAutoStack / HudMediaPlayerUseSpotify are sub-settings of the HUD, which is already
    //    fully hidden by HudEnabled going false. Clearing them only loses the chosen layout.
    private static readonly HashSet<string> NeverSwept = new()
    {
        nameof(CheatState.PanicHidden),
        nameof(CheatState.DebugBridgeEnabled),
        nameof(CheatState.EventLogging),
        nameof(CheatState.HudAutoStack),
        nameof(CheatState.HudMediaPlayerUseSpotify),
    };

    private static void CollectToggles()
    {
        if (_toggles.Count > 0) return;
        foreach (FieldInfo field in typeof(CheatState).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (NeverSwept.Contains(field.Name)) continue;
            if (field.GetValue(null) is ConfigEntry<bool> entry)
                _toggles[field.Name] = entry;
        }
    }

    public static void Deactivate()
    {
        if (Hidden) return;
        CollectToggles();

        var snapshot = new List<string>();
        foreach (KeyValuePair<string, ConfigEntry<bool>> kv in _toggles)
        {
            if (kv.Value.Value) snapshot.Add(kv.Key);
            kv.Value.Value = false;
        }
        CheatState.PanicSnapshot.Value = string.Join(";", snapshot);

        _fovBackup = CheatState.Fov.Value;
        CheatState.Fov.Value = 60;

        // Push the now-vanilla values into the live game objects before we stop ticking
        // altogether, instead of leaving everything frozen mid-effect.
        TimeTickPatch.RunApplyStatsOnce();

        Hidden = true;
        OverlayUI.Close();
        Plugin.Log.LogInfo($"PanicHack: deactivated - {snapshot.Count} active toggles forced off and remembered, press ']' to restore.");
        EventLog.Write("session", $"PANIC off - {snapshot.Count} toggles disabled and saved: {string.Join(",", snapshot)}");
        EventLog.Flush(); // panic is usually followed by quitting, so don't sit in the buffer
    }

    public static void Reactivate()
    {
        if (!Hidden) return;
        CollectToggles();

        int restored = 0;
        foreach (string name in CheatState.PanicSnapshot.Value.Split(';'))
        {
            if (name.Length == 0) continue;
            if (!_toggles.TryGetValue(name, out ConfigEntry<bool> entry)) continue;
            entry.Value = true;
            restored++;
        }
        CheatState.PanicSnapshot.Value = "";

        if (_fovBackup > 0) CheatState.Fov.Value = _fovBackup;

        Hidden = false;
        Plugin.Log.LogInfo($"PanicHack: reactivated - restored {restored} toggles.");
        EventLog.Write("session", $"PANIC restored - {restored} toggles re-enabled.");
    }
}
