// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// Часть дизайна, чисел и ресурсов здесь перенесена из клиента Rockstar;
// эти части принадлежат их авторам.
// ============================================================================

using UnityEngine;

namespace RepoCheatMenu;

// Rockstar's Custom Fog: linear fog with an adjustable start/end and a tint. Rockstar tints with the theme colour
// at ~46 % strength; here the tint is blended into the level's own fog colour at that strength, so a level that
// ships with its own fog keeps its mood. Backed up on the first write and put back the moment it is switched
// off; the level re-applies its own fog now and then, so the values are re-asserted every frame while on.
internal static class CustomFogHack
{
    private static bool _applied;
    private static bool _fog;
    private static FogMode _mode;
    private static float _start, _end, _density;
    private static Color _color;

    public static void Apply()
    {
        if (!CheatState.CustomFog.Value)
        {
            if (_applied) Restore();
            return;
        }

        // RenderSettings belong to the active scene: a newly loaded level comes with its own fog, so back that
        // one up instead of carrying the previous scene's values over (they tinted from the wrong colour and
        // were written into the wrong level on restore).
        int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
        if (!_applied || scene != _scene)
        {
            _scene = scene;
            _fog = RenderSettings.fog;
            _mode = RenderSettings.fogMode;
            _start = RenderSettings.fogStartDistance;
            _end = RenderSettings.fogEndDistance;
            _density = RenderSettings.fogDensity;
            _color = RenderSettings.fogColor;
            _applied = true;
        }

        float start = Mathf.Max(0f, CheatState.CustomFogStart.Value);
        float end = Mathf.Max(start + 1f, CheatState.CustomFogEnd.Value);
        Color tint = GlowFx.FxColor();
        float strength = Mathf.Clamp01(CheatState.CustomFogTint.Value / 100f);

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = start;
        RenderSettings.fogEndDistance = end;
        RenderSettings.fogColor = Color.Lerp(_color, tint, strength);
    }

    private static int _scene = -1;

    private static void Restore()
    {
        _applied = false;
        // A scene loaded since the last write still has its own untouched fog.
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle != _scene) return;
        RenderSettings.fog = _fog;
        RenderSettings.fogMode = _mode;
        RenderSettings.fogStartDistance = _start;
        RenderSettings.fogEndDistance = _end;
        RenderSettings.fogDensity = _density;
        RenderSettings.fogColor = _color;
    }

    public static void Shutdown()
    {
        if (_applied) Restore();
    }
}
