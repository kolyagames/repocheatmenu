using UnityEngine;

namespace RepoCheatMenu;

// Fly previously drove PlayerController.rb via MovePosition/velocity from a FixedUpdate postfix
// (the "win the physics step" approach) - reported as producing no visible movement at all in
// practice. Switched to the technique GameTools' own God Mode Flight uses (their compiled DLL
// decompiled to study the approach only - own implementation, not their code): patch
// PlayerController.Update (regular per-frame Update, not FixedUpdate) and write
// transform.position directly via Time.deltaTime, bypassing the Rigidbody's velocity/force path
// entirely instead of trying to out-write it. Rigidbody goes fully kinematic and the Collider
// component gets disabled for either toggle (see below) - a kinematic body isn't blocked by
// collision resolution regardless of its collider, so Fly now inherently passes through walls
// too (folding in what Noclip used to be needed for on top of Fly specifically).
//
// Noclip alone (no Fly) keeps its own separate fall-through-floor path - still useful by itself
// to sink through the floor without flying around. 2026-09-09: root-caused why it used to look
// like it "snapped back to the floor" after a moment - it only ever touched
// PlayerController.rb/transform, never PlayerTumble's own separate ragdoll rigidbody, which kept
// resting on the REAL floor undisturbed the whole time. Any tumble (a trip, a hit, fall damage)
// while noclipping made PlayerController.cs's own tumble-exit code (confirmed in the decompiled
// source) re-sync transform.position to wherever that ragdoll had settled, wiping out whatever
// fake sunken/flying position we'd reached.
//
// First fix attempt (Patches.cs's CoreStats tick forcing DebugNoTumble true whenever Fly/Noclip is
// on) was CONFIRMED LIVE via the debug bridge to be necessary but not sufficient. Root cause of
// the gap, confirmed in PlayerTumble.cs:615 (TumbleRequest): DebugNoTumble only gates the
// condition "(!DebugNoTumble || _playerInput)" - it blocks NEW environmental tumbles
// (_playerInput: false, e.g. every enemy/impact/PlayerAvatar.cs's own fall-damage-over-5-units
// check), but a tumble already IN PROGRESS has no automatic timeout - the only vanilla exit path
// (PlayerController.cs:1148) requires an actual Jump/Tumble keypress while tumbling. Two ways this
// still bit us live: fallDistance keeps accumulating the whole time Noclip has you "falling" and
// ungrounded (PlayerCollisionController.cs), so it blows past the 5-unit fall-damage threshold
// within the first second or so of any noclip session; and there's a real race on EVERY toggle-on
// - our own DebugNoTumble write happens on TimeTickPatch's ~33/sec cadence, not the same frame
// Noclip.Value flips true, so an environmental tumble can slip through in that gap before
// DebugNoTumble catches up. Once it does, and rb.isKinematic below reads false with isTumbling
// stuck true, PlayerTumble's OWN non-kinematic rigidbody keeps resting on the real floor untouched
// forever after - confirmed live: transform.position visibly oscillated (sank a bit from our
// write, snapped back near the ragdoll's real resting height the very next frame, repeatedly) for
// the rest of that session. Fix: force-exit any tumble that's already active the moment Fly/Noclip
// turns on, using the exact same call the game's own voluntary stand-up makes
// (TumbleRequest(false, playerInput: true) - the playerInput:true is what makes DebugNoTumble not
// matter for this specific call, same as a real Jump/Tumble press would).
internal static class FlyHack
{
    private static bool _wasFlying;
    private static bool _wasKinematic;
    private static bool _colliderDisabled;
    private static float _noclipFallSpeed;

