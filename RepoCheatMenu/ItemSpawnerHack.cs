using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UnityEngine;

namespace RepoCheatMenu;

// Spawns any item in the game for free. Originally read from ShopManager's "potential stock"
// lists, but GetAllItemsFromStatsManager() (confirmed in the decompiled source) - the method that
// actually fills those lists - starts with "if (SemiFunc.IsNotMasterClient()) return;", so as a
// guest those lists just stay empty forever regardless of level/shop phase. StatsManager.
// itemDictionary (confirmed public Dictionary<string, Item>) is the real fix: it's populated by
// LoadItemsFromFolder() via Resources.LoadAll<Item>(folderPath) - a local asset load off disk,
// not master-negotiated state, so every client (guest or host) has the exact same full item
// registry available regardless of what's currently rolled in the shop's rotation. Item.prefab
// (confirmed public PrefabRef field) is the exact same PrefabRef type EconomyHack.SpawnValuable
// already spawns valuables from. No filtering on PrefabRef.Bundle either - LevelGenerator sets
// PhotonNetwork.PrefabPool = RunManager's MultiplayerPool for the whole level, which resolves
// bundle-backed prefabs fine.
//
// The spawn call itself goes through NetSpawn, which picks the right Photon entry point for our
// role. This file previously claimed InstantiateRoomObject had "no master-only restriction
// (confirmed)" - that was wrong and never actually confirmed, and it is exactly why spawning
// silently did nothing as a guest. See NetSpawn for what the game's own call sites show.
internal static class ItemSpawnerHack
{
    public static List<string> ScanItemNames()
    {
        List<string> names = new();
        foreach (Item item in AllItems())
        {
            if (item == null || item.disabled || !item.physicalItem) continue;
            if (string.IsNullOrEmpty(item.itemName) || names.Contains(item.itemName)) continue;
            names.Add(item.itemName);
        }
        names.Sort();
        return names;
    }

    public static void SpawnSelected()
    {
        string targetName = CheatState.ItemSpawnerName.Value;
        if (string.IsNullOrEmpty(targetName) || targetName == "None")
        {
            Plugin.Log.LogInfo("Item Spawner had no effect - pick an item first (cycle or Refresh).");
            return;
        }

        Item item = AllItems().FirstOrDefault(i => i != null && i.itemName == targetName);
        if (item == null || item.prefab == null || !item.prefab.IsValid())
        {
            Plugin.Log.LogInfo($"Item Spawner had no effect - couldn't find a valid prefab for '{targetName}'.");
            return;
        }

        PlayerController controller = PlayerController.instance;
        if (controller == null) return;

        Vector3 position = controller.transform.position + controller.transform.forward * 1.5f + Vector3.up;
        Quaternion rotation = controller.transform.rotation;

        // Force the item's own AssetBundle to load before the network path does its name-only
        // lookup: MultiplayerPool.Instantiate (what the string path routes through) only searches
        // AssetBundle.GetAllLoadedAssetBundles() and has no fallback to load one, while
        // PrefabRef.Prefab's getter calls LoadAsset on the bundle reference the item already
        // holds. ItemSpawner pulls from the full global registry (StatsManager.itemDictionary),
        // which includes items whose bundle nothing has touched yet.
        GameObject localPrefab = item.prefab.Prefab;

        GameObject spawned = NetSpawn.Spawn(item.prefab.ResourcePath, localPrefab, position, rotation);
        if (spawned == null)
            Plugin.Log.LogInfo($"Item Spawner: spawn returned null for '{targetName}' " +
                $"(path: {item.prefab.ResourcePath}) - see the BepInEx log for the underlying " +
                "MultiplayerPool/PrefabRef error.");
        else
            Plugin.Log.LogInfo($"Spawned item: {targetName}");
    }

    private static IEnumerable<Item> AllItems()
    {
        StatsManager stats = StatsManager.instance;
        if (stats == null) yield break;

        foreach (Item item in stats.itemDictionary.Values) yield return item;
    }
}
