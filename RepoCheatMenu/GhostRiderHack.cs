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

// "Ghost Rider" - a field nothing else in the mod touches yet: PlayerAvatar.OnPhotonSerializeView
// also sends PlayerController.instance.CollisionGrounded.physRiding/physRidingID/physRidingPosition
// (all public, confirmed in the decompiled PlayerCollisionGrounded) - the game's own "am I
// currently standing on a physics prop" state, computed by that component's OverlapSphere check
// every ~0.1s. Confirmed in PlayerAvatarVisuals.cs (PhysRidingCheck/the visualPosition lerp around
// it): on every OTHER client, receiving clientPhysRiding=true makes them spawn a child object
// under PhotonView.Find(clientPhysRidingID)'s transform at clientPhysRidingPosition (a LOCAL
// offset) and lerp YOUR visual body toward that point instead of your broadcast position - this is
// literally how standing on a moving cart/elevator already looks to everyone else in vanilla play.
// Spoofing all three lets us point that same mechanism at ANY networked PhysGrabObject in the
// scene, so to everyone else your character appears glued to and riding around on some random
// valuable/prop/cart, tracking wherever it goes - completely decoupled from where you actually are
// or how you're actually moving, which stays 100% untouched locally (same broadcast-spoof shape as
// Invisible/Flip/GhostCrawl - only the outgoing stream is touched).
internal static class GhostRiderHack
{
    private const float RescanInterval = 1.5f;
    private static float _nextScan;

    private static PhysGrabObject _target;
    private static PhotonView _targetView;

    public static bool HasTarget => _targetView != null;
    public static int TargetViewId => _targetView != null ? _targetView.ViewID : 0;
    public static string TargetName => _target != null ? _target.name : null;

    // Static offset (just above the object's own origin) rather than anything dynamic - the
    // receiving client re-derives the actual world position itself every frame from the target's
    // live transform, so a fixed local offset already rides along naturally as that object moves,
    // spins, or gets thrown. No need to animate this ourselves.
    public static readonly Vector3 RidingLocalOffset = Vector3.up * 0.6f;

    // Ticked from Patches.cs's CoreStats (~33/sec, same cadence TrollHack's own rescans use) -
    // deliberately NOT scanning inside the OnPhotonSerializeView patch itself, which can fire
    // several times a second per PUN's own send rate and would make a FindObjectsByType sweep run
    // far more often than the target actually needs refreshing.
    public static void Apply()
    {
        if (!CheatState.GhostRider.Value)
        {
            _target = null;
            _targetView = null;
            return;
        }

        if (Time.unscaledTime < _nextScan && _targetView != null) return;
        _nextScan = Time.unscaledTime + RescanInterval;

        PlayerAvatar local = PlayerAvatar.instance;
        if (local == null) { _target = null; _targetView = null; return; }

        PhysGrabObject nearest = null;
        PhotonView nearestView = null;
        float nearestDist = float.MaxValue;

        foreach (PhysGrabObject candidate in Object.FindObjectsByType<PhysGrabObject>(FindObjectsSortMode.None))
        {
            if (candidate == null) continue;
            // Caught live via the debug bridge: player tumble ragdolls carry their own PhysGrabObject
            // too (confirmed elsewhere in the codebase - PlayerCollisionGrounded.ColliderCheck uses
            // this exact GetComponent<PlayerTumble>() check to tell a ragdoll apart from a real prop)
            // and sit essentially on top of the player at all times, so an unfiltered nearest-scan
            // picks your OWN tumble body as the "ride target" almost every time - pointless and
            // self-referential, not the "glued to a random valuable" effect this is meant to be.
            if (candidate.GetComponent<PlayerTumble>() != null) continue;
            PhotonView view = candidate.GetComponent<PhotonView>();
            if (view == null || view.ViewID == 0) continue;

            float dist = Vector3.Distance(local.transform.position, candidate.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = candidate;
                nearestView = view;
            }
        }

        _target = nearest;
        _targetView = nearestView;
    }
}
