using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace RepoCheatMenu;

// Three newly found holes, all about taking things away from people rather than moving them.
//
// 1) PhysGrabber.ReleaseObjectRPC(bool physGrabEnded, float _disableTimer, int _releaseObjectViewID)
//    Confirmed: no sender check of any kind, and its whole body is wrapped in "if (isLocal)" -
//    meaning it does nothing on anyone's machine except the OWNER of that grabber. Send it on a
//    victim's PhysGrabber view to RpcTarget.All and their own client drops whatever they're
//    holding AND sets grabDisableTimer = whatever we passed. That timer is a hard local lockout:
//    they cannot grab anything at all until it expires. This is strictly better than the existing
//    Teleport()-based disarm, which only yanks the item away and lets them grab it right back.
//    Vanilla itself uses this exact call (ExtractionPoint does
//    "photonView.RPC(\"ReleaseObjectRPC\", RpcTarget.All, true, 1.5f, viewID)"), so the arguments
//    are copied from a real call site, not guessed.
//
// 2) ItemEquippable.RPC_RequestEquip(int spotIndex, int playerViewID)
//    No check in its own body; it relays to RPC_UpdateItemState, which IS gated
//    (MasterAndOwnerOnlyRPC) - but the relay is sent BY THE MASTER, so it passes for real. Exact
//    same relay-through-the-real-master shape as ItemVehicle.RequestSeatRPC. Result: force an
//    item into any player's inventory slot from anywhere.
//
// 3) ItemEquippable.RPC_StartUnequip(int requestingPlayerId)
//    Also unchecked, and gated only by "ownerPlayerId == requestingPlayerId &&
//    PhysGrabber.instance.photonView.ViewID == ownerPlayerId" - i.e. it runs on the owner's own
//    machine. Send it to All with their grabber's view ID and their client unequips it itself.
//
// 4) ItemMelee.EnemyOrPVPSwingHitRPC(bool) / SwingHitRPC(bool)
//    Neither has a sender check; both subtract durabilityDrainOnEnemiesAndPVP from that weapon's
//    battery and trigger the deduction effect. Spammed, this burns any melee weapon in the level
//    down to dead without ever touching its owner.
internal static class GrabDenialHack
{
    // Long enough to be genuinely disruptive, short enough that the re-send loop below keeps it
    // topped up rather than one press locking someone out for a minute with no way to notice.
    private const float LockSeconds = 4f;
    private const float LockRefreshInterval = 2.5f;

    private static void ForceRelease(PlayerAvatar target, float disableSeconds)
    {
        PhysGrabber grabber = PlayerTargets.GrabberOf(target);
        if (grabber == null || grabber.photonView == null) return;

        if (GameManager.Multiplayer())
            grabber.photonView.RPC("ReleaseObjectRPC", RpcTarget.All, true, disableSeconds, -1);
        else
            grabber.ReleaseObjectRPC(true, disableSeconds, -1);
    }

    // 10) Drop whatever the picked target holds and lock their grab for a few seconds.
    public static void DisarmTarget()
    {
        PlayerAvatar target = PlayerTargets.Picked();
        if (target == null)
        {
            Plugin.Log.LogInfo("Disarm had no effect - no target selected.");
            return;
        }
        ForceRelease(target, LockSeconds);
        Plugin.Log.LogInfo($"Disarmed {PlayerTargets.NameOf(target)} and locked their grab for {LockSeconds}s.");
    }

    // 11) Same for everyone at once.
    public static void DisarmAll()
    {
        List<PlayerAvatar> others = PlayerTargets.AliveOthers();
        foreach (PlayerAvatar p in others) ForceRelease(p, LockSeconds);
        Plugin.Log.LogInfo($"Disarmed {others.Count} player(s).");
    }

    // 12) Toggle - keeps the picked target permanently unable to pick anything up.
    private static float _nextLockTarget;

    public static void ApplyGrabLockTarget()
    {
        if (!CheatState.GrabLockTarget.Value) return;
        if (Time.unscaledTime < _nextLockTarget) return;
        _nextLockTarget = Time.unscaledTime + LockRefreshInterval;

        PlayerAvatar target = PlayerTargets.Picked();
        if (target != null) ForceRelease(target, LockSeconds);
    }

    // 13) Toggle - nobody in the lobby can hold anything for as long as this is on.
    private static float _nextLockAll;

    public static void ApplyGrabLockAll()
    {
        if (!CheatState.GrabLockAll.Value) return;
        if (Time.unscaledTime < _nextLockAll) return;
        _nextLockAll = Time.unscaledTime + LockRefreshInterval;

        foreach (PlayerAvatar p in PlayerTargets.AliveOthers()) ForceRelease(p, LockSeconds);
    }

    // --- Equip / unequip ---------------------------------------------------------------------

