// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace RepoCheatMenu;

// Run journal: one line per finished level - level number and name, haul against the quota, extraction
// points, time spent, and who died to what - kept for the menu (last 5) and appended to
// RepoCheatMenu_Logs\run_history.txt, which survives between sessions.
//
// Deaths come from a postfix on PlayerAvatar.PlayerDeathRPC(int enemyIndex) (RunJournalDeathPatch in
// Patches.cs), which every client runs for every death; the killer is the game's own lookup
// SemiFunc.EnemyGetFromIndex -> EnemyParent.enemyName (-1 = not an enemy: a fall, a trap, an item...).
// With DeathRecap on, each death also pops an island notification "who - what killed them".
// A "level" is the life of one RoundDirector instance: it starts when one appears and is written out when
// it goes away or is replaced. Read-only, local, works for guest and host.
internal static class RunJournal
{
    private const int KeepInMenu = 5;

    private sealed class Entry
    {
        public int Level;
        public string Name = "";
        public float Start;
        public int Haul, Goal, PointsDone, Points, Lost;
        public readonly List<string> Deaths = new();
    }

    private static Entry _current;
    private static int _currentKey;
    private static readonly List<string> Recent = new();

    private static string HistoryPath =>
        Path.Combine(BepInEx.Paths.PluginPath, "RepoCheatMenu_Logs", "run_history.txt");

    // ApplyStats tick.
    public static void Apply()
    {
        RoundDirector rd = RoundDirector.instance;
        int key = rd != null ? rd.GetInstanceID() : 0;

        if (key != _currentKey)
        {
            Finish();
            _currentKey = key;
            if (rd != null) Begin();
        }
        if (_current == null || rd == null) return;

        // Keep the last values seen: by the time the level is torn down they are gone.
        _current.Haul = GameReflect.Get<int>(rd, "currentHaul");
        _current.Goal = GameReflect.Get<int>(rd, "haulGoal");
        _current.PointsDone = GameReflect.Get<int>(rd, "extractionPointsCompleted");
        _current.Points = GameReflect.Get<int>(rd, "extractionPoints");
        _current.Lost = Mathf.RoundToInt(ValuableDamageHack.LostThisLevel);
    }

    private static void Begin()
    {
        _current = new Entry { Start = Time.unscaledTime };
        ValuableDamageHack.ResetLevel();
        RunManager rm = RunManager.instance;
        if (rm != null)
        {
            _current.Level = GameReflect.Get<int>(rm, "levelsCompleted") + 1;
            Level level = GameReflect.Get<Level>(rm, "levelCurrent");
            if (level != null)
                _current.Name = !string.IsNullOrEmpty(level.NarrativeName) ? level.NarrativeName : level.name.Replace("Level - ", "");
        }
    }

    private static void Finish()
    {
        Entry e = _current;
        _current = null;
        if (e == null) return;
        // Lobby, shop and the like have no quota: nothing worth a line.
        if (e.Goal <= 0 && e.Deaths.Count == 0) return;

        int secs = Mathf.Max(0, (int)(Time.unscaledTime - e.Start));
        var sb = new StringBuilder();
        sb.Append($"Ур. {e.Level}");
        if (e.Name.Length > 0) sb.Append(' ').Append(e.Name);
        if (e.Goal > 0) sb.Append($": ${e.Haul} / ${e.Goal}").Append(e.Haul >= e.Goal ? " (квота)" : "");
        if (e.Points > 0) sb.Append($", точки {e.PointsDone}/{e.Points}");
        sb.Append($", {secs / 60}:{secs % 60:00}");
        if (e.Lost > 0) sb.Append($", поломки -${e.Lost}");
        sb.Append(e.Deaths.Count == 0 ? ", без смертей" : $", смерти: {string.Join("; ", e.Deaths)}");
        string line = sb.ToString();

        Recent.Insert(0, line);
        if (Recent.Count > KeepInMenu) Recent.RemoveAt(Recent.Count - 1);
        EventLog.Write("run", line);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(HistoryPath));
            File.AppendAllText(HistoryPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm}] {line}{Environment.NewLine}", new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning("RunJournal: could not write history: " + ex.Message);
        }
    }

    // From RunJournalDeathPatch, on every client, for every death.
    public static void OnDeath(PlayerAvatar who, int enemyIndex)
    {
        if (who == null) return;
        string killer = "не враг (падение, ловушка, предмет)";
        try
        {
            Enemy enemy = SemiFunc.EnemyGetFromIndex(enemyIndex);
            EnemyParent parent = enemy != null ? GameReflect.Get<EnemyParent>(enemy, "EnemyParent") : null;
            string name = parent != null ? GameReflect.Get<string>(parent, "enemyName") : null;
            if (!string.IsNullOrEmpty(name)) killer = name;
        }
        catch (Exception) { /* enemy list mid-change: keep the generic cause */ }

        bool local = GameReflect.Get<bool>(who, "isLocal");
        string victim = local ? "Вы" : PlayerTargets.DisplayName(who);
        _current?.Deaths.Add($"{victim} - {killer}");
        EventLog.Write("run", $"death: {victim} - {killer}");

        if (CheatState.DeathRecap.Value)
            DynamicIsland.Notify(victim, "Погиб: " + killer, false);
    }

    public static string Info()
    {
        var sb = new StringBuilder();
        if (_current != null)
        {
            int secs = Mathf.Max(0, (int)(Time.unscaledTime - _current.Start));
            sb.Append($"Сейчас: ур. {_current.Level} {_current.Name}, {secs / 60}:{secs % 60:00}, смертей {_current.Deaths.Count}");
        }
        if (Recent.Count == 0)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append("Законченных уровней в этой сессии пока нет.");
        }
        foreach (string line in Recent)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(line);
        }
        return sb.ToString();
    }

    public static void OpenHistory()
    {
        try
        {
            if (!File.Exists(HistoryPath)) { DynamicIsland.Notify("Журнал", "Файл появится после первого уровня", false); return; }
            Application.OpenURL("file:///" + HistoryPath.Replace('\\', '/'));
        }
        catch (Exception e) { DynamicIsland.Notify("Журнал", "Не открылся: " + e.Message, false); }
    }
}
