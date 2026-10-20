// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace RepoCheatMenu;

// Auto-activates the extraction sequence the instant none is active, skipping the need to walk
// up and physically press the button. RoundDirector.RequestExtractionPointActivation (confirmed
// public in the decompiled source) routes the request to RpcTarget.MasterClient, and the
// master-side handler (RequestExtractionPointActivationRPC) only checks
// SemiFunc.IsMasterClientOrSingleplayer() on ITS OWN machine when it receives the RPC - not who
// sent it - so it activates for real regardless of who asked, same "ask master nicely, master
// does it unconditionally" pattern as SetPositionRPC. Works for guest or host.
internal static class ExtractionActivatorHack
{
    private const float TickInterval = 0.5f;
    private static float _lastTry;

    public static void Apply()
    {
        if (!CheatState.AutoActivateExtraction.Value) return;
        if (Time.unscaledTime - _lastTry < TickInterval) return;
        _lastTry = Time.unscaledTime;

        PlayerAvatar local = PlayerAvatar.instance;
        if (local != null) RequestNearestIdle(local.transform.position);
    }

    // Only an Idle point. Requesting a finished one (the old "first point in the list") made the host set
    // extractionPointActive and lock every other point (RoundDirector.ExtractionPointActivateRPC ->
    // ExtractionPointsLock) while ButtonPress did nothing on it: the whole room's round got stuck.
    internal static void RequestNearestIdle(Vector3 from)
    {
        RoundDirector director = RoundDirector.instance;
        if (director == null) return;
        if (GameReflect.Get<bool>(director, "extractionPointActive")) return; // one's already active/running

        List<GameObject> points = GameReflect.Get<List<GameObject>>(director, "extractionPointList");
        if (points == null || points.Count == 0) return;

        ExtractionPoint idle = SemiFunc.ExtractionPointGetNearestNotActivated(from);
        PhotonView view = idle != null ? idle.GetComponent<PhotonView>() : null;
        if (view != null) director.RequestExtractionPointActivation(view.ViewID);
    }
}
