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
