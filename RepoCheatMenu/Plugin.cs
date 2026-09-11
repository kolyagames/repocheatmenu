using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace RepoCheatMenu;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BaseUnityPlugin
{
    public const string Guid = "local.repocheatmenu";
    public const string Name = "RepoCheatMenu";
    public const string Version = "1.0.36";

    public static Plugin Instance { get; private set; }
    public static ManualLogSource Log;
    public static ConfigEntry<KeyCode> MenuKey;
    public static Harmony HarmonyInstance { get; private set; }

    private Harmony _harmony;

    private void Awake()
    {
        Instance = this;
        Log = Logger;

        MenuKey = Config.Bind("General", "MenuKey", KeyCode.RightShift, "Key to open/close the cheat menu.");

        CheatState.Init(Config);
        PacketTestHack.Init();
        ScriptModuleManager.LoadAll();

        _harmony = new Harmony(Guid);
        HarmonyInstance = _harmony;
        _harmony.PatchAll();

        foreach (var m in Harmony.GetAllPatchedMethods())
        {
            Log.LogInfo($"Patched method: {m.DeclaringType?.FullName}.{m.Name}");
        }

        Log.LogInfo($"{Name} loaded. Press {MenuKey.Value} in-game to open the menu.");

        EventLog.Write("session", $"{Name} {Version} loaded (menu key {MenuKey.Value}).");
        EventLog.Flush(); // so the session folder exists immediately, before any gameplay event
        Log.LogInfo($"Event logs: {EventLog.SessionDir}");
    }

    // Hotkey detection lives in Patches.cs (TimeTickPatch), piggybacking on UnityEngine.Time's
    // deltaTime getter via Harmony - this plugin's own BaseUnityPlugin.Update() was observed to
    // never fire in testing, so we don't rely on it here. The menu itself is an always-on
    // Canvas overlay (OverlayUI), not a MenuLib popup page, so it isn't gated by the game's own
    // menu/page state.

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
        PacketTestHack.Shutdown();
        EventLog.Write("session", "Plugin unloading - flushing logs.");
        EventLog.Flush(); // buffered events would otherwise be lost on quit
    }
}
