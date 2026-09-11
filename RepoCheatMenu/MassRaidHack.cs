using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace RepoCheatMenu;

// Ten whole-lobby/high-impact raid tools, all built from mechanics already confirmed elsewhere
// in this mod rather than new guesses: KnockdownHack.TriggerImpact + PhysGrabObject.Teleport()
// for moving players (RaidHack's Cage/Yeet), the same Teleport() loophole for items/carts/
// valuables (LootMagnet/CartHack/ItemStealHack), PunManager.PlayerCausingRuckusRPC for framing
// someone to the shop NPC (RaidHack.FrameForShopKeeper), and ItemVehicle.RequestSeatRPC for
// forcing a seat (RaidHack.ForceSeat). Nothing here needed new reverse-engineering - it's the
// same handful of confirmed holes applied lobby-wide instead of to one picked target.
internal static class MassRaidHack
{
    private static PlayerAvatar Local => PlayerAvatar.instance;

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

    private static PhysGrabObject GetBody(PlayerAvatar target)
    {
        PlayerTumble tumble = GameReflect.Get<PlayerTumble>(target, "tumble");
        return tumble != null ? GameReflect.Get<PhysGrabObject>(tumble, "physGrabObject") : null;
    }

    // 1) Mass Seat Kidnap - flattens every seat of every vehicle on the map into one list, then
    // hands each other player their own slot via RequestSeatRPC (no sender check, same hole
    // RaidHack.ForceSeat already uses for one target). Whoever doesn't fit just doesn't get seated.
    public static void MassSeatKidnap()
    {
        PlayerAvatar local = Local;
        if (local == null) return;

        List<PlayerAvatar> others = GetAliveOthers(local);
        if (others.Count == 0)
        {
            Plugin.Log.LogInfo("Mass Seat Kidnap had no effect - no other players.");
            return;
        }

        var slots = new List<(ItemVehicle vehicle, int seatIndex)>();
        foreach (ItemVehicle vehicle in Object.FindObjectsByType<ItemVehicle>(FindObjectsSortMode.None))
        {
            if (vehicle == null || vehicle.seats == null) continue;
            for (int i = 0; i < vehicle.seats.Length; i++)
                slots.Add((vehicle, i));
        }

        if (slots.Count == 0)
        {
            Plugin.Log.LogInfo("Mass Seat Kidnap had no effect - no vehicle seats on this map.");
            return;
        }

        bool multiplayer = GameManager.Multiplayer();
        int seated = 0;
        for (int i = 0; i < others.Count && i < slots.Count; i++)
        {
            PlayerAvatar target = others[i];
            if (target.photonView == null) continue;
            (ItemVehicle vehicle, int seatIndex) = slots[i];

            if (multiplayer)
            {
                PhotonView view = GameReflect.Get<PhotonView>(vehicle, "photonView");
                view?.RPC("RequestSeatRPC", RpcTarget.MasterClient, seatIndex, target.photonView.ViewID);
            }
            else
            {
                // MethodInfo.Invoke on Mono doesn't fill in RequestSeatRPC's trailing optional
                // PhotonMessageInfo the way a normal call site does - throws
                // TargetParameterCountException every time this singleplayer branch actually runs
                // (confirmed live via the debug bridge while testing an unrelated RPC with the
                // same shape - this call has the identical gap, just never exercised in solo
                // testing before). Needs the explicit default(PhotonMessageInfo), same as
                // UtilityHolesHack.cs/WorldHolesHack2.cs already do for their own RPC calls.
                GameReflect.Invoke(vehicle, "RequestSeatRPC", seatIndex, target.photonView.ViewID, default(PhotonMessageInfo));
            }
            seated++;
        }

        Plugin.Log.LogInfo($"Mass Seat Kidnap: seated {seated}/{others.Count} player(s) across {slots.Count} seat(s).");
    }

