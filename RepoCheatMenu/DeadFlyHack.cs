// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

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
        // Don't steer the dead-fly while the player is typing in chat or the mod's menu - Space (Jump)
        // was teleporting the deathHead up every time the chatter hit the spacebar.
        if (UiInput.TypingInMenu()) return;

        float forwardIn = SemiFunc.InputMovementY();
        float strafeIn = SemiFunc.InputMovementX();
        Vector3 dir = camTransform.forward * forwardIn + camTransform.right * strafeIn;
        if (SemiFunc.InputHold(InputKey.Jump)) dir += Vector3.up;
        if (SemiFunc.InputHold(InputKey.Crouch)) dir += Vector3.down;
        if (dir.sqrMagnitude < 0.0001f) return;
        if (dir.sqrMagnitude > 1f) dir.Normalize();

        Vector3 next = physObject.transform.position + dir * CheatState.DeadFlySpeed.Value * TickInterval;
        GameReflect.TeleportSafe(physObject, next, physObject.transform.rotation, "DeadFlyHack");
    }
}
