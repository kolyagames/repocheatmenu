// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using UnityEngine;

namespace RepoCheatMenu;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BaseUnityPlugin
{
    public const string Guid = "local.repocheatmenu";
    public const string Name = "RepoCheatMenu";
    public const string Version = "1.0.112";

    public static Plugin Instance { get; private set; }
    public static ManualLogSource Log;
    public static ConfigEntry<KeyCode> MenuKey;
    public static Harmony HarmonyInstance { get; private set; }

    // Hot reload (RepoCheatMenu.Loader). Ready is set as the very last line of Awake: Unity logs an
    // exception thrown in Awake instead of rethrowing it, so this flag is how the loader knows the
    // core really came up. HotReloadBlocker is polled by the loader on the RUNNING core before it
    // swaps it out - null means "fine to swap now", otherwise the reason it is not.
    public static bool Ready;

    public static string HotReloadBlocker
    {
        get
        {
            if (MultiRoomHack.IsRunning) return "Sibling Rooms is running";
            try
            {
                LevelGenerator generator = LevelGenerator.Instance;
                if (generator != null && !generator.Generated) return "level is generating";
                if (RunManager.instance != null && GameReflect.Get<bool>(RunManager.instance, "restarting"))
                    return "scene change in progress";
            }
            catch (Exception)
            {
                // A game-state read failing is not a reason to refuse the swap.
            }
            return null;
        }
    }

    private Harmony _harmony;

    private void Awake()
    {
        Instance = this;
        Log = Logger;

        MenuKey = Config.Bind("General", "MenuKey", KeyCode.RightShift, "Key to open/close the cheat menu.");

        CheatState.Init(Config);
        ConfigBackup.AutoBackup(); // one copy of the .cfg per day, see ConfigBackup.cs
        PacketTestHack.Init();
        ScriptModuleManager.LoadAll();
        BlockScripts.Init(Config);

        _harmony = new Harmony(Guid);
        HarmonyInstance = _harmony;
        _harmony.PatchAll();
        GhostObserverHack.InstallVoicePatches(_harmony);

        foreach (var m in Harmony.GetAllPatchedMethods())
        {
            Log.LogInfo($"Patched method: {m.DeclaringType?.FullName}.{m.Name}");
        }

        Log.LogInfo($"{Name} loaded. Press {MenuKey.Value} in-game to open the menu.");

        EventLog.Write("session", $"{Name} {Version} by Kolya loaded (menu key {MenuKey.Value}).");
        EventLog.Flush(); // so the session folder exists immediately, before any gameplay event
        Log.LogInfo($"Event logs: {EventLog.SessionDir}");

        Ready = true;
    }

    // Hotkey detection lives in Patches.cs (TimeTickPatch), piggybacking on UnityEngine.Time's
    // deltaTime getter via Harmony - this plugin's own BaseUnityPlugin.Update() was observed to
    // never fire in testing, so we don't rely on it here. The menu itself is an always-on
    // Canvas overlay (OverlayUI), not a MenuLib popup page, so it isn't gated by the game's own
    // menu/page state.

    // Runs on a normal game quit AND on every hot reload (the loader destroys the host object), so
    // it has to leave nothing of the old copy behind: the new copy starts from a clean slate.
    private void OnDestroy()
    {
        Ready = false;

        // A menu left open would keep the cursor unlocked and its canvas alive.
        try { OverlayUI.Close(); } catch (Exception) { }
        try { CustomOverlayUI.Hide(); } catch (Exception) { }
        try { OptimizationHack.Shutdown(); } catch (Exception) { }
        try { CustomFogHack.Shutdown(); } catch (Exception) { }

        _harmony?.UnpatchSelf();
        PacketTestHack.Shutdown();
        try { DestroyOwnObjects(); } catch (Exception) { /* quitting: the engine is already tearing everything down */ }

        if (Instance == this) Instance = null;
        EventLog.Write("session", "Plugin unloading - flushing logs.");
        EventLog.Flush(); // buffered events would otherwise be lost on quit
    }

    // Every object this mod creates is a root object named RepoCheatMenu_* (canvases, event systems,
    // the ESP/chams camera, the auto-farm path line, ...), plus the chams ghost. They are
    // DontDestroyOnLoad, so nothing else removes them. The host and the loader's own objects are
    // left alone - the loader owns those.
    private static void DestroyOwnObjects()
    {
        foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (go == null || !go.scene.IsValid() || go.transform.parent != null) continue;
            string n = go.name;
            bool ours = n.StartsWith("RepoCheatMenu_", StringComparison.Ordinal) || n == "EspChamsGhost";
            if (!ours || n == "RepoCheatMenu_Host" || n.StartsWith("RepoCheatMenu_Loader", StringComparison.Ordinal)) continue;
            Destroy(go);
        }
    }
}
