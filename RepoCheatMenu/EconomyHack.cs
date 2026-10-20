// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace RepoCheatMenu;

// Mirrors the game's own "/spawn valuable <name>" debug console command (DebugCommandHandler.cs,
// confirmed in the decompiled source) - that command itself is only gated to host/singleplayer
// by the console's own UI enable-check, not by anything server-side. The actual spawn call rides
// NetSpawn.Spawn (see that file's comment - PhotonNetwork.InstantiateRoomObject IS master-only,
// PUN itself rejects it for a guest and logs an error; an earlier version of this file's comment
// claimed the opposite and was wrong), so this works for guest or host.
//
// The game has no dedicated "coin" object at all - "coins" only exists as a particle-effect field
// name on SurplusValuable (confirmed absent as a real prefab in the decompiled source), so instead
// of grabbing whatever valuable happens to be first in an unsorted list, this looks a valuable up
// by NAME (SpawnValuableName below), exactly like the console command does - pick whichever one
// actually looks like what you want from the list (ScanValuableNames), including a coin/chip-stack
// -looking one if this level's presets have one.
//
// Also searches ALL of RunManager.instance.levels, not just levelCurrent, matching the console
// command's own search scope exactly (confirmed in the decompiled source) - the old current-level-
// only search could come up completely empty depending on which level's presets happened to be
// thin, which is the likely reason this silently did nothing before.
//
// Directly writing run currency (SemiFunc.StatSetRunCurrency) is master-gated server-side with
// no unvalidated RPC loophole - confirmed in PunManager.SetRunStatSet, SetRunStatRPC re-checks
// SemiFunc.MasterOnlyRPC itself, so there's no guest-safe way to just add cash to the stat
// directly. Spawning a real valuable sidesteps that entirely: ValuableObject.Update() (confirmed
// in the decompiled source) adds itself to RoundDirector.dollarHaulList the moment its
// roomVolumeCheck reports it's sitting inside a room flagged Extraction - and that check runs
// unconditionally on whichever client satisfies "gameMode == 0 || PhotonNetwork.IsMasterClient",
// i.e. the MASTER's own replicated copy of this object, regardless of who spawned it. So spawning
// it directly inside an active extraction point's area instead of next to the player makes the
// master's own client credit it into the current haul total within half a second - same as if
// your team had physically carried it in there. It still only counts once the extraction sequence
// actually completes, same as any real loot - there's no way to skip that part, it's not a bug.
//
// The dollar value itself still isn't custom-settable: ValuableObject has no OnPhotonSerializeView,
// so DollarValueSetLogic() called here only sets the value on our own local copy; every client's
// own DollarValueSet() coroutine runs independently, and only the master's branch broadcasts a
// value via DollarValueSetRPC, overwriting ours moments later with its own randomly-rolled
// amount from the same valuePreset range every other copy of that valuable already uses.
internal static class EconomyHack
{
    // Same source/scope the console command's own autocomplete uses.
    public static List<string> ScanValuableNames()
    {
        List<Level> levels = RunManager.instance != null ? RunManager.instance.levels : null;
        if (levels == null) return new List<string>();

        return levels
            .Where(l => l != null && l.ValuablePresets != null)
            .SelectMany(l => l.ValuablePresets)
            .Where(p => p != null)
            .SelectMany(AllPrefabs)
            .Where(x => x != null && x.IsValid())
            .Select(x => Regex.Replace(x.PrefabName, "^Valuable ", ""))
            .Distinct()
            .OrderBy(n => n)
            .ToList();
    }

    public static void SpawnValuable() => SpawnValuable(CheatState.SpawnValuableName.Value);

    public static void SpawnValuable(string wanted)
    {
        if (string.IsNullOrEmpty(wanted) || wanted == "None")
        {
            Plugin.Log.LogInfo("Spawn Valuable had no effect - pick a valuable name first.");
            return;
        }

        List<Level> levels = RunManager.instance != null ? RunManager.instance.levels : null;
        if (levels == null)
        {
            Plugin.Log.LogInfo("Spawn Valuable had no effect - RunManager not ready.");
            return;
        }

        PrefabRef prefab = levels
            .Where(l => l != null && l.ValuablePresets != null)
            .SelectMany(l => l.ValuablePresets)
            .Where(p => p != null)
            .SelectMany(AllPrefabs)
            .FirstOrDefault(x => x != null && x.IsValid() && Regex.Replace(x.PrefabName, "^Valuable ", "") == wanted);

        if (prefab == null)
        {
            Plugin.Log.LogInfo($"Spawn Valuable had no effect - no valuable prefab matched \"{wanted}\".");
            return;
        }

        PlayerController controller = PlayerController.instance;
        if (controller == null)
        {
            Plugin.Log.LogInfo("Spawn Valuable had no effect - no local player.");
            return;
        }

        Vector3 playerPos = controller.transform.position;
        ExtractionPoint extraction = FindActiveExtractionPoint(playerPos) ?? SemiFunc.ExtractionPointGetNearest(playerPos);

        Vector3 position;
        Quaternion rotation;
        if (extraction != null && extraction.extractionArea != null)
        {
            position = extraction.extractionArea.transform.position + Vector3.up * 0.5f;
            rotation = extraction.extractionArea.transform.rotation;
        }
        else
        {
            // No extraction point found at all (shouldn't normally happen mid-level) - fall back
            // to spawning next to the player so the button still does something visible.
            Transform player = controller.transform;
            position = player.position + player.forward * 2f + Vector3.up;
            rotation = player.rotation;
        }

        GameObject spawned = NetSpawn.Spawn(prefab.ResourcePath, prefab.Prefab, position, rotation);
        if (spawned == null)
        {
            Plugin.Log.LogInfo($"Spawn Valuable had no effect - spawn returned null for {prefab.ResourcePath}.");
            return;
        }

        spawned.GetComponent<ValuableObject>()?.DollarValueSetLogic();
        Plugin.Log.LogInfo($"Spawned valuable \"{wanted}\": {prefab.ResourcePath}");
    }

    private static IEnumerable<PrefabRef> AllPrefabs(LevelValuables p) =>
        p.tiny.Concat(p.small).Concat(p.medium).Concat(p.big).Concat(p.wide).Concat(p.tall).Concat(p.veryTall);

    // Prefers an extraction point that's actually Active (tube down, currently counting haul) -
    // falls back to nearest regardless of state if none is active yet. Internal (not private) -
    // LootMagnetHack reuses this too.
    internal static ExtractionPoint FindActiveExtractionPoint(Vector3 fromPosition)
    {
        RoundDirector director = RoundDirector.instance;
        var points = director != null ? GameReflect.Get<System.Collections.Generic.List<GameObject>>(director, "extractionPointList") : null;
        if (points == null) return null;

        ExtractionPoint best = null;
        float bestDist = float.MaxValue;
        foreach (GameObject go in points)
        {
            if (go == null) continue;
            ExtractionPoint point = go.GetComponent<ExtractionPoint>();
            if (point == null) continue;
            if (GameReflect.Get<ExtractionPoint.State>(point, "currentState") != ExtractionPoint.State.Active) continue;

            float d = Vector3.Distance(fromPosition, go.transform.position);
            if (d < bestDist) { bestDist = d; best = point; }
        }
        return best;
    }
}
