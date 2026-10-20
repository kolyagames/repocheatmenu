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

// "Flying" in this game is the tumbling/ragdoll state (PlayerTumble.isTumbling) - confirmed in
// the decompiled source. This hack forces that state on demand and walks your own position next
// to every other player instead of waiting for an actual throw to put you there.
//
// Contact knockdown does NOT actually reach a guest through the vanilla hurtCollider/rbVelocity
// path, despite what earlier revisions of this file claimed - confirmed by reading
// PhysGrabObject.OnPhotonSerializeView in full: it starts with
// "if (!SemiFunc.MasterOnlyRPC(info)) return;" applying to BOTH the sending and receiving
// branches, so a non-master owner's PhysGrabObject (which includes your own tumble body - same
// component type as any grabbed prop) never sends ANY state update at all, rbVelocity included.
// Forcing rbVelocity locally therefore never reaches the target's client, their own copy of your
// hurtCollider never sees velocity > 4f, never activates, and no contact is ever detected on
// their end - PlayerHurt() (confirmed in HurtCollider.cs) only does anything on the client where
// "_player.photonView.IsMine" is true, i.e. the TARGET's own client, which never even runs the
// relevant branch without that sync.
//
// Bypassing the whole chain instead: PlayerTumble.TumbleImpactRPC (confirmed [PunRPC] public,
// no MasterAndOwnerOnlyRPC/sender check anywhere in its own body) is called directly on our own
// tumble's PhotonView once we're in contact range - Photon dispatches PunRPC methods by name
// regardless of the method's own C# visibility, so any client can send it. Every receiving
// client (including the real master, whoever that is) gets it; the master's own copy is the one
// that actually applies real force via FreezeForces (gated internally on
// SemiFunc.IsMasterClientOrSingleplayer() - true on whichever machine really is master,
// independent of who sent the RPC). This is exactly how the game's own vehicle-impact and other
// contact systems already get real cross-client effect from a non-authoritative sender.
//
// Movement rides Rigidbody.MovePosition instead of writing transform.position directly -
// PlayerController.rb is a normal (non-kinematic) Rigidbody, and Unity resyncs a non-kinematic
// body's Transform from its own internal physics position on the next physics step, which
// silently snapped any direct transform.position write back to wherever the Rigidbody still
// thought it was ("отфлагивает обратно"). MovePosition itself only actually takes effect on the
// next physics step, though - calling it from our own independent ~33/sec tick loop (driven by
// Time.deltaTime's getter, not aligned with FixedUpdate at all) meant the move could get queued
// and silently dropped/overwritten before a physics step ever consumed it, which looked like the
// character not even trying to fly. So the MovePosition call itself now happens from the same
// Harmony postfix on PlayerController.FixedUpdate that Fly already uses (see ApplyFixed below
// and PlayerControllerFixedUpdatePatch in Patches.cs) - guaranteed to run exactly once per
// physics step. The regular tick loop (Apply/ApplyBanish) still owns all the decision-making
// (who's the target, have we arrived, when to switch) using transform.position reads, which are
// always safe regardless of timing - it just no longer touches the Rigidbody directly.
//
// Forcing *someone else's* tumble state directly only works for the master client -
// PlayerTumble.TumbleSetRPC/TumbleRequestRPC both gate on SemiFunc.MasterAndOwnerOnlyRPC, which
// passes only if the sender is the master client or that player's own owner. A guest is neither
// for anyone but themselves, so the dead/spectating case (no body left to bump into people
// with) only actually knocks people down when hosting or in singleplayer - while alive, your
// own tumble request always succeeds regardless of host/guest because you always own your own
// PlayerAvatar. ForceTumble (below) is the single place that rule lives now - both alive modes
// call it on the target before TriggerImpact, and Banish checks its result (via isTumbling)
// before ever calling Teleport, instead of each duplicating the host/guest split separately.
// TumbleImpactRPC/TriggerImpact was previously assumed to also flip the target into real tumbling
// once the FreezeForces push landed - it doesn't, checked its full body, it only hurt-flashes and
// (master-side) disarms/shoves. That's why Banish's floor-teleport used to silently do nothing for
// guests: it was teleporting a Rigidbody that PlayerTumble.Update() keeps resyncing onto the live
// position every frame whenever isTumbling is still false, which for a guest it always was.
internal static class KnockdownHack
{
    private const float HomingSpeed = 25f; // units/sec - fast, but smooth instead of an instant jump
    private const float ArriveDistance = 1.2f;
    private const float MaxHomingTime = 1.5f; // give up and re-target if a target is unreachable (e.g. behind a wall/on another floor)

