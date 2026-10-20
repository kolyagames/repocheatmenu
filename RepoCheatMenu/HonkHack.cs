// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using Photon.Pun;
using UnityEngine;

namespace RepoCheatMenu;

// ItemVehicle.HonkRPC(int _seatIndex) - [PunRPC] with no guard of any kind (neither a sender check
// nor a context check), so a guest can address it at any vehicle. Every receiver runs PlayHonk(),
// which plays the horn at the vehicle's position; on the host it ALSO calls EnemyDirector.
// SetInvestigate(vehicle position, 35f) - i.e. a honk pulls monsters to the vehicle, no matter who
// pressed it or where that player is standing. The game's own caller (line ~3262) honks only for
// the driver's local seat; the RPC itself accepts any seat index (an out-of-range one just plays at
// pitch 1), so we send a random valid seat to vary the pitch between honks.
//
// Two uses: a harmless prank/signal (button), and a lure (storm) that keeps dragging the level's
// monsters to wherever the vehicles are parked - far from you if you leave them there.
internal static class HonkHack
{
    private static float _nextStorm;

    private static void Honk(ItemVehicle vehicle)
    {
        if (vehicle == null) return;
        int seat = 0;
        if (vehicle.seats != null && vehicle.seats.Length > 0)
            seat = Random.Range(0, vehicle.seats.Length);

        PhotonView view = GameReflect.Get<PhotonView>(vehicle, "photonView");
        if (GameManager.Multiplayer()) view?.RPC("HonkRPC", RpcTarget.All, seat);
        else GameReflect.Invoke(vehicle, "HonkRPC", seat);
    }

    public static void HonkAll()
    {
        int n = 0;
        foreach (ItemVehicle vehicle in Object.FindObjectsByType<ItemVehicle>(FindObjectsSortMode.None))
        {
            Honk(vehicle);
            n++;
        }
        Plugin.Log.LogInfo(n == 0 ? "Honk: no vehicle in the level." : $"Honked {n} vehicle(s).");
    }

    public static void HonkNearest()
    {
        PlayerController controller = PlayerController.instance;
        if (controller == null) return;

        ItemVehicle nearest = null;
        float best = float.MaxValue;
        foreach (ItemVehicle vehicle in Object.FindObjectsByType<ItemVehicle>(FindObjectsSortMode.None))
        {
            if (vehicle == null) continue;
            float d = (vehicle.transform.position - controller.transform.position).sqrMagnitude;
            if (d >= best) continue;
            best = d;
            nearest = vehicle;
        }

        if (nearest == null)
        {
            Plugin.Log.LogInfo("Honk: no vehicle in the level.");
            return;
        }
        Honk(nearest);
    }

    public static void ApplyStorm()
    {
        if (!CheatState.HonkStorm.Value) return;
        if (Time.unscaledTime < _nextStorm) return;
        _nextStorm = Time.unscaledTime + Mathf.Max(0.2f, CheatState.HonkStormInterval.Value);
        foreach (ItemVehicle vehicle in Object.FindObjectsByType<ItemVehicle>(FindObjectsSortMode.None))
            Honk(vehicle);
    }
}
