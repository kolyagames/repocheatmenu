// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using UnityEngine;

namespace RepoCheatMenu;

// Which window the RightShift menu is: its old mode (the original uGUI menu, OverlayUI) or its new mode
// (the Rockstar-style CustomGui window, CustomOverlayUI). F6 always opens the new-mode window. The choice
// lives in CheatState.MenuStyle and is changed from the HUD tab of either window; switching it while one
// of them is open swaps to the other one right away.
internal static class MenuRouter
{
    public const string OldMode = "Старый";
    public const string NewMode = "Новый";
    public const string DropdownMode = "Выпадающее";
    public static readonly string[] Modes = { OldMode, NewMode, DropdownMode };

    // New mode = the CustomGui window, either as the Modern layout or as Rockstar's Dropdown layout.
    public static bool UseNewMode => CheatState.MenuStyle.Value != OldMode;

    // RightShift
    public static void Toggle()
    {
        if (UseNewMode)
        {
            if (OverlayUI.IsOpen) OverlayUI.Close();
            CustomOverlayUI.Toggle();
        }
        else
        {
            if (CustomOverlayUI.IsOpen) CustomOverlayUI.Hide();
            OverlayUI.Toggle();
        }
    }

    // Escape
    public static void CloseAll()
    {
        OverlayUI.Close();
        if (CustomOverlayUI.IsOpen) CustomOverlayUI.Hide();
    }

    // Wired to CheatState.MenuStyle.SettingChanged.
    public static void OnStyleChanged()
    {
        if (UseNewMode)
        {
            if (OverlayUI.IsOpen)
            {
                OverlayUI.Close();
                CustomOverlayUI.Show();
            }
            else if (CustomOverlayUI.IsOpen)
            {
                // Modern <-> Dropdown: reopen so the layout is chosen again.
                CustomOverlayUI.Hide();
                CustomOverlayUI.Show();
            }
        }
        else if (CustomOverlayUI.IsOpen)
        {
            CustomOverlayUI.Hide();
            OverlayUI.Open();
        }
    }
}
