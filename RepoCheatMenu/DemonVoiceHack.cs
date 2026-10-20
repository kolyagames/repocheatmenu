// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using UnityEngine;

namespace RepoCheatMenu;

// The one voice trick in this mod that other people actually HEAR. CheatState.ChipmunkVoices next
// door only re-pitches THEIR voices in YOUR ears (PlayerVoiceChat.OverridePitch called locally on
// their instances - purely playback-side, nothing leaves your machine); this one goes the other
// way, and it's the game's own networked path for exactly that.
//
// ItemGunTranq.MakeHitPlayersHigh() is a PUBLIC method (confirmed in the decompiled source) that
// the tranq gun fires at whoever it hits: it dilates PlayerAvatar.instance's pupils and RPCs
// "SlowDownVoiceRPC" to RpcTarget.Others carrying that same avatar's ViewID, and every receiver
// then runs voiceChat.OverridePitch(0.65f, 1f, 1f, 3f, 0.1f, 20f) on their copy of you - your voice
// drops to 0.65x with a slow wobble on their end, for three seconds. The receive-side guard is
// SemiFunc.OwnerOnlyRPC(_info, playerAvatar.photonView) against the avatar named in the payload,
// and the method always names PlayerAvatar.instance - i.e. us, whose avatar we own - so it passes
// honestly, guest or host, no bypass.
//
// Two real constraints, both inherent to riding the game's own path:
//   - It needs an actual ItemGunTranq somewhere in the level to send through (the RPC is declared
//     on that component, so PUN needs its view to resolve). Nobody has to hold or fire it - it just
//     has to exist. If the level has none, spawn one from the World tab's item spawner.
//   - The pitch override is a 3-second countdown on the receiving end (overridePitchTimer), not a
//     latch, so it's re-sent a little before that lapses to stay continuous.
//
// Safety: OverridePitch only touches audioSource.pitch behind its own decaying timer - it never
// goes near ToggleMixer or any AudioMixer routing, which is what VoiceFilterHack.cs's own warning
// is about. It also runs on OTHER machines, not ours.
internal static class DemonVoiceHack
{
    private const float ResendInterval = 2.5f;
    private const float RescanInterval = 3f;

    private static float _nextSend;
    private static float _nextScan;
    private static ItemGunTranq _gun;

    public static bool HasGun => _gun != null;

    public static void Apply()
    {
        if (!CheatState.DemonVoice.Value) return;
        if (!GameManager.Multiplayer()) return; // nobody else's ears to reach in singleplayer

        if (_gun == null && Time.unscaledTime >= _nextScan)
        {
            _nextScan = Time.unscaledTime + RescanInterval;
            _gun = Object.FindFirstObjectByType<ItemGunTranq>();
        }
        if (_gun == null) return;

        if (Time.unscaledTime < _nextSend) return;
        _nextSend = Time.unscaledTime + ResendInterval;

        _gun.MakeHitPlayersHigh();
    }
}
