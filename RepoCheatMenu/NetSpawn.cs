using Photon.Pun;
using UnityEngine;

namespace RepoCheatMenu;

// One place for "spawn a networked prefab", because getting this wrong silently broke both
// spawners for guests.
//
// PhotonNetwork.InstantiateRoomObject is master-only: PUN checks IsMasterClient itself, logs an
// error and returns null for everyone else. The item and valuable spawners both called it
// unconditionally, so as a guest they did nothing at all - and the mod's own comment claimed the
// opposite ("no master-only restriction, confirmed"), which sent the earlier investigation off
// chasing asset-bundle loading instead.
//
// The game itself draws exactly this line and it's the clearest evidence for it: every
// InstantiateRoomObject call site in the decompiled source is master-side level content
// (LevelGenerator, ExtractionPoint, EnemyParent, ArenaRace), while the places where ANY client
// legitimately spawns something - staff projectiles in ItemStaffTorque/Void/ZeroGravity, the
// duct-taped grenade - all use plain PhotonNetwork.Instantiate.
//
// The difference that remains: a room object belongs to the room and outlives whoever created it,
// while a plain Instantiate belongs to us and Photon destroys it when we leave. For spawned loot
// that only matters if you disconnect mid-run.
internal static class NetSpawn
{
    public static GameObject Spawn(string resourcePath, GameObject localPrefab, Vector3 position, Quaternion rotation)
    {
        GameObject spawned;
        string route;

        if (!GameManager.Multiplayer())
        {
            route = "local";
            spawned = localPrefab != null ? Object.Instantiate(localPrefab, position, rotation) : null;
        }
        else if (PhotonNetwork.IsMasterClient)
        {
            route = "roomObject";
            spawned = PhotonNetwork.InstantiateRoomObject(resourcePath, position, rotation, 0);
        }
        else
        {
            route = "instantiate";
            spawned = PhotonNetwork.Instantiate(resourcePath, position, rotation, 0);
        }

        // Every spawn in the mod funnels through here, so this one line covers the item spawner,
        // the valuable spawner and the pillar-drop multiplier alike.
        EventLog.Write("spawn", $"{(spawned != null ? "ok" : "FAILED")} via {route}: " +
            $"{resourcePath} at {EventLog.Pos(position)}" +
            (spawned != null ? $" -> {spawned.name}" : ""));

        return spawned;
    }
}
