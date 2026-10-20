// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

namespace RepoCheatMenu;

// Confirmed from the real decompiled Assembly-CSharp source (PhysGrabObject.cs):
// PhysicsGrabbingManipulation() starts with
//   if (isGrabForceZero || (GameManager.Multiplayer() && !isMaster) || rb.isKinematic) return;
// so on a guest's own client that method's body never runs at all - grabStrength literally
// cannot affect anything there. Worse: PhysGrabObject's OnPhotonSerializeView has the owner
// (the master client) write rbVelocity/rbAngularVelocity every sync tick, and everyone else's
// rigidbody gets those values written straight over their own - so even a one-off
// Throw()-time force boost (line ~1928, force *= 0.5f + player.throwStrength) gets stomped by
// the next sync packet on a guest. There is no local-only trick that makes real grab/throw
// strength work for a guest; it only ever takes effect when the local player is master client
// or in singleplayer, which is exactly why RepoHax's own menu marks it host-only.
//
// grabRange is different: PhysGrabber.PhysGrabLogic()/RayCheck() have no isMaster gating at
// all, so the range patches in Patches.cs (PhysGrabLogicRangePatch/PhysGrabRayCheckRangePatch)
// work the same for guest or host. HighStrength's grabStrength/throwStrength forcing below
// only matters when hosting or in singleplayer - kept for that case, not as a guest fix.
internal static class EntityHack
{
    public static void Apply()
    {
        // EnemyDirector.debugNoGrabMaxTime is the game's own built-in toggle for this -
        // EnemyRigidbody's grab-shake-release check is literally "if (!EnemyDirector.instance.
        // debugNoGrabMaxTime && ... grabTimeCurrent >= grabTimeMaxRandom) GrabReleaseShake()".
        // Setting this one field directly is race-free, unlike our old approach of zeroing
        // grabTimeCurrent/grabShakeReleaseTimer on every enemy every tick, which fought the
        // enemy's own per-fixed-update increment of the same fields.
        if (EnemyDirector.instance != null)
            GameReflect.Set(EnemyDirector.instance, "debugNoGrabMaxTime", CheatState.NoGrabMaxTime.Value);

        if (CheatState.HighStrength.Value)
        {
            PlayerAvatar avatar = PlayerAvatar.instance;
            PhysGrabber grabber = avatar != null ? GameReflect.Get<PhysGrabber>(avatar, "physGrabber") : null;
            if (grabber != null)
                GameReflect.Set(grabber, "throwStrength", 100f);
        }
    }
}
