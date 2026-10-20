// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RepoCheatMenu;

// Clean screenshots and a "photo mode" without any interface on screen.
//
// Screenshot (button / bindable key): for exactly one frame every canvas the mod owns (all of them are
// created under GameObjects named "RepoCheatMenu_*": HUD and F6 menu, old menu, ESP labels, extraction
// radar) and the game's own HUD (HUD.instance.Hide - the same public call the game uses for cutscenes and
// the level intro) are switched off, ScreenCapture.CaptureScreenshot writes that frame to
// Pictures\REPO\REPO_<date>.png, and on the next frame everything comes back.
//
// Photo mode (toggle): the same hiding, held for as long as it is on - except while one of the mod's menus is
// open, so the toggle can always be switched off again from the menu it was switched on from.
//
// Only Canvas.enabled is touched (never the GameObjects), so the menus' own open/closed state, which they keep
// in GameObject.activeSelf, is not disturbed. The game's HUD is only shown again if it was this class that hid
// it, so a cutscene that hid it on its own is not cut short. Local only.
internal static class PhotoModeHack
{
    private static readonly List<Canvas> HiddenCanvases = new();
    private static bool _gameHudHiddenByUs;
    private static bool _shotPending;
    private static int _shotFrame = -1;
    private static string _shotPath;
    public static string LastShot = "";

    public static void TakeScreenshot() => _shotPending = true;

    public static void ForceShow()
    {
        _shotPending = false;
        _shotFrame = -1;
        ShowAll();
    }

    private static string ShotFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "REPO");

    // Called first thing in HudRoot.Tick, i.e. early in the frame, before anything renders.
    public static void Tick()
    {
        int frame = Time.frameCount;

        if (_shotPending)
        {
            _shotPending = false;
            try
            {
                Directory.CreateDirectory(ShotFolder);
                _shotPath = Path.Combine(ShotFolder, $"REPO_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png");
                HideAll(includeMenus: true);
                ScreenCapture.CaptureScreenshot(_shotPath); // written at the end of this frame
                _shotFrame = frame;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("PhotoMode: screenshot failed: " + e.Message);
                DynamicIsland.Notify("Скриншот", "Не удалось: " + e.Message, false);
                _shotFrame = -1;
            }
            return;
        }

        if (_shotFrame >= 0)
        {
            if (frame == _shotFrame) return; // still the capture frame
            _shotFrame = -1;
            LastShot = _shotPath;
            Plugin.Log.LogInfo("PhotoMode: screenshot saved to " + _shotPath);
            ShowAll();
            DynamicIsland.Notify("Скриншот", Path.GetFileName(_shotPath), true);
        }

        bool menuOpen = OverlayUI.IsOpen || CustomOverlayUI.IsOpen;
        if (CheatState.PhotoMode.Value && !menuOpen) HideAll(includeMenus: false);
        else ShowAll();
    }

    private static float _nextCanvasScan;

    private static void HideAll(bool includeMenus)
    {
        // The screenshot path must hide everything this very frame; the held photo mode only needs to
        // catch canvases created since the last pass, so the scene-wide scan runs twice a second there.
        bool scan = includeMenus || Time.unscaledTime >= _nextCanvasScan;
        if (scan) _nextCanvasScan = Time.unscaledTime + 0.5f;
        if (scan)
        foreach (Canvas canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (canvas == null || !canvas.enabled) continue;
            if (!canvas.transform.root.name.StartsWith("RepoCheatMenu_", StringComparison.Ordinal)) continue;
            if (!includeMenus && canvas.transform.root.name == "RepoCheatMenu_Overlay") continue;
            canvas.enabled = false;
            HiddenCanvases.Add(canvas);
        }

        HUD hud = HUD.instance;
        if (hud != null && !hud.hidden)
        {
            hud.Hide();
            _gameHudHiddenByUs = true;
        }
    }

    private static void ShowAll()
    {
        if (HiddenCanvases.Count > 0)
        {
            foreach (Canvas canvas in HiddenCanvases)
                if (canvas != null) canvas.enabled = true;
            HiddenCanvases.Clear();
        }

        if (_gameHudHiddenByUs)
        {
            _gameHudHiddenByUs = false;
            HUD hud = HUD.instance;
            if (hud != null && hud.hidden) hud.Show();
        }
    }
}
