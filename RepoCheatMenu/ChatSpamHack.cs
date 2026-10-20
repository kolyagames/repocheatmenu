// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using UnityEngine;

namespace RepoCheatMenu;

// Sends phrases from CheatState.ChatSpamText into the real in-game chat via
// ChatManager.instance.ForceSendMessage - confirmed public in the decompiled source, the same
// method vanilla chat itself calls once you hit Enter (ForceConfirmChat -> StateSet(Send) ->
// next Update's StateSend() -> MessageSend()). MessageSend() has its own private spamTimer
// guard (1s) that silently drops a send attempt made before it expires and can't be bypassed
// from outside the class - so ChatSpamInterval only controls how often WE ask, the game's own
// guard is still the real floor on how fast messages actually go out.
internal static class ChatSpamHack
{
    private static float _nextSend;
    private static int _index;

    public static void Apply()
    {
        if (!CheatState.ChatSpam.Value) return;
        // Chat is PlayerAvatar.ChatMessageSend -> photonView.RPC on our avatar's view. Under
        // Ghost Observer our outgoing RPCs for that view are filtered, so a spam attempt would
        // only echo in our own chat and nobody else would see it. Skip silently.
        if (GhostObserverHack.SessionGhost) return;
        if (ChatManager.instance == null) return;
        if (Time.unscaledTime < _nextSend) return;

        string[] phrases = CheatState.ChatSpamText.Value.Split('|');
        if (phrases.Length == 0) return;

        string phrase = phrases[_index % phrases.Length].Trim();
        _index++;
        _nextSend = Time.unscaledTime + Mathf.Max(1f, CheatState.ChatSpamInterval.Value);
        if (phrase.Length == 0) return;

        if (CheatState.ChatSpamRichText.Value)
            phrase = $"<color={CheatState.ChatSpamColor.Value}><size={CheatState.ChatSpamFontSize.Value}>{phrase}</size></color>";

        ChatManager.instance.ForceSendMessage(phrase);
    }
}
