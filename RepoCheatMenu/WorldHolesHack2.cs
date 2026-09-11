using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace RepoCheatMenu;

// The remaining strong holes from the full 537-RPC sweep, grouped by what they let you do to the
// level rather than to a person.
//
// 1) RoundDirector.RequestExtractionPointActivationRPC(int photonViewID)
//    Confirmed unguarded - no sender check at all, only "if (!extractionPointActive &&
//    IsMasterClientOrSingleplayer())", the execute-side check. The real master then broadcasts
//    ExtractionPointActivateRPC itself, which IS MasterOnlyRPC-gated and now passes legitimately.
//    So a guest can pick WHICH extraction point the round activates - including a bad one, since
//    the master's own handler then calls ExtractionPointsLock() on all the others, committing the
//    whole team to it for that round.
//
// 2) ValuableObject.DiscoverRPC / ValuableDiscoverCustom.DiscoverRPC
//    Unguarded, and its body is just "discovered = true; Map.Instance.AddValuable(this);" -
//    executed on every receiving client. Reveals valuables on the map without needing to find
//    them; the ESP already shows them to us, so the real use is the reverse (revealing everything
//    to everyone, which wrecks the exploration the mode is built on).
//
// 3) SlowProjectile.OnHurtPlayerRPC(Vector3 _position)
//    Unguarded, body is literally "Explosion(_position)". The follow-up ExplosionRPC reparents the
//    projectile's hurtCollider to that world position and inflates its radius tenfold before the
//    master applies the damage. So any live SlowProjectile in the level can be detonated at
//    coordinates of our choosing - real damage, dealt by the projectile, not by us.
//
// 4) ItemVehicle.WobbleTriggerRPC(float _force, Vector3 _direction)
//    Unguarded, body is just "ApplyWobbleTrigger(_force, _direction)" with both values ours -
//    but COSMETIC, which was worth checking properly rather than assuming: the wobble springs it
//    feeds drive meshTransform (the car's visual body, i.e. its suspension bounce) and play
//    wobbleImpactSound. The rigidbody is never touched, so handling is unaffected and nobody gets
//    thrown out. Both offsets are also clamped (wobblePositionMax 0.3, wobbleRotationMax 40), so
//    passing a huge force just saturates the clamp - it is not a stronger version of the vanilla
//    1.5f the game itself uses. Kept as an annoyance tool, not sold as one that ruins driving.
//
// 5) PlayerAvatar.ResetPhysPusher
//    Unguarded, sets playerPhysPusher.Reset = true on every client - the collision pusher that
//    keeps players from intersecting each other and geometry.
internal static class WorldHolesHack2
{
    // --- Extraction control -------------------------------------------------------------------

    private static List<ExtractionPoint> AllPoints()
    {
        List<ExtractionPoint> result = new();
        foreach (ExtractionPoint ep in Object.FindObjectsByType<ExtractionPoint>(FindObjectsSortMode.None))
            if (ep != null) result.Add(ep);
        return result;
    }

    private static void Activate(ExtractionPoint point)
    {
        RoundDirector director = RoundDirector.instance;
        if (director == null || point == null) return;

        PhotonView pointView = GameReflect.Get<PhotonView>(point, "photonView");
        if (pointView == null) return;

        if (GameManager.Multiplayer())
        {
            PhotonView view = GameReflect.Get<PhotonView>(director, "photonView");
            view?.RPC("RequestExtractionPointActivationRPC", RpcTarget.MasterClient, pointView.ViewID);
        }
        else
        {
            director.RequestExtractionPointActivationRPC(pointView.ViewID);
        }
    }

    private static ExtractionPoint PickByDistance(bool farthest)
    {
        PlayerAvatar local = PlayerTargets.Local;
        if (local == null) return null;

        ExtractionPoint best = null;
        float bestDist = farthest ? float.MinValue : float.MaxValue;
        foreach (ExtractionPoint ep in AllPoints())
        {
            float d = Vector3.Distance(local.transform.position, ep.transform.position);
            if (farthest ? d > bestDist : d < bestDist) { bestDist = d; best = ep; }
        }
        return best;
    }