    // 2) Frame All for Shop Keeper - same PlayerCausingRuckusRPC hole as RaidHack's single-target
    // version, just fired at every other player in the room at once. Shop only.
    public static void FrameAllForShopKeeper()
    {
        if (!SemiFunc.RunIsShop())
        {
            Plugin.Log.LogInfo("Frame All had no effect - only works while in the shop.");
            return;
        }

        PlayerAvatar local = Local;
        if (local == null) return;

        List<PlayerAvatar> others = GetAliveOthers(local);
        if (others.Count == 0) return;

        PunManager punManager = PunManager.instance;
        if (punManager == null) return;

        // Same gate as the single-target version - an off keeper discards every request.
        if (ShopKeeperWake.IsAsleep())
        {
            ShopKeeperWake.TryWake();
            Plugin.Log.LogInfo("Frame All: keeper was off - turning it on, press again once it's up.");
            return;
        }

        bool multiplayer = GameManager.Multiplayer();
        PhotonView view = multiplayer ? GameReflect.Get<PhotonView>(punManager, "photonView") : null;

        int framed = 0;
        foreach (PlayerAvatar target in others)
        {
            if (target.photonView == null) continue;
            if (multiplayer) view?.RPC("PlayerCausingRuckusRPC", RpcTarget.MasterClient, target.photonView.ViewID);
            else punManager.PlayerCausingRuckusRPC(target.photonView.ViewID);
            framed++;
        }

        Plugin.Log.LogInfo($"Frame All: sicced the shop keeper on {framed} player(s).");
    }

    // 3) Item Purge - one big sweep, scatters every ItemAttributes-tagged prop in the level (held
    // or not - Teleport() rips it out of whoever's hands same as ItemStealHack/ForceDropAll) to a
    // random nearby point underground. One-shot "wipe the level's gear" - Mass Disarm (below) is
    // the sustained version that only bothers with what's currently held.
    public static void ItemPurge()
    {
        int count = 0;
        foreach (ItemAttributes attributes in Object.FindObjectsByType<ItemAttributes>(FindObjectsSortMode.None))
        {
            if (attributes == null) continue;
            PhysGrabObject physObject = attributes.GetComponent<PhysGrabObject>();
            if (physObject == null) continue;

            Vector2 dir2 = Random.insideUnitCircle.normalized;
            Vector3 pos = physObject.transform.position + new Vector3(dir2.x, 0f, dir2.y) * 10f + Vector3.down * 30f;
            physObject.Teleport(pos, physObject.transform.rotation);
            count++;
        }
        Plugin.Log.LogInfo($"Item Purge: scattered {count} item(s).");
    }

    // 4) Mass Disarm - continuous version of RaidHack.ForceDropAll: every tick, rips whatever
    // anyone's currently holding out of their hands. Toggled on, nobody keeps a tool/weapon the
    // whole time.
    private const float MassDisarmInterval = 0.5f;
    private static float _massDisarmNextTime;

    public static void ApplyMassDisarm()
    {
        if (!CheatState.MassDisarm.Value) return;
        if (Time.unscaledTime < _massDisarmNextTime) return;
        _massDisarmNextTime = Time.unscaledTime + MassDisarmInterval;

        PlayerAvatar local = Local;
        if (local == null) return;

        List<PlayerAvatar> others = GetAliveOthers(local);
        foreach (PlayerAvatar p in others)
        {
            PhysGrabber grabber = GameReflect.Get<PhysGrabber>(p, "physGrabber");
            if (grabber == null || !GameReflect.Get<bool>(grabber, "grabbed")) continue;

            PhysGrabObject held = GameReflect.Get<PhysGrabObject>(grabber, "grabbedPhysGrabObject");
            if (held == null) continue;

            Vector2 dir2 = Random.insideUnitCircle.normalized;
            Vector3 pos = p.transform.position + new Vector3(dir2.x, 0f, dir2.y) * 5f;
            held.Teleport(pos, held.transform.rotation);
        }
    }

    // 5) Yeet Storm - toggle that automatically picks a random other player every few seconds and
    // yeets them, no target picker needed. Same TriggerImpact-then-wait-for-tumble-then-Teleport
    // handshake as RaidHack.Yeet, just self-driving.
    private const float YeetStormInterval = 6f;
    private const float YeetStormTimeout = 1.5f;
    private static float _yeetStormNextTime;
    private static PlayerAvatar _yeetStormPending;
    private static float _yeetStormDeadline;

