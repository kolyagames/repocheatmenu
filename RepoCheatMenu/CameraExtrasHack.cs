// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using UnityEngine;

namespace RepoCheatMenu;

// Two local-only camera conveniences (nothing is sent to the network, works for guest and host).
//
// StableCamera - the game has accessibility settings that scale camera motion; GameplayManager keeps
// the live values in cameraShake / cameraNoise / cameraAnimation, and every CameraShake, CameraBob,
// CameraJump, CameraTilt and CameraZoom (sprint/tumble kick) multiplies by them (confirmed by grep
// across the decompile). We pin those to 0:
//  - cameraShake and cameraNoise are re-read from the settings every frame in GameplayManager.Update
//    unless the matching *OverrideTimer is running, so we keep that timer topped up and the value 0;
//  - cameraAnimation is only recomputed when the setting changes (UpdateCameraAnimation), so one write
//    holds until we switch the feature off and ask the game to recompute it.
// Result: no screen shake from explosions/hits, no head bob, no sprint FOV kick - the view stays calm.
//
// Zoom - narrows Camera.main.fieldOfView toward ZoomFov with a smooth blend while the toggle is on
// (bind the toggle to a key with a middle click in the old menu). Runs right after VisionHack so it
// wins over the widened Fov setting; the game rewrites FOV every frame, so nothing to restore.
internal static class CameraExtrasHack
{
    private static bool _stableWasOn;
    private static float _zoomBlend;

    public static void Apply()
    {
        ApplyStable();
        ApplyZoom();
    }

    private static void ApplyStable()
    {
        GameplayManager gm = GameplayManager.instance;
        if (gm == null) return;

        if (CheatState.StableCamera.Value)
        {
            _stableWasOn = true;
            GameReflect.Set(gm, "cameraShake", 0f);
            GameReflect.Set(gm, "cameraShakeOverrideTimer", 0.5f);
            GameReflect.Set(gm, "cameraNoise", 0f);
            GameReflect.Set(gm, "cameraNoiseOverrideTimer", 0.5f);
            GameReflect.Set(gm, "cameraAnimation", 0f);
        }
        else if (_stableWasOn)
        {
            _stableWasOn = false;
            GameReflect.Set(gm, "cameraShakeOverrideTimer", 0f);
            GameReflect.Set(gm, "cameraNoiseOverrideTimer", 0f);
            gm.UpdateCameraAnimation();
        }
    }

    private static void ApplyZoom()
    {
        float target = CheatState.Zoom.Value ? 1f : 0f;
        _zoomBlend = Mathf.MoveTowards(_zoomBlend, target, Time.unscaledDeltaTime * 6f);
        if (_zoomBlend <= 0.001f) return;

        Camera cam = Camera.main;
        if (cam == null) return;
        float zoomFov = Mathf.Clamp(CheatState.ZoomFov.Value, 5f, 90f);
        if (cam.fieldOfView > zoomFov)
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, zoomFov, Mathf.SmoothStep(0f, 1f, _zoomBlend));
    }
}
