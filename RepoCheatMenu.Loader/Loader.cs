// ============================================================================
// RepoCheatMenu.Loader - загрузчик ядра RepoCheatMenu
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;

namespace RepoCheatMenu.Loader;

// Hot-reload host for RepoCheatMenu. This DLL is the only part that is loaded the normal BepInEx
// way and can never be swapped while the game runs, so it is kept tiny and knows nothing about the
// game. The real mod ("core") lives next to it as RepoCheatMenu.core - not .dll, because
// BepInEx's chainloader only scans *.dll and would otherwise start a second copy of the mod.
//
// What it does (the same pattern as BepInEx's own ScriptEngine plugin):
//   * boot: read RepoCheatMenu.core into memory, Assembly.Load(bytes) (so the file on disk is never
//     held open and can be replaced at any time), add the core's Plugin type to a hidden object;
//   * watch: poll the file once a second; a change counts only once size+timestamp stayed the same
//     for a full poll, so a half-copied file is never picked up (the deploy script also writes to a
//     temp name and renames, so this is a second line of defence);
//   * swap, in this order, so a bad build can never leave the player without the mod:
//       1. pre-flight the new bytes (loads, has the Plugin type, has the Ready flag) BEFORE touching
//          the running copy - a corrupt or incompatible file is rejected and the old one keeps running;
//       2. wait for a safe moment (the running core reports a blocker such as "level is generating");
//       3. destroy the old host - the core's OnDestroy unpatches Harmony and removes its objects;
//       4. start the new one; if its Awake fails, destroy it and start the last known-good bytes again.
//   * report: hot-status.json next to this DLL says what happened (deploy script reads it).
[BepInPlugin("local.repocheatmenu.loader", "RepoCheatMenu Loader", "1.0.0")]
public class LoaderPlugin : BaseUnityPlugin
{
    private const string CoreFileName = "RepoCheatMenu.core";
    private const string CoreGuid = "local.repocheatmenu";
    private const string CoreTypeName = "RepoCheatMenu.Plugin";
    private const string HostName = "RepoCheatMenu_Host";
    private const float PollSeconds = 1f;
    private const float MaxWaitForSafeMomentSeconds = 90f;

    private struct Stamp : IEquatable<Stamp>
    {
        public long Length;
        public long WriteTicks;
        public bool Equals(Stamp o) => Length == o.Length && WriteTicks == o.WriteTicks;
        public override bool Equals(object obj) => obj is Stamp s && Equals(s);
        public override int GetHashCode() => (int)(Length ^ WriteTicks);
        public static bool operator ==(Stamp a, Stamp b) => a.Equals(b);
        public static bool operator !=(Stamp a, Stamp b) => !a.Equals(b);
    }

    // Lives on its own object: BaseUnityPlugin.Update was observed not to fire in this game, so the
    // loader does not depend on it.
    private sealed class Ticker : MonoBehaviour
    {
        public LoaderPlugin Owner;
        private void Update() => Owner?.Tick("Update");
    }

    private string _dir;
    private string _corePath;
    private string _statusPath;

    private GameObject _host;
    private Type _hostType;
    private byte[] _goodBytes;
    private Stamp _loadedStamp;
    private Stamp _pendingStamp;
    private bool _hasPending;
    private float _nextPoll;
    private int _lastTickFrame = -1;
    private bool _busy;
    private bool _disabled;
    private int _reloadCount;
    private string _tickSource = "?";
    private Ticker _ticker;

    private void Awake()
    {
        _dir = Path.GetDirectoryName(Info.Location);
        _corePath = Path.Combine(_dir, CoreFileName);
        _statusPath = Path.Combine(_dir, "hot-status.json");

        var tickerGo = new GameObject("RepoCheatMenu_LoaderTicker");
        DontDestroyOnLoad(tickerGo);
        tickerGo.hideFlags = HideFlags.HideAndDontSave;
        _ticker = tickerGo.AddComponent<Ticker>();
        _ticker.Owner = this;

        // Second tick source in case Update does not run either; whichever fires first wins the frame.
        Application.onBeforeRender += OnBeforeRender;

        _ticker.StartCoroutine(Boot());
    }

    private void OnBeforeRender() => Tick("onBeforeRender");

    private void OnDestroy()
    {
        Application.onBeforeRender -= OnBeforeRender;
    }

    // --- boot ---------------------------------------------------------------------------------

