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

// PlayerAvatar.PlayerExpressionSetRPC/PlayerExpressionStopRPC (confirmed [PunRPC] in the decompiled
// source) are gated by SemiFunc.OwnerOnlyRPC(_info, photonView) - a real sender check, but you
// always own your own avatar, so this is a completely legitimate call, no bypass needed, same
// category as NameChangerHack. The game already lets a player show one of six expressions by
// HOLDING a keybind (InputKey.Expression1-6, confirmed in the decompiled PlayerExpression.cs,
// Update() calling DoExpression while SemiFunc.InputHold(...) is true) - this just triggers the
// same RPC directly so it can be locked ON from the menu instead of needing to physically hold a
// key. Confirmed in PlayerExpression.cs's own receive-side Update(): playerExpressions is a plain
// dictionary read every frame by every OTHER client's own component, which refreshes its own local
// decay timer by itself as long as the entry is present - so a single Set call holds the pose
// indefinitely, no per-tick resend needed, only one call per actual change (lock on/off, or index
// switched while locked).
internal static class ExpressionHack
{
    private static bool _lastApplied;
    private static int _lastIndex = -1;
    private static PlayerAvatar _appliedTo;

    public static void Apply()
    {
        if (GhostObserverHack.LocalRpcIsMuted) return; // avatar RPCs filtered under ghost
        bool want = CheatState.ExpressionLock.Value;
        int index = Mathf.Clamp(CheatState.ExpressionIndex.Value, 1, 6);
        PlayerAvatar avatar = PlayerAvatar.instance;

        // Every level spawns a fresh avatar with no expression, so "already sent" only counts for the avatar
        // it was sent to - otherwise the lock silently ended at the first scene change.
        if (avatar != _appliedTo) { _lastApplied = false; _lastIndex = -1; }
        if (want == _lastApplied && (!want || index == _lastIndex)) return;

        if (avatar == null || avatar.photonView == null) return; // retry next tick once it exists
        if (want && PlayerTargets.LocalAge() < 2f) return; // let the fresh avatar finish its own setup
        _appliedTo = avatar;

        if (want)
        {
            if (GameManager.Multiplayer())
                avatar.photonView.RPC("PlayerExpressionSetRPC", RpcTarget.All, index, 100f);
            else
                GameReflect.Invoke(avatar, "PlayerExpressionSetRPC", index, 100f, default(PhotonMessageInfo));
        }
        else
        {
            if (GameManager.Multiplayer())
                avatar.photonView.RPC("PlayerExpressionStopRPC", RpcTarget.All, _lastIndex);
            else
                GameReflect.Invoke(avatar, "PlayerExpressionStopRPC", _lastIndex, default(PhotonMessageInfo));
        }

        _lastApplied = want;
        _lastIndex = want ? index : -1;
    }
}
