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

// Carts aren't tracked in any director list (unlike valuables/cosmetics) - confirmed in the
// decompiled source, so this keeps its own periodically-refreshed scan (FindObjectsByType is
// comparatively expensive, no need for every-tick freshness here) shared by both Cart ESP
// (EspOverlay.cs) and the teleport button below.
internal static class CartHack
{
    private static float _nextRescan;
    private static readonly List<PhysGrabCart> _carts = new();

    public static List<PhysGrabCart> GetCarts()
    {
        if (Time.unscaledTime >= _nextRescan)
        {
            _nextRescan = Time.unscaledTime + 1f;
            _carts.Clear();
            _carts.AddRange(Object.FindObjectsByType<PhysGrabCart>(FindObjectsSortMode.None));
        }
        return _carts;
    }

    // Teleports the nearest cart to right in front of you. Rides PhysGrabObject.Teleport() -
    // SetPositionRPC to the master applied with zero validation, same loophole PowerGrab/Troll
    // destroy use - works for guest or host.
    public static void TeleportNearestCart()
    {
        PlayerAvatar local = PlayerAvatar.instance;
        if (local == null) return;

        PhysGrabCart nearest = null;
        float nearestDist = float.MaxValue;
        foreach (PhysGrabCart cart in GetCarts())
        {
            if (cart == null) continue;
            float d = Vector3.Distance(local.transform.position, cart.transform.position);
            if (d < nearestDist) { nearestDist = d; nearest = cart; }
        }

        if (nearest == null)
        {
            Plugin.Log.LogInfo("Teleport Nearest Cart had no effect - no carts found on the map.");
            return;
        }

        PhysGrabObject physObject = nearest.GetComponent<PhysGrabObject>();
        if (physObject == null) return;

        Vector3 pos = local.transform.position + local.transform.forward * 2.5f;
        GameReflect.TeleportSafe(physObject, pos, local.transform.rotation, "CartHack");
    }

    private const float RamTickInterval = 0.05f; // ~20/sec, same rate PowerGrabHack uses

    private static float _lastRamTick;
    private static bool _ramGrabbing;

    // "Cart Ram" - grabs the nearest cart (PhysGrabber.OverrideGrab, the same auto-equip path
    // drones use to put items straight into a player's hands - confirmed public in the decompiled
    // source) and every tick snaps it directly onto the picked target's position, same
    // Teleport()->SetPositionRPC loophole PowerGrab already rides (zero validation on the
    // master, works for guest or host). Whether the cart's own PhysGrabObjectImpactDetector
    // actually registers this as a hit on the target depends on runtime state this file can't see
    // from source alone (the cart's isValuable/hasHealth flags could be set on the prefab itself,
    // not in any script) - this reproduces the "someone is actively holding/dragging the cart
    // into a player" situation as closely as a cheat can, nothing more can be confirmed without
    // testing in-game.
    public static void ApplyRam()
    {
        PlayerAvatar local = PlayerAvatar.instance;
        // Ghost: needs OverrideGrab on our PhysGrabber (vetoed by GhostObserverHack.Tick) and a
        // GrabStartedRPC the ghost filter would drop anyway. The cart stays wherever it was.
        bool killed = GhostObserverHack.LocalRpcIsMuted;
        if (!CheatState.CartRam.Value || local == null || killed)
        {
            if (_ramGrabbing)
            {
                PhysGrabber ownGrabber = PlayerTargets.GrabberOf(local);
                ownGrabber?.ReleaseObject(-1);
                _ramGrabbing = false;
            }
            return;
        }

        PlayerAvatar target = PlayerTargets.Picked();
        if (target == null) return;

        PhysGrabber grabber = PlayerTargets.GrabberOf(local);
        if (grabber == null) return;

        bool grabbed = GameReflect.Get<bool>(grabber, "grabbed");
        PhysGrabObject held = grabbed ? GameReflect.Get<PhysGrabObject>(grabber, "grabbedPhysGrabObject") : null;
        bool holdingCart = held != null && held.GetComponent<PhysGrabCart>() != null;

        if (!holdingCart)
        {
            PhysGrabCart nearest = null;
            float nearestDist = float.MaxValue;
            foreach (PhysGrabCart cart in GetCarts())
            {
                if (cart == null) continue;
                float d = Vector3.Distance(local.transform.position, cart.transform.position);
                if (d < nearestDist) { nearestDist = d; nearest = cart; }
            }
            if (nearest == null) return;

            PhysGrabObject cartObject = nearest.GetComponent<PhysGrabObject>();
            if (cartObject == null) return;

            grabber.OverrideGrab(cartObject);
            _ramGrabbing = true;
            return; // give the override a tick to actually take effect before ramming
        }

        if (Time.unscaledTime - _lastRamTick < RamTickInterval) return;
        _lastRamTick = Time.unscaledTime;

        Vector3 ramPoint = target.transform.position + (local.transform.position - target.transform.position).normalized * 0.5f;
        GameReflect.TeleportSafe(held, ramPoint, held.transform.rotation, "CartHack");
    }

    private const float PullTickInterval = 0.1f;
    private const float PullSpeed = 6f; // slower than Loot Magnet - carts are bigger/heavier-looking, a slow drag reads better
    private const float PullArriveDistance = 0.5f;
    private const float HorizontalArriveDistance = 0.6f;
    private const float CruiseClearance = 3f; // carts are tall, give them more headroom than a valuable needs
    private const float OverlapCheckRadius = 0.4f; // carts are chunky - a bigger safety margin than a valuable's small radius

    private static float _lastPull;

    // Continuously pulls every cart on the map toward the active extraction point - same
    // SafeFlightHelper-based approach as LootMagnetHack (rise above obstacles, cross, descend,
    // skip any step that would clip into geometry), just with a bigger clearance/overlap radius
    // since carts are considerably bigger than a valuable.
    public static void PullAllToExtraction()
    {
        if (!CheatState.CartPuller.Value) return;
        if (Time.unscaledTime - _lastPull < PullTickInterval) return;
        _lastPull = Time.unscaledTime;

        float step = PullSpeed * PullTickInterval;
        int solidMask = LayerMask.GetMask("Default");

        foreach (PhysGrabCart cart in GetCarts())
        {
            if (cart == null) continue;

            PhysGrabObject physObject = cart.GetComponent<PhysGrabObject>();
            if (physObject == null) continue;

            Vector3 current = physObject.transform.position;
            ExtractionPoint extraction = EconomyHack.FindActiveExtractionPoint(current);
            if (extraction == null || extraction.extractionArea == null) continue;

            Vector3 target = extraction.extractionArea.transform.position + Vector3.up * 0.5f;
            if (Vector3.Distance(current, target) <= PullArriveDistance) continue;

            bool safe = SafeFlightHelper.TryStep(current, target, step, CruiseClearance,
                HorizontalArriveDistance, OverlapCheckRadius, solidMask, out Vector3 next);
            if (!safe) continue;

            GameReflect.TeleportSafe(physObject, next, physObject.transform.rotation, "CartHack");
        }
    }
}