    // 24) Start the round's extraction at the point nearest you - useful version.
    public static void ActivateNearestExtraction()
    {
        ExtractionPoint ep = PickByDistance(farthest: false);
        if (ep == null) { Plugin.Log.LogInfo("No extraction point found."); return; }
        Activate(ep);
        Plugin.Log.LogInfo("Requested activation of the nearest extraction point.");
    }

    // 25) Grief version: commits the whole round to the point FURTHEST from everyone. Because the
    // master's own handler locks every other point once one activates, the team is stuck hauling
    // to it for the rest of the round.
    public static void ActivateFarthestExtraction()
    {
        ExtractionPoint ep = PickByDistance(farthest: true);
        if (ep == null) { Plugin.Log.LogInfo("No extraction point found."); return; }
        Activate(ep);
        Plugin.Log.LogInfo("Locked the round onto the farthest extraction point.");
    }

    // 26) Toggle - re-requests every few seconds, so the moment a round becomes eligible for a new
    // extraction it immediately snaps to the far one before anyone can pick.
    private const float ExtractionSnipeInterval = 3f;
    private static float _nextSnipe;

    public static void ApplyExtractionSnipe()
    {
        if (!CheatState.ExtractionSnipe.Value) return;
        if (Time.unscaledTime < _nextSnipe) return;
        _nextSnipe = Time.unscaledTime + ExtractionSnipeInterval;

        ExtractionPoint ep = PickByDistance(farthest: true);
        if (ep != null) Activate(ep);
    }

    // --- Valuable discovery -------------------------------------------------------------------

    private static int Discover(bool everyone)
    {
        RpcTarget scope = everyone ? RpcTarget.All : RpcTarget.Others;
        int count = 0;

        ValuableDirector director = ValuableDirector.instance;
        List<ValuableObject> valuables = director != null
            ? GameReflect.Get<List<ValuableObject>>(director, "valuableList")
            : null;
        if (valuables == null) return 0;

        foreach (ValuableObject valuable in valuables)
        {
            // "discovered" is internal on the real runtime assembly (the publicized compile-time
            // stub lies about it), so reading it directly would throw FieldAccessException every
            // tick. It also has to be checked, not skipped: without it the auto-reveal toggle
            // would re-send an RPC for every valuable in the level every 5 seconds forever.
            if (valuable == null || GameReflect.Get<bool>(valuable, "discovered")) continue;
            PhotonView view = GameReflect.Get<PhotonView>(valuable, "photonView");

            if (GameManager.Multiplayer()) view?.RPC("DiscoverRPC", scope);
            else GameReflect.Invoke(valuable, "DiscoverRPC");
            count++;
        }
        return count;
    }

    // 27) Put every valuable on everyone's map at once - kills the whole search phase of the run.
    public static void RevealAllValuables()
    {
        int n = Discover(everyone: true);
        Plugin.Log.LogInfo($"Revealed {n} valuable(s) on everyone's map.");
    }

    // 28) Toggle - keeps revealing as the level streams more in.
    private const float RevealInterval = 5f;
    private static float _nextReveal;

    public static void ApplyAutoReveal()
    {
        if (!CheatState.AutoRevealValuables.Value) return;
        if (Time.unscaledTime < _nextReveal) return;
        _nextReveal = Time.unscaledTime + RevealInterval;
        Discover(everyone: true);
    }

    // --- Projectile detonation ----------------------------------------------------------------

    private static int DetonateAt(Vector3 position)
    {
        int count = 0;
        foreach (SlowProjectile projectile in Object.FindObjectsByType<SlowProjectile>(FindObjectsSortMode.None))
        {
            if (projectile == null) continue;
            PhotonView view = GameReflect.Get<PhotonView>(projectile, "photonView");

            if (GameManager.Multiplayer()) view?.RPC("OnHurtPlayerRPC", RpcTarget.All, position);
            else GameReflect.Invoke(projectile, "OnHurtPlayerRPC", position);
            count++;
        }
        return count;
    }

