using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace RepoCheatMenu;

// Two more confirmed-unguarded RPCs, this time genuinely guest-safe with no "relay through the
// real master" dance needed at all - unlike TumbleImpactRPC/PlayerCausingRuckusRPC (which only
// do something real when the RECEIVING client happens to be master), these run their effect
// unconditionally on every single client that receives them, master or not:
//   - TrapTypeIdentifier.DestroyTrigger/DestroyTrap - confirmed in the decompiled source, no
//     PhotonMessageInfo, no check at all, just Object.Destroy() on whichever GameObject the
//     component's own fields point to. Every client independently destroys its own local copy
//     when it receives the RPC, so RpcTarget.All removes it everywhere.
//   - ItemToggle.ToggleItemRPC - also no check, calls ToggleItemLogic(toggle, player) directly.
internal static class UtilityHolesHack
{
    // Removes just the trigger collider (Trigger field), not the whole trap prop - the trap
    // stays visible but can never activate again. TrapDirector.instance.TrapList already tracks
    // every trap in the level (confirmed public field), no scene scan needed.
    public static void DisarmAllTraps()
    {
        TrapDirector director = TrapDirector.instance;
        if (director == null || director.TrapList == null)
        {
            Plugin.Log.LogInfo("Disarm All Traps had no effect - no trap list found.");
            return;
        }

        bool multiplayer = GameManager.Multiplayer();
        int count = 0;
        foreach (GameObject trapGo in director.TrapList)
        {
            if (trapGo == null) continue;
            TrapTypeIdentifier trap = trapGo.GetComponent<TrapTypeIdentifier>();
            if (trap == null || trap.TriggerRemoved) continue;

            PhotonView view = trapGo.GetComponent<PhotonView>();
            if (multiplayer)
            {
                if (view == null) continue;
                view.RPC("DestroyTrigger", RpcTarget.All);
            }
            else
            {
                GameReflect.Invoke(trap, "DestroyTrigger");
            }
            trap.TriggerRemoved = true;
            count++;
        }

        Plugin.Log.LogInfo($"Disarm All Traps: disarmed {count} trap(s).");
    }

    // Flips every ItemToggle-based switch/lever currently in the level to "on" - doors, levers,
    // whatever that component type is wired to per-level. photonView is private on ItemToggle,
    // needs GameReflect same as everything else non-public in this mod.
    public static void ToggleAllSwitches()
    {
        bool multiplayer = GameManager.Multiplayer();
        int count = 0;
        foreach (ItemToggle toggle in Object.FindObjectsByType<ItemToggle>(FindObjectsSortMode.None))
        {
            if (toggle == null) continue;

            if (multiplayer)
            {
                PhotonView view = GameReflect.Get<PhotonView>(toggle, "photonView");
                view?.RPC("ToggleItemRPC", RpcTarget.All, true, -1);
            }
            else
            {
                GameReflect.Invoke(toggle, "ToggleItemRPC", true, -1, default(PhotonMessageInfo));
            }
            count++;
        }

        Plugin.Log.LogInfo($"Toggle All Switches: flipped {count} switch(es).");
    }

    // Lock All Doors - doors/cabinets/lids in this game are all PhysGrabHinge objects (confirmed
    // in the decompiled source - "openShake"/"closeShake"/"HingeBreak" naming). Its own
    // OpenImpulseRPC/CloseImpulseRPC/HingeBreakRPC are all properly MasterOnlyRPC-gated, no hole
    // there - but like every other grabbable prop, it carries its own PhysGrabObject
    // (confirmed field), so the same Teleport(position, rotation) loophole applies, rotation
    // included this time. Captures each door's position+rotation the moment this turns on, then
    // keeps snapping it back there every tick - jams it wherever it happened to be (ideally
    // closed) instead of actually closing an open one.
    private static readonly Dictionary<PhysGrabHinge, (Vector3 pos, Quaternion rot)> _doorLocks = new();

    public static void ApplyLockAllDoors()
    {
        if (!CheatState.LockAllDoors.Value)
        {
            _doorLocks.Clear();
            return;
        }

        // Hinges from a previous level are destroyed but their dictionary entries are not - a
        // destroyed Unity object is still a live C# reference, so it keeps its slot as a key
        // forever and the table grows every level for as long as this stays on. Prune them here
        // rather than only on toggle-off, which someone running this across a whole run never hits.
        if (_doorLocks.Count > 0)
        {
            List<PhysGrabHinge> dead = null;
            foreach (PhysGrabHinge key in _doorLocks.Keys)
                if (key == null) (dead ??= new()).Add(key);

            if (dead != null)
                foreach (PhysGrabHinge key in dead) _doorLocks.Remove(key);
        }

        foreach (PhysGrabHinge hinge in Object.FindObjectsByType<PhysGrabHinge>(FindObjectsSortMode.None))
        {
            if (hinge == null) continue;

            if (!_doorLocks.TryGetValue(hinge, out (Vector3 pos, Quaternion rot) locked))
            {
                locked = (hinge.transform.position, hinge.transform.rotation);
                _doorLocks[hinge] = locked;
            }

            PhysGrabObject physObject = GameReflect.Get<PhysGrabObject>(hinge, "physGrabObject");
            if (physObject != null && (physObject.transform.position - locked.pos).sqrMagnitude > 0.01f)
                physObject.Teleport(locked.pos, locked.rot);
        }
    }
}
