// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace RepoCheatMenu;

// "Raid" tools - actual control over a picked player, not cosmetics.
//
// Cage/Yeet both need the target genuinely in PlayerTumble.isTumbling state before moving them:
// confirmed in KnockdownHack.ApplyFixed's comment - while NOT tumbling, PlayerTumble.rb just
// follows the PlayerController's own transform every Update, so teleporting tumble.physGrabObject
// on a normal walking player would get silently overwritten the very next frame. Forcing someone
// ELSE into tumbling needs master authority for the direct route (TumbleRequestRPC/TumbleSetRPC
// are both MasterAndOwnerOnlyRPC-gated) - but KnockdownHack.TriggerImpact already solves this for
// guests too, by sending PlayerTumble.TumbleImpactRPC (no sender/proximity check in its own body)
// to every client including the real master; the master's own execution of that same method is
// what actually applies FreezeForces, and that's what pushes the target's own client over the
// rbVelocity threshold into real tumbling. So both features fire TriggerImpact first and wait for
// isTumbling to actually flip before teleporting - same reason KnockdownBanish waits through a
// "Frozen" phase before its own Banish() call.
//
// Once tumbling, moving them rides PhysGrabObject.Teleport() same as Banish/PowerGrab/LootMagnet -
// SetPositionRPC to the master, applied with zero validation. Works for guest or host.
internal static class RaidHack
{
    // Confirmed in the decompiled PlayerAvatar.cs Update(): a built-in safety net (master-side
    // only, "SemiFunc.IsMasterClientOrSingleplayer()") rescues anyone whose absolute world Y
    // drops below -100 by teleporting them to TruckSafetySpawnPoint ("base") - the exact "falls
    // under the map, ends up back at base" behavior being reported. 60-80 units straight down
    // from wherever the target happened to be standing could easily cross that absolute -100
    // line on a low/basement room, defeating the cage the instant the master's own Update tick
    // catches it. Shallow enough to actually clear a floor's own collision (so physics doesn't
    // just eject them straight back up through it) without needing anywhere near that threshold
    // on a normal room.
    private const float CageDepth = 15f;
    private const float WallPushDistance = 4f;
    private const float WallAimRange = 25f;
    private const float ImpactRetryInterval = 1f;
    private const float YeetTimeout = 1.5f;

    private static Vector3 ComputeWallCagePoint(Vector3 fallback)
    {
        Camera cam = Camera.main;
        if (cam == null) return fallback;

        int solidMask = LayerMask.GetMask("Default");
        if (Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, WallAimRange, solidMask))
            return hit.point + cam.transform.forward * WallPushDistance;

