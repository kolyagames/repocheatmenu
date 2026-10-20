// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using UnityEngine;

namespace RepoCheatMenu;

// What the game does while its window is not in focus (alt-tab, second monitor):
//   BackgroundFpsLimit - caps the frame rate at BackgroundFps (and turns VSync off, which would otherwise
//                        ignore the cap), so a game sitting in the background stops heating the GPU;
//   MuteInBackground   - silences all game audio (AudioListener.volume - the game itself never touches it).
// Both come back the moment the window is focused again: the frame rate through the game's own
// GraphicsManager.UpdateVsync / UpdateMaxFPS (so the player's settings apply, not a remembered number), the
// volume to what it was. Your microphone keeps working. Local only.
internal static class BackgroundHack
{
    private static bool _fpsLimited;
    private static bool _muted;
    private static float _volumeBefore = 1f;

    public static void Apply()
    {
        bool background = !Application.isFocused;

        if (background && CheatState.BackgroundFpsLimit.Value)
        {
            _fpsLimited = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = Mathf.Clamp(CheatState.BackgroundFps.Value, 5, 60);
        }
        else if (_fpsLimited)
        {
            _fpsLimited = false;
            GraphicsManager gm = GraphicsManager.instance;
            if (gm != null)
            {
                gm.UpdateVsync();
                gm.UpdateMaxFPS();
            }
            else
            {
                Application.targetFrameRate = -1;
            }
        }

        if (background && CheatState.MuteInBackground.Value)
        {
            if (!_muted)
            {
                _volumeBefore = AudioListener.volume;
                _muted = true;
            }
            AudioListener.volume = 0f;
        }
        else if (_muted)
        {
            _muted = false;
            AudioListener.volume = _volumeBefore;
        }
    }
}
