using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace RepoCheatMenu;

// "Shop Execution" - kills the whole lobby in the shop, one player at a time, via the shop's own
// security NPC. Same PunManager.PlayerCausingRuckusRPC hole RaidHack.FrameForShopKeeper already
// rides (no sender check in its own body - only an internal IsMasterClientOrSingleplayer() gate,
// which is about which machine EXECUTES it, not who sent it), so the real master ends up calling
// ShopKeeper.ShootAtPlayer() for real and the NPC deals genuine authoritative damage. That's the
// only route to real player damage as a guest - HurtOtherRPC is properly double-gated everywhere.
//
// Why one at a time instead of just framing everybody at once (MassRaidHack.FrameAllForShopKeeper
// already does that): confirmed in the decompiled ShopKeeper.cs, ShootAtPlayer() bails out
// immediately if InRuckusFlow() is already true - i.e. while the keeper is busy chasing, grabbing,
// judging or shooting somebody, every further request is silently DROPPED. Framing five people in
// the same frame therefore only ever punishes the first one; the other four calls evaporate. So
// this queues them, waits for the keeper to actually be free again, and only then fires at the
// next name on the list.
//
// The keeper's state is broadcast to every client (UpdateStateRPC to RpcTarget.All) and
// ShopKeeper.currentState is a public field, so a guest can read exactly when it's busy or idle -
// no guessing, no reflection needed for that part.
internal static class ShopExecutionHack
{
    // How often to re-fire at the CURRENT target while it's still alive and the keeper is idle.
    // One shot doesn't reliably kill (the keeper can miss, lose line of sight, or the target can
    // just tank it), so this keeps re-aggroing until deadSet actually flips.
    private const float RetryInterval = 3f;

    // How long to work on one target before rotating to the next. Someone who survives is NOT
    // dropped from the queue - they go to the back of it and come round again. Two separate
    // reasons a target can outlast the timer, and neither means "give up on them":
    //   * out of reach - the keeper's shot is a 50m spherecast, so anyone behind a wall or out of
    //     the shop room simply can't be hit from where the keeper stands, but they'll wander back;
    //   * god mode - we're not the only ones running a mod, and someone ignoring damage entirely
    //     would otherwise permanently absorb the whole queue if we kept hammering only them.
    // Rotating means the mortal players still get worked through instead of the list stalling on
    // the first immortal one, and the immortal ones keep getting retried forever in between.
    // Shorter than the original one-shot timeout precisely because it's now a rotation, not a
    // verdict - a fast lap is better than a slow one.
    private const float TargetTimeout = 25f;

    private static readonly List<PlayerAvatar> _queue = new();
    private static readonly Dictionary<PlayerAvatar, int> _attempts = new();
    private static bool _running;
    private static float _nextFire;
    private static float _targetDeadline;
    private static bool _warnedNotShop;
    private static bool _warnedAsleep;

    public static void Apply()
    {
        if (!CheatState.ShopExecution.Value)
        {
            if (_running) Stop("выключено вручную");
            return;
        }

        if (!SemiFunc.RunIsShop())
        {
            if (!_warnedNotShop)
            {
                Plugin.Log.LogInfo("Shop Execution: works only in the shop - waiting.");
                _warnedNotShop = true;
            }
            return;
        }
        _warnedNotShop = false;

        ShopKeeper keeper = ShopKeeper.instance;
        PlayerAvatar local = PlayerAvatar.instance;
        if (keeper == null || local == null) return;

        if (!_running) BuildQueue(local);

        // Drop anyone who died (that's the "disappears from the list" part), left the game, or
        // was already dead when the queue was built.
        PruneQueue();

        if (_queue.Count == 0)
        {
            Stop("очередь пуста - все мертвы");
            CheatState.ShopExecution.Value = false;
            return;
        }

        // A sleeping keeper refuses every request outright (ShootAtPlayer checks state != Sleeping
        // && != WakeUp), so flip its switch first (ShopKeeperWake retries on its own cooldown) and
        // wait for it to come up rather than burning frame RPCs into the void.
        if (ShopKeeperWake.IsAsleep())
        {
            ShopKeeperWake.TryWake();
            if (!_warnedAsleep)
            {
                Plugin.Log.LogInfo("Shop Execution: keeper is switched off - turning it on first.");
                _warnedAsleep = true;
            }
            return;
        }
        _warnedAsleep = false;

        // Busy hunting someone already - every ShootAtPlayer call right now would be discarded,
        // so don't waste them.
        if (IsBusy(keeper)) return;

        PlayerAvatar target = _queue[0];

        if (Time.unscaledTime >= _targetDeadline)
        {
            RotateHead(target);
            return;
        }

        if (Time.unscaledTime < _nextFire) return;
        _nextFire = Time.unscaledTime + RetryInterval;

        Frame(target);
    }

