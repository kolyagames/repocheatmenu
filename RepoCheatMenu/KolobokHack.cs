using UnityEngine;

namespace RepoCheatMenu;

// Automates the game's own built-in dead-spectator "charge jump" hop (hold Jump to charge,
// release to launch forward in your look direction, confirmed in the decompiled
// PlayerDeathHead.cs - the death head is a real PhysGrabObject with legs that hops around) instead
// of requiring you to manually hold/release the key yourself. SpectatedJumpLocalInput() (private,
// reflected below) routes its RPC to RpcTarget.MasterClient gated by SemiFunc.OwnerOnlyRPC - not
// master-gated, just owner-gated, and you always own your own death head - so this works for
// guest or host same as any of your own input always does.
//
// This is still a series of discrete hops, not smooth rolling: the game's own charge mechanic has
// a fixed ~2s cooldown between jumps (PlayerDeathHead.spectatedJumpCooldown) that nothing here
// bypasses - the actual force application only runs in PlayerDeathHead.FixedUpdate when
// SemiFunc.IsMasterClient() is true, so a guest has no way to shorten that cooldown, only to stop
// having to manually hold/release the key for each hop. "Kolobok" here means "keeps hopping
// itself in whatever direction you're looking", not continuous rolling motion.
internal static class KolobokHack
{
    private const float HoldDuration = 0.6f;
    private const float CycleCooldown = 2.5f; // clears the game's own ~2s cooldown safely before charging again

    private static bool _holding;
    private static float _phaseDeadline;
    private static bool _wasActive;

    public static void Apply()
    {
        PlayerDeathHead head = GetHead();
        bool active = CheatState.KolobokMode.Value && head != null && IsSpectatingHead();

        if (!active)
        {
            if (_wasActive && head != null)
                GameReflect.Invoke(head, "SpectatedJumpLocalInput", false);
            _wasActive = false;
            _holding = false;
            return;
        }

        if (!_wasActive)
        {
            _holding = true;
            _phaseDeadline = Time.unscaledTime + HoldDuration;
            GameReflect.Invoke(head, "SpectatedJumpLocalInput", true);
        }
        _wasActive = true;

        if (Time.unscaledTime < _phaseDeadline) return;

        _holding = !_holding;
        _phaseDeadline = Time.unscaledTime + (_holding ? HoldDuration : CycleCooldown);
        GameReflect.Invoke(head, "SpectatedJumpLocalInput", _holding);
    }

    private static PlayerDeathHead GetHead()
    {
        PlayerController controller = PlayerController.instance;
        if (controller == null || controller.playerAvatarScript == null) return null;
        // playerDeathHead is internal (confirmed in the decompiled source) - our mod is a
        // separate assembly, so internal is just as inaccessible as private here and needs
        // reflection like everything else GameReflect touches. Direct access threw
        // FieldAccessException every tick, spamming the log and never reaching the rest of
        // this method's logic at all.
        return GameReflect.Get<PlayerDeathHead>(controller.playerAvatarScript, "playerDeathHead");
    }

    private static bool IsSpectatingHead()
    {
        return SpectateCamera.instance != null && SpectateCamera.instance.CheckState(SpectateCamera.State.Head);
    }
}
