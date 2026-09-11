using UnityEngine;

namespace RepoCheatMenu;

// Free movement while dead, before revive. See CheatState.DeadFly's description for the full
// reasoning - short version: don't touch SpectateCamera at all, just relocate your own death
// head (it has the same PhysGrabObject/Teleport() every grabbable prop does) and the vanilla
// follow-cam tracks it on its own, since its anchor point is a child transform on the head.
internal static class DeadFlyHack
{
    private const float TickInterval = 0.05f; // ~20/sec, same rate PowerGrabHack uses

    private static float _lastTick;

    public static void Apply()
    {
        if (!CheatState.DeadFly.Value) return;

        PlayerAvatar local = PlayerAvatar.instance;
        if (local == null || !GameReflect.Get<bool>(local, "deadSet")) return;

        PlayerDeathHead deathHead = GameReflect.Get<PlayerDeathHead>(local, "playerDeathHead");
        if (deathHead == null) return;

        PhysGrabObject physObject = GameReflect.Get<PhysGrabObject>(deathHead, "physGrabObject");
        if (physObject == null) return;

        if (Time.unscaledTime - _lastTick < TickInterval) return;
        _lastTick = Time.unscaledTime;

        Transform camTransform = SpectateCamera.instance != null ? SpectateCamera.instance.transform : null;
        if (camTransform == null) return;

        float forwardIn = SemiFunc.InputMovementY();
        float strafeIn = SemiFunc.InputMovementX();
        Vector3 dir = camTransform.forward * forwardIn + camTransform.right * strafeIn;
        if (SemiFunc.InputDown(InputKey.Jump)) dir += Vector3.up;
        if (SemiFunc.InputDown(InputKey.Crouch)) dir += Vector3.down;
        if (dir.sqrMagnitude < 0.0001f) return;
        if (dir.sqrMagnitude > 1f) dir.Normalize();

        Vector3 next = physObject.transform.position + dir * CheatState.DeadFlySpeed.Value * TickInterval;
        physObject.Teleport(next, physObject.transform.rotation);
    }
}
