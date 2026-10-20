// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System;
using System.Reflection;

namespace RepoCheatMenu;

// Mirrors RepoHax's own IsInGame() check (confirmed from game_hooks.cpp): without it, ESP
// reads from PlayerList/enemiesSpawned/valuableList as soon as those are populated, which can
// happen while the level's own loading/intro screen is still covering the view - so markers
// appear to "see through" what is actually just a loading screen, not real geometry.
internal static class GameState
{
    private static int? _mainStateValue;

    public static bool IsInGame()
    {
        RunManager runManager = RunManager.instance;
        GameDirector gameDirector = GameDirector.instance;
        if (runManager == null || gameDirector == null || PlayerAvatar.instance == null)
            return false;

        object current = GameReflect.Get<object>(runManager, "levelCurrent");
        object lobby = GameReflect.Get<object>(runManager, "levelLobbyMenu");
        object mainMenu = GameReflect.Get<object>(runManager, "levelMainMenu");
        if (current == null || Equals(current, lobby) || Equals(current, mainMenu))
            return false;

        int state = GameReflect.Get<int>(gameDirector, "currentState");
        int mainState = MainStateValue();
        return mainState == int.MinValue || state == mainState;
    }

    private static int MainStateValue()
    {
        if (_mainStateValue.HasValue) return _mainStateValue.Value;

        Type enumType = typeof(GameDirector).GetNestedType("gameState", BindingFlags.Public | BindingFlags.NonPublic);
        if (enumType != null)
        {
            foreach (string name in Enum.GetNames(enumType))
            {
                if (name == "Main")
                {
                    _mainStateValue = (int)Enum.Parse(enumType, name);
                    return _mainStateValue.Value;
                }
            }
        }

        // Couldn't resolve the enum - fall back to "no extra gating beyond the level check".
        _mainStateValue = int.MinValue;
        return _mainStateValue.Value;
    }
}