    // The item already knows who owns it, and RPC_StartUnequip only acts when the ID we send back
    // matches that exact field - so read it off the item instead of guessing which of the owner's
    // two photon views (avatar or grabber) the game happened to store. Private field, so it goes
    // through GameReflect like every other non-public game field in this mod.
    private static int OwnerIdOf(ItemEquippable item) =>
        item != null ? GameReflect.Get<int>(item, "ownerPlayerId") : -1;

    private static void Unequip(ItemEquippable item)
    {
        int ownerId = OwnerIdOf(item);
        if (item == null || ownerId == -1) return;

        if (GameManager.Multiplayer())
            item.photonView.RPC("RPC_StartUnequip", RpcTarget.All, ownerId);
        else
            GameReflect.Invoke(item, "RPC_StartUnequip", ownerId);
    }

    // Was a real bug on the first pass: this unequipped EVERY equipped item in the level no matter
    // whose it was, so "unequip the target" quietly hit the whole lobby (and the "all" version
    // then repeated that once per player). Now it only touches items whose stored owner ID matches
    // this player - checked against both of their photon views, since the game's own two call
    // sites disagree about which one ends up in ownerPlayerId.
    private static int UnequipEverythingOf(PlayerAvatar owner)
    {
        if (owner == null || owner.photonView == null) return 0;

        int avatarView = owner.photonView.ViewID;
        PhysGrabber grabber = PlayerTargets.GrabberOf(owner);
        int grabberView = grabber != null && grabber.photonView != null ? grabber.photonView.ViewID : -1;

        int count = 0;
        foreach (ItemEquippable item in Object.FindObjectsByType<ItemEquippable>(FindObjectsSortMode.None))
        {
            if (item == null || !item.IsEquipped()) continue;

            int ownerId = OwnerIdOf(item);
            if (ownerId != avatarView && ownerId != grabberView) continue;

            Unequip(item);
            count++;
        }
        return count;
    }

    // 14) Rip every equipped item out of the picked target's inventory slots.
    public static void ForceUnequipTarget()
    {
        PlayerAvatar target = PlayerTargets.Picked();
        if (target == null) return;
        int n = UnequipEverythingOf(target);
        Plugin.Log.LogInfo($"Force Unequip: sent {n} unequip(s) at {PlayerTargets.NameOf(target)}.");
    }

    // 15) Everyone.
    public static void ForceUnequipAll()
    {
        int n = 0;
        foreach (PlayerAvatar p in PlayerTargets.AliveOthers()) n += UnequipEverythingOf(p);
        Plugin.Log.LogInfo($"Force Unequip All: sent {n} unequip(s).");
    }

    // 16) Toggle - nothing stays equipped anywhere while this is on.
    private const float UnequipStormInterval = 2f;
    private static float _nextUnequipStorm;

    public static void ApplyUnequipStorm()
    {
        if (!CheatState.UnequipStorm.Value) return;
        if (Time.unscaledTime < _nextUnequipStorm) return;
        _nextUnequipStorm = Time.unscaledTime + UnequipStormInterval;

        foreach (PlayerAvatar p in PlayerTargets.AliveOthers()) UnequipEverythingOf(p);
    }

    internal static void ForceEquip(ItemEquippable item, PlayerAvatar target, int spot)
    {
        if (item == null || target == null || target.photonView == null) return;

        if (GameManager.Multiplayer())
            item.photonView.RPC("RPC_RequestEquip", RpcTarget.MasterClient, spot, target.photonView.ViewID);
        else
            GameReflect.Invoke(item, "RPC_RequestEquip", spot, target.photonView.ViewID);
    }

    // 17) Stuff a loose item into the picked target's slot - they end up carrying something they
    // never picked up (and, in the shop, something they'll be charged for at checkout).
    public static void ForceEquipOnTarget()
    {
        PlayerAvatar target = PlayerTargets.Picked();
        if (target == null) return;

        int count = 0;
        foreach (ItemEquippable item in Object.FindObjectsByType<ItemEquippable>(FindObjectsSortMode.None))
        {
            if (item == null || item.IsEquipped()) continue;
            ForceEquip(item, target, count);
            if (++count >= 3) break; // fill a few slots, not the entire level
        }
        Plugin.Log.LogInfo($"Force Equip: pushed {count} item(s) onto {PlayerTargets.NameOf(target)}.");
    }

    // 18) Same, spread across the whole lobby - one item each, so everyone gets something.
    public static void ForceEquipJunkAll()
    {
        List<PlayerAvatar> others = PlayerTargets.AliveOthers();
        if (others.Count == 0) return;

        int i = 0;
        foreach (ItemEquippable item in Object.FindObjectsByType<ItemEquippable>(FindObjectsSortMode.None))
        {
            if (item == null || item.IsEquipped()) continue;
            ForceEquip(item, others[i % others.Count], 0);
            if (++i >= others.Count) break;
        }
        Plugin.Log.LogInfo($"Force Equip All: handed an item to {i} player(s).");
    }

    // --- Melee durability --------------------------------------------------------------------

