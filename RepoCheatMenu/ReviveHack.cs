// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

namespace RepoCheatMenu;

// PlayerAvatar.ReviveRPC goes to everyone and each client checks SemiFunc.MasterOnlyRPC itself.
// MasterRpcSelfBypass only patches OUR copy, so a guest calling Revive stood up on its own screen
// only - the host and everyone else still had the head, and the local body walked around desynced.
// Revive Self therefore goes through DeathRelocateHack for a guest (moves the head to where the game
// really revives it); Revive Other / auto-revive stay host-only for real.
internal static class ReviveHack
{
    public static void ReviveSelf() => DeathRelocateHack.ReviveSelf();

    public static void ReviveOther()
    {
        PlayerAvatar target = PlayerTargets.Picked();
        if (target == null) return;
        if (!GameReflect.Get<bool>(target, "deadSet")) return;
        target.Revive(false);
    }

    private const float AutoReviveInterval = 2f;
    private static float _nextAutoRevive;

    public static void ApplyAutoRevive()
    {
        if (!CheatState.AutoReviveTeammates.Value) return;
        if (UnityEngine.Time.unscaledTime < _nextAutoRevive) return;
        _nextAutoRevive = UnityEngine.Time.unscaledTime + AutoReviveInterval;
        // ReviveRPC's MasterOnlyRPC guard rejects us on the host side when we are a guest -
        // calling it every 2 s then just floods the wire with silently-dropped RPCs. The toggle
        // description already says the auto-revive is master-only; stay honest to that here.
        if (Photon.Pun.PhotonNetwork.InRoom && !Photon.Pun.PhotonNetwork.IsMasterClient) return;

        GameDirector director = GameDirector.instance;
        System.Collections.Generic.List<PlayerAvatar> players = director != null
            ? GameReflect.Get<System.Collections.Generic.List<PlayerAvatar>>(director, "PlayerList")
            : null;
        if (players == null) return;

        PlayerAvatar local = PlayerAvatar.instance;
        foreach (PlayerAvatar player in players)
        {
            if (player == null || player == local) continue;
            if (!GameReflect.Get<bool>(player, "deadSet")) continue;
            player.Revive(false);
        }
    }
}
