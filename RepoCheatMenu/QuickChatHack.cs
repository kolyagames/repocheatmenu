// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace RepoCheatMenu;

// Quick chat phrases: four editable lines, each sent to the normal in-game chat with one button or one key
// (middle-click the row to bind it). Goes through ChatManager.ForceSendMessage, the same public method the
// game's own chat calls after Enter (see ChatSpamHack), so everyone sees an ordinary chat message.
// The game drops a message sent within 1 s of the previous one, so a second press inside that window is
// ignored here instead of silently vanishing. Works for guest and host.
//
// Placeholders are filled in at the moment of sending, from state the game already has on this client:
//   {hp} {maxhp} - your health      {stamina} - stamina, %
//   {haul} {quota} - haul / quota    {points} - extraction points done/total
//   {near} - metres to the nearest extraction point     {level} - level number
//   {loot} - valuables left on the map
internal static class QuickChatHack
{
    public const string PlaceholderHelp = "Подстановки: {hp} {maxhp} {stamina} {haul} {quota} {points} {near} {level} {loot}";

    private static float _nextSend;

    public static void Send1() => Send(CheatState.QuickPhrase1.Value);
    public static void Send2() => Send(CheatState.QuickPhrase2.Value);
    public static void Send3() => Send(CheatState.QuickPhrase3.Value);
    public static void Send4() => Send(CheatState.QuickPhrase4.Value);

    private static void Send(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        ChatManager chat = ChatManager.instance;
        if (chat == null)
        {
            DynamicIsland.Notify("Быстрые фразы", "Чат недоступен (вы не в игре)", false);
            return;
        }
        if (Time.unscaledTime < _nextSend) return;
        _nextSend = Time.unscaledTime + 1.1f;
        chat.ForceSendMessage(Expand(text.Trim()));
    }

    public static string Expand(string text)
    {
        if (text.IndexOf('{') < 0) return text;

        PlayerAvatar me = PlayerTargets.Local;
        PlayerHealth health = me != null ? GameReflect.Get<PlayerHealth>(me, "playerHealth") : null;
        PlayerController pc = PlayerController.instance;
        RoundDirector rd = RoundDirector.instance;
        RunManager rm = RunManager.instance;

        var values = new Dictionary<string, string>
        {
            ["{hp}"] = health != null ? GameReflect.Get<int>(health, "health").ToString() : "?",
            ["{maxhp}"] = health != null ? GameReflect.Get<int>(health, "maxHealth").ToString() : "?",
            ["{stamina}"] = pc != null && pc.EnergyStart > 0f ? Mathf.RoundToInt(pc.EnergyCurrent / pc.EnergyStart * 100f) + "%" : "?",
            ["{haul}"] = rd != null ? "$" + GameReflect.Get<int>(rd, "currentHaul") : "?",
            ["{quota}"] = rd != null ? "$" + GameReflect.Get<int>(rd, "haulGoal") : "?",
            ["{points}"] = rd != null ? $"{GameReflect.Get<int>(rd, "extractionPointsCompleted")}/{GameReflect.Get<int>(rd, "extractionPoints")}" : "?",
            ["{level}"] = rm != null ? (GameReflect.Get<int>(rm, "levelsCompleted") + 1).ToString() : "?",
            ["{near}"] = NearestExtraction(me, rd),
            ["{loot}"] = LootLeft(),
        };
        foreach (var kv in values)
            text = text.Replace(kv.Key, kv.Value);
        return text;
    }

    private static string NearestExtraction(PlayerAvatar me, RoundDirector rd)
    {
        List<GameObject> points = rd != null ? GameReflect.Get<List<GameObject>>(rd, "extractionPointList") : null;
        if (me == null || points == null) return "?";
        float best = float.MaxValue;
        foreach (GameObject go in points)
            if (go != null) best = Mathf.Min(best, Vector3.Distance(me.transform.position, go.transform.position));
        return best < float.MaxValue ? $"{best:0} м" : "?";
    }

    private static string LootLeft()
    {
        ValuableDirector director = ValuableDirector.instance;
        List<ValuableObject> list = director != null ? GameReflect.Get<List<ValuableObject>>(director, "valuableList") : null;
        if (list == null) return "?";
        int n = 0;
        foreach (ValuableObject v in list) if (v != null) n++;
        return n.ToString();
    }
}