    // Called from the PlayerController.Update postfix - see Patches.cs.
    public static void ApplyUpdate(PlayerController controller)
    {
        if (controller == null || controller.rb == null) return;

        bool noclip = CheatState.Noclip.Value;
        bool fly = CheatState.Fly.Value;
        bool wantKinematic = fly || noclip;

        // Applied EVERY frame while active, not just on the transition. Measured live: with the
        // old edge-triggered writes, useGravity (written unconditionally below) stuck at false as
        // intended while isKinematic and col.enabled were both back to the game's own values -
        // PlayerController re-asserts them itself, and once _wasKinematic/_colliderDisabled were
        // set the mod never wrote them again. That's why Fly moved but still collided with walls.
        // Restoring on the way out still has to be edge-triggered, so we don't fight the game for
        // these fields while the cheat is off.
        if (wantKinematic)
        {
            if (!_wasKinematic) _noclipFallSpeed = 0f;
            controller.rb.isKinematic = true;
            _wasKinematic = true;

            // Force-exit an already-active tumble every frame Fly/Noclip is on - see the class
            // comment above for why DebugNoTumble alone (blocking new tumbles) isn't enough.
            // TumbleRequest(false, playerInput:true) was the first attempt here - confirmed LIVE
            // via the debug bridge to be unreliable: its gate gets routed through
            // MasterAndOwnerOnlyRPC/PhotonView.Owner checks that depend on Photon room state, which
            // didn't clear the tumble even once when tested. TumbleSet(false, true) - the same
            // lower-level, ungated "just set the fields" primitive PlayerAvatar.cs's own -100
            // world-bounds catch calls directly for the identical reason (force a stuck player out
            // of tumbling right now, no RPC round-trip) - worked instantly when tested the same
            // way. Called every frame (not once on a transition) because whatever keeps
            // re-triggering isTumbling here (fallDistance climbs the whole time Noclip has you
            // ungrounded, so PlayerAvatar.cs's own fall-damage tumble check re-fires constantly)
            // needs to lose every single frame, not just once - same "if the game owns the field,
            // write it every frame" rule as isKinematic above. Only a LOCAL override (no RPC), same
            // limitation MasterRpcSelfBypass already documents elsewhere - fine here, Fly/Noclip
            // were never going to look normal to other clients anyway.
            PlayerAvatar avatar = PlayerAvatar.instance;
            if (avatar != null && GameReflect.Get<bool>(avatar, "isTumbling"))
            {
                PlayerTumble tumble = GameReflect.Get<PlayerTumble>(avatar, "tumble");
                tumble?.TumbleSet(_isTumbling: false, _playerInput: true);
            }
        }
        else if (_wasKinematic)
        {
            controller.rb.isKinematic = false;
            _wasKinematic = false;
            _noclipFallSpeed = 0f;
        }

        // Was fly-only - Noclip alone left the collider enabled with no stated reason. Kinematic
        // bodies ghost through static geometry regardless of collider state (that part was never
        // what needed this), but a live collider still lets traps/enemy hitboxes register you
        // while sinking through the floor. Disabling it for wantKinematic (either toggle) instead
        // of fly-only closes that gap and matches what Fly already does for the same reason.
        if (wantKinematic && controller.col != null)
        {
            controller.col.enabled = false;
            _colliderDisabled = true;
        }
        else if (!wantKinematic && _colliderDisabled && controller.col != null)
        {
            controller.col.enabled = true;
            _colliderDisabled = false;
        }

        if (fly)
        {
            controller.rb.useGravity = false;
            controller.rb.velocity = Vector3.zero;

            Camera cam = GameDirector.instance != null ? GameDirector.instance.MainCamera : null;
            Vector3 dir = Vector3.zero;
            if (cam != null)
            {
                float forwardIn = SemiFunc.InputMovementY();
                float strafeIn = SemiFunc.InputMovementX();
                dir = cam.transform.forward * forwardIn + cam.transform.right * strafeIn;
            }
            if (SemiFunc.InputDown(InputKey.Jump)) dir += Vector3.up;
            if (SemiFunc.InputDown(InputKey.Crouch)) dir += Vector3.down;
            if (dir.sqrMagnitude > 1f) dir.Normalize();

            float speed = CheatState.FlySpeed.Value;
            if (CheatState.FlySprintBoost.Value && SemiFunc.InputDown(InputKey.Sprint))
                speed *= 2f;

            if (dir.sqrMagnitude > 0.0001f)
                controller.transform.position += dir * speed * Time.deltaTime;

            _wasFlying = true;
        }
        else if (noclip)
        {
            // Kinematic bodies ignore Physics.gravity, so falling is integrated by hand here.
            _noclipFallSpeed += Physics.gravity.y * Time.deltaTime;
            controller.transform.position += Vector3.up * (_noclipFallSpeed * Time.deltaTime);
        }
        else if (_wasFlying)
        {
            controller.rb.useGravity = true;
            _wasFlying = false;
        }
    }
}
