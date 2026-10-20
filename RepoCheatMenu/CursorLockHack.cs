// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using UnityEngine;

namespace RepoCheatMenu;

// The cursor could still wander off to a second monitor during NORMAL gameplay in fullscreen,
// not just while our own overlay was open - OverlayUI.Show()/Hide() only ever controls
// Cursor.lockState for the duration our own menu is up, restoring whatever the base game had
// before. Whatever state the game leaves Cursor.lockState in during actual gameplay, it should
// never sit at CursorLockMode.None (fully unconfined) unless some menu - ours or the game's own
// (pause/shop/inventory, which legitimately need a free cursor) - is actually open. This closes
// that gap: if nothing is open and the cursor is at None, clamp it to Confined instead - keeps it
// free to move, just bounded to the game window, without recentering/hiding it the way Locked
// would (avoids any side effect on whatever look/aim logic reads cursor state).
internal static class CursorLockHack
{
    public static void Apply()
    {
        if (OverlayUI.IsOpen) return; // already manages Cursor.lockState itself while open
        if (EspOverlay.IsGameMenuOpen()) return; // let the game's own menu keep its free cursor

        if (Cursor.lockState == CursorLockMode.None)
            Cursor.lockState = CursorLockMode.Confined;
    }
}
