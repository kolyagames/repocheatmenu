// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System;
using UnityEngine;

namespace RepoCheatMenu;

// GPS destination, the same idea as Kimstar's Navigator (.gps): one place that knows "where am I
// being sent", read by anything that wants to point there - the Dynamic Island shows distance and
// a direction arrow. Purely local: it only reads the scene, nothing is sent over the network.
//
// Modes (CheatState.GpsMode):
//   Evacuation - the nearest ACTIVE extraction point (same finder the radar and loot magnet use);
//   Truck      - the truck / base spawn point (TruckSafetySpawnPoint);
//   Mark       - a point you saved with "set mark here";
//   Off        - no target.
internal static class Gps
{
    public static readonly string[] Modes = { "Evacuation", "Truck", "Mark", "Off" };

    public static Vector3? Mark { get; private set; }

    public static bool Active { get; private set; }
    public static Vector3 Target { get; private set; }
    public static string Label { get; private set; } = "";
    public static bool IsEvacuation { get; private set; }

    // Vector from you to the target, refreshed by Update().
    public static Vector3 Delta { get; private set; }

    public static float Distance => Delta.magnitude;

    // Recomputed at most once per frame, however many readers ask.
    private static int _frame = -1;

    public static void Update()
    {
        if (_frame == Time.frameCount) return;
        _frame = Time.frameCount;

        Active = false;
        IsEvacuation = false;

        PlayerAvatar local = PlayerAvatar.instance;
        if (local == null) return;
        Vector3 from = local.transform.position;

        switch (CheatState.GpsMode.Value)
        {
            case "Evacuation":
            {
                ExtractionPoint point = null;
                try { point = EconomyHack.FindActiveExtractionPoint(from); }
                catch (Exception) { /* director not ready yet */ }
                if (point == null) return;
                Set(point.transform.position, "Эвакуация", from);
                IsEvacuation = true;
                break;
            }
            case "Truck":
                if (TruckSafetySpawnPoint.instance == null) return;
                Set(TruckSafetySpawnPoint.instance.transform.position, "Грузовик", from);
                break;
            case "Mark":
                if (Mark == null) return;
                Set(Mark.Value, "Метка", from);
                break;
        }
    }

    private static void Set(Vector3 target, string label, Vector3 from)
    {
        Target = target;
        Label = label;
        Delta = target - from;
        Active = true;
    }

    // Degrees relative to where the camera looks: 0 = straight ahead, 90 = right, +-180 = behind.
    public static float Bearing()
    {
        Camera cam = Camera.main;
        if (cam == null) return 0f;
        Vector3 f = cam.transform.forward; f.y = 0f;
        Vector3 t = Delta; t.y = 0f;
        if (f.sqrMagnitude < 1e-4f || t.sqrMagnitude < 1e-4f) return 0f;
        return Vector3.SignedAngle(f, t, Vector3.up);
    }

    public static void SetMarkHere()
    {
        PlayerAvatar local = PlayerAvatar.instance;
        if (local == null) return;
        Mark = local.transform.position;
        CheatState.GpsMode.Value = "Mark";
        DynamicIsland.Notify("GPS", "Метка поставлена здесь", true);
    }

    public static void NextMode()
    {
        int i = Array.IndexOf(Modes, CheatState.GpsMode.Value);
        string next = Modes[(i + 1) % Modes.Length];
        CheatState.GpsMode.Value = next;
        DynamicIsland.Notify("GPS", "Цель: " + ModeTitle(next), next != "Off");
    }

    public static string ModeTitle(string mode) => mode switch
    {
        "Evacuation" => "эвакуация",
        "Truck" => "грузовик",
        "Mark" => "своя метка",
        _ => "выключен",
    };
}
