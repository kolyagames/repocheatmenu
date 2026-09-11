namespace RepoCheatMenu;

// PlayerAvatar.ReviveRPC only checks SemiFunc.MasterOnlyRPC(_info), nothing else - no
// photonView.IsMine gate like HurtOtherRPC/HealOtherRPC have. That means Revive Self genuinely
// works for a guest once MasterRpcSelfBypass is on (patched in Patches.cs), since ReviveRPC then
// runs to completion on your own client the same way it would on the real master's. Revive Other
// is a different story: sending it targets someone else's PlayerAvatar, so it also runs on THEIR
// client - and their machine isn't patched by anything running on yours, so their own copy of
// MasterOnlyRPC still correctly rejects it unless you're actually master. As a guest all you get
// is a local-only illusion (your own replica of them stands back up on your screen, nothing
// changes on their end or anyone else's) - real for host/singleplayer only.
internal static class ReviveHack
{
    public static void ReviveSelf()
    {
        PlayerAvatar local = PlayerAvatar.instance;
        if (local == null) return;
        if (!GameReflect.Get<bool>(local, "deadSet")) return;
        local.Revive(false);
    }

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