    public static void ApplyYeetStorm()
    {
        if (!CheatState.YeetStorm.Value)
        {
            _yeetStormPending = null;
            return;
        }

        PlayerAvatar local = Local;
        if (local == null) return;

        if (_yeetStormPending == null && Time.unscaledTime >= _yeetStormNextTime)
        {
            List<PlayerAvatar> others = GetAliveOthers(local);
            if (others.Count > 0)
            {
                PlayerAvatar target = others[Random.Range(0, others.Count)];
                KnockdownHack.TriggerImpact(local, target);
                _yeetStormPending = target;
                _yeetStormDeadline = Time.unscaledTime + YeetStormTimeout;
            }
            _yeetStormNextTime = Time.unscaledTime + YeetStormInterval;
        }

        if (_yeetStormPending == null) return;
        if (Time.unscaledTime >= _yeetStormDeadline) { _yeetStormPending = null; return; }
        if (!GameReflect.Get<bool>(_yeetStormPending, "isTumbling")) return;

        PhysGrabObject body = GetBody(_yeetStormPending);
        if (body != null)
        {
            Vector2 dir2 = Random.insideUnitCircle.normalized;
            Vector3 pos = _yeetStormPending.transform.position + new Vector3(dir2.x, 0f, dir2.y) * 80f + Vector3.up * 40f;
            body.Teleport(pos, _yeetStormPending.transform.rotation);
        }
        _yeetStormPending = null;
    }

    // 6) Cart Yeet - same Teleport() loophole CartHack already rides, just scatters every cart
    // instead of pulling it to extraction.
    public static void CartYeetAll()
    {
        int count = 0;
        foreach (PhysGrabCart cart in CartHack.GetCarts())
        {
            if (cart == null) continue;
            PhysGrabObject physObject = cart.GetComponent<PhysGrabObject>();
            if (physObject == null) continue;

            Vector2 dir2 = Random.insideUnitCircle.normalized;
            Vector3 pos = physObject.transform.position + new Vector3(dir2.x, 0f, dir2.y) * 15f + Vector3.down * 40f;
            physObject.Teleport(pos, physObject.transform.rotation);
            count++;
        }
        Plugin.Log.LogInfo($"Cart Yeet: scattered {count} cart(s).");
    }

    // 7) Valuable Blackhole - inverse of Loot Magnet: instead of pulling every valuable to
    // extraction, sends them all to one shared hidden point far under the map. Nothing gets
    // destroyed (unlike Destroy All Valuables) - it's still out there, just gone for the run.
    public static void ValuableBlackhole()
    {
        PlayerAvatar local = Local;
        if (local == null) return;

        ValuableDirector director = ValuableDirector.instance;
        if (director == null) return;

        List<ValuableObject> valuables = GameReflect.Get<List<ValuableObject>>(director, "valuableList");
        if (valuables == null) return;

        Vector3 hidePoint = local.transform.position + Vector3.down * 80f;
        int count = 0;
        foreach (ValuableObject valuable in valuables)
        {
            if (valuable == null) continue;
            PhysGrabObject physObject = GameReflect.Get<PhysGrabObject>(valuable, "physGrabObject");
            if (physObject == null) continue;

            Vector2 dir2 = Random.insideUnitCircle.normalized;
            physObject.Teleport(hidePoint + new Vector3(dir2.x, 0f, dir2.y) * 3f, physObject.transform.rotation);
            count++;
        }
        Plugin.Log.LogInfo($"Valuable Blackhole: hid {count} valuable(s).");
    }

    // 8) Extraction Camper Punish - toggle that watches every active extraction point; whoever's
    // standing within range gets caged (same pin-in-place mechanic as RaidHack's Cage) until they
    // back off or this is turned off. Punishes camping the exit instead of actually playing.
    private const float CamperRadius = 8f;
    private static readonly Dictionary<PlayerAvatar, Vector3> _camperPoints = new();
    private static readonly Dictionary<PlayerAvatar, float> _camperNextImpact = new();

