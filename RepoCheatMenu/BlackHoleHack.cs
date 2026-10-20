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

// "Чёрная дыра" (BlackHole) - every valuable and every door on the level orbits a single anchor
// high above the map. Pure show-off / finisher: the entire level's loot and doors visibly stack
// up over the center of play, like a cartoon tornado picking the room clean.
//
// Mechanic: same loophole as every other raid feature here - PhysGrabObject.Teleport routes a
// SetPositionRPC on the object's own view (confirmed in the decompile: the method body is
// `if (SemiFunc.IsMultiplayer() && !SemiFunc.IsMasterClient()) photonView.RPC("SetPositionRPC",
// RpcTarget.MasterClient, position, rotation); else SetPositionRPC(position, rotation);`). The
// scene view is not ours, so the ghost's own OpRaiseEvent filter also lets this through for a
// ghost. Works for guest or host.
//
// Throttling: Photon has a per-room RPC rate cap. LootMagnet already paid this bill once (see
// its ToPickCount round-robin). BlackHole scans every tick but only teleports a rotating window
// of ~12 valuables and ~6 hinges each tick, so the orbit "spins up" over about half a second on
// a crowded level instead of pushing hundreds of RPCs in one frame.
//
// Anchor: average position of all valuables on the level at the moment the toggle flips on,
// plus a configurable height. Cached per-session so the anchor does not drift as objects start
// orbiting (if it recomputed every tick from live positions, it would wander upward with them).
internal static class BlackHoleHack
{
    private static Vector3 _anchor;
    private static bool _anchorKnown;
    private static int _valuableRotor;
    private static int _hingeRotor;

    private const int ValuablesPerTick = 12;
    private const int HingesPerTick = 6;
    private const float HeightOverMap = 55f;
    private const float OrbitRadiusValuables = 3.5f;
    private const float OrbitRadiusDoors = 7f;
    private const float OrbitSpeed = 1.4f; // radians/sec of the first object; staggered per-index

    // FindObjectsByType is comparatively expensive; valuables and hinges don't come and go every
    // tick, so refresh those lists on a 1s cadence instead of every ~0.15s hack tick. Previous
    // behaviour ran two scene-wide FindObjectsByType calls every tick, which showed up in the FPS
    // profile on heavier levels.
    private static ValuableObject[] _vals;
    private static PhysGrabHinge[] _hinges;
    private static float _rescanAt;
    private const float RescanInterval = 1f;

