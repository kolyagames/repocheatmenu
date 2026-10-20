// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace RepoCheatMenu;

// FlashlightController.spotlight/baseIntensity confirmed from RepoHax's game_classes.h.
//
// FindObjectsOfType used to run unconditionally every tick (~33/sec) - its cost scales with how
// many FlashlightController instances exist in the scene, which only grows over a run (more
// players/flashlights). Same throttled-rescan pattern as CartHack: refresh the list once a
// second instead, since which flashlights exist doesn't need 33/sec freshness.
internal static class FlashlightHack
{
    private static float _nextRescan;
    private static readonly List<FlashlightController> _flashlights = new();

    public static void Apply()
    {
        if (Time.unscaledTime >= _nextRescan)
        {
            _nextRescan = Time.unscaledTime + 1f;
            _flashlights.Clear();
            _flashlights.AddRange(Object.FindObjectsByType<FlashlightController>(FindObjectsSortMode.None));
        }

        foreach (FlashlightController flashlight in _flashlights)
        {
            if (flashlight == null) continue;
            Light spotlight = GameReflect.Get<Light>(flashlight, "spotlight");
            if (spotlight == null) continue;

            // 60/10 are the documented vanilla defaults - skip touching the light at all unless
            // the slider was actually moved, otherwise we'd fight any flicker/animation the game
            // itself applies to the flashlight every tick (same flicker bug as VisionHack's Fov).
            if (CheatState.FlashAngle.Value != 60)
                spotlight.spotAngle = CheatState.FlashAngle.Value;

            if (CheatState.FlashlightIntensity.Value != 10)
            {
                float baseIntensity = GameReflect.Get<float>(flashlight, "baseIntensity");
                spotlight.intensity = baseIntensity * (CheatState.FlashlightIntensity.Value / 10f);
            }

            if (CheatState.FlashInCrouch.Value)
                spotlight.enabled = true;

            if (CheatState.FlashlightColorEnabled.Value)
                spotlight.color = GetColor();
        }
    }

    private static Color _cachedRainbowColor = Color.white;
    private static float _nextRainbowUpdate;

    private static Color GetColor()
    {
        if (!CheatState.FlashlightColorRainbow.Value)
            return ColorUtility.TryParseHtmlString(CheatState.FlashlightColorHex.Value, out Color c) ? c : Color.white;

        if (Time.unscaledTime >= _nextRainbowUpdate)
        {
            _nextRainbowUpdate = Time.unscaledTime + 0.12f;
            _cachedRainbowColor = Color.HSVToRGB(Mathf.Repeat(Time.time * 0.3f, 1f), 1f, 1f);
        }
        return _cachedRainbowColor;
    }
}