    public static void ApplyExtractionCamperPunish()
    {
        if (!CheatState.ExtractionCamperPunish.Value)
        {
            _camperPoints.Clear();
            _camperNextImpact.Clear();
            return;
        }

        PlayerAvatar local = Local;
        if (local == null) return;

        List<PlayerAvatar> others = GetAliveOthers(local);
        List<PlayerAvatar> campers = new();
        foreach (PlayerAvatar p in others)
        {
            ExtractionPoint ep = EconomyHack.FindActiveExtractionPoint(p.transform.position);
            if (ep == null || ep.extractionArea == null) continue;
            if (Vector3.Distance(p.transform.position, ep.extractionArea.transform.position) <= CamperRadius)
                campers.Add(p);
        }

        foreach (PlayerAvatar target in campers)
        {
            if (!_camperPoints.TryGetValue(target, out Vector3 point))
            {
                point = target.transform.position + Vector3.down * 60f;
                _camperPoints[target] = point;
            }

            bool tumbling = GameReflect.Get<bool>(target, "isTumbling");
            if (!tumbling)
            {
                if (!_camperNextImpact.TryGetValue(target, out float next) || Time.unscaledTime >= next)
                {
                    KnockdownHack.TriggerImpact(local, target);
                    _camperNextImpact[target] = Time.unscaledTime + 1f;
                }
                continue;
            }

            PhysGrabObject body = GetBody(target);
            if (body != null && (body.transform.position - point).sqrMagnitude > 0.25f)
                body.Teleport(point, target.transform.rotation);
        }

        if (_camperPoints.Count > 0)
        {
            List<PlayerAvatar> stale = null;
            foreach (PlayerAvatar key in _camperPoints.Keys)
                if (!campers.Contains(key)) (stale ??= new()).Add(key);

            if (stale != null)
            {
                foreach (PlayerAvatar k in stale)
                {
                    _camperPoints.Remove(k);
                    _camperNextImpact.Remove(k);
                }
            }
        }
    }

    // 9) Instant Group Knockdown - fires KnockdownHack.TriggerImpact at every other player in the
    // same tick, no homing/distance/wait required (the RPC has no proximity check - confirmed in
    // KnockdownHack's own class comment). Knocks the whole lobby down from wherever you're
    // standing, unlike Rывок which does it one at a time by physically approaching each one.
    // A single TriggerImpact call applies a fairly weak FreezeForces push (magnitude 5 for 0.15s)
    // - Cage/Yeet/Camper Punish all retry it every ~1s until isTumbling actually flips instead of
    // firing once and assuming it landed, because on real network latency (or just bad luck on
    // which physics step it lands on) one shot doesn't always clear the >4f rbVelocity threshold
    // the real hurtCollider/tumble activation needs. This fired-and-forgot the first time, which
    // is why it could silently do nothing - now it keeps retrying each target for a few seconds.
    private const float GroupKnockdownRetryInterval = 0.6f;
    private const float GroupKnockdownTimeout = 3f;
    private static readonly List<PlayerAvatar> _pendingGroupKnockdown = new();
    private static readonly Dictionary<PlayerAvatar, float> _groupKnockdownNextImpact = new();
    private static float _groupKnockdownDeadline;

    public static void InstantGroupKnockdown()
    {
        PlayerAvatar local = Local;
        if (local == null) return;

        List<PlayerAvatar> others = GetAliveOthers(local);
        if (others.Count == 0)
        {
            Plugin.Log.LogInfo("Instant Group Knockdown had no effect - no other players.");
            return;
        }

        _pendingGroupKnockdown.Clear();
        _groupKnockdownNextImpact.Clear();
        foreach (PlayerAvatar target in others)
        {
            KnockdownHack.TriggerImpact(local, target);
            _pendingGroupKnockdown.Add(target);
            _groupKnockdownNextImpact[target] = Time.unscaledTime + GroupKnockdownRetryInterval;
        }
        _groupKnockdownDeadline = Time.unscaledTime + GroupKnockdownTimeout;

        Plugin.Log.LogInfo($"Instant Group Knockdown: hit {others.Count} player(s), confirming...");
    }

