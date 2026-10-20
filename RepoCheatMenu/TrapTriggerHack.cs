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

// Trap.cs (decompiled) is the shared base class for most trap types in the level (bottle, chomp
// book, frog, propane tank, toy monkey, music box, grandfather clock, gramophone, TV, radio -
// confirmed by grep: 13 files reference the shared "trapStart" field). Its own TrapStartRPC is
// gated only by SemiFunc.MasterOnlyRPC(_info) - same shallow gate as Revive/Freeze/reroll, already
// exploitable through the same MasterRpcSelfBypassPatch this mod installs. TrapStart() (the
// vanilla PUBLIC wrapper players never call directly - it's the game's own internal trigger, e.g.
// from stepping on a pressure plate) additionally gates on SemiFunc.IsMasterClientOrSingleplayer()
// AND a random SemiFunc.ValuableTrapActivatedDiceRoll() chance before even sending the RPC - both
// are the CALLER's own courtesy checks, not enforced inside TrapStartRPC itself, so calling the RPC
// directly (skipping the wrapper) works for guest or host with no dice roll, matching every other
// "vanilla wrapper adds gates the RPC itself doesn't have" trick already in this mod.
//
// ClownTrap is the one exception - it doesn't read trapStart at all, using its own separate
// TouchNoseRPC/TouchNoseLogic instead (confirmed: no "trapStart" reference anywhere in
// ClownTrap.cs) - handled as its own case below so the classic clown-bomb sequence stays
// triggerable too.
internal static class TrapTriggerHack
{
    private static void FireTrap(Trap trap)
    {
        if (GameManager.Multiplayer())
        {
            PhotonView view = GameReflect.Get<PhotonView>(trap, "photonView");
            view?.RPC("TrapStartRPC", RpcTarget.All);
        }
        else
        {
            GameReflect.Invoke(trap, "TrapStartRPC", default(PhotonMessageInfo));
        }
    }

    private static void FireClown(ClownTrap clown)
    {
        if (GameManager.Multiplayer())
        {
            PhotonView view = GameReflect.Get<PhotonView>(clown, "photonView");
            view?.RPC("TouchNoseRPC", RpcTarget.All);
        }
        else
        {
            GameReflect.Invoke(clown, "TouchNoseRPC", default(PhotonMessageInfo));
        }
    }

    public static void TriggerNearest()
    {
        PlayerController controller = PlayerController.instance;
        if (controller == null) return;
        Vector3 origin = controller.transform.position;

        Component nearest = null;
        bool nearestIsClown = false;
        float nearestDist = float.MaxValue;

        foreach (Trap trap in Object.FindObjectsByType<Trap>(FindObjectsSortMode.None))
        {
            if (trap == null) continue;
            float d = Vector3.Distance(origin, trap.transform.position);
            if (d >= nearestDist) continue;
            nearestDist = d;
            nearest = trap;
            nearestIsClown = trap is ClownTrap;
        }

        if (nearest == null)
        {
            Plugin.Log.LogInfo("Trigger Nearest Trap had no effect - no traps found in the level.");
            return;
        }

        if (nearestIsClown) FireClown((ClownTrap)nearest);
        else FireTrap((Trap)nearest);

        Plugin.Log.LogInfo($"Triggered nearest trap ({nearest.GetType().Name}) at {nearestDist:0.#}m.");
    }

    public static void TriggerAllInLevel()
    {
        int count = 0;
        foreach (Trap trap in Object.FindObjectsByType<Trap>(FindObjectsSortMode.None))
        {
            if (trap == null) continue;
            if (trap is ClownTrap clown) FireClown(clown);
            else FireTrap(trap);
            count++;
        }
        Plugin.Log.LogInfo($"Triggered {count} trap(s) across the level.");
    }
}
