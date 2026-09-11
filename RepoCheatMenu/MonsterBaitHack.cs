using Photon.Pun;
using UnityEngine;

namespace RepoCheatMenu;

internal static class MonsterBaitHack
{
    private const float BaitInterval = 6f;
    private static float _nextBait;

    public static void Apply()
    {
        if (!CheatState.MonsterBait.Value) return;
        if (Time.unscaledTime < _nextBait) return;
        _nextBait = Time.unscaledTime + BaitInterval;

        PlayerAvatar target = PlayerTargets.Picked();
        if (target == null) return;

        MusicalValuableLogic nearest = null;
        float nearestDist = float.MaxValue;
        foreach (MusicalValuableLogic candidate in Object.FindObjectsByType<MusicalValuableLogic>(FindObjectsSortMode.None))
        {
            if (candidate == null) continue;
            float d = Vector3.Distance(candidate.transform.position, target.transform.position);
            if (d < nearestDist) { nearestDist = d; nearest = candidate; }
        }
        if (nearest == null) return;

        PhysGrabObject body = nearest.GetComponent<PhysGrabObject>();
        PhotonView view = nearest.GetComponent<PhotonView>();
        if (body == null || view == null) return;

        body.Teleport(target.transform.position, nearest.transform.rotation);

        if (GameManager.Multiplayer())
            view.RPC("MusicKeyPressedRPC", RpcTarget.All, 0, -1);
        else
            GameReflect.Invoke(nearest, "MusicKeyPressedRPC", 0, -1);
    }
}
