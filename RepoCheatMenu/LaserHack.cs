using System.Collections.Generic;
using UnityEngine;

namespace RepoCheatMenu;

// Ported from RepoHax's Hooked__ItemGun_Update: adds a LineRenderer from the gun's muzzle
// out to its range while it's locally held. ItemGun.gunMuzzle/gunRange and
// PhysGrabObject.grabbedLocal confirmed from RepoHax's game_classes.h.
//
// Same throttled-rescan pattern as CartHack/FlashlightHack - FindObjectsOfType used to run
// unconditionally every tick regardless of how many guns exist in the level, which only grows
// over a run. Refreshing the list once a second instead is plenty for which guns exist; the
// held gun's laser position below still updates every tick for smooth tracking.
internal static class LaserHack
{
    private static float _nextRescan;
    private static readonly List<ItemGun> _guns = new();

    public static void Apply()
    {
        if (Time.unscaledTime >= _nextRescan)
        {
            _nextRescan = Time.unscaledTime + 1f;
            _guns.Clear();
            _guns.AddRange(Object.FindObjectsByType<ItemGun>(FindObjectsSortMode.None));
        }

        foreach (ItemGun gun in _guns)
        {
            if (gun == null) continue;

            PhysGrabObject grabObject = GameReflect.Get<PhysGrabObject>(gun, "physGrabObject");
            bool held = CheatState.UseLaser.Value
                && grabObject != null
                && GameReflect.Get<bool>(grabObject, "grabbedLocal");

            LineRenderer laser = gun.GetComponent<LineRenderer>();

            if (!held)
            {
                if (laser != null) laser.enabled = false;
                continue;
            }

            if (laser == null)
            {
                laser = gun.gameObject.AddComponent<LineRenderer>();
                laser.startWidth = 0.02f;
                laser.endWidth = 0.02f;
                laser.sortingOrder = 1;
                laser.material = new Material(Shader.Find("Sprites/Default"));
                laser.material.color = Color.red;
                laser.positionCount = 2;
            }

            Transform muzzle = GameReflect.Get<Transform>(gun, "gunMuzzle");
            if (muzzle == null) continue;

            float range = GameReflect.Get<float>(gun, "gunRange");
            laser.enabled = true;
            laser.SetPosition(0, muzzle.position);
            laser.SetPosition(1, muzzle.position + muzzle.forward * range);
        }
    }
}
