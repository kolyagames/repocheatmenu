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

// Real grab/throw force (PhysicsGrabbingManipulation) is computed exclusively on the master
// client - confirmed straight from Assembly-CSharp: "if (... || (GameManager.Multiplayer() &&
// !isMaster) || ...) return;". A guest's own client never runs that code, and the object's
// rigidbody velocity gets overwritten by the master's OnPhotonSerializeView every sync tick
// regardless, so no local force trick can work there.
//
// PhysGrabObject.Teleport(position, rotation) is a different, legitimate path: when called by
// a non-master client it sends photonView.RPC("SetPositionRPC", RpcTarget.MasterClient, ...),
// and the host's SetPositionRPC handler applies it with zero validation (it's the same RPC the
// game itself uses for things like ItemDrone's magnet). So instead of fighting for force
// authority, this snaps whatever you're holding straight to your aim point every tick,
// bypassing weight/strength entirely - and it works as a guest because it rides the same
// RPC channel the host already accepts unconditionally.
//
// Only engages when the normal force-based path wouldn't run anyway (guest in multiplayer);
// when hosting or in singleplayer, HighStrength's PhysicsGrabManipulationPatch already handles
// it and this would just fight that system for control of the object each tick.
internal static class PowerGrabHack
{
    private static float _lastSent;

    public static void Apply()
    {
        if (!CheatState.PowerGrab.Value) return;
        if (!GameManager.Multiplayer() || PhotonNetwork.IsMasterClient) return;

        PlayerAvatar avatar = PlayerAvatar.instance;
        if (avatar == null) return;

        PhysGrabber grabber = GameReflect.Get<PhysGrabber>(avatar, "physGrabber");
        if (grabber == null) return;

        bool grabbed = GameReflect.Get<bool>(grabber, "grabbed");
        if (!grabbed) return;

        PhysGrabObject target = GameReflect.Get<PhysGrabObject>(grabber, "grabbedPhysGrabObject");
        if (target == null) return;

        if (Time.unscaledTime - _lastSent < 0.05f) return; // ~20/sec, smooth without flooding the RPC channel
        _lastSent = Time.unscaledTime;

        Vector3 pullTarget = GameReflect.Get<Vector3>(grabber, "physGrabPointPullerPosition");
        GameReflect.TeleportSafe(target, pullTarget, target.transform.rotation, "PowerGrabHack");
    }
}
