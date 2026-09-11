using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace RepoCheatMenu;

// "Shop Destroyer 3000": same DestroyObject()/Teleport-slam approach as TrollHack's
// DestroyAllValuables (see that file's comment for the full master/guest breakdown), but scoped
// to the shop level and scanning ItemAttributes broadly instead of just ValuableDirector's list -
// covers guns, melee weapons, health packs, batteries, anything with the generic Item/
// ItemAttributes system (confirmed the same component ItemStealHack.ScanItemNames() already
// scans for), not just valuables/carts/melee weapons like the regular Troll-tab destroyer. Only
// runs while SemiFunc.RunIsShop() is true, so it never touches a normal level's items - separate
// toggle, separate feature from Destroy All Valuables.
internal static class ShopDestroyerHack
{
    private const float TickInterval = 0.15f;
    private const float GuestSlamCooldown = 0.4f;

    private static float _lastSent;
    private static float _nextSmashClear;
    private static readonly HashSet<int> _smashRequested = new();
    private static readonly Dictionary<int, float> _guestNextSlamTime = new();

    public static void Apply()
    {
        if (!CheatState.ShopDestroyer.Value) return;
        if (!SemiFunc.RunIsShop()) return;
        if (Time.unscaledTime - _lastSent < TickInterval) return;
        _lastSent = Time.unscaledTime;

        if (Time.unscaledTime >= _nextSmashClear)
        {
            _nextSmashClear = Time.unscaledTime + 1f;
            _smashRequested.Clear();
            _guestNextSlamTime.Clear();
        }

        bool masterOrSolo = !GameManager.Multiplayer() || PhotonNetwork.IsMasterClient;

        foreach (ItemAttributes attributes in Object.FindObjectsByType<ItemAttributes>(FindObjectsSortMode.None))
        {
            if (attributes == null) continue;
            Smash(attributes.GetComponent<PhysGrabObject>(), masterOrSolo);
        }
    }

    private static void Smash(PhysGrabObject physObject, bool masterOrSolo)
    {
        if (physObject == null) return;

        if (masterOrSolo)
        {
            if (!_smashRequested.Add(physObject.GetInstanceID())) return; // already told this one to destroy this pass

            PhysGrabObjectImpactDetector impact = GameReflect.Get<PhysGrabObjectImpactDetector>(physObject, "impactDetector");
            if (impact == null) return;
            impact.destroyDisable = false; // some prefabs ship with this set - force it off so DestroyObject() actually fires
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
