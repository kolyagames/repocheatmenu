using UnityEngine;

namespace RepoCheatMenu;

// Shared "fly above obstacles" helper for anything that needs to smoothly reposition a
// PhysGrabObject toward a target without teleporting it through walls/floors along the way -
// used by LootMagnetHack and CartHack's cart-puller. Teleport() itself is a clean reposition
// (photonTransformView.Teleport in multiplayer, confirmed in the decompiled
// PhysGrabObject.SetPositionLogic), not a simulated-velocity impact by itself - but a
// straight-line path has no idea what's in between, and a step landing inside solid geometry
// gets violently shoved back out by Unity's own de-penetration response next physics step, which
// is exactly what PhysGrabObjectImpactDetector reads as a break-worthy impact. Rising straight up
// first, crossing at that height, then descending only once above the target avoids most of
// that; the overlap check below is the safety net for whatever's left.
internal static class SafeFlightHelper
{
    // Returns true if it's safe to move to `next` this tick (false = would clip into solid
    // geometry, caller should skip the move and try again next tick).
    public static bool TryStep(Vector3 current, Vector3 target, float step, float cruiseClearance,
        float horizontalArriveDistance, float overlapCheckRadius, int solidMask, out Vector3 next)
    {
        Vector3 waypoint = NextWaypoint(current, target, cruiseClearance, horizontalArriveDistance);
        next = Vector3.MoveTowards(current, waypoint, step);
        return !Physics.CheckSphere(next, overlapCheckRadius, solidMask);
    }

    private static Vector3 NextWaypoint(Vector3 current, Vector3 target, float cruiseClearance, float horizontalArriveDistance)
    {
        float cruiseHeight = Mathf.Max(current.y, target.y) + cruiseClearance;

        if (current.y < cruiseHeight - 0.3f)
            return new Vector3(current.x, cruiseHeight, current.z);

        float horizontalDist = Vector2.Distance(new Vector2(current.x, current.z), new Vector2(target.x, target.z));
        return horizontalDist > horizontalArriveDistance
            ? new Vector3(target.x, cruiseHeight, target.z)
            : target;
    }
}
