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

// Money lost to breakage.
//   DamageAlert - an island notification "-$X" when the valuable YOU are holding loses value (it hit
//                 something). Hits that land within half a second are added up into one notification.
//   The run journal also gets the level's total: every valuable on the map is compared with its value a
//   moment ago, and every drop is added to LostThisLevel (RunJournal reads and resets it).
// Values come from ValuableObject.dollarValueCurrent, which the game keeps in sync on every client.
// An extracted or destroyed valuable simply leaves the list, so it does not count as a loss. Read-only.
internal static class ValuableDamageHack
{
    private static readonly Dictionary<int, float> Last = new();
    private static readonly HashSet<int> Seen = new();
    private static readonly List<int> Gone = new();
    private static float _nextScan;

    private static ValuableObject _held;
    private static float _heldValue;
    private static float _pendingLoss;
    private static float _pendingSince;
    private static string _pendingName = "";

    public static float LostThisLevel;

    public static void ResetLevel()
    {
        LostThisLevel = 0f;
        Last.Clear();
    }

    // ApplyStats tick.
    public static void Apply()
    {
        TrackHeld();
        FlushNotification();

        if (Time.unscaledTime < _nextScan) return;
        _nextScan = Time.unscaledTime + 0.5f;
        ScanAll();
    }

    private static void TrackHeld()
    {
        PhysGrabber grabber = PhysGrabber.instance;
        PhysGrabObject obj = grabber != null && grabber.grabbed ? GameReflect.Get<PhysGrabObject>(grabber, "grabbedPhysGrabObject") : null;
        ValuableObject valuable = obj != null ? obj.GetComponent<ValuableObject>() : null;

        if (valuable != _held)
        {
            _held = valuable;
            _heldValue = valuable != null ? GameReflect.Get<float>(valuable, "dollarValueCurrent") : 0f;
            return;
        }
        if (_held == null) return;

        float now = GameReflect.Get<float>(_held, "dollarValueCurrent");
        if (now < _heldValue - 0.5f && CheatState.DamageAlert.Value)
        {
            if (_pendingLoss <= 0f) _pendingSince = Time.unscaledTime;
            _pendingLoss += _heldValue - now;
            _pendingName = CleanName(_held.name);
        }
        _heldValue = now;
    }

    private static void FlushNotification()
    {
        if (_pendingLoss <= 0f || Time.unscaledTime - _pendingSince < 0.5f) return;
        DynamicIsland.Notify(_pendingName, $"Повреждение: -${Mathf.RoundToInt(_pendingLoss)}", false);
        _pendingLoss = 0f;
    }

    private static void ScanAll()
    {
        ValuableDirector director = ValuableDirector.instance;
        List<ValuableObject> list = director != null ? GameReflect.Get<List<ValuableObject>>(director, "valuableList") : null;
        if (list == null) { Last.Clear(); return; }

        Seen.Clear();
        foreach (ValuableObject v in list)
        {
            if (v == null) continue;
            int id = v.GetInstanceID();
            Seen.Add(id);
            float value = GameReflect.Get<float>(v, "dollarValueCurrent");
            if (Last.TryGetValue(id, out float before) && value < before - 0.5f)
                LostThisLevel += before - value;
            Last[id] = value;
        }

        Gone.Clear();
        foreach (int id in Last.Keys)
            if (!Seen.Contains(id)) Gone.Add(id);
        foreach (int id in Gone) Last.Remove(id);
    }

    private static string CleanName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "Ценность";
        string n = raw.Replace("(Clone)", "").Replace("Valuable ", "").Replace('_', ' ').Trim();
        return n.Length == 0 ? "Ценность" : n;
    }
}
