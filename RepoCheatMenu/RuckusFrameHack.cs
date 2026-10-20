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

// PunManager.PlayerDamagingPlayerRPC - the strongest shop-side hole found so far, and strictly
// better than the PlayerCausingRuckusRPC one every existing framing tool uses.
//
// Confirmed unguarded in the decompiled source: no MasterOnlyRPC / OwnerOnlyRPC /
// MasterAndOwnerOnlyRPC anywhere in its body, only an internal IsMasterClientOrSingleplayer()
// check - which is about which machine EXECUTES it, not who sent it. Every argument is ours to
// choose: attacker view ID, victim view ID, damage amount, tumbled flag, killed flag, forceRuckus
// flag. It routes to SemiFunc.PlayerDamagingPlayerHost -> ShopKeeper.PlayerDamagedPlayer on the
// real master.
//
// What that buys over PlayerCausingRuckus:
//   * We choose the VICTIM too, so the keeper's own records show a specific person being beaten
//     by a specific person - it reads like a real fight, not an unexplained aggro.
//   * The damage number feeds AddRuckusScore(_attacker, _damage * 5f) unclamped, and the keeper
//     shoots at 1000 score (confirmed: "value.score >= 1000f -> ShootAtPlayer"). A single call
//     with a large damage value therefore takes someone from clean to executed instantly, with
//     no queue and no waiting for the keeper to be free.
//   * forceRuckus / killed short-circuit the accumulation thresholds outright.
//
// Shop only, same as everything else in the ruckus system - PlayerDamagingPlayerHost opens with
// "if (RunIsShop() && ShopKeeper.instance)".
internal static class RuckusFrameHack
{
    // Past the 1000-score shoot threshold in one call (score gain is damage * 5).
    private const int ExecuteDamage = 400;

    // Below the 200/400/600/800 anger thresholds' top end but enough to trip warnings - used by
    // the subtler "make the keeper suspicious of them" tools.
    private const int WarnDamage = 30;

    private static bool ReadyInShop()
    {
        if (!SemiFunc.RunIsShop())
        {
            Plugin.Log.LogInfo("Ruckus framing had no effect - only works in the shop.");
            return false;
        }
        if (ShopKeeperWake.IsAsleep())
        {
            ShopKeeperWake.TryWake();
            Plugin.Log.LogInfo("Ruckus framing: keeper was off - turning it on first.");
            return false;
        }
        return PunManager.instance != null;
    }

    private static void Report(PlayerAvatar attacker, PlayerAvatar victim, int damage, bool tumbled, bool killed, bool force)
    {
        if (attacker == null || victim == null || attacker == victim) return;
        if (attacker.photonView == null || victim.photonView == null) return;

        PunManager punManager = PunManager.instance;
        if (punManager == null) return;

        if (GameManager.Multiplayer())
        {
            PhotonView view = GameReflect.Get<PhotonView>(punManager, "photonView");
            view?.RPC("PlayerDamagingPlayerRPC", RpcTarget.MasterClient,
                attacker.photonView.ViewID, victim.photonView.ViewID, damage, tumbled, killed, force);
        }
        else
        {
            punManager.PlayerDamagingPlayerRPC(attacker.photonView.ViewID, victim.photonView.ViewID, damage, tumbled, killed, force);
        }
    }

    // Any other living player will do as the "victim" on the record - it just needs to be someone
    // other than the framed attacker for ShopKeeper.PlayerDamagedPlayer to accept it at all
    // (it drops _attacker == _victim outright).
    private static PlayerAvatar SomeoneElseThan(PlayerAvatar attacker)
    {
        foreach (PlayerAvatar p in PlayerTargets.AliveOthers())
            if (p != attacker) return p;

        PlayerAvatar local = PlayerTargets.Local;
        return local != attacker ? local : null;
    }

    // 1) Instant execution of the picked target - one RPC, no queue, no waiting for the keeper.
    public static void InstantExecute()
    {
        if (!ReadyInShop()) return;

        PlayerAvatar target = PlayerTargets.Picked();
        if (target == null)
        {
            Plugin.Log.LogInfo("Instant Execute had no effect - no target selected.");
            return;
        }

        Report(target, SomeoneElseThan(target), ExecuteDamage, true, true, true);
        Plugin.Log.LogInfo($"Instant Execute: reported {PlayerTargets.NameOf(target)} to the keeper with lethal force.");
    }

    // 2) Same, whole lobby at once. The keeper can only chase one at a time, but every score is
    // already past the threshold, so it works through them back to back on its own.
    public static void InstantExecuteAll()
    {
        if (!ReadyInShop()) return;

        List<PlayerAvatar> others = PlayerTargets.AliveOthers();
        foreach (PlayerAvatar target in others)
            Report(target, SomeoneElseThan(target), ExecuteDamage, true, true, true);

        Plugin.Log.LogInfo($"Instant Execute All: reported {others.Count} player(s).");
    }

    // 3) Toggle - keeps everyone's score pinned above the threshold, so anyone who survives (or
    // respawns) gets picked up again instead of the effect being one-shot.
    private const float AutoExecuteInterval = 5f;
    private static float _nextAutoExecute;

