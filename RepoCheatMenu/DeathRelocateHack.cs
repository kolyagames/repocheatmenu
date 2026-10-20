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

// Guest side of DeathAutoToTruck / DeathAutoToExtraction, plus the guest "revive self".
//
// PlayerDeathHeadTriggerPatch rewrites PlayerDeathHead.triggeredPosition, but the game only fills and
// uses that field on the master (PlayerDeathHead.Trigger and Update both gate on IsMasterClient), so for
// a guest those two toggles never did anything. A guest can still move its own head: PhysGrabObject.
// Teleport sends SetPositionRPC to the master, which has no guard. We wait out the master's own 0.1 s
// "hold the head at the death spot" window first, otherwise it teleports the head straight back.
//
// Revive: the game revives a head that sits in an extraction point when that point finishes
// (ExtractionPoint -> playerDeathHead.Revive), and in the truck once every point is done
// (PlayerDeathHead.Update, master side). That is the only real revive a guest can reach.
internal static class DeathRelocateHack
{
    // Counted from OUR isDisabled (head exists). The host runs the same 0.5 s death timer only after our
    // death RPC reaches it, then holds the head for 0.1 s - so its placement lands ~0.7-0.9 s after ours.
    // Two sends: the second one wins even over a laggy host and is harmless otherwise.
    private static readonly float[] SendAt = { 1f, 2f };

    private static float _disabledSince = -1f;
    private static int _sent;

    public static void Apply()
    {
        PlayerAvatar local = PlayerAvatar.instance;
        if (local == null || !GameReflect.Get<bool>(local, "deadSet") || !GameReflect.Get<bool>(local, "isDisabled"))
        {
            _disabledSince = -1f;
            _sent = 0;
            return;
        }
        if (_disabledSince < 0f) _disabledSince = Time.unscaledTime;

        if (_sent >= SendAt.Length || !GameManager.Multiplayer() || PhotonNetwork.IsMasterClient) return;
        if (!CheatState.DeathAutoToTruck.Value && !CheatState.DeathAutoToExtraction.Value) return;
        if (Time.unscaledTime - _disabledSince < SendAt[_sent]) return;

        Vector3? target = CheatState.DeathAutoToTruck.Value ? TruckPosition() : ExtractionPosition(local);
        if (target == null) return;
        if (MoveHead(local, target.Value)) _sent++;
    }

    // Button: put the head where the game will actually revive it.
    public static void ReviveSelf()
    {
        PlayerAvatar local = PlayerAvatar.instance;
        if (local == null || !GameReflect.Get<bool>(local, "deadSet")) return;

        if (!GameManager.Multiplayer() || PhotonNetwork.IsMasterClient)
        {
            local.Revive(false);
            return;
        }

        RoundDirector round = RoundDirector.instance;
        bool allDone = round != null && GameReflect.Get<bool>(round, "allExtractionPointsCompleted");
        ExtractionPoint active = round != null ? GameReflect.Get<ExtractionPoint>(round, "extractionPointCurrent") : null;

        if (allDone && TruckPosition() is Vector3 truck)
        {
            if (MoveHead(local, truck))
                DynamicIsland.Notify("Возрождение", "Голова в грузовике - хост оживит через пару секунд", true);
            return;
        }
        if (active != null)
        {
            if (MoveHead(local, InsideArea(active)))
                DynamicIsland.Notify("Возрождение", "Голова в точке выхода - оживёте, когда сдача закончится", true);
            return;
        }
        DynamicIsland.Notify("Возрождение", "Гостю сейчас негде ожить: нет активной точки выхода", false);
    }

    private static Vector3? TruckPosition()
    {
        return TruckSafetySpawnPoint.instance != null ? TruckSafetySpawnPoint.instance.transform.position : null;
    }

    internal static Vector3? ExtractionPosition(PlayerAvatar local)
    {
        RoundDirector round = RoundDirector.instance;
        ExtractionPoint point = round != null ? GameReflect.Get<ExtractionPoint>(round, "extractionPointCurrent") : null;
        if (point == null) point = SemiFunc.ExtractionPointGetNearest(local.transform.position);
        return point != null ? InsideArea(point) : null;
    }

    // The revive needs the head inside the point's room volume (RoomVolumeCheck.inExtractionPoint), so aim
    // at the extraction area itself, like LootMagnet does, not at the point's root transform.
    private static Vector3 InsideArea(ExtractionPoint point)
    {
        Transform area = point.extractionArea != null ? point.extractionArea.transform : point.transform;
        return area.position + Vector3.up * 0.5f;
    }

    private static bool MoveHead(PlayerAvatar local, Vector3 position)
    {
        PlayerDeathHead head = GameReflect.Get<PlayerDeathHead>(local, "playerDeathHead");
        PhysGrabObject body = head != null ? GameReflect.Get<PhysGrabObject>(head, "physGrabObject") : null;
        if (body == null) return false;
        GameReflect.TeleportSafe(body, position, body.transform.rotation, "DeathRelocateHack");
        return true;
    }
}
