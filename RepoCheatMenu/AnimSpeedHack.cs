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

// PlayerAvatar.OverrideAnimationSpeedActivateRPC(bool active, float speedMulti, float in, float out,
// float time) - [PunRPC], gated by SemiFunc.OwnerOnlyRPC(_info, photonView), which you always pass
// on your own avatar, so this is a legitimate call with no bypass, same category as NameChangerHack
// and ExpressionHack. The game itself only ever reaches for this from ValuableWizardTimeGlass
// (0.2x slow motion while holding it) - the RPC accepts any multiplier and any duration.
//
// Holding it needs exactly one call, not a per-tick resend: confirmed in PlayerAvatar.
// OverrideAnimationSpeedLogic(), every OTHER client runs "if (!isLocal && overrideAnimationSpeedActive)
// OverrideAnimationSpeed(...)" every frame, which re-arms that effect's own decay timer locally on
// their machine for as long as the active flag stays set. Their re-arm can't echo back at us either
// (the send wrapper behind it is gated "if (isLocal)", and our avatar isn't local to them).
//
// Only animator.speed and PlayerAvatarVisuals' own internal deltaTime are touched downstream - no
// Transform, Rigidbody or collider anywhere in that path, so this is the same safe class as
// FakePing/FakeOvercharge, not the position-spoof class that needed rb.position/velocity hardening.
// Our own real movement speed is untouched - only how fast the animation of it plays elsewhere.
internal static class AnimSpeedHack
{
    // Matches ValuableWizardTimeGlass's own fade shape (1s in, 2s out) - the game's only real
    // caller, so remote clients ease into and out of it the way they already do for that item.
    private const float FadeIn = 1f;
    private const float FadeOut = 2f;
    private const float ArmTime = 0.1f;

    private static bool _lastApplied;
    private static float _lastSpeed = -1f;
    private static PlayerAvatar _appliedTo;

    public static void Apply()
    {
        // Ghost: our outgoing avatar RPCs are filtered, so the spoof would echo only on our screen.
        if (GhostObserverHack.LocalRpcIsMuted) return;
        bool want = CheatState.AnimSpeedSpoof.Value;
        float speed = Mathf.Clamp(CheatState.AnimSpeedValue.Value, 0.1f, 5f);
        PlayerAvatar avatar = PlayerAvatar.instance;

        // A new level means a new avatar at normal speed: re-send for it instead of trusting the old state.
        if (avatar != _appliedTo) { _lastApplied = false; _lastSpeed = -1f; }
        if (want == _lastApplied && (!want || Mathf.Approximately(speed, _lastSpeed))) return;

        if (avatar == null || avatar.photonView == null) return; // retry next tick once it exists
        if (want && PlayerTargets.LocalAge() < 2f) return; // let the fresh avatar finish its own setup
        _appliedTo = avatar;

        // active:false restores the normal rate - the multiplier passed alongside it is only what
        // the (now inactive) target lerps out from, so 1f keeps that tail-off pointed at normal.
        bool active = want;
        float multi = want ? speed : 1f;

        if (GameManager.Multiplayer())
            avatar.photonView.RPC("OverrideAnimationSpeedActivateRPC", RpcTarget.All,
                active, multi, FadeIn, FadeOut, ArmTime);
        else
            GameReflect.Invoke(avatar, "OverrideAnimationSpeedActivateRPC",
                active, multi, FadeIn, FadeOut, ArmTime, default(PhotonMessageInfo));

        _lastApplied = want;
        _lastSpeed = want ? speed : -1f;
    }
}
