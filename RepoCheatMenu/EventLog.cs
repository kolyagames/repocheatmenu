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
using BepInEx;
using UnityEngine;

namespace RepoCheatMenu;

// Own structured event log, split by category into RepoCheatMenu_Logs/<session>/<category>.log
// next to this plugin's DLL - separate from BepInEx's single LogOutput.log, which interleaves
// every mod in the profile (Potato Pc Mod, GameTools, the FPS counter, ...) into one stream that's
// painful to read back afterwards. Each line is timestamped and carries the concrete details worth
// having later: what spawned and at which coordinates, who got teleported from where to where,
// what got taken out of whose hands.
//
// Writes are buffered and flushed on a timer (FlushIntervalSeconds) rather than hitting the disk
// per event, because some of these fire from per-frame tick hacks - a File.AppendAllText per call
// would be a real frame-time cost for something that's only ever read after the fact.
internal static class EventLog
{
    private const float FlushIntervalSeconds = 2f;

    // One folder per game session so runs never blend together - that's the "sorted" half of this,
    // the per-category files being the other.
    private static string _sessionDir;
    private static readonly Dictionary<string, StringBuilder> _buffers = new();
    private static float _nextFlush;

    public static string SessionDir => _sessionDir;

    private static string EnsureSessionDir()
    {
        if (_sessionDir != null) return _sessionDir;

        string root = Path.Combine(Paths.PluginPath, "RepoCheatMenu_Logs");
        _sessionDir = Path.Combine(root, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        try { Directory.CreateDirectory(_sessionDir); }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("EventLog: could not create log folder: " + e.Message);
            _sessionDir = root; // still try to write flat rather than losing events entirely
        }
        return _sessionDir;
    }

    // category becomes the file name - keep it short and stable ("spawn", "teleport", "items",
    // "players", "session"), since that's what makes the folder readable at a glance later.
    public static void Write(string category, string message)
    {
        if (!CheatState.EventLogging.Value) return;

        string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        lock (_buffers)
        {
            if (!_buffers.TryGetValue(category, out StringBuilder sb))
                _buffers[category] = sb = new StringBuilder();
            sb.AppendLine(line);

            // "all.log" keeps the same events in one chronological stream, for when you don't yet
            // know which category the thing you're chasing lives in.
            if (category != "all")
            {
                if (!_buffers.TryGetValue("all", out StringBuilder allSb))
                    _buffers["all"] = allSb = new StringBuilder();
                allSb.AppendLine($"[{category}] {line}");
            }
        }
    }

    // Vector3.ToString() only gives one decimal ("(1.0, 2.0, 3.0)") - coordinates are the whole
    // point of most of these lines, so they get two and no surrounding noise.
    public static string Pos(Vector3 v) => $"{v.x:0.##},{v.y:0.##},{v.z:0.##}";

    // Called from the per-frame tick (Patches.ApplyStats) - the actual disk write happens here,
    // off the event's own call path.
    public static void Apply()
    {
        if (Time.unscaledTime < _nextFlush) return;
        _nextFlush = Time.unscaledTime + FlushIntervalSeconds;
        Flush();
    }

    public static void Flush()
    {
        Dictionary<string, string> pending = null;
        lock (_buffers)
        {
            foreach (KeyValuePair<string, StringBuilder> kv in _buffers)
            {
                if (kv.Value.Length == 0) continue;
                (pending ??= new Dictionary<string, string>())[kv.Key] = kv.Value.ToString();
                kv.Value.Clear();
            }
        }
        if (pending == null) return;

        string dir = EnsureSessionDir();
        foreach (KeyValuePair<string, string> kv in pending)
        {
            try { File.AppendAllText(Path.Combine(dir, kv.Key + ".log"), kv.Value); }
            catch (Exception e) { Plugin.Log.LogWarning($"EventLog: write to {kv.Key}.log failed: {e.Message}"); }
        }
    }
}
