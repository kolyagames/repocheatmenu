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

// "Loot Magnet": pulls every valuable in the level toward the nearest active extraction point,
// instead of destroying anything. Pure repositioning via PhysGrabObject.Teleport() - SetPositionRPC
// to the master, applied with zero validation, same loophole PowerGrab/ItemSteal/the Troll
// destroyers already use - so it works for guest or host. Nothing gets broken or removed: once a
// valuable's RoomVolumeCheck reports it's sitting inside the extraction room, the game's own
// ValuableObject.Update() credits it to the haul exactly like carrying it there by hand would
// (confirmed in the decompiled source, same mechanic EconomyHack.SpawnValuable already rides) -
// height/exact position within the room doesn't matter for that credit, only being inside it.
//
// Two separate breakage causes, both fixed:
//  1) Every valuable used to target the exact same point, piling several on top of each other -
//     the resulting shove was hard enough for PhysGrabObjectImpactDetector's own impact-force
//     check to register a break. Each valuable now gets its own stable target point
//     (deterministic from its own instance ID, so it doesn't jitter between ticks) spread around
//     the extraction area instead of stacking on one spot.
//  2) Far more damaging in practice: a straight-line path clipping through walls/furniture along
//     the way triggered the same break mechanic repeatedly. Fixed via SafeFlightHelper - see that
//     file's comment for the full explanation.
internal static class LootMagnetHack
{
    private const float TickInterval = 0.1f; // 10/sec - smooth pull without flooding the RPC channel
    private const float PullSpeed = 8f; // units/sec - gentle approach, not a slam
    private const float ArriveDistance = 0.3f;
    private const float HorizontalArriveDistance = 0.4f;
    private const float CruiseClearance = 2.5f;
    private const float OverlapCheckRadius = 0.15f;
    private const float SpreadRadiusMin = 0.5f;
    private const float SpreadRadiusMax = 1.8f;
    private const float SpreadHeightMax = 1f;

    private static float _lastSent;

    public static void Apply()
    {
        if (!CheatState.LootMagnet.Value) return;
        if (Time.unscaledTime - _lastSent < TickInterval) return;
        _lastSent = Time.unscaledTime;

        ValuableDirector director = ValuableDirector.instance;
        if (director == null) return;

        List<ValuableObject> valuables = GameReflect.Get<List<ValuableObject>>(director, "valuableList");
        if (valuables == null) return;

        float step = PullSpeed * TickInterval;
        int solidMask = LayerMask.GetMask("Default");

        // A guest's Teleport is one SetPositionRPC to the host per object per tick. With dozens of valuables
        // that ran into the hundreds of messages a second - past Photon's per-room message limit, which gets
        // players disconnected. The host teleports locally, no messages, so no cap there.
        bool guest = GameManager.Multiplayer() && !Photon.Pun.PhotonNetwork.IsMasterClient;
        int budget = guest ? MaxMovesPerTickGuest : int.MaxValue;
        int count = valuables.Count;
        if (count == 0) return;
        _cursor %= count;

        for (int n = 0; n < count && budget > 0; n++)
        {
            ValuableObject valuable = valuables[(_cursor + n) % count];
            if (valuable == null) continue;

            PhysGrabObject physObject = GameReflect.Get<PhysGrabObject>(valuable, "physGrabObject");
            if (physObject == null) continue;

            Vector3 current = physObject.transform.position;
            ExtractionPoint extraction = EconomyHack.FindActiveExtractionPoint(current);
            if (extraction == null || extraction.extractionArea == null) continue;

            Vector3 target = SpreadTarget(extraction.extractionArea.transform.position, physObject.GetInstanceID());
            if (Vector3.Distance(current, target) <= ArriveDistance) continue;

            bool safe = SafeFlightHelper.TryStep(current, target, step, CruiseClearance,
                HorizontalArriveDistance, OverlapCheckRadius, solidMask, out Vector3 next);
            if (!safe) continue;

            GameReflect.TeleportSafe(physObject, next, physObject.transform.rotation, "LootMagnetHack");
            budget--;
            // Next tick continues after the last one moved, so every valuable gets its turn.
            if (budget == 0) _cursor = (_cursor + n + 1) % count;
        }
    }

    private const int MaxMovesPerTickGuest = 6;
    private static int _cursor;

    // Deterministic per-object offset (same instance ID always maps to the same spot, no jitter)
    // spread around the extraction area's center instead of every valuable converging on the
    // exact same point.
    private static Vector3 SpreadTarget(Vector3 basePosition, int instanceId)
    {
        System.Random rng = new System.Random(instanceId);
        float angle = (float)(rng.NextDouble() * (Mathf.PI * 2));
        float radius = SpreadRadiusMin + (float)rng.NextDouble() * (SpreadRadiusMax - SpreadRadiusMin);
        float height = 0.5f + (float)rng.NextDouble() * SpreadHeightMax;
        Vector3 offset = new Vector3(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius);
        return basePosition + offset;
    }
}
