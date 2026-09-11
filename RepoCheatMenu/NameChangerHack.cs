using Photon.Pun;

namespace RepoCheatMenu;

// PlayerAvatar.AddToStatsManagerRPC(string _playerName, string _steamID) (confirmed [PunRPC] in
// the decompiled source) is gated by SemiFunc.OwnerOnlyRPC(_info, photonView) - checks the
// SENDER, not just execution context, but you always own your own avatar's PhotonView, so this
// passes cleanly with no bypass trick needed, unlike most of this mod's other tricks. It sets
// playerName/steamID on PlayerAvatar, mirrors the name onto PlayerController.PlayerSetName() for
// the local copy, and registers it with StatsManager.PlayerAdd() - broadcast RpcTarget.AllBuffered
// so it also replays for anyone who joins the room later. steamID is deliberately kept as your
// REAL one (not overwritten) - StatsManager keys your own upgrade levels (strength/range/wings/
// etc, confirmed in the decompiled source) off steamID, so changing it would orphan your existing
// upgrade progress from a StatsManager lookup perspective. Only the displayed name changes.
internal static class NameChangerHack
{
    public static void SetName(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;

        PlayerAvatar avatar = PlayerAvatar.instance;
        if (avatar == null) return;

        string steamId = GameReflect.Get<string>(avatar, "steamID");

        if (GameManager.Multiplayer())
        {
            PhotonView view = GameReflect.Get<PhotonView>(avatar, "photonView");
            view?.RPC("AddToStatsManagerRPC", RpcTarget.AllBuffered, newName, steamId);
        }
        else
        {
            GameReflect.Invoke(avatar, "AddToStatsManagerRPC", newName, steamId, default(PhotonMessageInfo));
        }
    }

    // Auto-impersonation - see CheatState.MimicHostName's own comment for the full mechanism.
    // Ticked continuously (RunSafe in Patches.cs) rather than once at join so it also catches a
    // later host rename or a master-client migration mid-session, not just the name seen at load.
    private static string _lastMimickedHostName;

    public static void ApplyMimicHost()
    {
        if (!CheatState.MimicHostName.Value) return;
        if (!GameManager.Multiplayer()) return; // no separate "host" identity to copy in true singleplayer

        PlayerAvatar local = PlayerAvatar.instance;
        if (local == null || local.photonView == null) return;
        if (local.photonView.Owner == PhotonNetwork.MasterClient) return; // we ARE the host - nothing to mimic

        PlayerAvatar host = PlayerTargets.Host();
        string hostName = host != null ? GameReflect.Get<string>(host, "playerName") : null;
        if (string.IsNullOrEmpty(hostName) || hostName == _lastMimickedHostName) return;

        _lastMimickedHostName = hostName;
        SetName(hostName);
    }
}