        return fallback;
    }

    private static PlayerAvatar _cageTarget;
    private static Vector3 _cagePoint;
    private static float _cageNextImpact;

    private static PlayerAvatar _pendingYeet;
    private static float _yeetDeadline;

    private static readonly Dictionary<PlayerAvatar, Vector3> _cageAllPoints = new();
    private static readonly Dictionary<PlayerAvatar, float> _cageAllNextImpact = new();

    private static readonly List<PlayerAvatar> _pendingYeetAll = new();
    private static float _yeetAllDeadline;

    public static void ApplyCage()
    {
        if (!CheatState.PlayerCage.Value)
        {
            _cageTarget = null;
            return;
        }

        PlayerAvatar local = PlayerAvatar.instance;
        if (local == null) return;

        PlayerAvatar target = FindTarget();
        if (target == null)
        {
            _cageTarget = null;
            return;
        }

        if (target != _cageTarget)
        {
            _cageTarget = target;
            _cagePoint = ComputeWallCagePoint(target.transform.position + Vector3.down * CageDepth);
            _cageNextImpact = 0f;
        }

        bool tumbling = GameReflect.Get<bool>(target, "isTumbling");
        if (!tumbling)
        {
            // Keep re-triggering until it actually lands (the master-side force takes a
            // network round trip, and tumbling can also lapse on its own after a while if the
            // cage point ever lets them settle) instead of firing every single tick.
            if (Time.unscaledTime >= _cageNextImpact)
            {
                KnockdownHack.TriggerImpact(local, target);
                _cageNextImpact = Time.unscaledTime + ImpactRetryInterval;
            }
            return;
        }

        PhysGrabObject body = GetBody(target);
        if (body == null) return;

        // Only re-teleport when they've actually drifted off the cage point, not every single
        // tick unconditionally - Teleport() is a position snap, and every repeated snap onto the
        // same underground spot registers as a fresh collision with the level geometry there,
        // which retriggers PhysGrabObjectImpactDetector's ImpactSmoke particle burst before the
        // previous one has finished - stacking into a solid glowing blob that blocked the whole
        // screen (reported in play) instead of the brief, isolated flash a real impact gives.
        if ((body.transform.position - _cagePoint).sqrMagnitude > 0.25f)
            GameReflect.TeleportSafe(body, _cagePoint, target.transform.rotation, "RaidHack");
    }

    public static void Yeet()
    {
        PlayerAvatar local = PlayerAvatar.instance;
        PlayerAvatar target = FindTarget();
        if (local == null || target == null)
        {
            Plugin.Log.LogInfo("Yeet had no effect - no target player selected.");
            return;
        }

        KnockdownHack.TriggerImpact(local, target);
        _pendingYeet = target;
        _yeetDeadline = Time.unscaledTime + YeetTimeout;
    }

    // Ticked every frame (RunSafe in Patches.cs) to catch the moment isTumbling actually flips
    // after Yeet() fires the impact - same wait Cage does, just one-shot instead of continuous.
    public static void ApplyYeetPending()
    {
        if (_pendingYeet == null) return;

        if (Time.unscaledTime >= _yeetDeadline)
        {
            _pendingYeet = null; // gave up - target never entered tumble state in time
            return;
        }

        if (!GameReflect.Get<bool>(_pendingYeet, "isTumbling")) return;

        PhysGrabObject body = GetBody(_pendingYeet);
        if (body != null)
        {
            Vector2 dir2 = Random.insideUnitCircle.normalized;
            Vector3 pos = _pendingYeet.transform.position + new Vector3(dir2.x, 0f, dir2.y) * 80f + Vector3.up * 40f;
            GameReflect.TeleportSafe(body, pos, _pendingYeet.transform.rotation, "RaidHack");
        }
        _pendingYeet = null;
    }

    // Same idea as Cage, applied to every other player at once instead of one picked target -
    // each gets their own deterministic (per-instance-ID, so it's stable tick to tick) spread
    // point under the map so they don't all pile on the exact same spot fighting each other's
    // colliders, and each is tracked through its own homing-to-tumbling handshake independently.
    public static void ApplyCageAll()
    {
        if (!CheatState.PlayerCageAll.Value)
        {
            _cageAllPoints.Clear();
            _cageAllNextImpact.Clear();
            return;
        }

        PlayerAvatar local = PlayerAvatar.instance;
        if (local == null) return;

        List<PlayerAvatar> others = GetAliveOthers(local);
        if (others.Count == 0) return;

        // Same pruning Freeze All does, and for the same reason - without it, everyone who dies
        // or leaves stays in the table for the rest of the session, and destroyed PlayerAvatars
        // from previous levels are kept alive as keys.
        if (_cageAllPoints.Count > 0)
        {
            List<PlayerAvatar> stale = null;
            foreach (PlayerAvatar key in _cageAllPoints.Keys)
                if (key == null || !others.Contains(key)) (stale ??= new()).Add(key);

            if (stale != null)
            {
                foreach (PlayerAvatar k in stale)
                {
                    _cageAllPoints.Remove(k);
                    _cageAllNextImpact.Remove(k);
                }
            }
        }

        foreach (PlayerAvatar target in others)
        {
            if (!_cageAllPoints.TryGetValue(target, out Vector3 point))
            {
                point = SpreadCagePoint(local.transform.position, target.GetInstanceID());
                _cageAllPoints[target] = point;
            }

            bool tumbling = GameReflect.Get<bool>(target, "isTumbling");
            if (!tumbling)
            {
                if (!_cageAllNextImpact.TryGetValue(target, out float next) || Time.unscaledTime >= next)
                {
                    KnockdownHack.TriggerImpact(local, target);
                    _cageAllNextImpact[target] = Time.unscaledTime + ImpactRetryInterval;
                }
                continue;
            }

            PhysGrabObject body = GetBody(target);
            if (body == null) continue;

            // Same fix as ApplyCage above - only re-teleport on actual drift, not every tick.
            if ((body.transform.position - point).sqrMagnitude > 0.25f)
                GameReflect.TeleportSafe(body, point, target.transform.rotation, "RaidHack");
        }
    }

    // One-shot version of Cage All - impacts everyone at once, then flings each one into the
    // air individually as their own tumble state actually lands (the network round trip means
    // they won't all flip at exactly the same instant).
    public static void YeetAll()
    {
        PlayerAvatar local = PlayerAvatar.instance;
        if (local == null) return;

        List<PlayerAvatar> others = GetAliveOthers(local);
        if (others.Count == 0)
        {
            Plugin.Log.LogInfo("Yeet All had no effect - no other players.");
            return;
        }

        _pendingYeetAll.Clear();
        foreach (PlayerAvatar target in others)
        {
            KnockdownHack.TriggerImpact(local, target);
            _pendingYeetAll.Add(target);
        }
        _yeetAllDeadline = Time.unscaledTime + YeetTimeout;
    }

    public static void ApplyYeetAllPending()
    {
        if (_pendingYeetAll.Count == 0) return;

        if (Time.unscaledTime >= _yeetAllDeadline)
        {
            _pendingYeetAll.Clear();
            return;
        }

        for (int i = _pendingYeetAll.Count - 1; i >= 0; i--)
        {
            PlayerAvatar target = _pendingYeetAll[i];
            if (target == null || !GameReflect.Get<bool>(target, "isTumbling")) continue;

            PhysGrabObject body = GetBody(target);
            if (body != null)
            {
                Vector2 dir2 = Random.insideUnitCircle.normalized;
                Vector3 pos = target.transform.position + new Vector3(dir2.x, 0f, dir2.y) * 80f + Vector3.up * 40f;
                GameReflect.TeleportSafe(body, pos, target.transform.rotation, "RaidHack");
            }
            _pendingYeetAll.RemoveAt(i);
        }
    }

    private static Vector3 SpreadCagePoint(Vector3 basePosition, int instanceId)
    {
        System.Random rng = new System.Random(instanceId);
        float angle = (float)(rng.NextDouble() * (Mathf.PI * 2));
        float radius = 2f + (float)rng.NextDouble() * 4f;
        Vector3 offset = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        return basePosition + offset + Vector3.down * CageDepth;
    }

    private static List<PlayerAvatar> GetAliveOthers(PlayerAvatar local)
    {
        List<PlayerAvatar> result = new();
        GameDirector director = GameDirector.instance;
        if (director == null) return result;

        List<PlayerAvatar> players = GameReflect.Get<List<PlayerAvatar>>(director, "PlayerList");
        if (players == null) return result;

        foreach (PlayerAvatar p in players)
        {
            if (p == null || p == local) continue;
            if (GameReflect.Get<bool>(p, "deadSet")) continue;
            result.Add(p);
        }
        return result;
    }

    // "Freeze All" - same tumble-then-Teleport mechanic as Cage All, but each target's pin point
    // is wherever THEY already were the instant freezing caught up to them, not a shared point
    // under the map - holds the whole lobby exactly in place for as long as the toggle is on,
    // instead of dragging anyone anywhere. Releases naturally once turned off: nothing re-triggers
    // TriggerImpact anymore, so tumbling just lapses on its own like it always does.
    private static readonly Dictionary<PlayerAvatar, Vector3> _freezePoints = new();
    private static readonly Dictionary<PlayerAvatar, float> _freezeNextImpact = new();

    public static void ApplyFreezeAll()
    {
        if (!CheatState.FreezeAll.Value)
        {
            _freezePoints.Clear();
            _freezeNextImpact.Clear();
            return;
        }

        PlayerAvatar local = PlayerAvatar.instance;
        if (local == null) return;

        List<PlayerAvatar> others = GetAliveOthers(local);

        foreach (PlayerAvatar target in others)
        {
            if (!_freezePoints.TryGetValue(target, out Vector3 point))
            {
                point = target.transform.position; // wherever they are right now - not a shared void point
                _freezePoints[target] = point;
            }

            bool tumbling = GameReflect.Get<bool>(target, "isTumbling");
            if (!tumbling)
            {
                if (!_freezeNextImpact.TryGetValue(target, out float next) || Time.unscaledTime >= next)
                {
                    KnockdownHack.TriggerImpact(local, target);
                    _freezeNextImpact[target] = Time.unscaledTime + ImpactRetryInterval;
                }
                continue;
            }

            PhysGrabObject body = GetBody(target);
            if (body != null) GameReflect.TeleportSafe(body, point, target.transform.rotation, "RaidHack");
        }

        // Unconditional, not "only when the table is bigger than the live list": those two counts
        // can match while the contents differ (someone dies as someone else joins), which left
        // dead players pinned in the table indefinitely and, across levels, kept destroyed
        // PlayerAvatars alive as dictionary keys.
        if (_freezePoints.Count > 0)
        {
            List<PlayerAvatar> stale = null;
            foreach (PlayerAvatar key in _freezePoints.Keys)
                if (key == null || !others.Contains(key)) (stale ??= new()).Add(key);

            if (stale != null)
            {
                foreach (PlayerAvatar k in stale)
                {
                    _freezePoints.Remove(k);
                    _freezeNextImpact.Remove(k);
                }
            }
        }
    }

    // Unlike Cage/Yeet, items don't need any tumble dance - ItemStealHack already confirmed
    // Teleport() alone is enough to yank a held item out of someone's grip (it releases once the
    // teleported distance clears their own grab range). Same call, opposite destination: a random
    // point a few meters from THEM instead of pulled to us.
    public static void ForceDropAll()
    {
        PlayerAvatar local = PlayerAvatar.instance;
        if (local == null) return;

        GameDirector director = GameDirector.instance;
        if (director == null) return;

        List<PlayerAvatar> players = GameReflect.Get<List<PlayerAvatar>>(director, "PlayerList");
        if (players == null) return;

        int count = 0;
        foreach (PlayerAvatar p in players)
        {
            if (p == null || p == local) continue;

            PhysGrabber grabber = GameReflect.Get<PhysGrabber>(p, "physGrabber");
            if (grabber == null || !GameReflect.Get<bool>(grabber, "grabbed")) continue;

            PhysGrabObject held = GameReflect.Get<PhysGrabObject>(grabber, "grabbedPhysGrabObject");
            if (held == null) continue;

            Vector2 dir2 = Random.insideUnitCircle.normalized;
            Vector3 pos = p.transform.position + new Vector3(dir2.x, 0f, dir2.y) * 5f;
            GameReflect.TeleportSafe(held, pos, held.transform.rotation, "RaidHack");
            count++;
        }

        Plugin.Log.LogInfo($"Force Drop All: yanked {count} held item(s) loose.");
    }

    // "Frame for Shop Keeper" - shop-only. PunManager.PlayerCausingRuckusRPC (confirmed in the
    // decompiled source) has no OwnerOnlyRPC/MasterOnlyRPC/MasterAndOwnerOnlyRPC check anywhere
    // in its own body, same class of hole as PlayerTumble.TumbleImpactRPC - it only checks
    // SemiFunc.IsMasterClientOrSingleplayer() internally, which is about which machine is
    // executing it, not who sent it. Every client receiving it runs the same method; whichever
    // one really is master calls ShopKeeper.instance.ShootAtPlayer(_player) for real, aggroing
    // the shop's own security NPC onto whoever's PhotonViewID we passed in - since the NPC deals
    // real authoritative damage on its own, this is real damage on the target that doesn't run
    // into the "guest can't hurt another player directly" wall HurtOtherRPC enforces everywhere
    // else, because we're not the one dealing it. Matches the vanilla call site exactly
    // (SemiFunc.PlayerCausingRuckus uses the same RpcTarget.MasterClient, not All - only the
    // master's own execution does anything here, no need for every client to also run it).
    public static void FrameForShopKeeper()
    {
        if (!SemiFunc.RunIsShop())
        {
            Plugin.Log.LogInfo("Frame for Shop Keeper had no effect - only works while in the shop.");
            return;
        }

        PlayerAvatar target = FindTarget();
        if (target == null || target.photonView == null)
        {
            Plugin.Log.LogInfo("Frame for Shop Keeper had no effect - no target player selected.");
            return;
        }

        PunManager punManager = PunManager.instance;
        if (punManager == null) return;

        // The keeper ignores every ShootAtPlayer request while it's switched off, so turn it on
        // first - otherwise this button silently does nothing whenever someone flipped the switch.
        if (ShopKeeperWake.IsAsleep())
        {
            ShopKeeperWake.TryWake();
            Plugin.Log.LogInfo("Frame for Shop Keeper: keeper was off - turning it on, press again once it's up.");
            return;
        }

        if (GameManager.Multiplayer())
        {
            PhotonView view = GameReflect.Get<PhotonView>(punManager, "photonView");
            view?.RPC("PlayerCausingRuckusRPC", RpcTarget.MasterClient, target.photonView.ViewID);
        }
        else
        {
            punManager.PlayerCausingRuckusRPC(target.photonView.ViewID);
        }
    }

    // "Force Seat" - vehicle maps only. Confirmed in the decompiled ItemVehicle.cs:
    // RequestSeatRPC has no sender check of its own (same class of hole as TumbleImpactRPC/
    // PlayerCausingRuckusRPC), only IsMasterClientOrSingleplayer() internally - every client
    // receiving it runs that same check, and whichever one really is master then re-broadcasts
    // SetSeatGrabberRPC itself. That second RPC IS sender-gated (MasterOnlyRPC), but it now
    // genuinely passes because the real master is the one sending it this time, not us. End
    // result: forces the target into the given seat - including a real brief tumble via
    // PlayerTumble.TumbleRequest, the same one the vehicle's own mounting animation uses - from
    // any distance, no need to be anywhere near the vehicle ourselves. Unlike Cage, the target
    // can freely exit the seat themselves right after (normal vehicle exit input still works),
    // so this is a surprise yoink into a car, not a lasting lockdown.
    public static void ForceSeat()
    {
        PlayerAvatar target = FindTarget();
        if (target == null || target.photonView == null)
        {
            Plugin.Log.LogInfo("Force Seat had no effect - no target player selected.");
            return;
        }

        ItemVehicle vehicle = Object.FindFirstObjectByType<ItemVehicle>();
        if (vehicle == null)
        {
            Plugin.Log.LogInfo("Force Seat had no effect - no vehicle found on this map.");
            return;
        }

        if (vehicle.seats == null || vehicle.seats.Length == 0)
        {
            Plugin.Log.LogInfo("Force Seat had no effect - that vehicle has no seats.");
            return;
        }

        const int seatIndex = 0; // driver's seat - the most disruptive slot to land them in
        if (GameManager.Multiplayer())
        {
            PhotonView view = GameReflect.Get<PhotonView>(vehicle, "photonView");
            view?.RPC("RequestSeatRPC", RpcTarget.MasterClient, seatIndex, target.photonView.ViewID);
        }
        else
        {
            // MethodInfo.Invoke on Mono doesn't fill in RequestSeatRPC's trailing optional
            // PhotonMessageInfo the way a normal call site does - throws
            // TargetParameterCountException every time this singleplayer branch actually runs
            // (confirmed live via the debug bridge testing an unrelated RPC with the same shape -
            // never caught here before because it was never exercised in solo testing). Needs the
            // explicit default(PhotonMessageInfo), same as UtilityHolesHack.cs/WorldHolesHack2.cs
            // already do for their own RPC calls.
            GameReflect.Invoke(vehicle, "RequestSeatRPC", seatIndex, target.photonView.ViewID, default(PhotonMessageInfo));
        }
    }

    private const float CursedHandsInterval = 3f;
    private static float _nextCursedHands;

    public static void ApplyCursedHands()
    {
        if (!CheatState.CursedHands.Value) return;
        if (Time.unscaledTime < _nextCursedHands) return;
        _nextCursedHands = Time.unscaledTime + CursedHandsInterval;

        PlayerAvatar target = FindTarget();
        if (target == null || target.photonView == null) return;

        List<ItemEquippable> candidates = new();
        foreach (ItemEquippable item in Object.FindObjectsByType<ItemEquippable>(FindObjectsSortMode.None))
        {
            if (item != null && !item.IsEquipped()) candidates.Add(item);
        }
        if (candidates.Count == 0) return;

        ItemEquippable pick = candidates[Random.Range(0, candidates.Count)];
        GrabDenialHack.ForceEquip(pick, target, 0);
    }

    private const float SeatShuffleInterval = 4f;
    private static float _nextSeatShuffle;

    public static void ApplySeatShuffle()
    {
        if (!CheatState.SeatShuffle.Value) return;
        if (Time.unscaledTime < _nextSeatShuffle) return;
        _nextSeatShuffle = Time.unscaledTime + SeatShuffleInterval;

        PlayerAvatar target = FindTarget();
        if (target == null || target.photonView == null) return;

        List<ItemVehicle> vehicles = new();
        foreach (ItemVehicle v in Object.FindObjectsByType<ItemVehicle>(FindObjectsSortMode.None))
        {
            if (v != null && v.seats != null && v.seats.Length > 0) vehicles.Add(v);
        }
        if (vehicles.Count == 0) return;

        ItemVehicle vehicle = vehicles[Random.Range(0, vehicles.Count)];
        int seatIndex = Random.Range(0, vehicle.seats.Length);

        if (GameManager.Multiplayer())
        {
            PhotonView view = GameReflect.Get<PhotonView>(vehicle, "photonView");
            view?.RPC("RequestSeatRPC", RpcTarget.MasterClient, seatIndex, target.photonView.ViewID);
        }
        else
        {
            // MethodInfo.Invoke on Mono doesn't fill in RequestSeatRPC's trailing optional
            // PhotonMessageInfo the way a normal call site does - throws
            // TargetParameterCountException every time this singleplayer branch actually runs
            // (confirmed live via the debug bridge testing an unrelated RPC with the same shape -
            // never caught here before because it was never exercised in solo testing). Needs the
            // explicit default(PhotonMessageInfo), same as UtilityHolesHack.cs/WorldHolesHack2.cs
            // already do for their own RPC calls.
            GameReflect.Invoke(vehicle, "RequestSeatRPC", seatIndex, target.photonView.ViewID, default(PhotonMessageInfo));
        }
    }

    // Shared target picker with the Fake Monster Sound tool - both are "pick a player, do
    // something to them from a distance" actions, no reason to keep two separate pickers.
    private static PlayerAvatar FindTarget()
    {
        string targetName = CheatState.FakeSoundTargetName.Value;
        if (string.IsNullOrEmpty(targetName) || targetName == "None") return null;

        GameDirector director = GameDirector.instance;
        List<PlayerAvatar> players = director != null ? GameReflect.Get<List<PlayerAvatar>>(director, "PlayerList") : null;
        if (players == null) return null;

        foreach (PlayerAvatar p in players)
        {
            if (p == null) continue;
            if (GameReflect.Get<bool>(p, "deadSet")) continue;
            if (GameReflect.Get<string>(p, "playerName") == targetName) return p;
        }
        return null;
    }

    private static PhysGrabObject GetBody(PlayerAvatar target)
    {
        PlayerTumble tumble = GameReflect.Get<PlayerTumble>(target, "tumble");
        return tumble != null ? GameReflect.Get<PhysGrabObject>(tumble, "physGrabObject") : null;
    }
}
