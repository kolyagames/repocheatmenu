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

        RoundDirector director = RoundDirector.instance;
        if (director == null) return;
        if (GameReflect.Get<bool>(director, "extractionPointActive")) return; // one's already active/running

        List<GameObject> points = GameReflect.Get<List<GameObject>>(director, "extractionPointList");
        if (points == null) return;

        foreach (GameObject go in points)
        {
            if (go == null) continue;
            PhotonView view = go.GetComponent<PhotonView>();
            if (view == null) continue;

            director.RequestExtractionPointActivation(view.ViewID);
            return; // only need to kick off one - the game handles the rest of the sequence itself
        }
    }
}
