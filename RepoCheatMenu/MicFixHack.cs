// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace RepoCheatMenu;

// Microphone diagnostics + "pick a physical mic" fix.
//
// REPO picks its microphone by NAME (PlayerVoiceChat.Update): it only opens the device when that
// name is in Unity's Microphone.devices list, otherwise microphoneEnabled = false and the recorder
// gets an empty device - the mic is silent while the mute indicator still looks unmuted. The name is
// stored in two places that the game reads back on start: DataDirector.micDevice (saved to
// SettingsData.es3 as "micDevice") and SessionManager.micDeviceCurrent (set from the menu slider).
// Choosing a device in the menu only updates these when the menu slider is applied, so a stale
// virtual device (SteelSeries Sonar / VB-Audio / NVIDIA Broadcast) can stay selected.
//
// PickPhysical() writes both fields to a real hardware microphone. PlayerVoiceChat then sees the new
// name on its next Update and re-opens the device itself (its own currentDeviceName check).
internal static class MicFixHack
{
    private static bool _logged;

    // Virtual / loopback devices that look like microphones but often carry no real signal into Unity.
    private static readonly string[] VirtualMarks =
    {
        "Sonar", "VB-Audio", "CABLE", "NVIDIA Broadcast", "Steam Streaming", "Virtual"
    };

    public static void Apply()
    {
        if (_logged) return;
        if (PlayerAvatar.instance == null) return; // wait until we're in a level
        _logged = true;
        Log("on enter");
    }

    public static void Log(string why)
    {
        string[] devices = Microphone.devices;
        Plugin.Log.LogInfo("[Mic] " + why + ": Unity sees " + devices.Length + " device(s): [" + string.Join(" | ", devices) + "]");

        DataDirector dd = DataDirector.instance;
        string saved = dd != null ? GameReflect.Get<string>(dd, "micDevice") : "n/a";
        SessionManager sm = SessionManager.instance;
        string current = sm != null ? GameReflect.Get<string>(sm, "micDeviceCurrent") : "n/a";
        Plugin.Log.LogInfo("[Mic] game saved device='" + saved + "', current='" + current + "'");

        PlayerVoiceChat pvc = PlayerVoiceChat.instance;
        Photon.Voice.Unity.Recorder rec = pvc != null ? GameReflect.Get<Photon.Voice.Unity.Recorder>(pvc, "recorder") : null;
        if (rec != null)
            Plugin.Log.LogInfo("[Mic] recorder.MicrophoneDevice='" + rec.MicrophoneDevice.ToString() + "'");
    }

    // One-line status for the menu: the device name the game will open on its next start.
    public static string StatusLine()
    {
        DataDirector dd = DataDirector.instance;
        string saved = dd != null ? GameReflect.Get<string>(dd, "micDevice") : null;
        if (string.IsNullOrEmpty(saved)) return "  -> Игра: микрофон ещё не выбран (откройте уровень)";
        string tag = IsVirtual(saved) ? " (ВИРТУАЛЬНЫЙ, может не передавать звук)" : "";
        return "  -> Игра использует: " + saved + tag;
    }

    // First real hardware mic: prefer one that looks like a headset/mic of ours (Arctis), otherwise
    // the first non-virtual entry. Writes both game fields so the game re-opens it immediately.
    public static string PickPhysical()
    {
        string[] devices = Microphone.devices;
        string pick = null;
        foreach (string d in devices)
            if (d.Contains("Arctis")) { pick = d; break; }
        if (pick == null)
            foreach (string d in devices)
                if (!IsVirtual(d)) { pick = d; break; }

        if (pick == null)
        {
            Plugin.Log.LogWarning("[Mic] no physical microphone in Unity's device list.");
            return null;
        }

        DataDirector dd = DataDirector.instance;
        if (dd != null) GameReflect.Set(dd, "micDevice", pick);
        SessionManager sm = SessionManager.instance;
        if (sm != null) GameReflect.Set(sm, "micDeviceCurrent", pick);

        Plugin.Log.LogInfo("[Mic] picked physical device: '" + pick + "'");
        Log("after pick");
        return pick;
    }

    private static bool IsVirtual(string name)
    {
        foreach (string mark in VirtualMarks)
            if (name.Contains(mark)) return true;
        return false;
    }

    public static void PickButton()
    {
        string pick = PickPhysical();
        if (pick == null)
            DynamicIsland.Notify("Микрофон", "Нет физического микрофона в списке Unity", false);
        else
            DynamicIsland.Notify("Микрофон", "Выбран: " + pick, true);
    }
}
