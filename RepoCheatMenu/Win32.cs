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

    public static bool IsKeyDown(KeyCode keyCode)
    {
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
    public static void PollFrame()
    {
        for (int vk = 1; vk <= VkRangeMax; vk++)
        {
            PrevDown[vk] = Down[vk];
            Down[vk] = (GetAsyncKeyState(vk) & 0x8000) != 0;
        }
    }

    public static bool WasVkPressed(int vk) => vk > 0 && vk <= VkRangeMax && Down[vk] && !PrevDown[vk];

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

    // Null = Spotify isn't running (window not found). "Spotify" (untranslated - the client
    // itself never localizes its own idle title) = running but paused/nothing loaded.
    public static string GetSpotifyWindowTitle()
    {
        IntPtr hwnd = FindWindow("SpotifyMainWindow", null);
        if (hwnd == IntPtr.Zero) return null;

        var sb = new StringBuilder(256);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
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

    private static int ToVirtualKey(KeyCode keyCode)
    {
        if (keyCode >= KeyCode.A && keyCode <= KeyCode.Z)
            return 0x41 + (keyCode - KeyCode.A);
        if (keyCode >= KeyCode.Alpha0 && keyCode <= KeyCode.Alpha9)
            return 0x30 + (keyCode - KeyCode.Alpha0);
        if (keyCode >= KeyCode.F1 && keyCode <= KeyCode.F12)
            return 0x70 + (keyCode - KeyCode.F1);

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
            default: return 0;
        }
    }
}