    // Ticked every frame (RunSafe in Patches.cs) - retries anyone who hasn't actually started
    // tumbling yet, drops them once they have (or once the timeout gives up on them).
    public static void ApplyGroupKnockdownPending()
    {
        if (_pendingGroupKnockdown.Count == 0) return;

        PlayerAvatar local = Local;
        if (local == null) return;

        bool expired = Time.unscaledTime >= _groupKnockdownDeadline;
        for (int i = _pendingGroupKnockdown.Count - 1; i >= 0; i--)
        {
            PlayerAvatar target = _pendingGroupKnockdown[i];
            if (target == null || expired || GameReflect.Get<bool>(target, "isTumbling"))
            {
                _pendingGroupKnockdown.RemoveAt(i);
                _groupKnockdownNextImpact.Remove(target);
                continue;
            }

            // TryGetValue, not the indexer: a missing key would throw KeyNotFoundException here,
            // and because this runs every frame under RunSafe that turns into a silent per-frame
            // exception rather than a visible failure. Treating "no entry" as "fire now" is also
            // the behaviour we want if the two collections ever drift apart.
            if (!_groupKnockdownNextImpact.TryGetValue(target, out float next) || Time.unscaledTime >= next)
            {
                KnockdownHack.TriggerImpact(local, target);
                _groupKnockdownNextImpact[target] = Time.unscaledTime + GroupKnockdownRetryInterval;
            }
        }
    }

    // Arena (weapons) Auto-Win - confirmed in the decompiled Arena.cs (the falling-floor PVP mode
    // with melee/gun pickups - "SpawnMidWeapons", States.Level1-3/Falling, distinct from the kart
    // race in ArenaRace.cs): every RPC (StateSetRPC, PlayerKilledRPC, CrownGrabRPC, ...) is
    // properly MasterOnlyRPC-gated, no forging a win there either. Same answer as the race though -
    // don't fake the result, physically cause it. Every other player is a PlayerAvatar like
    // anywhere else, so the exact same TriggerImpact-then-Teleport handshake Cage/Yeet/Freeze
    // already rely on works here too: knock them into real tumble state, then drop them off the
    // platform into the pit below instead of at a chosen point. Only runs while Arena.instance
    // exists (i.e. actually in this specific mode), never touches you.
    private const float ArenaWeaponsInterval = 3f;
    private const float ArenaWeaponsDropDepth = 30f;
    private static readonly Dictionary<PlayerAvatar, float> _arenaWeaponsNextImpact = new();
    private static float _arenaWeaponsNextSweep;

    public static void ApplyArenaWeaponsSabotage()
    {
        if (!CheatState.ArenaWeaponsSabotage.Value)
        {
            _arenaWeaponsNextImpact.Clear();
            return;
        }

        if (Arena.instance == null) return; // only the weapons-arena mode, not the kart race or a normal level

        PlayerAvatar local = Local;
        if (local == null) return;

        if (Time.unscaledTime < _arenaWeaponsNextSweep) return;
        _arenaWeaponsNextSweep = Time.unscaledTime + ArenaWeaponsInterval;

        foreach (PlayerAvatar target in GetAliveOthers(local))
        {
            bool tumbling = GameReflect.Get<bool>(target, "isTumbling");
            if (!tumbling)
            {
                if (!_arenaWeaponsNextImpact.TryGetValue(target, out float next) || Time.unscaledTime >= next)
                {
                    KnockdownHack.TriggerImpact(local, target);
                    _arenaWeaponsNextImpact[target] = Time.unscaledTime + 1f;
                }
                continue;
            }

            PhysGrabObject body = GetBody(target);
            body?.Teleport(target.transform.position + Vector3.down * ArenaWeaponsDropDepth, target.transform.rotation);
        }
    }

    // Arena "Legit" Auto-Win - same race, but instead of directly teleporting an opponent's kart
    // (which looks exactly like what it is the moment anyone's watching), this leaves their
    // vehicle alone entirely and instead drops a real physical obstacle - any other PhysGrabObject
    // prop found on the track, same Teleport() loophole as everything else - a few meters ahead
    // of them along their own forward direction. They just plow into "something that happened to
    // be there" like a normal racing mishap; nothing about their own kart's position/velocity is
    // ever touched by us. Best-effort: needs actual movable props to exist near the track to use
    // as obstacles, and I can't verify how convincingly the collision itself reads without
    // testing it live - tune ArenaLegitAheadDistance if it's landing too close/far to matter.
    private const float ArenaLegitInterval = 2.5f;
    private const float ArenaLegitAheadDistance = 8f;
    private static float _nextLegitSweep;

