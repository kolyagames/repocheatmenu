// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

namespace RepoCheatMenu;

// FPS boost for weak PCs - the idea of the REPO Potato Performance mod (its code was read to see
// which Unity switches it flips, this is an independent implementation), with two differences:
//   * nothing needs a restart: every value is backed up on the first apply and put back the moment
//     the master toggle goes off;
//   * post-processing is turned off through the game's own PostProcessing.Instance effect
//     settings (Unity PostProcessing v2, ".active"), not by guessing component names on cameras,
//     and the way back is the game's own GraphicsManager.Update*() methods, which re-apply
//     whatever the player chose in Settings -> Graphics.
//
// The game re-applies its own graphics settings on scene load and whenever a Graphics slider is
// touched, so the values are re-asserted once a second while the toggle is on.
internal static class OptimizationHack
{
    private static bool _applied;
    private static float _nextTick;

    // Originals, captured right before the first write.
    private static ShadowQuality _shadows;
    private static float _shadowDistance;
    private static int _antiAliasing;
    private static int _pixelLights;
    private static float _lodBias;
    private static bool _softParticles;
    private static bool _softVegetation;
    private static bool _reflectionProbes;
    private static bool _fog;

    // Hot reload / unload: put every value back right away (the next copy re-applies if still on).
    public static void Shutdown()
    {
        if (_applied) Restore();
    }

    public static void Tick()
    {
        bool on = CheatState.Optimization.Value;

        if (!on)
        {
            if (_applied) Restore();
            return;
        }

        if (Time.unscaledTime < _nextTick) return;
        _nextTick = Time.unscaledTime + 1f;

        if (!_applied) Capture();
        Apply();
    }

    private static void Capture()
    {
        _shadows = QualitySettings.shadows;
        _shadowDistance = QualitySettings.shadowDistance;
        _antiAliasing = QualitySettings.antiAliasing;
        _pixelLights = QualitySettings.pixelLightCount;
        _lodBias = QualitySettings.lodBias;
        _softParticles = QualitySettings.softParticles;
        _softVegetation = QualitySettings.softVegetation;
        _reflectionProbes = QualitySettings.realtimeReflectionProbes;
        _fogScene = -1;
        _applied = true;
    }

    // RenderSettings.fog belongs to the active scene, unlike the QualitySettings above. Backed up per scene
    // the first time we switch it off there, and only ever written back into that same scene - restoring a
    // value captured in another scene left levels without fog (or gave the menu fog).
    private static int _fogScene = -1;

    private static void DisableFog()
    {
        int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
        if (scene != _fogScene)
        {
            _fog = RenderSettings.fog;
            _fogScene = scene;
        }
        RenderSettings.fog = false;
    }

    private static void Apply()
    {
        if (CheatState.OptShadows.Value)
        {
            QualitySettings.shadows = ShadowQuality.Disable;
            QualitySettings.shadowDistance = 0f;
        }
        if (CheatState.OptAntiAliasing.Value) QualitySettings.antiAliasing = 0;
        if (CheatState.OptPixelLights.Value) QualitySettings.pixelLightCount = 0;
        // Potato Performance sets this to 0, which forces the lowest LOD on everything and makes
        // distant props pop into blocks. A quarter of the default keeps shapes readable.
        if (CheatState.OptLodBias.Value) QualitySettings.lodBias = Mathf.Min(QualitySettings.lodBias, 0.5f);
        if (CheatState.OptSoftParticles.Value)
        {
            QualitySettings.softParticles = false;
            QualitySettings.softVegetation = false;
        }
        if (CheatState.OptReflectionProbes.Value) QualitySettings.realtimeReflectionProbes = false;
        // Custom Fog turns fog back on every tick; fighting it once a second made the fog blink.
        if (CheatState.OptFog.Value && !CheatState.CustomFog.Value) DisableFog();
        if (CheatState.OptPostProcessing.Value) SetPostProcessing(false);
    }

    private static void Restore()
    {
        _applied = false;

        QualitySettings.shadows = _shadows;
        QualitySettings.shadowDistance = _shadowDistance;
        QualitySettings.antiAliasing = _antiAliasing;
        QualitySettings.pixelLightCount = _pixelLights;
        QualitySettings.lodBias = _lodBias;
        QualitySettings.softParticles = _softParticles;
        QualitySettings.softVegetation = _softVegetation;
        QualitySettings.realtimeReflectionProbes = _reflectionProbes;
        if (_fogScene == UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle) RenderSettings.fog = _fog;
        _fogScene = -1;

        // The game's own "apply what the player picked" - brings back shadow distance/quality and
        // every post effect to the Graphics-settings values.
        GraphicsManager gm = GraphicsManager.instance;
        if (gm != null && DataDirector.instance != null)
        {
            gm.UpdateShadowQuality();
            gm.UpdateShadowDistance();
            if (PostProcessing.Instance != null)
            {
                gm.UpdateMotionBlur();
                gm.UpdateLensDistortion();
                gm.UpdateBloom();
                gm.UpdateChromaticAberration();
                gm.UpdateGrain();
            }
        }
    }

    // Only the effects with a real GPU cost. Vignette and colour grading stay: the game also uses
    // them as gameplay feedback (damage flash, death/spectate look), and they are cheap.
    private static void SetPostProcessing(bool active)
    {
        PostProcessing pp = PostProcessing.Instance;
        if (pp == null) return;

        // The effect fields are private in the real game assembly (the compile-time stub only makes them
        // look public) - touching them directly threw FieldAccessException every second.
        foreach (string field in new[] { "bloom", "grain", "motionBlur", "lensDistortion", "chromaticAberration" })
        {
            PostProcessEffectSettings effect = GameReflect.Get<PostProcessEffectSettings>(pp, field);
            if (effect != null) effect.active = active;
        }
    }
}
