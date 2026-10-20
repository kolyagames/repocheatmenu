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

// Two "say something happened on my avatar" tricks, both riding RPCs the game's own code sends from
// the avatar's owner and that receivers accept as long as the SENDER owns the PhotonView - which
// you always do for your own PlayerAvatar, so no bypass is involved (same class as NameChangerHack
// and AnimSpeedHack). Both go to RpcTarget.Others: your own client already shows the truth and
// would only replay the sound/effect at you.
//
// 1) PhantomJump - PlayerAvatar.JumpRPC(bool _powerupEffect), gated SemiFunc.OwnerOnlyRPC. Receivers
//    play the avatar's jump impulse, the jump sound, a footstep impulse on the floor material and,
//    with _powerupEffect = true, the extra-jump sound + sparkle. Nothing touches position or
//    physics - it is the same cosmetic call the game makes on every real jump.
//
// 2) FakeHurt - PlayerHealth.UpdateHealthRPC(healthNew, healthMax, effect, hurtByHeal), gated
//    SemiFunc.MasterAndOwnerOnlyRPC (the owner passes). On a receiver it only writes that player's
//    DISPLAY health, plays hurtOther/healOther and flashes the model red/green; death is a separate
//    RPC (PlayerDeathRPC) and is never triggered here. We alternate "health-1 with the hurt effect"
//    and "real health with the heal effect", so others see you flinch red and then flash green, and
//    every cycle ends on the TRUE value. Switching it off resyncs the real value once, silently.
//    Skipped while real health <= 1 (a healthNew of 0 is the game's death-display value).
internal static class FakeMotionHack
{
    private static float _nextJump;
    private static float _nextHurt;
    private static bool _hurtPhase;
    private static bool _hurtWasOn;

    public static void Apply()
    {
        if (GhostObserverHack.LocalRpcIsMuted) return; // our Jump/Hp RPCs are dropped under ghost
        PlayerAvatar avatar = PlayerAvatar.instance;
        if (avatar == null || avatar.photonView == null) return;
        if (!GameManager.Multiplayer()) return;
        if (!CheatState.PhantomJump.Value && !CheatState.FakeHurt.Value && !_hurtWasOn) return;

        ApplyJump(avatar);
        ApplyHurt(avatar);
    }

    private static void ApplyJump(PlayerAvatar avatar)
    {
        if (!CheatState.PhantomJump.Value) return;
        if (Time.unscaledTime < _nextJump) return;
        _nextJump = Time.unscaledTime + Mathf.Max(0.15f, CheatState.FakeMotionInterval.Value);
        avatar.photonView.RPC("JumpRPC", RpcTarget.Others, true);
    }

    private static void ApplyHurt(PlayerAvatar avatar)
    {
        bool want = CheatState.FakeHurt.Value;
        PlayerHealth ph = avatar.playerHealth;
        if (ph == null) return;

        int real = GameReflect.Get<int>(ph, "health");
        int max = GameReflect.Get<int>(ph, "maxHealth");

        if (!want)
        {
            if (_hurtWasOn)
            {
                _hurtWasOn = false;
                _hurtPhase = false;
                avatar.photonView.RPC("UpdateHealthRPC", RpcTarget.Others, real, max, false, false);
            }
            return;
        }

        _hurtWasOn = true;
        if (Time.unscaledTime < _nextHurt) return;
        _nextHurt = Time.unscaledTime + Mathf.Max(0.3f, CheatState.FakeMotionInterval.Value);

        if (!_hurtPhase)
        {
            if (real <= 1) return;
            avatar.photonView.RPC("UpdateHealthRPC", RpcTarget.Others, real - 1, max, true, false);
            _hurtPhase = true;
        }
        else
        {
            avatar.photonView.RPC("UpdateHealthRPC", RpcTarget.Others, real, max, true, false);
            _hurtPhase = false;
        }
    }
}
