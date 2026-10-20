// ============================================================================
// RepoInfiniteStrength - мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RepoInfiniteStrength;

[HarmonyPatch]
internal static class StrengthPatch
{
    private const string StatFieldName = "playerUpgradeStrength";
    private static FieldInfo _statField;
    private static Coroutine _watchdog;

    private static Dictionary<string, int> GetDict()
    {
        _statField ??= typeof(StatsManager).GetField(
            StatFieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return _statField?.GetValue(StatsManager.instance) as Dictionary<string, int>;
    }

    private static void ForceStrength()
    {
        if (StatsManager.instance == null || PlayerController.instance == null) return;

        Dictionary<string, int> dict = GetDict();
        if (dict == null) return;

        string steamId = PlayerController.instance.playerSteamID;
        int target = Plugin.StrengthLevel.Value;

        dict.TryGetValue(steamId, out int current);
        if (current == target) return;

        dict[steamId] = target;
        Plugin.Log.LogDebug($"Strength forced to {target} (was {current}).");
    }

    // Fires when the local player is registered for a level - apply immediately.
    [HarmonyPatch(typeof(StatsManager), nameof(StatsManager.PlayerAdd))]
    [HarmonyPostfix]
    private static void OnPlayerAdd(string _steamID)
    {
        if (PlayerController.instance == null || _steamID != PlayerController.instance.playerSteamID)
            return;

        ForceStrength();
        StartWatchdog();
    }

    // Host sync can overwrite local stats right after it lands - reassert ours.
    [HarmonyPatch(typeof(PunManager), nameof(PunManager.ReceiveSyncData))]
    [HarmonyPostfix]
    private static void OnSyncReceived(bool finalChunk)
    {
        if (!finalChunk) return;
        ForceStrength();
    }

    [HarmonyPatch(typeof(SemiFunc), nameof(SemiFunc.OnSceneSwitch))]
    [HarmonyPrefix]
    private static void OnSceneSwitch()
    {
        StopWatchdog();
    }

    private static void StartWatchdog()
    {
        StopWatchdog();
        _watchdog = Plugin.Instance.StartCoroutine(WatchdogLoop());
    }

    private static void StopWatchdog()
    {
        if (_watchdog != null)
        {
            Plugin.Instance.StopCoroutine(_watchdog);
            _watchdog = null;
        }
    }

    // Periodically re-asserts the value while in a level, in case something else changes it mid-run.
    private static IEnumerator WatchdogLoop()
    {
        while (SemiFunc.RunIsLevel())
        {
            ForceStrength();
            yield return new WaitForSeconds(0.5f);
        }

        _watchdog = null;
    }
}