    public static void ApplyArenaLegitSabotage()
    {
        if (!CheatState.ArenaLegitSabotage.Value) return;
        if (ArenaRace.instance == null) return;

        PlayerAvatar local = Local;
        if (local == null) return;

        if (Time.unscaledTime < _nextLegitSweep) return;
        _nextLegitSweep = Time.unscaledTime + ArenaLegitInterval;

        List<ItemVehicle> opponentVehicles = new();
        foreach (ItemVehicle vehicle in Object.FindObjectsByType<ItemVehicle>(FindObjectsSortMode.None))
        {
            if (vehicle == null || vehicle.seats == null || vehicle.seats.Length == 0) continue;
            PlayerAvatar driver = vehicle.seats[0].seatedPlayer;
            if (driver == null || driver == local) continue;
            opponentVehicles.Add(vehicle);
        }
        if (opponentVehicles.Count == 0) return;

        List<PhysGrabObject> obstacles = new();
        foreach (PhysGrabObject obj in Object.FindObjectsByType<PhysGrabObject>(FindObjectsSortMode.None))
        {
            if (obj == null || obj.GetComponent<ItemVehicle>() != null) continue; // never move an actual kart here
            obstacles.Add(obj);
        }
        if (obstacles.Count == 0)
        {
            Plugin.Log.LogInfo("Arena Legit Sabotage had no effect - no movable props found near the track.");
            return;
        }

        int oi = 0;
        foreach (ItemVehicle vehicle in opponentVehicles)
        {
            if (oi >= obstacles.Count) break;
            PhysGrabObject obstacle = obstacles[oi++];

            Vector3 aheadPoint = vehicle.transform.position + vehicle.transform.forward * ArenaLegitAheadDistance;
            Quaternion blockFacing = Quaternion.LookRotation(vehicle.transform.right, Vector3.up);
            obstacle.Teleport(aheadPoint, blockFacing);
        }
    }

    // Arena Auto-Win - confirmed in the decompiled ArenaRace.cs: every single one of its RPCs
    // (SetWinnerRPC, PiecePassedRPC, PlayerEliminatedFromRaceRPC, ...) is properly MasterOnlyRPC-
    // gated, no forging a win directly. But ItemVehicle (the race kart itself) carries the same
    // PhysGrabObject every cart/valuable/enemy does - confirmed via its "physGrabObject" field -
    // so we don't need to forge anything: physically dropping every opponent's kart off the track
    // via the same Teleport() loophole makes the master's own legitimate race logic register them
    // as behind/off-course for real. Only touches vehicles whose driver seat (index 0) is someone
    // else - never our own.
    private const float ArenaSabotageInterval = 2.5f;
    private static float _nextArenaSabotageSweep;

    public static void ApplyArenaSabotage()
    {
        if (!CheatState.ArenaSabotage.Value) return;
        if (!SemiFunc.RunIsArena()) return;
        if (Time.unscaledTime < _nextArenaSabotageSweep) return;
        _nextArenaSabotageSweep = Time.unscaledTime + ArenaSabotageInterval;

        PlayerAvatar local = Local;
        if (local == null) return;

        foreach (ItemVehicle vehicle in Object.FindObjectsByType<ItemVehicle>(FindObjectsSortMode.None))
        {
            if (vehicle == null || vehicle.seats == null || vehicle.seats.Length == 0) continue;

            PlayerAvatar driver = vehicle.seats[0].seatedPlayer;
            if (driver == null || driver == local) continue;

            PhysGrabObject physObject = GameReflect.Get<PhysGrabObject>(vehicle, "physGrabObject");
            if (physObject == null) continue;

            Vector3 pos = vehicle.transform.position + Vector3.down * 30f;
            physObject.Teleport(pos, vehicle.transform.rotation);
        }
    }

    // 10) NUKE - one button aimed at the PEOPLE, not their stuff: knocks the whole lobby down and
    // rips whatever everyone's holding out of their hands, all at once.
    // Deliberately does NOT touch loot any more - Item Purge, Cart Yeet and Valuable Blackhole
    // used to be part of this, but scattering/hiding valuables costs the whole run its payout
    // (including yours) and takes ages to undo. Those three are still there as their own separate
    // buttons for when that's actually what you want. Force Drop only shoves held items a few
    // metres away - nothing is destroyed or hidden, they're picked straight back up.
    public static void Nuke()
    {
        InstantGroupKnockdown();
        RaidHack.ForceDropAll();
        Plugin.Log.LogInfo("NUKE: group knockdown + force drop fired together (loot untouched).");
    }
}