    // 29) Detonate every live projectile on top of the picked target.
    public static void StrikeTarget()
    {
        PlayerAvatar target = PlayerTargets.Picked();
        if (target == null) { Plugin.Log.LogInfo("Strike had no effect - no target selected."); return; }

        int n = DetonateAt(target.transform.position);
        if (n == 0) Plugin.Log.LogInfo("Strike had no effect - no live projectile in the level.");
        else Plugin.Log.LogInfo($"Detonated {n} projectile(s) on {PlayerTargets.NameOf(target)}.");
    }

    // 30) One detonation per player, everywhere at once.
    public static void StrikeEveryone()
    {
        int n = 0;
        foreach (PlayerAvatar p in PlayerTargets.AliveOthers()) n += DetonateAt(p.transform.position);
        Plugin.Log.LogInfo($"Detonated {n} projectile(s) across the lobby.");
    }

    // 31) Toggle - repeats on whoever the picker points at.
    private const float StrikeInterval = 4f;
    private static float _nextStrike;

    public static void ApplyStrikeStorm()
    {
        if (!CheatState.ProjectileStrikeStorm.Value) return;
        if (Time.unscaledTime < _nextStrike) return;
        _nextStrike = Time.unscaledTime + StrikeInterval;

        foreach (PlayerAvatar p in PlayerTargets.AliveOthers()) DetonateAt(p.transform.position);
    }

    // --- Vehicle wobble -----------------------------------------------------------------------

    private static void Wobble(ItemVehicle vehicle, float force, Vector3 direction)
    {
        if (vehicle == null) return;
        PhotonView view = GameReflect.Get<PhotonView>(vehicle, "photonView");

        if (GameManager.Multiplayer()) view?.RPC("WobbleTriggerRPC", RpcTarget.All, force, direction);
        else GameReflect.Invoke(vehicle, "WobbleTriggerRPC", force, direction, default(PhotonMessageInfo));
    }

    // 32) Shake every vehicle on the map hard.
    public static void WobbleAllVehicles()
    {
        int n = 0;
        foreach (ItemVehicle vehicle in Object.FindObjectsByType<ItemVehicle>(FindObjectsSortMode.None))
        {
            Wobble(vehicle, 40f, Random.onUnitSphere);
            n++;
        }
        Plugin.Log.LogInfo($"Wobbled {n} vehicle(s).");
    }

    // 33) Only the one the picked target is sitting in - everyone else's ride stays normal, so it
    // reads as their car specifically being cursed.
    public static void WobbleTargetVehicle()
    {
        PlayerAvatar target = PlayerTargets.Picked();
        if (target == null) return;

        foreach (ItemVehicle vehicle in Object.FindObjectsByType<ItemVehicle>(FindObjectsSortMode.None))
        {
            if (vehicle == null || vehicle.seats == null) continue;
            foreach (var seat in vehicle.seats)
            {
                if (seat == null || seat.seatedPlayer != target) continue;
                Wobble(vehicle, 60f, Vector3.up);
                Plugin.Log.LogInfo($"Wobbled {PlayerTargets.NameOf(target)}'s vehicle.");
                return;
            }
        }
        Plugin.Log.LogInfo("Wobble had no effect - target isn't in a vehicle.");
    }

    // 34) Toggle - continuous shaking, nobody can drive straight.
    private const float WobbleInterval = 0.8f;
    private static float _nextWobble;

    public static void ApplyWobbleStorm()
    {
        if (!CheatState.VehicleWobbleStorm.Value) return;
        if (Time.unscaledTime < _nextWobble) return;
        _nextWobble = Time.unscaledTime + WobbleInterval;

        foreach (ItemVehicle vehicle in Object.FindObjectsByType<ItemVehicle>(FindObjectsSortMode.None))
            Wobble(vehicle, 35f, Random.onUnitSphere);
    }

    // --- Phys pusher --------------------------------------------------------------------------

    private static void ResetPusher(PlayerAvatar player)
    {
        if (player == null || player.photonView == null) return;

        if (GameManager.Multiplayer()) player.photonView.RPC("ResetPhysPusher", RpcTarget.All);
        else player.ResetPhysPusher();
    }

