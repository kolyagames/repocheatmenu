// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System.Collections.Generic;
using System.Text;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace RepoCheatMenu;

// Who is in the Photon room but has no avatar on our side - another ghost observer, or someone hiding the
// same way. Every in-game list (lobby, Esc menu, spectate, truck screen) is built from avatars, so such a
// player is otherwise invisible; the Photon room still has them. A player who is just loading also has no
// avatar for a moment, so someone only counts after GraceSeconds without one, and only in scenes where
// avatars exist at all (gameMode 1 with our own avatar up).
internal static class HiddenPlayersHack
{
    private const float GraceSeconds = 8f;
    private const float Interval = 1f;

    private static readonly Dictionary<int, float> MissingSince = new();
    private static readonly HashSet<int> Announced = new();
    private static readonly List<string> Hidden = new();
    private static readonly List<int> Scratch = new();
    private static float _next;

    public static void Apply()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + Interval;

        Room room = PhotonNetwork.CurrentRoom;
        GameDirector director = GameDirector.instance;
        List<PlayerAvatar> avatars = director != null ? GameReflect.Get<List<PlayerAvatar>>(director, "PlayerList") : null;
        bool avatarScene = room != null && GameManager.instance != null && GameManager.instance.gameMode == 1
            && PlayerAvatar.instance != null && avatars != null;
        if (!avatarScene)
        {
            MissingSince.Clear();
            Hidden.Clear();
            if (room == null) Announced.Clear();
            return;
        }

        HashSet<int> withAvatar = new();
        foreach (PlayerAvatar a in avatars)
            if (a != null && a.photonView != null) withAvatar.Add(a.photonView.OwnerActorNr);

        Hidden.Clear();
        foreach (Player p in room.Players.Values)
        {
            if (p == null || p.IsLocal) continue;
            if (withAvatar.Contains(p.ActorNumber))
            {
                MissingSince.Remove(p.ActorNumber);
                continue;
            }
            if (!MissingSince.TryGetValue(p.ActorNumber, out float since))
            {
                MissingSince[p.ActorNumber] = Time.unscaledTime;
                continue;
            }
            if (Time.unscaledTime - since < GraceSeconds) continue;

            string name = string.IsNullOrEmpty(p.NickName) ? "#" + p.ActorNumber : p.NickName;
            Hidden.Add(name);
            if (CheatState.HiddenPlayerAlert.Value && Announced.Add(p.ActorNumber))
                DynamicIsland.Notify("Невидимый игрок", name + " в комнате, но без персонажа", false);
        }

        // Forget players who left the room.
        Scratch.Clear();
        foreach (int actor in MissingSince.Keys)
            if (room.GetPlayer(actor) == null) Scratch.Add(actor);
        foreach (int actor in Scratch) { MissingSince.Remove(actor); Announced.Remove(actor); }
    }

    public static string Status()
    {
        if (PhotonNetwork.CurrentRoom == null) return "  -> Не в комнате";
        if (Hidden.Count == 0) return "  -> Невидимых игроков нет";
        var sb = new StringBuilder("  -> Без персонажа: ");
        for (int i = 0; i < Hidden.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(Hidden[i]);
        }
        return sb.ToString();
    }
}
