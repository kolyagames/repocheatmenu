using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace RepoCheatMenu;

// PhysGrabObjectImpactDetector.DestroyObject() (confirmed in the decompiled source) starts
// with "if (destroyDisable || !SemiFunc.IsMasterClientOrSingleplayer()) return;" and BreakRPC's
// own handler rejects any caller that isn't the master client (SemiFunc.MasterOnlyRPC) - so
// there is no direct "destroy this" call that works for a guest, the same wall PowerGrabHack
// hit with grab/throw force. Host/singleplayer gets the clean, guaranteed path (forcing
// destroyDisable off first, since cart/weapon prefabs commonly ship with it set); guest reuses
// PowerGrabHack's own SetPositionRPC trick instead - teleporting the object straight to a
// random nearby point lands it inside walls/floor/other objects, and the host's own
// PhysGrabObjectImpactDetector.OnCollisionStay (which computes break force from the resulting
// velocity change) does the rest.
//
// Four target categories, each tracked differently:
//  - Valuables: ValuableDirector.valuableList (director-maintained list, cheap every tick).
//  - Cosmetic boxes: RoundDirector.cosmeticWorldObjects (same, director-maintained, public field).
//  - Carts and weapons (ItemGun/ItemGunTranq/ItemMelee/ItemMeleeInflatableHammer): nothing
//    tracks these in one place, so they need an actual scene scan - throttled to once a second
//    instead of every tick like the two lists above, since FindObjectsByType is comparatively
//    expensive and these counts don't change fast enough to need 20/sec freshness.
internal static class TrollHack
{
    // Was 0.05f (~20/sec) - on a level with a few dozen valuables that's several hundred
    // reflection reads (GameReflect.Get on every valuable's dollarValueCurrent) and, on the
    // guest path, several hundred Teleport RPCs *per second*, each one a hard physics-position
    // reset. Confirmed as the actual FPS-drop source: RPC traffic and repeated violent
    // Rigidbody teleports are both expensive, and nothing about clearing a level needs 20/sec
    // freshness. 6-7/sec still clears everything in a couple seconds.
    private const float TickInterval = 0.15f;

    private static float _lastSent;
    private static float _nextRescan;
    private static float _nextSmashClear;
    private static readonly List<PhysGrabObject> _cartsAndWeapons = new();

    // Host/singleplayer instance IDs already told to DestroyObject() this pass - skip them on
    // the next tick instead of re-issuing the call. DestroyObject() is an immediate, one-shot
    // destroy (unlike the guest branch's repeated Teleport-slam, which intentionally keeps
    // nudging the same object every tick until physics actually breaks it), so calling it twice
    // on the same object serves no purpose - and confirmed to be exactly what was spamming
    // "Ev Destroy Failed. Could not find PhotonView with instantiationId X" in the log: the
    // director's own valuableList/cosmeticWorldObjects don't drop a destroyed entry the very
    // same tick, so the next pass found the same (already-destroying) object again and called
    // DestroyObject() a second time on a PhotonView Photon had already torn down from the first
    // call. Cleared once a second (_nextSmashClear) instead of tracked per-object removal - by
    // then any genuinely-destroyed object is long gone from every director list, so a blanket
    // clear is simpler and just as safe as the per-ID bookkeeping would be.
    private static readonly HashSet<int> _smashRequested = new();

    // Guest path only: per-object cooldown before re-teleporting the SAME still-unbroken object
    // again. Without this, an object that hadn't broken yet got slammed again before its
    // Rigidbody had even settled from the previous slam or the master's own OnCollisionStay had
    // finished processing the resulting impact - wasted RPCs/physics work fighting themselves
    // instead of ever letting one slam's impact register cleanly. Cleared on the same 1s cadence
    // as _smashRequested, well above this cooldown, so it's still a strict improvement over the
    // old every-tick spam, not a behavior change.
    private static readonly Dictionary<int, float> _guestNextSlamTime = new();
    private const float GuestSlamCooldown = 0.4f;

