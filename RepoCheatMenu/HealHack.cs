namespace RepoCheatMenu;

// PlayerHealth.Heal() (confirmed in the decompiled source) only checks
// "GameManager.Multiplayer() && !photonView.IsMine" - and you always own your own avatar's
// PhotonView regardless of host/guest status, so this works for guest or host. It also clamps
// to maxHealth itself, so any amount at or above your max heals you to full in one call.
internal static class HealHack
{
    public static void HealFull()
    {
        PlayerHealth health = PlayerAvatar.instance != null ? PlayerAvatar.instance.playerHealth : null;
        health?.Heal(9999, effect: true);
    }

    private const float SpamInterval = 1f;
    private static float _nextSpam;

    public static void ApplyHealSpam()
    {
        if (!CheatState.HealSpam.Value) return;
        if (UnityEngine.Time.unscaledTime < _nextSpam) return;
        _nextSpam = UnityEngine.Time.unscaledTime + SpamInterval;

        PlayerAvatar target = PlayerTargets.Picked();
        PlayerHealth health = target != null ? target.playerHealth : null;
        if (health == null) return;

        // photonView went from a public to a non-public field in a later game update (confirmed
        // via a FieldAccessException flood in the log after that update) - read it through
        // GameReflect like everywhere else in the codebase instead of assuming accessibility.
        Photon.Pun.PhotonView photonView = GameReflect.Get<Photon.Pun.PhotonView>(health, "photonView");
        if (photonView == null) return;

        if (GameManager.Multiplayer())
            photonView.RPC("HealOtherRPC", Photon.Pun.RpcTarget.All, 25, true);
        else
            GameReflect.Invoke(health, "HealOtherRPC", 25, true);
    }
}
