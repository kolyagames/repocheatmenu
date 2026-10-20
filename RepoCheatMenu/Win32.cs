// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace RepoCheatMenu;

// Reads keyboard state straight from the OS (like RepoHax does by hooking GetAsyncKeyState)
// instead of going through UnityEngine.Input, which we observed never reporting key presses
// reliably to our patches in this game.
internal static class Win32
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    // GetAsyncKeyState reports the keyboard system-wide, so without this check typing in a browser or
    // Discord while the game sits in the background fired every hotkey and keybind of the mod. All key
    // polling below reports "nothing pressed" while the game window is not the focused one.
    public static bool GameFocused
    {
        get
        {
            try { return Application.isFocused; }
            catch { return true; }
        }
    }

    public static bool IsKeyDown(KeyCode keyCode)
    {
        if (!GameFocused) return false;
        int vk = ToVirtualKey(keyCode);
        if (vk == 0) return false;
        return (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    // --- Generic virtual-key polling for the keybind system (KeybindManager) ---
    // Raw VK codes instead of KeyCode so mouse buttons (which UnityEngine.KeyCode/Input also
    // doesn't report reliably here, same as keyboard) work the same way as keys.
    private const int VkRangeMax = 254;
    private static readonly bool[] Down = new bool[VkRangeMax + 1];
    private static readonly bool[] PrevDown = new bool[VkRangeMax + 1];

    private static readonly Dictionary<int, string> VkNames = new()
    {
        { 0x01, "Mouse Left" }, { 0x02, "Mouse Right" }, { 0x04, "Mouse Middle" },
        { 0x05, "Mouse 4" }, { 0x06, "Mouse 5" },
        { 0x09, "Tab" }, { 0x14, "CapsLock" }, { 0x20, "Space" },
        { 0x21, "PageUp" }, { 0x22, "PageDown" }, { 0x23, "End" }, { 0x24, "Home" },
        { 0x2D, "Insert" }, { 0x2E, "Delete" },
        { 0x25, "←" }, { 0x26, "↑" }, { 0x27, "→" }, { 0x28, "↓" },
        { 0xA0, "LShift" }, { 0xA1, "RShift" }, { 0xA2, "LCtrl" }, { 0xA3, "RCtrl" },
        { 0xA4, "LAlt" }, { 0xA5, "RAlt" }, { 0xC0, "`" },
    };

    private static readonly int[] BindableVks = BuildBindableVks();

    private static int[] BuildBindableVks()
    {
        List<int> vks = new(VkNames.Keys);
        for (int vk = 0x41; vk <= 0x5A; vk++) vks.Add(vk); // A-Z
        for (int vk = 0x30; vk <= 0x39; vk++) vks.Add(vk); // 0-9
        for (int vk = 0x70; vk <= 0x7B; vk++) vks.Add(vk); // F1-F12
        return vks.ToArray();
    }

    // Call once per tick, before any IsVkDown/WasVkPressed/PollNewlyPressedVk queries for that tick.
    private static bool _swallowFocusReturn;

    public static void PollFrame()
    {
        if (!GameFocused)
        {
            // Not our window: nothing is pressed as far as the mod is concerned.
            for (int vk = 1; vk <= VkRangeMax; vk++) { PrevDown[vk] = false; Down[vk] = false; }
            _swallowFocusReturn = true;
            return;
        }

        if (_swallowFocusReturn)
        {
            // The window just came back: keys still held from before (alt-tab itself, a half-typed
            // word) must not count as fresh presses.
            _swallowFocusReturn = false;
            for (int vk = 1; vk <= VkRangeMax; vk++)
            {
                bool held = (GetAsyncKeyState(vk) & 0x8000) != 0;
                PrevDown[vk] = held;
                Down[vk] = held;
            }
            return;
        }

        for (int vk = 1; vk <= VkRangeMax; vk++)
        {
            PrevDown[vk] = Down[vk];
            Down[vk] = (GetAsyncKeyState(vk) & 0x8000) != 0;
        }
    }

    public static bool WasVkPressed(int vk) => vk > 0 && vk <= VkRangeMax && Down[vk] && !PrevDown[vk];

    // Is any bindable key/mouse button held right now (as of the last PollFrame)?
    public static bool AnyBindableDown()
    {
        foreach (int vk in BindableVks)
            if (vk > 0 && vk <= VkRangeMax && Down[vk]) return true;
        return false;
    }

    // First bindable key/mouse button newly pressed this tick, for "click Set then press a key" capture. 0 = none.
    public static int PollNewlyPressedVk()
    {
        foreach (int vk in BindableVks)
            if (WasVkPressed(vk)) return vk;
        return 0;
    }

    public static string VkName(int vk)
    {
        if (vk == 0) return "Нет";
        if (VkNames.TryGetValue(vk, out string name)) return name;
        if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
        if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
        if (vk >= 0x70 && vk <= 0x7B) return "F" + (vk - 0x70 + 1);
        return "VK" + vk.ToString("X2");
    }

    // --- Spotify "now playing" for PlayerHud's media widget ---
    // Deliberately NOT using the Windows System Media Transport Controls API
    // (Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager) even though it's
    // the "real" system-wide now-playing source - that's a WinRT/COM API designed for apps
    // running on .NET Framework/.NET Core's WinRT projection support, and this whole plugin runs
    // under Unity's MONO runtime (confirmed: R.E.P.O. ships with UnityEngine.Modules 2022.3, a
    // Mono-backend BepInEx 5 target, not IL2CPP or .NET). Mono's WinRT/COM activation support is
    // known-incomplete - the exact same class of risk this project's own CLAUDE.md already
    // documents for touching the wrong API at the wrong time (the PhotonNetwork premature-init
    // saga), except a bad WinRT call risks hanging/crashing the whole game process, not just a
    // menu feature. Reading Spotify's own window title instead is plain user32.dll P/Invoke -
    // the exact same proven-safe mechanism GetAsyncKeyState above already uses in this file - and
    // is a long-standing, well-known Spotify desktop client behavior: the main window's title is
    // "Spotify" while paused/idle and "Artist - Track" while something is actually playing.
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private static string _spotifyTitleCache;
    private static float _spotifyTitleCacheUntil;

    // Null = Spotify isn't running. "Spotify" = running but paused / nothing loaded / no window.
    // Otherwise "Artist - Track".
    //
    // The old lookup was FindWindow("SpotifyMainWindow"), which only matches the pre-2021 client:
    // the current desktop client is Chromium-based and its main window class is
    // Chrome_WidgetWin_1 (seen live: Spotify.exe owns ~7 processes and a dozen hidden helper
    // windows, only one with a title). So: take the Spotify.exe process ids, walk the top-level
    // windows, and keep the titled one of those classes. Cached for 0.4 s because the player
    // widget and the lyrics both ask every frame or two.
    public static string GetSpotifyWindowTitle()
    {
        if (Time.unscaledTime < _spotifyTitleCacheUntil) return _spotifyTitleCache;
        _spotifyTitleCacheUntil = Time.unscaledTime + 0.4f;
        _spotifyTitleCache = ReadSpotifyWindowTitle();
        return _spotifyTitleCache;
    }

    private static string ReadSpotifyWindowTitle()
    {
        var pids = new HashSet<uint>();
        try
        {
            foreach (System.Diagnostics.Process p in System.Diagnostics.Process.GetProcessesByName("Spotify"))
            {
                pids.Add((uint)p.Id);
                p.Dispose();
            }
        }
        catch (Exception)
        {
            return null;
        }
        if (pids.Count == 0) return null;

        string found = null;
        var title = new StringBuilder(512);
        var cls = new StringBuilder(128);
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (!pids.Contains(pid)) return true;

            cls.Clear();
            GetClassName(hwnd, cls, cls.Capacity);
            string c = cls.ToString();
            if (c != "Chrome_WidgetWin_1" && c != "SpotifyMainWindow") return true;

            title.Clear();
            GetWindowText(hwnd, title, title.Capacity);
            if (title.Length == 0) return true;

            found = title.ToString();
            return false;
        }, IntPtr.Zero);

        // Spotify running but the main window is closed to the tray, or it shows its idle title
        // ("Spotify", "Spotify Free", "Spotify Premium"): nothing is playing as far as we can tell.
        if (found == null || found.StartsWith("Spotify", StringComparison.Ordinal) && !found.Contains(" - "))
            return "Spotify";
        return found;
    }

    // Real transport control for the HUD media widget's play/pause/prev/next row - the OS-level
    // media keys (the same virtual keys a physical keyboard's media row sends), not anything
    // Spotify-specific. keybd_event is the same plain user32.dll mechanism as GetAsyncKeyState/
    // FindWindow above, not the WinRT session-manager API this file already explains avoiding -
    // any app that responds to hardware media keys (Spotify included) responds to these the same
    // way it would to a real keyboard, so this works even if the active window is the game itself.
    private const byte VkMediaNextTrack = 0xB0;
    private const byte VkMediaPrevTrack = 0xB1;
    private const byte VkMediaPlayPause = 0xB3;
    private const uint KeyEventFKeyUp = 0x0002;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private static void TapMediaKey(byte vk)
    {
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, KeyEventFKeyUp, UIntPtr.Zero);
    }

    public static void MediaPlayPause() => TapMediaKey(VkMediaPlayPause);
    public static void MediaNextTrack() => TapMediaKey(VkMediaNextTrack);
    public static void MediaPrevTrack() => TapMediaKey(VkMediaPrevTrack);

    // Plugin.MenuKey is a plain ConfigEntry<KeyCode> - BepInEx's config UI (and a hand-edited .cfg)
    // let the user pick ANY of Unity's ~150 KeyCode values, not just the handful this switch
    // originally covered. Found live-auditing this file: KeyCode.Escape fell through to the
    // `default: return 0` case below, and IsKeyDown short-circuits to false whenever vk==0 - so
    // Patches.cs's "Escape closes the RightShift menu" never fired a single time, silently, no
    // exception anywhere (same numeric VK_ESCAPE, 0x1B, that KeybindManager already uses correctly
    // for its own bind-capture-cancel feature, via a path that calls Win32.WasVkPressed(0x1B)
    // directly and never goes through this switch at all - that's why THAT Escape feature works).
    // Same failure mode was reachable for anyone who rebinds MenuKey itself to an arrow key, Enter,
    // Space, Backspace, or a mouse button - none of those were mapped either, which would have
    // silently bricked the ONE hotkey that opens the whole menu with no error to explain why.
    // Expanded to cover the realistic range of keys someone would plausibly pick for a hotkey,
    // not just what the mod's own hardcoded shortcuts (RightShift/F6/']'/Escape) happened to need.
    private static int ToVirtualKey(KeyCode keyCode)
    {
        if (keyCode >= KeyCode.A && keyCode <= KeyCode.Z)
            return 0x41 + (keyCode - KeyCode.A);
        if (keyCode >= KeyCode.Alpha0 && keyCode <= KeyCode.Alpha9)
            return 0x30 + (keyCode - KeyCode.Alpha0);
        if (keyCode >= KeyCode.F1 && keyCode <= KeyCode.F12)
            return 0x70 + (keyCode - KeyCode.F1);
        if (keyCode >= KeyCode.Keypad0 && keyCode <= KeyCode.Keypad9)
            return 0x60 + (keyCode - KeyCode.Keypad0); // VK_NUMPAD0..9
        if (keyCode >= KeyCode.Mouse0 && keyCode <= KeyCode.Mouse2)
            return keyCode switch { KeyCode.Mouse0 => 0x01, KeyCode.Mouse1 => 0x02, _ => 0x04 }; // LBUTTON/RBUTTON/MBUTTON
        if (keyCode == KeyCode.Mouse3 || keyCode == KeyCode.Mouse4)
            return keyCode == KeyCode.Mouse3 ? 0x05 : 0x06; // XBUTTON1/2 - Win32 has no VK for Mouse5/6, unmapped

        switch (keyCode)
        {
            case KeyCode.RightShift: return 0xA1;
            case KeyCode.LeftShift: return 0xA0;
            case KeyCode.RightControl: return 0xA3;
            case KeyCode.LeftControl: return 0xA2;
            case KeyCode.RightAlt: return 0xA5;
            case KeyCode.LeftAlt: return 0xA4;
            case KeyCode.Insert: return 0x2D;
            case KeyCode.Delete: return 0x2E;
            case KeyCode.Home: return 0x24;
            case KeyCode.End: return 0x23;
            case KeyCode.PageUp: return 0x21;
            case KeyCode.PageDown: return 0x22;
            case KeyCode.Tab: return 0x09;
            case KeyCode.CapsLock: return 0x14;
            case KeyCode.BackQuote: return 0xC0;
            case KeyCode.RightBracket: return 0xDD; // VK_OEM_6 - PanicHack's reactivate key
            case KeyCode.LeftBracket: return 0xDB; // VK_OEM_4
            case KeyCode.Escape: return 0x1B;
            case KeyCode.Space: return 0x20;
            case KeyCode.Return: return 0x0D;
            case KeyCode.KeypadEnter: return 0x0D; // Windows doesn't distinguish main/numpad Enter via GetAsyncKeyState
            case KeyCode.Backspace: return 0x08;
            case KeyCode.UpArrow: return 0x26;
            case KeyCode.DownArrow: return 0x28;
            case KeyCode.LeftArrow: return 0x25;
            case KeyCode.RightArrow: return 0x27;
            case KeyCode.Numlock: return 0x90;
            case KeyCode.ScrollLock: return 0x91;
            case KeyCode.Pause: return 0x13;
            case KeyCode.Print: return 0x2A;
            case KeyCode.Minus: return 0xBD; // VK_OEM_MINUS
            case KeyCode.Equals: return 0xBB; // VK_OEM_PLUS
            case KeyCode.Comma: return 0xBC; // VK_OEM_COMMA
            case KeyCode.Period: return 0xBE; // VK_OEM_PERIOD
            case KeyCode.Slash: return 0xBF; // VK_OEM_2
            case KeyCode.Backslash: return 0xDC; // VK_OEM_5
            case KeyCode.Semicolon: return 0xBA; // VK_OEM_1
            case KeyCode.Quote: return 0xDE; // VK_OEM_7
            case KeyCode.KeypadPlus: return 0x6B;
            case KeyCode.KeypadMinus: return 0x6D;
            case KeyCode.KeypadMultiply: return 0x6A;
            case KeyCode.KeypadDivide: return 0x6F;
            case KeyCode.KeypadPeriod: return 0x6E;
            default: return 0;
        }
    }
}