    public static void Apply()
    {
        if (!CheatState.BlackHole.Value)
        {
            _anchorKnown = false;
            _vals = null;
            _hinges = null;
            return;
        }

        if (!_anchorKnown)
        {
            if (!TryComputeAnchor(out _anchor))
            {
                // Nothing to vacuum up on this scene yet - defer silently.
                return;
            }
            _anchorKnown = true;
            Plugin.Log.LogInfo("[BlackHole] anchor at " + _anchor);
        }

        float t = Time.unscaledTime;
        if (_vals == null || t >= _rescanAt)
        {
            _vals = Object.FindObjectsByType<ValuableObject>(FindObjectsSortMode.None);
            _hinges = Object.FindObjectsByType<PhysGrabHinge>(FindObjectsSortMode.None);
            _rescanAt = t + RescanInterval;
            // Re-anchor when the stored anchor is clearly off-level (scene change): if we have
            // live valuables and the first one is 100 m+ from the stored anchor horizontally,
            // the anchor is from the previous level and would send this level's loot flying off
            // into empty space.
            if (_anchorKnown && _vals.Length > 0 && _vals[0] != null)
            {
                Vector3 flat = _vals[0].transform.position - _anchor; flat.y = 0f;
                if (flat.sqrMagnitude > 100f * 100f)
                {
                    _anchorKnown = false;
                    Plugin.Log.LogInfo("[BlackHole] stale anchor dropped (scene change) - recomputing.");
                }
            }
        }

        // Valuables orbit at a tight radius, lower part of the stack.
        var vals = _vals;
        if (vals.Length > 0)
        {
            int start = _valuableRotor % vals.Length;
            int span = Mathf.Min(ValuablesPerTick, vals.Length);
            for (int step = 0; step < span; step++)
            {
                int i = (start + step) % vals.Length;
                ValuableObject v = vals[i];
                if (v == null) continue;
                PhysGrabObject obj = v.GetComponent<PhysGrabObject>();
                if (obj == null) continue;

                float ang = t * OrbitSpeed + i * 0.37f;
                float r = OrbitRadiusValuables + ((i % 5) * 0.4f);
                float h = (i % 7) * 0.5f;
                Vector3 pos = _anchor + new Vector3(Mathf.Cos(ang) * r, h, Mathf.Sin(ang) * r);
                GameReflect.TeleportSafe(obj, pos, obj.transform.rotation, "BlackHole.val");
            }
            _valuableRotor = (start + span) % vals.Length;
        }

        // Doors / cabinet lids / crate hinges - wider ring, slightly above the valuables; same
        // reflection shim as TrapRoom's door wall uses to turn a hinge into its PhysGrabObject.
        var hinges = _hinges;
        if (hinges.Length > 0)
        {
            int start = _hingeRotor % hinges.Length;
            int span = Mathf.Min(HingesPerTick, hinges.Length);
            for (int step = 0; step < span; step++)
            {
                int i = (start + step) % hinges.Length;
                PhysGrabHinge h = hinges[i];
                if (h == null) continue;
                PhysGrabObject obj = GameReflect.Get<PhysGrabObject>(h, "physGrabObject");
                if (obj == null) continue;

                float ang = t * (OrbitSpeed * 0.75f) + i * 0.51f + Mathf.PI;
                float r = OrbitRadiusDoors + ((i % 4) * 0.6f);
                float lift = 2f + (i % 6) * 0.6f;
                Vector3 pos = _anchor + new Vector3(Mathf.Cos(ang) * r, lift, Mathf.Sin(ang) * r);
                // Face inward toward the column so the doors look like a cylinder, not a scatter.
                Quaternion rot = Quaternion.LookRotation((pos - _anchor).normalized, Vector3.up);
                GameReflect.TeleportSafe(obj, pos, rot, "BlackHole.door");
            }
            _hingeRotor = (start + span) % hinges.Length;
        }
    }

    private static bool TryComputeAnchor(out Vector3 anchor)
    {
        anchor = Vector3.zero;
        List<Vector3> samples = new();

        // Prefer the center-of-mass of the loot itself - that puts the column over wherever the
        // level actually generated, no matter where the truck is parked.
        foreach (ValuableObject v in Object.FindObjectsByType<ValuableObject>(FindObjectsSortMode.None))
        {
            if (v != null) samples.Add(v.transform.position);
        }
        // Fallback: average player position. Rare case where a level has zero loot (lobby/shop).
        if (samples.Count == 0 && GameDirector.instance != null)
        {
            var list = GameReflect.Get<List<PlayerAvatar>>(GameDirector.instance, "PlayerList");
            if (list != null)
                foreach (PlayerAvatar p in list)
                    if (p != null) samples.Add(p.transform.position);
        }
        if (samples.Count == 0) return false;

        Vector3 sum = Vector3.zero;
        foreach (Vector3 s in samples) sum += s;
        anchor = sum / samples.Count;
        anchor.y += HeightOverMap;
        return true;
    }

    public static string Status()
    {
        if (!CheatState.BlackHole.Value) return "  -> Выключено";
        if (!_anchorKnown) return "  -> Ждёт появления ценностей на уровне...";
        return "  -> Центр: (" + _anchor.x.ToString("0") + ", " + _anchor.y.ToString("0") + ", " + _anchor.z.ToString("0") + ")  — тянет ценности и двери";
    }
}
