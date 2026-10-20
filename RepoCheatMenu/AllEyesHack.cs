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

// PlayerVoiceChat.IsTalkingRPC(bool) - [PunRPC], gated by SemiFunc.OwnerOnlyRPC(_info, photonView),
// which you always pass on your own voice chat, so this is a legitimate call with no bypass, same
// category as NameChangerHack/ExpressionHack/AnimSpeedHack.
//
// What the flag actually drives on every OTHER client (confirmed by grepping the decompile for the
// field, all three consumers are visual - nothing audio, nothing physics):
//   - PlayerEyes.Update(): every avatar within 6m scans the player list for whoever is isTalking
//     and locks its EYES onto them (currentTalker). Forced on, everyone nearby keeps staring at you.
//   - MenuPlayerListed: the lobby player list's "currently talking" indicator next to your name.
//   - MenuPlayerHead: the lobby menu head reacts to the same flag.
// The receive side never gates audio on it - the whole mic/stream half of PlayerVoiceChat sits
// behind "if (photonView.IsMine)", so nobody's client tries to play anything because of this.
//
// Why this needs a periodic resend, unlike ExpressionHack/AnimSpeedHack: the game's own sender
// (PlayerVoiceChat.Update, confirmed) is edge-triggered - "if (isTalking != isTalkingPrevious)
// photonView.RPC(..., RpcTarget.Others, isTalking)" - so the moment your real mic actually closes
// it broadcasts a genuine false that overwrites ours. Re-asserting once per interval wins that race
// back within at most one interval. Note it sends to RpcTarget.Others, never All - and so do we, so
// our own local isTalking stays completely real and InvisibleRevealWhileTalking (which reads that
// same local field) keeps behaving exactly as it does now.
internal static class AllEyesHack
{
    private const float ResendInterval = 0.5f;

    private static float _nextSend;
    private static bool _wasOn;

    public static void Apply()
    {
        bool want = CheatState.AllEyesOnMe.Value;

        if (!want)
        {
            // One release on the falling edge - otherwise the stale true sits on their clients until
            // your real mic next opens and closes to produce a genuine false.
            if (_wasOn)
            {
                _wasOn = false;
                Send(false);
            }
            return;
        }

        _wasOn = true;
        if (Time.unscaledTime < _nextSend) return;
        _nextSend = Time.unscaledTime + ResendInterval;
        Send(true);
    }

    private static void Send(bool isTalking)
    {
        if (!GameManager.Multiplayer()) return; // nobody else's eyes to attract in singleplayer

        PlayerVoiceChat voice = PlayerVoiceChat.instance;
        if (voice == null) return;

        PhotonView view = GameReflect.Get<PhotonView>(voice, "photonView");
        view?.RPC("IsTalkingRPC", RpcTarget.Others, isTalking);
    }
}
