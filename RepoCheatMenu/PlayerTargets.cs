using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace RepoCheatMenu;

// Shared player lookup for the newer hack files. RaidHack/MassRaidHack/TeleportHack each grew
// their own private copy of exactly these three helpers before this existed; new code uses this
// one instead of adding a fourth.
internal static class PlayerTargets
{
    public static PlayerAvatar Local => PlayerAvatar.instance;

    public static List<PlayerAvatar> AliveOthers()
    {
        List<PlayerAvatar> result = new();
        PlayerAvatar local = Local;
        GameDirector director = GameDirector.instance;
        List<PlayerAvatar> players = director != null ? GameReflect.Get<List<PlayerAvatar>>(director, "PlayerList") : null;
        if (players == null) return result;

        foreach (PlayerAvatar p in players)
        {
            if (p == null || p == local) continue;
            if (GameReflect.Get<bool>(p, "deadSet")) continue;
            result.Add(p);
        }
        return result;
    }

    // Same picker every "pick a player" row in the menu writes to.
    public static PlayerAvatar Picked()
    {
        string targetName = CheatState.FakeSoundTargetName.Value;
        if (string.IsNullOrEmpty(targetName) || targetName == "None") return null;

        GameDirector director = GameDirector.instance;
        List<PlayerAvatar> players = director != null ? GameReflect.Get<List<PlayerAvatar>>(director, "PlayerList") : null;
        if (players == null) return null;

        foreach (PlayerAvatar p in players)
        {
            if (p == null) continue;
            if (GameReflect.Get<string>(p, "playerName") == targetName) return p;
        }
        return null;
    }

    public static string NameOf(PlayerAvatar player)
    {
        if (player == null) return "???";
        string name = GameReflect.Get<string>(player, "playerName");
        return string.IsNullOrEmpty(name) ? "???" : name;
    }

    public static PhysGrabber GrabberOf(PlayerAvatar player) =>
        player != null ? GameReflect.Get<PhysGrabber>(player, "physGrabber") : null;

    // Whichever PlayerAvatar's photonView.Owner is the current PhotonNetwork.MasterClient - "host"
    // everywhere else in this mod already means master client (see e.g. every "хост/соло" tooltip),
    // so this reuses that same definition rather than inventing a separate one. Shared by
    // NameChangerHack.ApplyMimicHost and CosmeticHack.ApplyMimicHostSkin - both were about to grow
    // their own private copy of this exact scan.
    public static PlayerAvatar Host()
    {
        GameDirector director = GameDirector.instance;
        List<PlayerAvatar> players = director != null ? GameReflect.Get<List<PlayerAvatar>>(director, "PlayerList") : null;
        if (players == null) return null;

        foreach (PlayerAvatar p in players)
        {
            if (p == null || p.photonView == null) continue;
            if (p.photonView.Owner == PhotonNetwork.MasterClient) return p;
        }
        return null;
    }
}
