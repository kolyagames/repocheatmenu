using System.Collections.Generic;
using UnityEngine;

namespace RepoCheatMenu;

// Auto-Loot: automatically picks up any nearby loose item (the generic ItemAttributes component -
// same one ItemStealHack/ShopDestroyerHack scan for) once you're within pickup range and not
// already holding something, instead of needing to aim and press the grab key yourself. Rides
// PhysGrabber.OverrideGrab() (confirmed public in the decompiled source, the same method
// drones/ItemStealHack already use to put something straight into a player's hands) - purely
// local, no network authority needed, works for guest or host. Only picks up items nobody's
// currently holding (checks PhysGrabObject.playerGrabbing) - never yanks something out of a
// teammate's hands, that's ItemStealHack's job, not this one's.
internal static class AutoLootHack
{
    private const float TickInterval = 0.5f;
    private const float PickupRadius = 2f;

    private static float _lastScan;

    public static void Apply()
    {
        if (!CheatState.AutoLoot.Value) return;
        if (Time.unscaledTime - _lastScan < TickInterval) return;
        _lastScan = Time.unscaledTime;

        PlayerAvatar local = PlayerAvatar.instance;
        if (local == null) return;

        PhysGrabber grabber = GameReflect.Get<PhysGrabber>(local, "physGrabber");
        if (grabber == null) return;
        if (GameReflect.Get<bool>(grabber, "grabbed")) return; // already holding something

        ItemAttributes nearest = null;
        float nearestDist = PickupRadius;

        foreach (ItemAttributes attributes in Object.FindObjectsByType<ItemAttributes>(FindObjectsSortMode.None))
        {
            if (attributes == null) continue;

            PhysGrabObject physObject = attributes.GetComponent<PhysGrabObject>();
            if (physObject == null) continue;

            List<PhysGrabber> playerGrabbing = GameReflect.Get<List<PhysGrabber>>(physObject, "playerGrabbing");
            if (playerGrabbing != null && playerGrabbing.Count > 0) continue; // someone's already holding it

            float d = Vector3.Distance(local.transform.position, physObject.transform.position);
            if (d < nearestDist) { nearestDist = d; nearest = attributes; }
        }

        if (nearest == null) return;

        PhysGrabObject held = nearest.GetComponent<PhysGrabObject>();
        grabber.OverrideGrab(held);
    }
}
