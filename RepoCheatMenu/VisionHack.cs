// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace RepoCheatMenu;

// BetterVision/Fov/ThirdPerson logic ported from RepoHax's PostLateUpdateLoop hook (extend
// far clip plane, force FOV, offset the camera behind the player with a SphereCast so it
// doesn't clip through walls).
//
// Tried riding the game's own CameraPosition.OverridePosition() (confirmed in the decompiled
// source - the built-in mechanism some other game feature uses for a temporary third-person-style
// camera peek) instead of touching Camera.main directly, hoping its own SetThirdPerson() calls on
// physGrabber/flashlightController would fix grab-follows-camera for free. REVERTED - confirmed
// live via the debug bridge that calling OverridePosition() continuously (every tick, to keep an
// indefinite session alive rather than the brief peek it was designed for) eventually sent the
// PLAYER'S OWN PlayerController.transform.position into freefall (observed reaching five and six
// digits negative Y, PlayerAvatar.instance null shortly after) - not just a camera artifact, a
// real, severe, reproduced-once regression. Not worth the risk for this mod's actual purpose - back
// to direct Camera.main.transform manipulation, which is simple and has no player-physics blast
// radius (confirmed years of prior use in this file with no comparable incident).
internal static class VisionHack
{
    private static bool _wasThirdPerson;
    private static float? _backupVignetteIntensity;
    private static Vector3 _smoothedThirdPersonOffset;
    private static Vector3? _backupVisualsScale;
    private static Vector3? _backupCamLocalPos;

    // Where the camera would actually be/point in first person right now, kept up to date every
    // tick while Third Person is on - ThirdPersonAimPatch (Patches.cs) substitutes this for
    // PlayerLocalCamera.GetOverrideTransform()'s normal result, which every grab/tool/weapon/
    // vehicle aim in the game reads (confirmed by a grep across the whole decompiled source - this
    // one method is THE canonical "where is the player aiming" accessor, not something specific to
    // grabbing). Read-only substitution, touches no player physics - safe, unlike the
    // CameraPosition experiment above. Reconstructed from _backupCamLocalPos (the real local
    // offset captured before we ever touched it) re-applied to the camera's CURRENT parent
    // transform each tick, rather than reading cam.transform.position directly - by the time this
    // runs, cam's own local position has already been overwritten with the third-person offset
    // from this same tick, so reading it straight back out would just return the displaced value,
    // not the true one. Rotation needs no reconstruction: Third Person never touches
    // localRotation, only localPosition, so cam.transform.rotation is always already the true aim
    // rotation.
    public static Vector3? TrueAimPosition { get; private set; }
    public static Quaternion? TrueAimRotation { get; private set; }

