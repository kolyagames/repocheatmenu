// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using UnityEngine;

namespace RepoCheatMenu;

// PlayerHealth.EyeMaterialOverride(EyeOverrideState, float time, int priority) - PUBLIC method,
// the game's own, called exactly like this by ChatManager (Love/Red/Green on certain chat events),
// GumballValuable (Inverted) and EnemyCeilingEye (CeilingEye). We just call it ourselves, which
// makes this the most "legitimate" feature in the whole mod: no RPC by name, no bypass, no spoofed
// broadcast field - the game's OWN sync code (PlayerHealth.Update, edge-triggered
// photonView.RPC("EyeMaterialOverrideRPC", RpcTarget.Others, ...)) does the networking for us.
//
// Which means, unlike CheatState.EyeColor (a fixed palette pushed through our own RCM_-prefixed
// custom-properties channel, so only players who ALSO run this mod ever see it), this one is
// visible to EVERY player in the room, mod or no mod - it's a stock game effect on a stock game
// channel. Five looks available, straight out of the decompiled enum: Red, Green, Love, CeilingEye,
// Inverted.
//
// Needs re-arming on a timer, not a per-change call: EyeMaterialOverride sets a countdown
// (overrideEyeMaterialTimer) rather than a latch, and PlayerHealth.Update clears the effect when it
// runs out. Re-arming at half the requested duration keeps it continuous. Nothing needs to be sent
// on the way out - letting the timer lapse makes the game itself broadcast the deactivation, AND it
// resets overrideEyePriority to -999 (confirmed in that same Update), so our high priority can't
// linger and suppress the game's own legitimate eye effects once this is switched off.
internal static class EyeOverrideHack
{
    public static readonly string[] StateNames = { "Red", "Green", "Love", "CeilingEye", "Inverted" };

    private const float HoldTime = 0.5f;
    private const float RearmInterval = 0.25f;
    // Above the 0/1 the game's own callers use, so a chat reaction or a gumball can't steal the
    // look mid-toggle. Safe to sit this high because the priority self-resets on expiry.
    private const int Priority = 2;

    private static float _nextRearm;

    public static void Apply()
    {
        if (!CheatState.EyeOverride.Value) return;
        if (Time.unscaledTime < _nextRearm) return;
        _nextRearm = Time.unscaledTime + RearmInterval;

        PlayerAvatar avatar = PlayerAvatar.instance;
        PlayerHealth health = avatar != null ? avatar.playerHealth : null;
        if (health == null) return;

        health.EyeMaterialOverride(StateFromName(CheatState.EyeOverrideState.Value), HoldTime, Priority);
    }

    private static PlayerHealth.EyeOverrideState StateFromName(string name) => name switch
    {
        "Green" => PlayerHealth.EyeOverrideState.Green,
        "Love" => PlayerHealth.EyeOverrideState.Love,
        "CeilingEye" => PlayerHealth.EyeOverrideState.CeilingEye,
        "Inverted" => PlayerHealth.EyeOverrideState.Inverted,
        _ => PlayerHealth.EyeOverrideState.Red,
    };
}