    private IEnumerator Boot()
    {
        // One frame: by then the chainloader has instantiated every normal plugin, so a core that
        // was installed the old way (RepoCheatMenu.dll in the plugins folder) is visible.
        yield return null;

        if (Chainloader.PluginInfos.ContainsKey(CoreGuid))
        {
            _disabled = true;
            Status("conflict", "The mod is also installed as a normal plugin (RepoCheatMenu.dll). " +
                "Remove that file; the loader stays idle so the mod is not started twice.");
            yield break;
        }
        if (!File.Exists(_corePath))
        {
            Status("no-core", CoreFileName + " not found next to the loader.");
            yield break;
        }

        byte[] bytes;
        Stamp stamp = ReadStamp();
        if (!TryReadFile(out bytes))
        {
            Status("failed", "Could not read " + CoreFileName + ".");
            yield break;
        }
        if (!Preflight(bytes, out Assembly asm, out Type type, out string err))
        {
            Status("failed", "Core rejected: " + err);
            _loadedStamp = stamp;
            yield break;
        }
        if (!StartHost(type, out err))
        {
            DestroyHost();
            Status("failed", "Core failed to start: " + err);
            _loadedStamp = stamp;
            yield break;
        }

        _goodBytes = bytes;
        _loadedStamp = stamp;
        Status("loaded", "Core started.");
    }

    // --- watching -----------------------------------------------------------------------------

    private void Tick(string source)
    {
        if (_disabled || _busy) return;
        if (_lastTickFrame == Time.frameCount) return;
        _lastTickFrame = Time.frameCount;
        if (_tickSource != source) _tickSource = source;

        float now = Time.realtimeSinceStartup;
        if (now < _nextPoll) return;
        _nextPoll = now + PollSeconds;

        if (_goodBytes == null && _loadedStamp == default) return; // boot not finished yet
        if (!File.Exists(_corePath)) return;

        Stamp stamp = ReadStamp();
        if (stamp == _loadedStamp)
        {
            _hasPending = false;
            return;
        }
        if (!_hasPending || stamp != _pendingStamp)
        {
            // Changed, but might still be being written - confirm on the next poll.
            _pendingStamp = stamp;
            _hasPending = true;
            return;
        }

        _hasPending = false;
        if (!TryReadFile(out byte[] bytes)) return; // locked right now - try again at the next poll
        _ticker.StartCoroutine(Swap(bytes, stamp));
    }

    private IEnumerator Swap(byte[] bytes, Stamp stamp)
    {
        _busy = true;
        try
        {
            // 1. pre-flight: nothing running is touched yet.
            if (!Preflight(bytes, out Assembly asm, out Type type, out string err))
            {
                _loadedStamp = stamp; // do not retry the same broken file every second
                Status("failed", "New build rejected, old one keeps running: " + err);
                yield break;
            }

            // 2. wait for a moment when swapping is safe.
            float deadline = Time.realtimeSinceStartup + MaxWaitForSafeMomentSeconds;
            while (true)
            {
                string blocker = Blocker();
                if (blocker == null) break;
                if (Time.realtimeSinceStartup > deadline)
                {
                    Status("failed", "Still blocked (" + blocker + ") after " + (int)MaxWaitForSafeMomentSeconds +
                        " s - not reloaded, will retry.");
                    yield break; // _loadedStamp untouched, so the next poll tries again
                }
                Status("waiting", blocker);
                yield return new WaitForSecondsRealtime(0.5f);
            }

            // 3. stop the old core. Destroy runs at the end of the frame (that is when the core's
            //    OnDestroy unpatches Harmony and removes its objects); two frames of margin.
            DestroyHost();
            yield return null;
            yield return null;

            // 4. start the new core, or roll back to the last good bytes.
            if (StartHost(type, out err))
            {
                _goodBytes = bytes;
                _loadedStamp = stamp;
                _reloadCount++;
                Status("reloaded", "Hot-swapped.");
                yield break;
            }

            DestroyHost();
            yield return null;
            yield return null;
            string failure = err;
            _loadedStamp = stamp;

            if (_goodBytes != null && Preflight(_goodBytes, out Assembly goodAsm, out Type goodType, out err)
                && StartHost(goodType, out err))
                Status("rolled-back", "New build failed to start (" + failure + "); the previous build is running again.");
            else
                Status("dead", "New build failed to start (" + failure + ") and the rollback failed too: " + err);
        }
        finally
        {
            _busy = false;
        }
    }