    public static void Apply()
    {
        Camera cam = Camera.main;
        PlayerAvatar avatar = PlayerAvatar.instance;
        if (cam == null || avatar == null) return;

        if (CheatState.BetterVision.Value && cam.farClipPlane < 1000f)
            cam.farClipPlane = 1000f;

        // The tooltip's "disables the level's darkness post-processing" half was never actually
        // implemented - only draw distance was. Confirmed in the decompiled PostProcessing.cs:
        // the level's darkness comes from its Vignette effect (PostProcessing.Setup() sets
        // vignette.intensity from Level.VignetteIntensity once at level start), and it gets
        // pulled back UP by VignetteOverride() (damage flashes, tumbling, etc. all briefly spike
        // it before easing back to that same non-zero baseline) - so this has to keep forcing it
        // to 0 every tick, a one-shot set at level start would just fade back to dark. Only the
        // edge-darkening vignette is touched, not colorFilter/temperature (those are each level's
        // actual mood/palette, not "darkness").
        Vignette vignette = GameReflect.Get<Vignette>(PostProcessing.Instance, "vignette");
        if (vignette != null)
        {
            if (CheatState.BetterVision.Value)
            {
                _backupVignetteIntensity ??= vignette.intensity.value;
                vignette.intensity.value = 0f;
            }
            else if (_backupVignetteIntensity.HasValue)
            {
                vignette.intensity.value = _backupVignetteIntensity.Value;
                _backupVignetteIntensity = null;
            }
        }

        // 60 is the documented vanilla baseline (see CheatState's tooltip) - treat it as "off"
        // so we don't fight the camera's own FOV animations (sprint kick, transitions) every
        // tick when the slider hasn't been touched. That constant fighting is what was causing
        // the environment to visibly flicker. Once raised above vanilla, only widen (never
        // narrow) so we don't cancel out any intentional zoom-in the game does on its own.
        if (CheatState.Fov.Value > 60 && cam.fieldOfView < CheatState.Fov.Value)
            cam.fieldOfView = CheatState.Fov.Value;

        bool thirdPerson = CheatState.ThirdPerson.Value && SpectateCamera.instance == null;
        if (thirdPerson)
        {
            float maxDist = Mathf.Max(0.5f, CheatState.ThirdPersonDistance.Value);
            float dist = maxDist;
            Transform pivot = GameReflect.Get<Transform>(avatar, "spectatePoint");
            MapToolController mapTool = GameReflect.Get<MapToolController>(avatar, "mapToolController");
            bool mapOpen = mapTool != null && GameReflect.Get<bool>(mapTool, "Active");
            if (mapOpen)
                dist = Mathf.Min(1f, maxDist);
            // Reported live (2026-09-10): camera still flickering with the smoothing below already
            // in place. Root cause the smoothing couldn't fully absorb: this cast had NO layer mask
            // at all (Unity defaults an unspecified one to every layer), so ANYTHING with a collider
            // crossing the line - another player walking past, a thrown item, a rolling valuable,
            // even the local player's own capsule collider - counted as "wall" and yanked the raw
            // target distance around every time one did, not just the occasional wall-corner
            // flip-flop the smoothing was originally built for. Only "Default" (confirmed the static
            // level-geometry layer via SemiFunc.LayerMaskGetVisionObstruct's own list of what counts
            // as solid, though that mask also includes Player/PhysGrabObject on purpose for ITS use
            // case - ours specifically wants just the architecture) should ever push the camera in.
            else if (pivot != null && Physics.SphereCast(pivot.position, 0.1f, -pivot.forward, out RaycastHit hit, maxDist,
                LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore))
                dist = Mathf.Clamp(hit.distance, Mathf.Min(1f, maxDist), maxDist);

            Vector3 target = new Vector3(CheatState.ThirdPersonHorizontal.Value,
                CheatState.ThirdPersonHeight.Value, -dist);

            if (!_wasThirdPerson)
            {
                // Capture whatever the camera's real local position actually was right before we
                // touch it - NOT an assumed Vector3.zero. Reported live: turning Third Person off
                // left the camera stuck away from normal first-person view instead of restoring
                // properly - the old code force-set Vector3.zero on exit, which is only correct if
                // the camera's own rest-state local position genuinely IS the origin. Confirmed
                // live via the debug bridge it is NOT - the real rest value measured ~(0, 0.46,
                // -2.0), not the origin - so snapping to zero on exit moved it to a DIFFERENT wrong
                // spot instead of back to normal, which read exactly like "the toggle doesn't
                // work, still stuck in third person".
                _backupCamLocalPos ??= cam.transform.localPosition;
                _smoothedThirdPersonOffset = target; // snap on entry, nothing to ease from yet
            }
            else
            {
                // Exponential-decay follow (the "smooth camera" style Minecraft visual clients use
                // for cinematic recording, not just a jitter patch) - frame-rate independent, unlike
                // a naive Lerp(a, b, speed * deltaTime) which overshoots into instability once
                // speed * deltaTime exceeds 1. Also what actually fixed the reported jitter: the
                // wall-avoidance SphereCast above is a fresh independent cast every tick, and at
                // longer distances indoors it can flip between "clear" and "blocked" on consecutive
                // frames as the look direction shifts by fractions of a degree - easing toward the
                // target instead of snapping to it absorbs exactly that single-frame flip-flop.
                float smoothing = Mathf.Max(0.5f, CheatState.ThirdPersonSmoothness.Value);
                float t = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
                _smoothedThirdPersonOffset = Vector3.Lerp(_smoothedThirdPersonOffset, target, t);
            }

            Transform camParent = cam.transform.parent;
            TrueAimPosition = _backupCamLocalPos.HasValue
                ? (camParent != null ? camParent.TransformPoint(_backupCamLocalPos.Value) : _backupCamLocalPos.Value)
                : cam.transform.position;
            TrueAimRotation = cam.transform.rotation;

            cam.transform.localPosition = _smoothedThirdPersonOffset;

            // GameTools' "Third Person Model Scale" - shrinks/grows your own visible mesh while
            // looking at yourself in Third Person, purely cosmetic. playerAvatarVisuals is
            // confirmed public (CosmeticHack already reads it directly, no reflection needed) and
            // is its own visuals-only component - scaling its transform never touches
            // PlayerController's actual movement/collision transform, same separation
            // DinnerBone/Flip's self-preview already rely on for meshParent specifically.
            Transform visualsRoot = avatar.playerAvatarVisuals != null ? avatar.playerAvatarVisuals.transform : null;
            if (visualsRoot != null)
            {
                _backupVisualsScale ??= visualsRoot.localScale;
                float scale = Mathf.Clamp(CheatState.ThirdPersonModelScale.Value, 0.1f, 3f);
                visualsRoot.localScale = Vector3.one * scale;
            }
        }
        else if (_wasThirdPerson)
        {
            if (_backupCamLocalPos.HasValue)
            {
                cam.transform.localPosition = _backupCamLocalPos.Value;
                _backupCamLocalPos = null;
            }
            if (_backupVisualsScale.HasValue && avatar.playerAvatarVisuals != null)
            {
                avatar.playerAvatarVisuals.transform.localScale = _backupVisualsScale.Value;
                _backupVisualsScale = null;
            }
            TrueAimPosition = null;
            TrueAimRotation = null;
        }
        _wasThirdPerson = thirdPerson;
    }
}