    private static bool _jumpWasDown;
    private static bool _active;

    // Homing moves a Rigidbody via MovePosition every physics step while ALSO writing its
    // velocity directly (see ApplyFixed - the velocity write is what makes hurtCollider's contact
    // gate trigger, not optional). Two motion sources on one non-kinematic body every step reads
    // as visibly jerky. Interpolation only smooths the RENDERED transform between physics steps -
    // it doesn't touch the physics/velocity this relies on - so it's a free fix for the jitter.
    // Tracked per-Rigidbody since the live body swaps between controller.rb and tumble.rb mid-homing.
    private static readonly Dictionary<Rigidbody, RigidbodyInterpolation> _savedInterpolation = new();

    private static void SetHomingInterpolation(Rigidbody rb)
    {
        if (rb == null || rb.interpolation == RigidbodyInterpolation.Interpolate) return;
        if (!_savedInterpolation.ContainsKey(rb))
            _savedInterpolation[rb] = rb.interpolation;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private static void RestoreInterpolation()
    {
        foreach (var kv in _savedInterpolation)
            if (kv.Key != null) kv.Key.interpolation = kv.Value;
        _savedInterpolation.Clear();
    }

    private static PlayerAvatar _aliveTarget;
    private static PlayerAvatar _alivePrevious;
    private static float _aliveDeadline;

    private static ItemUpgradePlayerTumbleWingsLogic _suppressedWings;

    // Both the "Launch" and "Wings" tumble upgrades (playerUpgradeLaunch/playerUpgradeTumbleWings)
    // add real physics force on top of our own controlled MovePosition homing when the ATTACKER
    // has them purchased - confirmed in the decompiled source:
    //  - Launch (PlayerTumble.TumbleSet, tumbleLaunch field): adds a one-time forward-camera
    //    impulse to tumbleForce the instant tumbling starts WITH _playerInput true, which is
    //    exactly how we activate it - every knockdown activation would also launch us in
    //    whatever direction we happen to be looking, on top of the homing.
    //  - Wings (ItemUpgradePlayerTumbleWingsLogic.FixedUpdate): continuously applies lift +
    //    forward thrust + look-alignment torque to the SAME Rigidbody we're MovePosition-ing,
    //    for as long as we're airborne and tumbling - fighting our homing every physics step.
    // Both only ever apply real force on whichever machine is master (same PhysGrabObject-
    // authority pattern as everything else here), so this only actually mattered when
    // hosting/singleplayer - but there it was genuinely fighting the dash.
    private static void ActivateTumble(PlayerAvatar local)
    {
        if (GameReflect.Get<bool>(local, "isTumbling")) return;

        PlayerTumble localTumble = GameReflect.Get<PlayerTumble>(local, "tumble");
        if (localTumble == null) return;

        int launchBackup = GameReflect.Get<int>(localTumble, "tumbleLaunch");
        if (launchBackup != 0)
            GameReflect.Set(localTumble, "tumbleLaunch", 0);

        localTumble.TumbleRequest(true, true);

        if (launchBackup != 0)
            GameReflect.Set(localTumble, "tumbleLaunch", launchBackup);
    }

    private static void SuppressWings(PlayerAvatar local)
    {
        if (local.upgradeTumbleWingsLogic != null && local.upgradeTumbleWingsLogic.enabled)
        {
            local.upgradeTumbleWingsLogic.enabled = false;
            _suppressedWings = local.upgradeTumbleWingsLogic;
        }
    }

    private static void RestoreWings()
    {
        if (_suppressedWings != null)
        {
            _suppressedWings.enabled = true;
            _suppressedWings = null;
        }
    }

    public static void Apply()
    {
        PlayerAvatar local = PlayerAvatar.instance;
        if (local == null) return;

        bool dead = GameReflect.Get<bool>(local, "deadSet");
        bool wantAliveLoop = CheatState.KnockdownAll.Value && !dead;

        if (!wantAliveLoop && _active)
        {
            RestoreLocal(local);
            _active = false;
            _aliveTarget = null;
            _alivePrevious = null;
        }

        if (!CheatState.KnockdownAll.Value)
        {
            _jumpWasDown = false;
            return;
        }

        if (dead)
        {
            ApplyDead(local);
            return;
        }

        ApplyAlive(local);
        _active = true;
    }

    // Dead/spectating: jump (the same key that flies the spectator camera upward) fires one
    // knockdown pulse across every living player on the map instead of toggling anything.
    private static void ApplyDead(PlayerAvatar local)
    {
        // Don't fire the dead-mode knockdown pulse while the player is typing in chat or the mod's
        // menu - Space (Jump) was triggering a mass-knockdown every time a chat message had a space.
        bool jumpDown = !UiInput.TypingInMenu() && SemiFunc.InputDown(InputKey.Jump);
        bool pressedNow = jumpDown && !_jumpWasDown;
        _jumpWasDown = jumpDown;
        if (!pressedNow) return;

        if (GameManager.Multiplayer() && !PhotonNetwork.IsMasterClient) return; // see class comment - no authority over anyone else while dead

        foreach (PlayerAvatar player in GetOthers(local))
        {
            PlayerTumble tumble = GameReflect.Get<PlayerTumble>(player, "tumble");
            tumble?.TumbleSet(true, true);
        }
    }

    // Alive: freeze in the air (no gravity, no residual velocity), force yourself into the
    // tumbling state so your hurtCollider is live, then smoothly home in on the nearest other
    // player - arriving (or a snapped contact) triggers the game's own knockdown via your
    // hurtCollider, works for guest or host. Once you arrive (or give up on an unreachable
    // target) it picks the next-nearest player, excluding whoever you just visited.
    private static void ApplyAlive(PlayerAvatar local)
    {
        PlayerController controller = PlayerController.instance;
        if (controller == null || controller.rb == null) return;

        // controller.AntiGravity() only suppresses gravity on PlayerController.rb, which is only
        // the live body for the brief window BEFORE isTumbling flips true - real gravity
        // suppression for the actual homing (which happens WHILE tumbling, on PlayerTumble.rb
        // instead) now lives in ApplyFixed, see its comment for why this used to just fall.
        controller.AntiGravity(1f);
        if (!controller.rb.isKinematic) // tumbling briefly makes the rb kinematic - writing velocity then just spams a Unity warning
            controller.rb.velocity = Vector3.zero;

        SuppressWings(local);
        ActivateTumble(local);

        List<PlayerAvatar> others = GetOthers(local);
        if (others.Count == 0) return;

        bool targetGone = _aliveTarget == null || !others.Contains(_aliveTarget);
        if (targetGone)
            PickNextAliveTarget(local, others, null);

        Vector3 desired = _aliveTarget.transform.position + Vector3.up * 0.3f;
        bool arrived = Vector3.Distance(controller.transform.position, desired) <= ArriveDistance;
        bool timedOut = Time.unscaledTime >= _aliveDeadline;
        if (arrived)
        {
            // ForceTumble first (host/singleplayer only, real ragdoll) - it's what makes the
            // FreezeForces push inside TriggerImpact actually land instead of getting eaten by
            // the resync described on ForceTumble. As guest ForceTumble is a no-op and this is
            // just the disarm+hurt-flash TriggerImpact always gives.
            ForceTumble(_aliveTarget);
            TriggerImpact(local, _aliveTarget);
        }
        if (arrived || timedOut)
            PickNextAliveTarget(local, others, _aliveTarget);
    }

    // See the class comment - bypasses the vanilla hurtCollider/rbVelocity path entirely (that
    // one never reaches a guest) by sending PlayerTumble.TumbleImpactRPC directly on our own
    // tumble's PhotonView. Whichever client actually is master applies the real disarm+shove when
    // it receives this, regardless of who sent it - but read TumbleImpactRPC's own body in full
    // and it never touches isTumbling either way, on any machine. The only guaranteed, universal
    // effect of this call is PlayerHealth.HurtFreezeOverride's red hurt-flash on the target (that
    // line runs unconditionally, no role check). Disarm (ReleaseObjectRPC) and the push
    // (FreezeForces) both only actually execute on whichever machine really is master - real
    // value for us regardless of whether THAT'S us, since Photon still delivers this to
    // everyone. Internal (not private) - RaidHack reuses this exact call as a from-any-distance
    // disarm/hit-flash (the RPC itself has no proximity check anywhere in its body - the
    // homing/contact-range dance elsewhere in this file is our own gameplay choice for the
    // "tackle" feel, not a requirement of the RPC). It does NOT put the target into real tumble
    // state - see ForceTumble below for that.
    internal static void TriggerImpact(PlayerAvatar local, PlayerAvatar target)
    {
        if (local == null || target == null) return;

        PlayerTumble localTumble = GameReflect.Get<PlayerTumble>(local, "tumble");
        if (localTumble == null) return;

        int targetViewId = target.photonView != null ? target.photonView.ViewID : 0;
        if (targetViewId == 0) return;

        if (GameManager.Multiplayer())
        {
            PhotonView tumbleView = GameReflect.Get<PhotonView>(localTumble, "photonView");
            tumbleView?.RPC("TumbleImpactRPC", RpcTarget.All, targetViewId);
        }
        else
        {
            GameReflect.Invoke(localTumble, "TumbleImpactRPC", targetViewId);
        }
    }

    // Puts `target` into the game's own real PlayerTumble.isTumbling state - the only condition
    // under which a Teleport() on their tumble body (Banish, below) actually sticks instead of
    // snapping back. Confirmed exhaustively in the decompiled source: PlayerTumble.Update() runs
    // "if (!isTumbling) rb.MovePosition(livePosition)" every single frame on every client watching
    // them (ours included), so anything that moves that Rigidbody while isTumbling is still false
    // gets silently overwritten the very next frame. Setting isTumbling on someone else has
    // exactly one legitimate path - PlayerTumble.TumbleRequest/TumbleRequestRPC/TumbleSetRPC are
    // all MasterAndOwnerOnlyRPC-gated, and every other caller in the game that tumbles a player who
    // isn't its own owner (enemies, the cosmetic shop's own tumble trick) is wrapped in
    // SemiFunc.IsMasterClientOrSingleplayer() first - no exception anywhere. TumbleImpactRPC (see
    // TriggerImpact above) is not that exception either - it never touches isTumbling. So this
    // only ever succeeds as host/singleplayer; as guest it's a deterministic no-op, decided
    // up-front by a plain role check, not something worth retrying.
    internal static bool ForceTumble(PlayerAvatar target)
    {
        if (target == null) return false;
        if (GameManager.Multiplayer() && !PhotonNetwork.IsMasterClient) return false;

        PlayerTumble targetTumble = GameReflect.Get<PlayerTumble>(target, "tumble");
        if (targetTumble == null) return false;

        targetTumble.TumbleRequest(true, true);
        return true;
    }

    private static void PickNextAliveTarget(PlayerAvatar local, List<PlayerAvatar> others, PlayerAvatar justVisited)
    {
        _alivePrevious = justVisited;
        _aliveTarget = NearestExcluding(local, others, _alivePrevious);
        _aliveDeadline = Time.unscaledTime + MaxHomingTime;
    }

    private enum BanishPhase { Homing, Frozen }

    private static BanishPhase _banishPhase;
    private static PlayerAvatar _banishTarget;
    private static PlayerAvatar _banishPrevious;
    private static float _banishDeadline;
    private static bool _banishActive;

    // Mode 2 ("KnockdownBanish"): home in on the nearest other player, freeze next to them for
    // 2s once you arrive (same contact knockdown as mode 1), then grab and banish that player
    // through the floor before moving to the next-nearest one. The banish itself rides
    // PhysGrabObject.Teleport() - confirmed in the decompiled source, it sends SetPositionRPC to
    // RpcTarget.MasterClient and the master applies it with zero validation, same loophole
    // PowerGrab/the Troll destroy fallback already use - so this works for guest or host on
    // living targets. Don't enable this at the same time as Knockdown All (mode 1), they'll
    // fight over your own position/tumble state.
    public static void ApplyBanish()
    {
        PlayerAvatar local = PlayerAvatar.instance;
        if (local == null) return;

        bool dead = GameReflect.Get<bool>(local, "deadSet");
        // Banish's "tackle" step rides TriggerImpact -> TumbleImpactRPC on our own PlayerTumble
        // view; under a session ghost that RPC is filtered, so the target never sees the hit and
        // Banish silently does nothing but immobilise us.
        bool muted = GhostObserverHack.LocalRpcIsMuted;
        bool want = CheatState.KnockdownBanish.Value && !dead && !muted;

        if (!want)
        {
            if (_banishActive)
            {
                RestoreLocal(local);
                _banishActive = false;
                _banishTarget = null;
                _banishPrevious = null;
                _banishPhase = BanishPhase.Homing;
            }
            return;
        }

        PlayerController controller = PlayerController.instance;
        if (controller == null || controller.rb == null) return;

        controller.AntiGravity(1f); // see ApplyAlive's comment - only matters for the brief pre-tumble window, real fix is in ApplyFixed
        if (!controller.rb.isKinematic)
            controller.rb.velocity = Vector3.zero;

        SuppressWings(local);
        ActivateTumble(local);

        List<PlayerAvatar> others = GetOthers(local);
        if (others.Count == 0) return;

        _banishActive = true;

        bool targetGone = _banishTarget == null || !others.Contains(_banishTarget);
        if (targetGone)
        {
            _banishPrevious = null; // they left/died - don't hold an exclusion against a target that no longer exists
            _banishTarget = NearestExcluding(local, others, _banishPrevious);
            _banishPhase = BanishPhase.Homing;
            _banishDeadline = Time.unscaledTime + MaxHomingTime;
        }

        Vector3 desired = _banishTarget.transform.position + Vector3.up * 0.3f;

        if (_banishPhase == BanishPhase.Homing)
        {
            bool arrived = Vector3.Distance(controller.transform.position, desired) <= ArriveDistance;
            if (arrived)
            {
                ForceTumble(_banishTarget); // see ForceTumble - real ragdoll if host, no-op if guest
                TriggerImpact(local, _banishTarget);
                _banishPhase = BanishPhase.Frozen;
                _banishDeadline = Time.unscaledTime + 2f;
            }
            else if (Time.unscaledTime >= _banishDeadline)
            {
                // unreachable - give up and try the next-nearest instead
                _banishPrevious = _banishTarget;
                _banishTarget = NearestExcluding(local, others, _banishPrevious);
                _banishDeadline = Time.unscaledTime + MaxHomingTime;
            }
        }
        else // Frozen
        {
            if (Time.unscaledTime >= _banishDeadline)
            {
                PlayerAvatar justFrozen = _banishTarget;
                _banishPrevious = justFrozen;
                _banishTarget = NearestExcluding(local, others, _banishPrevious);
                _banishPhase = BanishPhase.Homing;
                _banishDeadline = Time.unscaledTime + MaxHomingTime;

                Banish(local, justFrozen);
            }
        }
    }

    // Called from the PlayerController.FixedUpdate postfix (Patches.cs) - the only place a
    // Rigidbody.MovePosition call is guaranteed to actually be consumed by the physics step
    // that follows it. See the class comment for why this is split out from Apply/ApplyBanish.
    //
    // Which Rigidbody actually matters flips depending on isTumbling (confirmed in the
    // decompiled PlayerController.FixedUpdate/PlayerTumble.Update): while NOT tumbling,
    // PlayerTumble.rb is the follower (it MovePosition's itself onto the PlayerAvatar's
    // transform every Update) and controller.rb is what's live. While tumbling, that
    // relationship reverses - PlayerController.FixedUpdate overwrites its own transform.position
    // from tumble.transform.position every step, making controller.rb the dead one. Moving
    // controller.rb during a knockdown (as this used to do) compiled fine and produced zero
    // visible motion - the immediately-following position overwrite from tumble's transform
    // erased it every single step, which is exactly the "knocks you down but never flies
    // anywhere" symptom this fixes.
    //
    // Getting next to a target used to be the whole story, but it never actually knocked anyone
    // down - confirmed root cause in the decompiled PlayerTumble.Update(): the hurtCollider that
    // does the real knockdown-on-contact only gets SetActive(true) when
    // "physGrabObject.rbVelocity.magnitude > 4f" (line ~222). MovePosition teleports the
    // Rigidbody without ever producing a real Rigidbody.velocity reading, so that gate never
    // passed - you'd visibly glide onto a player and nothing would happen. Worse,
    // PhysGrabObject.FixedUpdate only recomputes rbVelocity from the real rb.velocity when
    // "SemiFunc.IsMasterClientOrSingleplayer()" is true for the LOCAL client (not gated by who
    // owns the object) - so even directly setting rb.velocity wouldn't reach rbVelocity for a
    // guest. Forcing the rbVelocity field itself below sidesteps both problems and matches what
    // a real high-speed tumble impact would report.
    //
    // "Just falls" was a separate, more basic bug on top of the above: ApplyAlive/ApplyBanish call
    // controller.AntiGravity(1f) every tick, but that only suppresses gravity on
    // PlayerController.rb - and per the comment above, controller.rb is the DEAD body the entire
    // time we're actually tumbling/homing (PlayerTumble.rb is live then instead). PlayerTumble.rb
    // has no anti-gravity concept of its own anywhere in the vanilla game (real tumbling is
    // supposed to fall/bounce like a ragdoll), so gravity was pulling the one Rigidbody that
    // actually mattered down the entire time, fighting the MovePosition calls below and mostly
    // just looking like a normal fall. Toggling useGravity directly on whichever Rigidbody
    // ApplyFixed already resolves as "live" this tick (moveRb) fixes both the pre-tumble window
    // (controller.rb) and the actual homing (tumble.rb) from one place.
    public static void ApplyFixed(PlayerController controller)
    {
        if (controller == null) return;

        PlayerAvatar local = PlayerAvatar.instance;
        if (local == null) return;

        PlayerTumble tumble = GameReflect.Get<PlayerTumble>(local, "tumble");
        bool tumbling = GameReflect.Get<bool>(local, "isTumbling");
        Rigidbody moveRb = tumbling && tumble != null ? GameReflect.Get<Rigidbody>(tumble, "rb") : controller.rb;
        if (moveRb == null) return;

        moveRb.useGravity = !(_active || _banishActive);

        bool moving = false;
        Vector3 moveVelocity = Vector3.zero;

        if (_active && _aliveTarget != null)
        {
            SetHomingInterpolation(moveRb);
            Vector3 desired = _aliveTarget.transform.position + Vector3.up * 0.3f;
            Vector3 next = Vector3.MoveTowards(moveRb.position, desired, HomingSpeed * Time.fixedDeltaTime);
            moveVelocity = (next - moveRb.position) / Time.fixedDeltaTime;
            moveRb.MovePosition(next);
            moving = true;
        }

        if (_banishActive && _banishTarget != null)
        {
            if (_banishPhase == BanishPhase.Homing)
            {
                SetHomingInterpolation(moveRb);
                Vector3 desired = _banishTarget.transform.position + Vector3.up * 0.3f;
                Vector3 next = Vector3.MoveTowards(moveRb.position, desired, HomingSpeed * Time.fixedDeltaTime);
                moveVelocity = (next - moveRb.position) / Time.fixedDeltaTime;
                moveRb.MovePosition(next);
                moving = true;
            }
            else
            {
                moveRb.MovePosition(moveRb.position); // Frozen - hold position without drifting off-physics
            }
        }

        if (tumbling && tumble != null)
        {
            if (moving && !moveRb.isKinematic)
                moveRb.velocity = moveVelocity; // real physical velocity, for actual collision response on contact

            PhysGrabObject tumbleGrabObject = GameReflect.Get<PhysGrabObject>(tumble, "physGrabObject");
            if (tumbleGrabObject != null)
                GameReflect.Set(tumbleGrabObject, "rbVelocity", moving ? moveVelocity : Vector3.zero);
        }
    }

    private static bool _loggedGuestBanishLimit;

    private static void Banish(PlayerAvatar local, PlayerAvatar target)
    {
        PlayerTumble tumble = GameReflect.Get<PlayerTumble>(target, "tumble");
        PhysGrabObject physGrabObject = tumble != null ? GameReflect.Get<PhysGrabObject>(tumble, "physGrabObject") : null;
        if (physGrabObject == null) return;

        // Only actually sticks while the target is really tumbling (ForceTumble in the Homing
        // phase above, host/singleplayer only) - otherwise PlayerTumble.Update() resyncs this same
        // Rigidbody onto their live position on the very next frame, on every client including
        // ours, and the teleport never visibly happens. Don't call it and pretend it worked - as
        // guest this just falls through to the log below instead, same disarm/hit-flash contact
        // effect as Knockdown All without the fake banish.
        if (!GameReflect.Get<bool>(target, "isTumbling"))
        {
            if (!_loggedGuestBanishLimit)
            {
                _loggedGuestBanishLimit = true;
                Plugin.Log.LogInfo("Knockdown Banish: under-the-floor teleport needs real tumble " +
                    "authority (host/singleplayer only, see KnockdownHack.ForceTumble) - as guest " +
                    "this only disarms/shoves+hurt-flashes on contact like Knockdown All, it won't " +
                    "actually banish anyone under the map.");
            }
            return;
        }

        Vector3 away = target.transform.position - local.transform.position;
        away.y = 0f;
        away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;

        Vector3 pos = target.transform.position + away * 6f + Vector3.down * 40f;
        GameReflect.TeleportSafe(physGrabObject, pos, target.transform.rotation, "KnockdownHack");

        // Actually killing them (not just stranding them under the map) needs real damage
        // authority over someone else's character - confirmed in the decompiled PlayerHealth
        // source, HurtOtherRPC is gated by SemiFunc.MasterAndOwnerOnlyRPC, which checks who SENT
        // the RPC (_info.Sender), not who called HurtOther() locally. As a guest, calling this
        // just fires an RPC that every client (including the target's own) rejects - same
        // authority wall as HighStrength/MeleeVibrate/etc. As master, _info.Sender ==
        // PhotonNetwork.MasterClient passes everywhere including on the target's own client,
        // which is what actually applies the damage (health is otherwise client-authoritative).
        // 9999 damage with savingGrace:false skips the "can't be one-shot below 20 HP" mechanic
        // entirely (that only ever triggers for damage <= 25) regardless of their current/max HP.
        // Unreachable as guest anyway now (we returned above), kept as its own explicit role
        // check since KnockdownBanishKill could theoretically be reused from a context where
        // isTumbling is true for some other reason.
        if (CheatState.KnockdownBanishKill.Value && (!GameManager.Multiplayer() || PhotonNetwork.IsMasterClient))
        {
            target.playerHealth?.HurtOther(9999, Vector3.zero, savingGrace: false);
        }
    }

    private static List<PlayerAvatar> GetOthers(PlayerAvatar local)
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

    // Nearest player to local, preferring to skip "exclude" (the one just visited/banished) as
    // long as someone else is available, so the loop doesn't just bounce between two players
    // standing next to each other forever.
    private static PlayerAvatar NearestExcluding(PlayerAvatar local, List<PlayerAvatar> others, PlayerAvatar exclude)
    {
        PlayerAvatar best = null;
        float bestDist = float.MaxValue;
        PlayerAvatar fallback = null;
        float fallbackDist = float.MaxValue;

        foreach (PlayerAvatar p in others)
        {
            float d = Vector3.Distance(local.transform.position, p.transform.position);
            if (p == exclude)
            {
                if (d < fallbackDist) { fallbackDist = d; fallback = p; }
                continue;
            }
            if (d < bestDist) { bestDist = d; best = p; }
        }

        return best ?? fallback;
    }

    private static void RestoreLocal(PlayerAvatar local)
    {
        PlayerController controller = PlayerController.instance;
        if (controller != null && controller.rb != null)
            controller.AntiGravity(0f); // clears the timer immediately instead of waiting ~1s for it to decay on its own

        if (local != null && GameReflect.Get<bool>(local, "isTumbling"))
        {
            PlayerTumble tumble = GameReflect.Get<PlayerTumble>(local, "tumble");
            tumble?.TumbleRequest(false, true);
        }

        RestoreWings();
        RestoreInterpolation();
    }
}
