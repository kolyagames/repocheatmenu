// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using Photon.Pun;
using UnityEngine;

namespace RepoCheatMenu;

// Flips the shop keeper's own on/off switch so the framing tools (Frame for Shop Keeper, Frame
// All, Shop Execution) still work when someone left it switched off - ShootAtPlayer refuses
// outright while currentState is Sleeping or WakeUp, so an off keeper made all three silently
// do nothing.
//
// There is no forgeable "wake" RPC: ShopKeeper.WakeUpRPC is MasterOnlyRPC-gated, and
// WakeUpOrSleepLogic() (public, ungated) only *sends* that RPC - fired from a guest it gets
// rejected on every receiver, including the master. Host/singleplayer can just call it directly;
// that path is exact and reliable.
//
// The guest path reproduces what actually happens when a player presses the switch by hand,
// rather than trying to forge the result. Confirmed in the decompiled source, that button is a
// PhysGrabObjectGrabArea whose Update() runs ONLY on the master ("if
// (!SemiFunc.IsMasterClientOrSingleplayer()) return;") and fires grabAreaEventOnStart - wired to
// WakeUpOrSleepLogic - as soon as it sees, in the MASTER's own copies:
//   1. the grabber present in the object's playerGrabbing list,
//   2. that grabber's initialPressTimer > 0,
//   3. that grabber's physGrabPoint.position inside the switch's collider.
// So the master itself ends up sending WakeUpRPC, and it passes MasterOnlyRPC for real. Each of
// those three pieces of state has an RPC a guest is legitimately allowed to send:
//   1. StaticGrabObject.GrabStartedRPC - no PhotonMessageInfo, no check of any kind.
//   2. PhysGrabber.PhysGrabBeamActivateRPC - OwnerOnlyRPC against OUR OWN PhysGrabber's view,
//      which we own, so it passes and sets initialPressTimer = 0.1f on every client.
//   3. StaticGrabObject.GrabPointSyncRPC - OwnerOnlyRPC against our own PhysGrabber's view again;
//      writes physGrabPoint.localPosition on the master, which is exactly the field its grab-area
//      check reads.
//
// UNVERIFIED - reasoned from the source, not tested live. The weak link is (3): localPosition is
// relative to physGrabPoint's parent, and the master's copy of that parent follows the master's
// (slightly interpolated) view of our player, so the resulting world point can land just outside
// a small collider. If it turns out not to fire, walk over and press the switch by hand - the
// framing tools work normally once the keeper is on.
internal static class ShopKeeperWake
{
    // The master needs a moment to run its own grab-area Update, fire the event and broadcast
    // WakeUpRPC back; retrying every frame would just spam RPCs through that whole window.
    private const float RetryInterval = 2f;
    private static float _nextAttempt;

    // True while the master still has us listed as grabbing the keeper from a previous press.
    private static bool _pendingRelease;

    public static bool IsAsleep()
    {
        ShopKeeper keeper = ShopKeeper.instance;
        return keeper != null &&
               (keeper.currentState == ShopKeeper.State.Sleeping || keeper.currentState == ShopKeeper.State.WakeUp);
    }

    // Returns false if it didn't even get as far as sending anything (no keeper, no switch,
    // still on cooldown) - the caller decides whether that's worth logging.
    public static bool TryWake()
    {
        ShopKeeper keeper = ShopKeeper.instance;
        if (keeper == null) return false;
        if (keeper.currentState != ShopKeeper.State.Sleeping) return false; // already on, or mid-wake
        // The multi-step press needs a PhysGrabBeamActivateRPC off OUR grabber view; under a
        // session ghost that RPC is filtered, so the master never updates initialPressTimer and
        // the switch does nothing. No clean way around it, skip quietly.
        if (GhostObserverHack.LocalRpcIsMuted) return false;

        if (Time.unscaledTime < _nextAttempt) return false;
        _nextAttempt = Time.unscaledTime + RetryInterval;

        if (!GameManager.Multiplayer() || PhotonNetwork.IsMasterClient)
        {
            keeper.WakeUpOrSleepLogic();
            Plugin.Log.LogInfo("Shop keeper: flipped the switch directly (we're the host).");
            return true;
        }

        return PressSwitchAsGuest(keeper);
    }

