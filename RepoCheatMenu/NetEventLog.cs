// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System;
using System.Collections;
using System.Text;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;

namespace RepoCheatMenu;

// Read-only debug log of the raw Photon events that reach this client, written to
// RepoCheatMenu_Logs/<session>/network.log (and all.log). It changes nothing: it only looks at an event
// on its way through PhotonNetwork.OnEvent and records who sent it, what it carried and how it relates
// to us (are we the target, who is master).
//
// Why these codes: the game's host-side kick/ban raises custom events 123 / 124 (NetworkManager.KickPlayer
// / BanPlayer, received by everyone), the client-side handler only knows 199 ("You were kicked by the
// server"), and Photon's own CloseConnection is 203. Seeing which of them actually arrive, from whom and
// in what order, shows how the kick is carried out. 254 / 255 are Photon's Leave / Join, so the leave that
// follows a kick lines up in the same file. Every custom event below 200 is logged, since nothing else in
// the game uses that range.
internal static class NetEventLog
{
    private const byte CloseConnection = 203, EvLeave = 254, EvJoin = 255;
    private const int MaxValueChars = 160;

    // Called from the deferred PhotonNetwork.OnEvent prefix (Patches.cs). Never throws, never blocks the event.
    public static void Note(EventData e)
    {
        try
        {
            if (e == null || !CheatState.EventLogging.Value) return;
            byte code = e.Code;
            bool interesting = code < 200 || code == CloseConnection || code == EvLeave || code == EvJoin;
            if (!interesting) return;

            Room room = PhotonNetwork.CurrentRoom;
            Player sender = room?.GetPlayer(e.Sender);
            Player me = PhotonNetwork.LocalPlayer;
            Player master = PhotonNetwork.MasterClient;

            var sb = new StringBuilder(160);
            sb.Append("event ").Append(code).Append(' ').Append(Describe(code));
            sb.Append(" from ").Append(Who(e.Sender, sender));
            sb.Append(" | me=").Append(Who(me?.ActorNumber ?? -1, me));
            sb.Append(" master=").Append(Who(master?.ActorNumber ?? -1, master));
            sb.Append(" | ").Append(Dump(e.Parameters));

            // For the game's own kick/ban events, say outright whether the target is us.
            if ((code == 123 || code == 124) && me != null && FirstInt(e.Parameters, out int target))
                sb.Append(target == me.ActorNumber ? " | TARGET IS ME" : $" | target actor {target}");

            string line = sb.ToString();
            EventLog.Write("network", line);
            if (code == 123 || code == 124 || code == 199 || code == CloseConnection)
                Plugin.Log.LogInfo("[NetEvent] " + line);
        }
        catch
        {
            // a diagnostic must never get in the way of the game's own event handling
        }
    }

    private static string Describe(byte code) => code switch
    {
        123 => "(game: kick)",
        124 => "(game: ban)",
        199 => "(game: you were kicked)",
        CloseConnection => "(Photon: CloseConnection)",
        EvLeave => "(Photon: player left)",
        EvJoin => "(Photon: player joined)",
        _ => "",
    };

    private static string Who(int actor, Player p) =>
        p != null ? $"#{actor} \"{p.NickName}\"" : $"#{actor}";

    private static string Dump(ParameterDictionary parameters)
    {
        if (parameters == null) return "no data";
        var sb = new StringBuilder("data:");
        foreach (var kv in parameters)
        {
            sb.Append(' ').Append(kv.Key).Append('=');
            AppendValue(sb, kv.Value);
        }
        return sb.ToString();
    }

    private static void AppendValue(StringBuilder sb, object v)
    {
        if (v == null) { sb.Append("null"); return; }
        int start = sb.Length;
        if (v is string || !(v is IEnumerable en))
        {
            sb.Append(v);
        }
        else
        {
            sb.Append('[');
            bool first = true;
            foreach (object item in en)
            {
                if (!first) sb.Append(", ");
                first = false;
                AppendValue(sb, item);
                if (sb.Length - start > MaxValueChars) { sb.Append("…"); break; }
            }
            sb.Append(']');
        }
        if (sb.Length - start > MaxValueChars) sb.Length = start + MaxValueChars;
    }

    // The game sends the target's actor number as the first (only) element of the event content array.
    private static bool FirstInt(ParameterDictionary parameters, out int value)
    {
        value = 0;
        if (parameters == null) return false;
        foreach (var kv in parameters)
        {
            if (kv.Value is object[] arr && arr.Length > 0 && arr[0] is int i) { value = i; return true; }
            if (kv.Value is int single && kv.Key != 254) { value = single; return true; }
        }
        return false;
    }
}