    public static void Apply()
    {
        if (!CheatState.DestroyAllValuables.Value) return;
        if (Time.unscaledTime - _lastSent < TickInterval) return;
        _lastSent = Time.unscaledTime;

        if (Time.unscaledTime >= _nextSmashClear)
        {
            _nextSmashClear = Time.unscaledTime + 1f;
            _smashRequested.Clear();
            _guestNextSlamTime.Clear();
        }

        bool masterOrSolo = !GameManager.Multiplayer() || PhotonNetwork.IsMasterClient;

        DestroyValuables(masterOrSolo);
        DestroyCosmeticBoxes(masterOrSolo);
        DestroyCartsAndWeapons(masterOrSolo);
    }

    private static void DestroyValuables(bool masterOrSolo)
    {
        ValuableDirector director = ValuableDirector.instance;
        if (director == null) return;

        List<ValuableObject> valuables = GameReflect.Get<List<ValuableObject>>(director, "valuableList");
        if (valuables == null) return;

        foreach (ValuableObject valuable in valuables)
        {
            if (valuable == null) continue;
            // dollarValueCurrent is the price actually shown/paid out (confirmed in the
            // decompiled source) - skip the cheap $100 ones so low-tier valuables survive.
            if (Mathf.RoundToInt(GameReflect.Get<float>(valuable, "dollarValueCurrent")) == 100) continue;
            Smash(GameReflect.Get<PhysGrabObject>(valuable, "physGrabObject"), masterOrSolo);
        }
    }

    private static void DestroyCosmeticBoxes(bool masterOrSolo)
    {
        RoundDirector director = RoundDirector.instance;
        if (director == null) return;

        foreach (CosmeticWorldObject box in director.cosmeticWorldObjects)
        {
            if (box == null) continue;
            Smash(GameReflect.Get<PhysGrabObject>(box, "physGrabObject"), masterOrSolo);
        }
    }

    private static void DestroyCartsAndWeapons(bool masterOrSolo)
    {
        if (Time.unscaledTime >= _nextRescan)
        {
            _nextRescan = Time.unscaledTime + 1f;
            RescanCartsAndWeapons();
        }

        foreach (PhysGrabObject physObject in _cartsAndWeapons)
            Smash(physObject, masterOrSolo);
    }

    private static void RescanCartsAndWeapons()
    {
        _cartsAndWeapons.Clear();

        // Every cart size (small/medium/large) uses this same component - no separate "small
        // cart" type to special-case.
        foreach (PhysGrabCart cart in Object.FindObjectsByType<PhysGrabCart>(FindObjectsSortMode.None))
            AddPhysObject(cart.gameObject);

        foreach (ItemGun gun in Object.FindObjectsByType<ItemGun>(FindObjectsSortMode.None))
            AddPhysObject(gun.gameObject);
        foreach (ItemGunTranq gun in Object.FindObjectsByType<ItemGunTranq>(FindObjectsSortMode.None))
            AddPhysObject(gun.gameObject);
        foreach (ItemMelee melee in Object.FindObjectsByType<ItemMelee>(FindObjectsSortMode.None))
            AddPhysObject(melee.gameObject);
        foreach (ItemMeleeInflatableHammer hammer in Object.FindObjectsByType<ItemMeleeInflatableHammer>(FindObjectsSortMode.None))
            AddPhysObject(hammer.gameObject);
    }

    private static void AddPhysObject(GameObject go)
    {
        PhysGrabObject physObject = go.GetComponent<PhysGrabObject>();
        if (physObject != null) _cartsAndWeapons.Add(physObject);
    }

    private static void Smash(PhysGrabObject physObject, bool masterOrSolo)
    {
        if (physObject == null) return;

        if (masterOrSolo)
        {
            if (!_smashRequested.Add(physObject.GetInstanceID())) return; // already told this one to destroy this pass

            PhysGrabObjectImpactDetector impact = GameReflect.Get<PhysGrabObjectImpactDetector>(physObject, "impactDetector");
            if (impact == null) return;
            impact.destroyDisable = false; // carts/weapons commonly ship with this set in the prefab - force it off so DestroyObject() actually fires
            impact.DestroyObject();
        }
        else
        {
            int id = physObject.GetInstanceID();
            if (_guestNextSlamTime.TryGetValue(id, out float next) && Time.unscaledTime < next) return;
            _guestNextSlamTime[id] = Time.unscaledTime + GuestSlamCooldown;

            Vector3 slamTo = physObject.transform.position + Random.insideUnitSphere * 6f;
            physObject.Teleport(slamTo, physObject.transform.rotation);
        }
    }
}
