// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

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

    // Seconds since our avatar (a new one every level) appeared. A fresh avatar sets up its own wings,
    // expression and cosmetics during its first moments, so anything we send it too early gets overwritten.
    private static PlayerAvatar _seenAvatar;
    private static float _seenAt;

    // Where the player actually is: a dead player's avatar stays at the death spot while the death head is
    // what moves around (carried, rolled, hopping in spectate).
    public static Vector3 WorldPosition(PlayerAvatar player)
    {
        if (GameReflect.Get<bool>(player, "deadSet"))
        {
            PlayerDeathHead head = GameReflect.Get<PlayerDeathHead>(player, "playerDeathHead");
            PhysGrabObject body = head != null ? GameReflect.Get<PhysGrabObject>(head, "physGrabObject") : null;
            if (body != null) return body.transform.position;
        }
        return player.transform.position;
    }

    public static float LocalAge()
    {
        PlayerAvatar local = PlayerAvatar.instance;
        if (local != _seenAvatar)
        {
            _seenAvatar = local;
            _seenAt = Time.unscaledTime;
        }
        return local == null ? 0f : Time.unscaledTime - _seenAt;
    }

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

    // The name to SHOW on screen (HUD, island, ESP, pings, run journal). With StreamerMode on, other players
    // become "Игрок 1", "Игрок 2"... (numbered in the order they were first shown, stable for the session)
    // and you become "Вы". NameOf stays the real name: target pickers and saved settings match players by it.
    private static readonly System.Collections.Generic.Dictionary<int, int> Aliases = new();

    public static string DisplayName(PlayerAvatar player)
    {
        if (!CheatState.StreamerMode.Value) return NameOf(player);
        if (player == null) return "???";
        if (player == Local) return "Вы";
        // Actor number, not the avatar object: avatars are re-created on every level, which renumbered
        // everyone each scene.
        int id = player.photonView != null ? player.photonView.OwnerActorNr : player.GetInstanceID();
        if (!Aliases.TryGetValue(id, out int n)) Aliases[id] = n = Aliases.Count + 1;
        return "Игрок " + n;
    }

    public static PhysGrabber GrabberOf(PlayerAvatar player) =>
        player != null ? GameReflect.Get<PhysGrabber>(player, "physGrabber") : null;

    // Whichever PlayerAvatar's photonView.Owner is the current PhotonNetwork.MasterClient - "host"
    // everywhere else in this mod already means master client (see e.g. every "хост/соло" tooltip),
    // so this reuses that same definition rather than inventing a separate one. Shared by
    // NameChangerHack.ApplyMimicHost and CosmeticHack.ApplyMimicHostSkin - both were about to grow
    // their own private copy of this exact scan.
    // Who the auto-mimic toggles copy: the picked player when MimicFollowPicked is on, otherwise
    // the host. Null when there is nobody sensible to copy (you are the host, or you picked yourself).
    public static PlayerAvatar MimicSource()
    {
        PlayerAvatar local = PlayerAvatar.instance;
        if (local == null) return null;

        if (CheatState.MimicFollowPicked.Value)
        {
            PlayerAvatar picked = Picked();
            return picked == local ? null : picked;
        }

        if (local.photonView == null || local.photonView.Owner == PhotonNetwork.MasterClient) return null; // we ARE the host
        return Host();
    }

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
