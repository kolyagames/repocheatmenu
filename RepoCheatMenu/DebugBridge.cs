// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System;
using System.IO;
using BepInEx;
using UnityEngine;

namespace RepoCheatMenu;

// File-polling remote-execution bridge for autonomous testing from outside the game - the coding
// assistant writes a C# snippet to command.txt, this runs it through the SAME ScriptEngine/
// ScriptGlobals the Scripts tab already uses (full access to CheatState, PlayerAvatar, Photon,
// etc.), and writes the result to result.txt. Lets the assistant query live internal state
// (config values, menu open/closed, exceptions) directly instead of relying on a screenshot or a
// human relaying what they see - a real data channel, not a vision problem.
//
// Off by default (CheatState.DebugBridgeEnabled) - this is arbitrary code execution, gated the
// same way every other risky feature in this mod is. Not a new attack surface in practice: writing
// the command file requires the same local filesystem access the person running this development
// session already has, which already implies full control of the machine - the toggle exists so
// it never runs unless a session was deliberately given permission to drive it.
internal static class DebugBridge
{
    private static string BridgeDir => Path.Combine(Paths.PluginPath, "RepoCheatMenu_Bridge");
    private static string CommandPath => Path.Combine(BridgeDir, "command.txt");
    private static string ResultPath => Path.Combine(BridgeDir, "result.txt");

    // Was a copy of the last command's TEXT, to avoid re-running it if the delete below failed.
    // That silently broke polling: sending the same query twice in a row (the normal way to watch
    // a value change over time) had every repeat after the first discarded as a "duplicate", so
    // the caller just saw timeouts. The file's own write time is the honest freshness signal -
    // identical text written again is a genuinely new request.
    private static DateTime _lastCommandStamp = DateTime.MinValue;
    private static float _nextPoll;
    private static int _sequence;

    public static void Apply()
    {
        if (!CheatState.DebugBridgeEnabled.Value) return;
        if (Time.unscaledTime < _nextPoll) return;
        _nextPoll = Time.unscaledTime + 0.25f;

        string path = CommandPath;
        if (!File.Exists(path)) return;

        string code;
        DateTime stamp;
        try
        {
            stamp = File.GetLastWriteTimeUtc(path);
            code = File.ReadAllText(path);
        }
        catch { return; } // file mid-write from the other side - try again next poll

        if (string.IsNullOrWhiteSpace(code) || stamp <= _lastCommandStamp) return;
        _lastCommandStamp = stamp;

        try { File.Delete(path); }
        catch (Exception e) { Plugin.Log.LogWarning("[DebugBridge] Failed to clear command file: " + e); }

        int seq = ++_sequence;
        Plugin.Log.LogInfo($"[DebugBridge] Running command #{seq}.");
        WriteResult(seq, "running", "");

        EventLog.Write("bridge", $"#{seq} run: {code.Replace("\r", "").Replace("\n", " | ")}");

        ScriptEngine.StartRun(code, result =>
        {
            string body = result.Success ? (result.ReturnValue ?? "") : result.Message;
            WriteResult(seq, result.Success ? "ok" : "error", body);
            Plugin.Log.LogInfo($"[DebugBridge] Command #{seq} finished: {(result.Success ? "ok" : "error")}");
            EventLog.Write("bridge", $"#{seq} {(result.Success ? "ok" : "error")}: {body.Replace("\n", " | ")}");
        });
    }

    private static void WriteResult(int seq, string status, string body)
    {
        try
        {
            Directory.CreateDirectory(BridgeDir);
            File.WriteAllText(ResultPath, $"seq={seq}\nstatus={status}\n{body}");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[DebugBridge] Failed to write result file: " + e);
        }
    }
}
