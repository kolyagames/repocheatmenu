// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace RepoCheatMenu;

internal sealed class Keybind
{
    public string Label;
    public ConfigEntry<int> Vk; // raw Win32 virtual-key code, 0 = unbound
    public Action OnPress;

    // What this bind is attached to, so a menu row can find its own bind without a separate
    // hand-maintained label->row table: toggles match by their ConfigEntry, one-shot actions by
    // the delegate itself (a static method group compares equal across separate conversions,
    // so OverlayUI passing e.g. RaidHack.Yeet again matches the one registered here).
    public ConfigEntry<bool> Toggle;
    public Action Action;
}

// Lets any toggle/button in the menu also fire from a key or mouse button, polled straight
// from the OS via Win32 (same approach as the existing menu hotkey, since UnityEngine.Input
// doesn't reliably report presses to our patches in this game). Bind assignment is config-backed
// (BepInEx ConfigEntry<int>), so picked binds persist across restarts like every other setting.
//
// Binds are assigned directly on the row that owns them - middle-click a row in the menu, then
// press the key you want. There used to be a separate "Keybinds" tab listing every bindable
// action by its internal English name; that meant scrolling a second, parallel list of ~50 rows
// and matching them up by name to the actual settings. Removed in favour of binding in place.
internal static class KeybindManager
{
    private const int VkEscape = 0x1B;

    public static readonly List<Keybind> All = new();
    public static Keybind Capturing;

    public static ConfigEntry<int> RegisterToggle(ConfigFile config, string label, ConfigEntry<bool> toggle)
    {
        ConfigEntry<int> vk = config.Bind("Keybinds", label + "Key", 0,
            $"Virtual-key code bound to toggle '{label}' (0 = unbound). Set by middle-clicking its row in the menu.");
        All.Add(new Keybind { Label = label, Vk = vk, Toggle = toggle, OnPress = () => toggle.Value = !toggle.Value });
        return vk;
    }

    public static ConfigEntry<int> RegisterAction(ConfigFile config, string label, Action action, int defaultVk = 0)
    {
        ConfigEntry<int> vk = config.Bind("Keybinds", label + "Key", defaultVk,
            $"Virtual-key code bound to action '{label}' (0 = unbound). Set by middle-clicking its row in the menu.");
        All.Add(new Keybind { Label = label, Vk = vk, Action = action, OnPress = action });
        return vk;
    }

    public static Keybind Find(ConfigEntry<bool> toggle)
    {
        if (toggle == null) return null;
        foreach (Keybind bind in All)
            if (bind.Toggle == toggle) return bind;
        return null;
    }

    public static Keybind Find(Action action)
    {
        if (action == null) return null;
        foreach (Keybind bind in All)
            if (bind.Action != null && bind.Action.Equals(action)) return bind;
        return null;
    }

    // The new menu starts a capture on the middle button going DOWN; the next 30 ms poll then still saw
    // that button held, counted it as a fresh press and bound "Mouse Middle" immediately. A capture only
    // starts listening once every bindable key/button has been released.
    private static bool _captureArmed;

    public static void StartCapture(Keybind bind)
    {
        Capturing = bind;
        _captureArmed = false;
    }

    public static void Apply()
    {
        Win32.PollFrame();

        if (Capturing != null)
        {
            // Escape clears the bind instead of becoming it - that's the only way to unbind now
            // that the separate keybind tab (which had its own "Сброс" button) is gone.
            if (Win32.WasVkPressed(VkEscape))
            {
                Capturing.Vk.Value = 0;
                Capturing = null;
                return;
            }

            if (!_captureArmed)
            {
                if (!Win32.AnyBindableDown()) _captureArmed = true;
                return;
            }

            int pressed = Win32.PollNewlyPressedVk();
            if (pressed != 0)
            {
                Capturing.Vk.Value = pressed;
                Capturing = null;
            }
            return; // don't also fire binds while picking a new one
        }

        if (UiInput.TypingInMenu()) return; // letters typed into a menu field are text, not hotkeys

        foreach (Keybind bind in All)
        {
            if (bind.Vk.Value == 0) continue;
            if (!Win32.WasVkPressed(bind.Vk.Value)) continue;

            try
            {
                bind.OnPress();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Exception running keybind '{bind.Label}': {e}");
            }
        }
    }
}