    private static bool PressSwitchAsGuest(ShopKeeper keeper)
    {
        PlayerAvatar local = PlayerAvatar.instance;
        PhysGrabber grabber = local != null ? GameReflect.Get<PhysGrabber>(local, "physGrabber") : null;
        if (grabber == null || grabber.photonView == null || grabber.physGrabPoint == null) return false;

        StaticGrabObject staticGrab = keeper.GetComponentInChildren<StaticGrabObject>()
                                      ?? keeper.GetComponentInParent<StaticGrabObject>();
        PhotonView staticView = staticGrab != null ? GameReflect.Get<PhotonView>(staticGrab, "photonView") : null;
        if (staticView == null) return false;

        Vector3 switchPoint = FindSwitchPoint(keeper);
        Transform parent = grabber.physGrabPoint.parent;
        if (parent == null) return false;

        int grabberViewId = grabber.photonView.ViewID;

        // Clean up the previous attempt before starting another. GrabStartedRPC appends us to the
        // keeper's playerGrabbing list on the master and nothing ever removes us again - that list
        // is only pruned for null entries and on OnDisable, so repeated attempts would otherwise
        // pile up stale copies of our grabber in the host's state and keep its Update loop
        // computing pull velocity from a grab that isn't happening. This is deliberately done on
        // the NEXT attempt rather than right after the press: RPCs arrive in order, so releasing
        // immediately could land before the master's own grab-area Update ever ran, cancelling
        // the press we just set up.
        if (_pendingRelease)
        {
            staticView.RPC("GrabEndedRPC", RpcTarget.MasterClient, grabberViewId);
            _pendingRelease = false;
        }

        // 1) get counted as grabbing the keeper at all
        staticView.RPC("GrabStartedRPC", RpcTarget.MasterClient, grabberViewId);
        _pendingRelease = true;

        // 2) satisfy the initialPressTimer > 0 gate on the master's copy of our grabber
        grabber.photonView.RPC("PhysGrabBeamActivateRPC", RpcTarget.All);

        // 3) put our grab point on the switch, in the master's coordinates
        staticView.RPC("GrabPointSyncRPC", RpcTarget.MasterClient, grabberViewId, parent.InverseTransformPoint(switchPoint));

        Plugin.Log.LogInfo("Shop keeper is off - sent a switch press. If it stays off, flip it by hand.");
        return true;
    }

    // The grab area itself isn't reachable by name, but ShopKeeper.OnOffSwitch is a public field
    // pointing straight at the switch object, and the grab area is built around it - so aim at
    // whichever grab-area collider actually contains/sits nearest that switch, falling back to
    // the switch's own position if the areas can't be read for any reason.
    private static Vector3 FindSwitchPoint(ShopKeeper keeper)
    {
        Vector3 fallback = keeper.OnOffSwitch != null
            ? keeper.OnOffSwitch.transform.position
            : keeper.transform.position;

        var grabArea = keeper.GetComponentInChildren<PhysGrabObjectGrabArea>();
        if (grabArea == null || grabArea.grabAreas == null) return fallback;

        float bestDist = float.MaxValue;
        Vector3 best = fallback;
        foreach (PhysGrabObjectGrabArea.GrabArea area in grabArea.grabAreas)
        {
            if (area?.grabAreaTransform == null) continue;
            float d = Vector3.Distance(area.grabAreaTransform.position, fallback);
            if (d < bestDist)
            {
                bestDist = d;
                best = area.grabAreaTransform.position;
            }
        }
        return best;
    }
}