    // --- core lifecycle -----------------------------------------------------------------------

    // Loads the bytes as a fresh assembly and checks it is something this loader can host. Does not
    // start anything.
    private bool Preflight(byte[] bytes, out Assembly asm, out Type type, out string error)
    {
        asm = null;
        type = null;
        error = null;
        try
        {
            asm = Assembly.Load(bytes);
            type = asm.GetType(CoreTypeName, false);
            if (type == null) { error = "type " + CoreTypeName + " not found"; return false; }
            if (!typeof(BaseUnityPlugin).IsAssignableFrom(type)) { error = "core Plugin is not a BaseUnityPlugin"; return false; }
            if (type.GetField("Ready", BindingFlags.Public | BindingFlags.Static) == null)
            {
                error = "core has no static Ready flag (build is too old for hot reload)";
                return false;
            }
            return true;
        }
        catch (Exception e)
        {
            error = e.GetType().Name + ": " + e.Message;
            return false;
        }
    }

    private bool StartHost(Type type, out string error)
    {
        error = null;
        var go = new GameObject(HostName);
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        _host = go;
        _hostType = type;
        try
        {
            go.AddComponent(type); // Awake runs right here
        }
        catch (Exception e)
        {
            error = e.GetType().Name + ": " + e.Message;
            return false;
        }

        // Unity logs an exception thrown in Awake instead of rethrowing it, so the core says it got
        // all the way through by setting Plugin.Ready at the very end of Awake.
        FieldInfo ready = type.GetField("Ready", BindingFlags.Public | BindingFlags.Static);
        if (ready == null || !(ready.GetValue(null) is bool ok) || !ok)
        {
            error = "core Awake did not finish (see the BepInEx log)";
            return false;
        }
        return true;
    }

    private void DestroyHost()
    {
        if (_host != null) Destroy(_host);
        _host = null;
    }

    // Asks the running core whether now is a bad moment (level generating, scene change, a long
    // operation of its own in progress). A core that cannot answer never blocks the swap.
    private string Blocker()
    {
        try
        {
            PropertyInfo p = _hostType?.GetProperty("HotReloadBlocker", BindingFlags.Public | BindingFlags.Static);
            return p?.GetValue(null, null) as string;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // --- file + status helpers ----------------------------------------------------------------

    private Stamp ReadStamp()
    {
        try
        {
            var fi = new FileInfo(_corePath);
            return new Stamp { Length = fi.Length, WriteTicks = fi.LastWriteTimeUtc.Ticks };
        }
        catch (Exception)
        {
            return default;
        }
    }

    private bool TryReadFile(out byte[] bytes)
    {
        bytes = null;
        try
        {
            using var fs = new FileStream(_corePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            bytes = new byte[fs.Length];
            int read = 0;
            while (read < bytes.Length)
            {
                int n = fs.Read(bytes, read, bytes.Length - read);
                if (n <= 0) break;
                read += n;
            }
            return read == bytes.Length && bytes.Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void Status(string state, string message)
    {
        string version = "?";
        try
        {
            FieldInfo v = _hostType?.GetField("Version", BindingFlags.Public | BindingFlags.Static);
            if (v != null) version = (v.IsLiteral ? v.GetRawConstantValue() : v.GetValue(null)) as string ?? "?";
        }
        catch (Exception) { }

        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append("  \"state\": ").Append(Json(state)).Append(",\n");
        sb.Append("  \"message\": ").Append(Json(message)).Append(",\n");
        sb.Append("  \"coreVersion\": ").Append(Json(version)).Append(",\n");
        sb.Append("  \"reloadCount\": ").Append(_reloadCount).Append(",\n");
        sb.Append("  \"tickSource\": ").Append(Json(_tickSource)).Append(",\n");
        sb.Append("  \"time\": ").Append(Json(DateTime.UtcNow.ToString("o"))).Append("\n");
        sb.Append("}\n");
        try { File.WriteAllText(_statusPath, sb.ToString(), Encoding.UTF8); }
        catch (Exception) { }

        Logger.LogInfo("[" + state + "] " + message);
    }

    private static string Json(string s)
    {
        if (s == null) return "null";
        var sb = new StringBuilder("\"");
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c < 32 ? ' ' : c); break;
            }
        }
        return sb.Append('"').ToString();
    }
}
