using UnityEngine;

namespace RepoCheatMenu;

// PlayerHealth.Hurt() only checks "you own your own avatar", so Kill Self always works. HurtOther
// -> HurtOtherRPC needs SemiFunc.MasterAndOwnerOnlyRPC(_info, photonView) AND photonView.IsMine to
// both pass, and photonView.IsMine is only ever true on the target's own machine - patching the
// sender check (MasterRpcSelfBypass, or a matching one for MasterAndOwnerOnlyRPC) wouldn't change
// that second condition, so Kill Other is a genuine host/singleplayer-only wall, same as
// KnockdownBanishKill.
internal static class InstantKillHack
{
    public static void KillSelf()
    {
        PlayerHealth health = PlayerAvatar.instance != null ? PlayerAvatar.instance.playerHealth : null;
        health?.Hurt(9999, false, -1, false);
    }

    public static void KillOther()
    {
        PlayerAvatar target = PlayerTargets.Picked();
        PlayerHealth health = target != null ? target.playerHealth : null;
        health?.HurtOther(9999, Vector3.zero, false, -1, false);
    }
}
