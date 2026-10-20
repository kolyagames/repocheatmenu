// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using UnityEngine;

namespace RepoCheatMenu;

internal static class ChargingStationHack
{
    private const float HealInterval = 1f;
    private const float HealRange = 3f;
    private static float _nextHeal;

    public static void ApplyHeal()
    {
        if (!CheatState.ChargingStationHeal.Value) return;
        if (Time.unscaledTime < _nextHeal) return;

        ChargingStation station = ChargingStation.instance;
        PlayerAvatar local = PlayerAvatar.instance;
        if (station == null || local == null) return;
        if (Vector3.Distance(local.transform.position, station.transform.position) > HealRange) return;

        _nextHeal = Time.unscaledTime + HealInterval;
        local.playerHealth?.Heal(5, true);
    }
}