    // 35) Reset everyone's collision pusher - genuinely useful when someone is wedged into
    // geometry or stuck inside another player, which happens a lot after the raid tools.
    public static void ResetAllPhysPushers()
    {
        int n = 0;
        foreach (PlayerAvatar p in PlayerTargets.AliveOthers()) { ResetPusher(p); n++; }
        ResetPusher(PlayerTargets.Local);
        Plugin.Log.LogInfo($"Reset phys pusher on {n + 1} player(s).");
    }

    // 36) Just the picked target.
    public static void ResetTargetPhysPusher()
    {
        PlayerAvatar target = PlayerTargets.Picked();
        if (target == null) return;
        ResetPusher(target);
        Plugin.Log.LogInfo($"Reset {PlayerTargets.NameOf(target)}'s phys pusher.");
    }

    // --- Traps --------------------------------------------------------------------------------
    // TrapTypeIdentifier.DestroyTrap is the sibling of DestroyTrigger that UtilityHolesHack
    // already uses - equally unguarded, but removes the whole trap prop instead of only its
    // trigger collider, so the level visibly loses its hazards rather than quietly disarming them.

    // 37) Delete every trap in the level outright.
    public static void DestroyAllTrapsFully()
    {
        TrapDirector director = TrapDirector.instance;
        if (director == null || director.TrapList == null)
        {
            Plugin.Log.LogInfo("Destroy Traps had no effect - no trap list found.");
            return;
        }

        int count = 0;
        foreach (GameObject trapGo in director.TrapList)
        {
            if (trapGo == null) continue;
            TrapTypeIdentifier trap = trapGo.GetComponent<TrapTypeIdentifier>();
            if (trap == null) continue;

            PhotonView view = trapGo.GetComponent<PhotonView>();
            if (GameManager.Multiplayer())
            {
                if (view == null) continue;
                view.RPC("DestroyTrap", RpcTarget.All);
            }
            else
            {
                GameReflect.Invoke(trap, "DestroyTrap");
            }
            count++;
        }
        Plugin.Log.LogInfo($"Destroyed {count} trap(s) completely.");
    }

    // 38) Music box traps: MusicBoxTrap.OpenTheBox(float) is unguarded and opens the box on every
    // client - pops every one in the level at once, dancers and all.
    public static void TriggerAllMusicBoxes()
    {
        int count = 0;
        foreach (MusicBoxTrap box in Object.FindObjectsByType<MusicBoxTrap>(FindObjectsSortMode.None))
        {
            if (box == null) continue;
            PhotonView view = box.GetComponent<PhotonView>();

            if (GameManager.Multiplayer()) view?.RPC("OpenTheBox", RpcTarget.All, 0f);
            else GameReflect.Invoke(box, "OpenTheBox", 0f);
            count++;
        }
        Plugin.Log.LogInfo($"Opened {count} music box trap(s).");
    }

    // 39) ChompBookTrap.TrapStopRPC is unguarded too - stops every chomping book, the counterpart
    // to opening the music boxes.
    public static void StopAllChompBooks()
    {
        int count = 0;
        foreach (ChompBookTrap book in Object.FindObjectsByType<ChompBookTrap>(FindObjectsSortMode.None))
        {
            if (book == null) continue;
            PhotonView view = book.GetComponent<PhotonView>();

            if (GameManager.Multiplayer()) view?.RPC("TrapStopRPC", RpcTarget.All);
            else GameReflect.Invoke(book, "TrapStopRPC");
            count++;
        }
        Plugin.Log.LogInfo($"Stopped {count} chomp book trap(s).");
    }

    // 40) Everything in this file that doesn't need a target, in one press: reveal the map, wreck
    // the traps, shake the vehicles and reset everyone's pusher so nobody stays wedged.
    public static void WorldSweep()
    {
        RevealAllValuables();
        DestroyAllTrapsFully();
        WobbleAllVehicles();
        ResetAllPhysPushers();
        Plugin.Log.LogInfo("World sweep done.");
    }
}
