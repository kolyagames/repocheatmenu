using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace RepoInfiniteStrength;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BaseUnityPlugin
{
    public const string Guid = "local.repoinfinitestrength";
    public const string Name = "InfiniteStrength";
    public const string Version = "1.0.0";

    public static Plugin Instance { get; private set; }
    public static ManualLogSource Log;
    public static ConfigEntry<int> StrengthLevel;

    private Harmony _harmony;

    private void Awake()
    {
        Instance = this;
        Log = Logger;

        StrengthLevel = Config.Bind(
            "General",
            "StrengthLevel",
            20,
            "Strength upgrade level forced every time you load into a level, skipping the shop entirely. " +
            "Written only to your own local stat (no network broadcast), so it isn't overwritten by the host.");

        _harmony = new Harmony(Guid);
        _harmony.PatchAll(typeof(StrengthPatch));

        Log.LogInfo($"{Name} loaded - forcing strength to {StrengthLevel.Value}.");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}