    private static void Frame(PlayerAvatar target)
    {
        if (target.photonView == null) return;

        PunManager punManager = PunManager.instance;
        if (punManager == null) return;

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

    private static void BuildQueue(PlayerAvatar local)
    {
        _queue.Clear();
        _attempts.Clear();

        GameDirector director = GameDirector.instance;
        List<PlayerAvatar> players = director != null ? GameReflect.Get<List<PlayerAvatar>>(director, "PlayerList") : null;
        if (players != null)
        {
            foreach (PlayerAvatar p in players)
            {
                if (p == null || p == local) continue;
                if (GameReflect.Get<bool>(p, "deadSet")) continue;
                _queue.Add(p);
            }
        }

        _running = true;
        StartTarget();
        Plugin.Log.LogInfo($"Shop Execution: queued {_queue.Count} player(s).");
    }

    // Removes dead/despawned players from the front of the queue and logs each kill, so the list
    // visibly shrinks by one every time somebody drops.
    private static void PruneQueue()
    {
        bool headChanged = false;

        for (int i = _queue.Count - 1; i >= 0; i--)
        {
            PlayerAvatar p = _queue[i];
            if (p != null && !GameReflect.Get<bool>(p, "deadSet")) continue;

            if (p != null) Plugin.Log.LogInfo($"Shop Execution: {NameOf(p)} is dead - {_queue.Count - 1} left.");
            _queue.RemoveAt(i);
            if (p != null) _attempts.Remove(p);
            if (i == 0) headChanged = true;
        }

        if (headChanged) StartTarget();
    }

    // Survived their turn - send them to the BACK of the queue instead of dropping them, and keep
    // count of how many turns they've eaten. Only death removes anyone from this list; a target
    // that can't be killed just gets retried after everyone else has had a turn, so one immortal
    // player can't hold the whole queue hostage and can't quietly escape it either.
    private static void RotateHead(PlayerAvatar target)
    {
        _queue.RemoveAt(0);
        _queue.Add(target);

        _attempts.TryGetValue(target, out int tries);
        _attempts[target] = ++tries;

        string note = tries >= 3
            ? $"survived {tries} turns - god mode or permanently out of the keeper's reach"
            : "survived this turn (out of reach, or tanked it)";
        Plugin.Log.LogInfo($"Shop Execution: {NameOf(target)} {note} - sent to the back, {_queue.Count} in the queue.");

        StartTarget();
    }

    // Resets the per-target clocks so a fresh target gets fired at immediately and gets its own
    // full timeout budget rather than inheriting the previous one's.
    private static void StartTarget()
    {
        _nextFire = 0f;
        _targetDeadline = Time.unscaledTime + TargetTimeout;
    }

    private static void Stop(string reason)
    {
        _running = false;
        _queue.Clear();
        _attempts.Clear();
        _warnedAsleep = false;
        _warnedNotShop = false;
        Plugin.Log.LogInfo($"Shop Execution: stopped ({reason}).");
    }

    private static bool IsBusy(ShopKeeper keeper)
    {
        switch (keeper.currentState)
        {
            case ShopKeeper.State.WaitForRuckusPlayer:
            case ShopKeeper.State.SeeRuckusPlayer:
            case ShopKeeper.State.GrabRuckusPlayer:
            case ShopKeeper.State.JudgeRuckusPlayer:
            case ShopKeeper.State.Shoot:
                return true;
            default:
                return false;
        }
    }

    private static string NameOf(PlayerAvatar player)
    {
        string name = GameReflect.Get<string>(player, "playerName");
        return string.IsNullOrEmpty(name) ? "???" : name;
    }
}
