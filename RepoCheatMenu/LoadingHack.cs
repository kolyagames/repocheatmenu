// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

namespace RepoCheatMenu;

// Live multiplayer testing found that the original single-mode version of this (the ForceSkip
// path below) desyncs other players: GameDirector.PlayerList's per-player levelAnimationCompleted
// flags only ever flip true via PlayerAvatar.LoadingLevelAnimationCompletedRPC (RpcTarget.All) -
// the proper "I finished my card animation" signal that's broadcast to everyone. ForceSkip
// instead pokes LoadingUI's own (local-only) levelAnimationCompleted field directly and disables
// its GameObject, which stops LoadingUI's own LateUpdate - the only code that normally
// re-evaluates that field from everyone's real flags. If other players genuinely hadn't finished
// yet, their clients are left waiting forever on an RPC that this path never sends, while our own
// client has already jumped into gameState.Main ahead of them - which is exactly what produced
// the "limbo"/stuck-loading reports. NormalSkip below sends the real RPC instead, so we only ever
// stop being the bottleneck ourselves - we don't jump the group. ForceSkip is kept as an opt-in
// fallback (toggled via CheatState.LoadingForceSkip) for when the screen is stuck specifically
// because of a desync that NormalSkip can't fix anyway.
internal static class LoadingHack
{
    public static void SkipNow()
    {
        if (CheatState.LoadingForceSkip.Value)
            ForceSkip();
        else
            NormalSkip();
    }

    private static void NormalSkip()
    {
        if (PlayerController.instance == null || PlayerController.instance.playerAvatarScript == null) return;
        PlayerController.instance.playerAvatarScript.LoadingLevelAnimationCompleted();
    }

    private static void ForceSkip()
    {
        if (LoadingUI.instance == null) return;

        GameReflect.Set(LoadingUI.instance, "levelAnimationCompleted", true);
        LoadingUI.instance.StopLoading();
    }
}