    public static void ApplyAutoExecute()
    {
        if (!CheatState.AutoShopExecute.Value) return;
        if (!SemiFunc.RunIsShop() || PunManager.instance == null) return;
        if (Time.unscaledTime < _nextAutoExecute) return;
        _nextAutoExecute = Time.unscaledTime + AutoExecuteInterval;

        if (ShopKeeperWake.IsAsleep()) { ShopKeeperWake.TryWake(); return; }

        foreach (PlayerAvatar target in PlayerTargets.AliveOthers())
            Report(target, SomeoneElseThan(target), ExecuteDamage, true, true, true);
    }

    // 4) Frame the target for beating a specific person, at a survivable damage level - the keeper
    // gets angry, warns and lectures them instead of executing. Much harder to read as a cheat
    // than someone spontaneously exploding.
    public static void FrameForAttack()
    {
        if (!ReadyInShop()) return;

        PlayerAvatar target = PlayerTargets.Picked();
        if (target == null) return;

        PlayerAvatar victim = SomeoneElseThan(target);
        Report(target, victim, WarnDamage, true, false, false);
        Plugin.Log.LogInfo($"Framed {PlayerTargets.NameOf(target)} for hitting {PlayerTargets.NameOf(victim)}.");
    }

    // 5) Crossfire - frames everyone for hitting the NEXT person in the list, so the keeper's own
    // records show a brawl involving the whole lobby rather than one culprit.
    public static void FrameEveryoneCrossfire()
    {
        if (!ReadyInShop()) return;

        List<PlayerAvatar> others = PlayerTargets.AliveOthers();
        if (others.Count < 2)
        {
            Plugin.Log.LogInfo("Crossfire needs at least two other players.");
            return;
        }

        for (int i = 0; i < others.Count; i++)
            Report(others[i], others[(i + 1) % others.Count], WarnDamage, true, false, false);

        Plugin.Log.LogInfo($"Crossfire: {others.Count} player(s) framed against each other.");
    }

    // 6) Killer flag only - hits ShopKeeper.PlayerDamagedPlayer's own "_killed" shortcut, which
    // skips every accumulation threshold and (while the keeper is already mid-warning at them)
    // jumps straight to ShootAtPlayer.
    public static void FrameAsKiller()
    {
        if (!ReadyInShop()) return;

        PlayerAvatar target = PlayerTargets.Picked();
        if (target == null) return;

        Report(target, SomeoneElseThan(target), 0, false, true, false);
        Plugin.Log.LogInfo($"{PlayerTargets.NameOf(target)} reported to the keeper as a killer.");
    }

    // 7) Score bomb - five separate reports instead of one, walking the target up through the
    // keeper's 200/400/600/800 anger thresholds so it visibly gets angrier each time before
    // finally shooting, rather than snapping straight to execution.
    public static void ScoreBomb()
    {
        if (!ReadyInShop()) return;

        PlayerAvatar target = PlayerTargets.Picked();
        if (target == null) return;

        PlayerAvatar victim = SomeoneElseThan(target);
        for (int i = 0; i < 5; i++)
            Report(target, victim, 40, true, false, true);

        Plugin.Log.LogInfo($"Score bomb dropped on {PlayerTargets.NameOf(target)}.");
    }

    // --- PunManager.PlayerCarryThrustRPC ------------------------------------------------------
    // Second unguarded ruckus entry point, same lack of any sender check. Feeds
    // ShopKeeper.PlayerCarryThrust, whose own record trips at 6 thrusts inside a 12s window
    // (confirmed constants: carryThrustThreshold = 6, carryThrustWindow = 12f). Separate counter
    // from the damage one, so it's an independent path to the same anger - useful when you want
    // the keeper annoyed at someone without any "damage" ever being reported.

    private static void Thrust(PlayerAvatar carrier, PlayerAvatar victim)
    {
        if (carrier == null || victim == null || carrier == victim) return;
        if (carrier.photonView == null || victim.photonView == null) return;

        PunManager punManager = PunManager.instance;
        if (punManager == null) return;

        if (GameManager.Multiplayer())
        {
            PhotonView view = GameReflect.Get<PhotonView>(punManager, "photonView");
            view?.RPC("PlayerCarryThrustRPC", RpcTarget.MasterClient, carrier.photonView.ViewID, victim.photonView.ViewID);
        }
        else
        {
            punManager.PlayerCarryThrustRPC(carrier.photonView.ViewID, victim.photonView.ViewID);
        }
    }

    // 8) Straight past the 6-thrust threshold in one press.
    public static void CarryThrustFrame()
    {
        if (!ReadyInShop()) return;

        PlayerAvatar target = PlayerTargets.Picked();
        if (target == null) return;

        PlayerAvatar victim = SomeoneElseThan(target);
        for (int i = 0; i < 7; i++) Thrust(target, victim);

        Plugin.Log.LogInfo($"{PlayerTargets.NameOf(target)} framed for carry-thrusting {PlayerTargets.NameOf(victim)}.");
    }

    // 9) Toggle version across the whole lobby - the 12s window means a slow trickle keeps
    // everyone permanently just under/over the line instead of tripping once and decaying.
    private const float ThrustStormInterval = 1.5f;
    private static float _nextThrust;

    public static void ApplyCarryThrustStorm()
    {
        if (!CheatState.CarryThrustStorm.Value) return;
        if (!SemiFunc.RunIsShop() || PunManager.instance == null) return;
        if (Time.unscaledTime < _nextThrust) return;
        _nextThrust = Time.unscaledTime + ThrustStormInterval;

        List<PlayerAvatar> others = PlayerTargets.AliveOthers();
        for (int i = 0; i < others.Count; i++)
            Thrust(others[i], others[(i + 1) % others.Count]);
    }
}