    private static void DrainWeapon(ItemMelee melee)
    {
        if (melee == null) return;
        PhotonView view = GameReflect.Get<PhotonView>(melee, "photonView");

        if (GameManager.Multiplayer())
        {
            view?.RPC("EnemyOrPVPSwingHitRPC", RpcTarget.All, false);
            view?.RPC("SwingHitRPC", RpcTarget.All, true);
        }
        else
        {
            melee.EnemyOrPVPSwingHitRPC(false);
            melee.SwingHitRPC(true);
        }
    }

    private static ItemMelee HeldMeleeOf(PlayerAvatar player)
    {
        PhysGrabber grabber = PlayerTargets.GrabberOf(player);
        if (grabber == null || !GameReflect.Get<bool>(grabber, "grabbed")) return null;

        PhysGrabObject held = GameReflect.Get<PhysGrabObject>(grabber, "grabbedPhysGrabObject");
        return held != null ? held.GetComponent<ItemMelee>() : null;
    }

    // 19) Burn the durability of whatever melee weapon the target is holding right now.
    public static void DrainTargetWeapon()
    {
        PlayerAvatar target = PlayerTargets.Picked();
        ItemMelee melee = HeldMeleeOf(target);
        if (melee == null)
        {
            Plugin.Log.LogInfo("Drain Weapon had no effect - target isn't holding a melee weapon.");
            return;
        }

        for (int i = 0; i < 10; i++) DrainWeapon(melee);
        Plugin.Log.LogInfo($"Drained {PlayerTargets.NameOf(target)}'s weapon.");
    }

    // 20) Every melee weapon in the level, once.
    public static void DrainAllWeaponsNow()
    {
        int n = 0;
        foreach (ItemMelee melee in Object.FindObjectsByType<ItemMelee>(FindObjectsSortMode.None))
        {
            if (melee == null) continue;
            DrainWeapon(melee);
            n++;
        }
        Plugin.Log.LogInfo($"Weapon drain: hit {n} melee weapon(s).");
    }

    // 21) Toggle - continuous drain, so nobody in the lobby ever has a charged weapon.
    // Two RPCs per weapon per pass, so the interval is deliberately not tight: a level full of
    // melee weapons at one-second intervals is a lot of traffic for an effect that only needs to
    // out-pace recharging, not be instant.
    private const float DrainInterval = 2.5f;
    private static float _nextDrain;

    public static void ApplyWeaponDrainAll()
    {
        if (!CheatState.WeaponDrainAll.Value) return;
        if (Time.unscaledTime < _nextDrain) return;
        _nextDrain = Time.unscaledTime + DrainInterval;

        foreach (ItemMelee melee in Object.FindObjectsByType<ItemMelee>(FindObjectsSortMode.None))
            DrainWeapon(melee);
    }

    // 22) Reactive toggle - the instant anyone picks anything up, it's taken away again. Cheaper
    // and far more maddening than the blanket lock: they can walk around normally, they just can
    // never actually hold onto anything.
    //
    // Per-player cooldown, not per-frame: this originally fired on every tick a player was
    // holding something, i.e. ~60 RPCs a second per player. This mod has already caused a real
    // Photon server-timeout kick once from per-frame spam, and a release doesn't register
    // instantly anyway (the RPC has to reach them and their own client has to act on it), so
    // re-firing before that round trip completes is pure waste.
    private const float DisarmOnGrabCooldown = 1.2f;
    private static readonly Dictionary<PlayerAvatar, float> _disarmNext = new();

    public static void ApplyDisarmOnGrab()
    {
        if (!CheatState.DisarmOnGrab.Value)
        {
            if (_disarmNext.Count > 0) _disarmNext.Clear();
            return;
        }

        foreach (PlayerAvatar p in PlayerTargets.AliveOthers())
        {
            PhysGrabber grabber = PlayerTargets.GrabberOf(p);
            if (grabber == null || !GameReflect.Get<bool>(grabber, "grabbed")) continue;

            if (_disarmNext.TryGetValue(p, out float next) && Time.unscaledTime < next) continue;
            _disarmNext[p] = Time.unscaledTime + DisarmOnGrabCooldown;

            ForceRelease(p, 1f);
        }
    }

    // 23) Everything at once on one target: unequip their slots, drop what they hold, lock their
    // grab, and burn the weapon they were holding.
    public static void StripTarget()
    {
        PlayerAvatar target = PlayerTargets.Picked();
        if (target == null)
        {
            Plugin.Log.LogInfo("Strip had no effect - no target selected.");
            return;
        }

        ItemMelee melee = HeldMeleeOf(target);
        UnequipEverythingOf(target);
        ForceRelease(target, LockSeconds * 2f);
        if (melee != null) for (int i = 0; i < 10; i++) DrainWeapon(melee);

        Plugin.Log.LogInfo($"Stripped {PlayerTargets.NameOf(target)} clean.");
    }
}
