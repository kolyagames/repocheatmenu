// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;

namespace RepoCheatMenu;

// R.E.P.O.'s chat is spoken, not shown: PlayerAvatar.ChatMessageSendRPC hands the text to the sender's
// text-to-speech voice and nothing keeps it. This keeps it:
//   - every message that really gets spoken (the same guard the game applies: master/owner sender and the
//     Main game state) goes to the event log ("chat") and to the last few lines in the menu;
//   - ChatToIsland also shows each one as an island notification, so a missed line can be read;
//   - MuteOthersTts stops other players' messages from being spoken on THIS machine (they are still
//     logged/shown) - for the TTS spam some lobbies have. Your own messages are always spoken.
// The hook is ChatLogPatch in Patches.cs (low priority, so it sees the text after the anti-crash clamp).
internal static class ChatLogHack
{
    private const int KeepInMenu = 8;
    private static readonly List<string> Recent = new();

    // Returns false when the game should NOT speak this message here.
    public static bool OnMessage(PlayerAvatar from, string message)
    {
        if (from == null || string.IsNullOrEmpty(message)) return true;
        bool local = from == PlayerTargets.Local;
        string name = local ? "Вы" : PlayerTargets.DisplayName(from);
        string text = message.Length > 120 ? message.Substring(0, 120) + "..." : message;

        Recent.Insert(0, $"[{DateTime.Now:HH:mm}] {name}: {text}");
        if (Recent.Count > KeepInMenu) Recent.RemoveAt(Recent.Count - 1);
        EventLog.Write("chat", $"{PlayerTargets.NameOf(from)}: {message}");

        if (CheatState.ChatToIsland.Value && !local)
            DynamicIsland.Notify(name, text, true);

        return local || !CheatState.MuteOthersTts.Value;
    }

    public static string Info()
    {
        if (Recent.Count == 0) return "Сообщений пока не было.";
        var sb = new StringBuilder();
        foreach (string line in Recent)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(line);
        }
        return sb.ToString();
    }
}
