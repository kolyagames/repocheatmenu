using System.Collections.Generic;
using UnityEngine;

namespace RepoCheatMenu;

// Voice filter - mute remote players by alive/dead state.
//
// HISTORY (do not re-add the removed part without a live test):
// This session added a PlayerVoiceChat.ToggleMixer(true, false) call on our OWN voice chat,
// on the theory (cross-checked against RepoHax's Entities tab) that opening our own lobby/dead
// mixer route is what actually gates hearing dead players. That call broke voice chat outright -
// three separate real-world reports, both in the lobby and in-level, hearing AND transmitting -
// and narrowing its conditions three times never fixed it. Disabling the whole file restored
// voice, confirmed by the user.
//
// The per-source mute loop below predates this session and voice worked fine with it, so the
// ToggleMixer call is the culprit and is now gone for good. Do not reintroduce it without being
// able to test live: it mutates a shared AudioMixer route, and getting it wrong takes out your
// own mic as well as your ears.
//
// The other half - actually making dead players audible while alive - is handled separately by
// DeadVoiceHearPatch in Patches.cs, which only touches outputAudioMixerGroup directly (no
// ToggleMixer call, no position write) - confirmed safe by reading ToggleMixer's own source:
// its "_lobby: true" branch literally teleports the voice chat object to (1000,1000,1000), which
// is what broke things above.
internal static class VoiceFilterHack
{
    // Everything muted while a filter was active, so switching back to "All" can put it back
    // instead of leaving someone silenced for the rest of the run.
    private static readonly List<PlayerVoiceChat> _touched = new();

    public static void Apply()
    {
        string filter = CheatState.VoiceFilter.Value;

        // "All" is the default and must be a genuine no-op - not "write unmuted to everything
        // every tick". Anything previously muted gets restored once, then this touches nothing
        // at all, so voice behaves exactly like vanilla unless a filter is deliberately picked.
        if (filter != "Living" && filter != "Dead")
        {
            if (_touched.Count > 0) RestoreTouched();
            return;
        }

        RunManager runManager = RunManager.instance;
        if (runManager == null) return;

        List<PlayerVoiceChat> voiceChats = GameReflect.Get<List<PlayerVoiceChat>>(runManager, "voiceChats");
        if (voiceChats == null) return;

        foreach (PlayerVoiceChat voiceChat in voiceChats)
        {
            if (voiceChat == null) continue;

            // Never touch our own voice chat - muting our own AudioSource/Speaker is a way to
            // cut off transmitting, not just hearing.
            if (voiceChat == PlayerVoiceChat.instance) continue;

            PlayerAvatar avatar = GameReflect.Get<PlayerAvatar>(voiceChat, "playerAvatar");
            if (avatar == null) continue;

            bool dead = GameReflect.Get<bool>(avatar, "deadSet");
            bool mute = filter == "Living" ? dead : !dead;

            SetMuted(voiceChat, mute);
            if (mute && !_touched.Contains(voiceChat)) _touched.Add(voiceChat);
        }
    }

    private static void RestoreTouched()
    {
        foreach (PlayerVoiceChat voiceChat in _touched)
        {
            if (voiceChat == null) continue;
            SetMuted(voiceChat, false);
        }
        _touched.Clear();
    }

    private static void SetMuted(PlayerVoiceChat voiceChat, bool mute)
    {
        AudioSource mic = GameReflect.Get<AudioSource>(voiceChat, "audioSource");
        if (mic != null) mic.mute = mute;

        Behaviour speaker = GameReflect.Get<Behaviour>(voiceChat, "speaker");
        if (speaker != null) speaker.enabled = !mute;

        if (voiceChat.ttsAudioSource != null) voiceChat.ttsAudioSource.mute = mute;
    }

    // "Chipmunk Voices" - PlayerVoiceChat.OverridePitch(multiplier, timeIn, timeOut, overrideTimer,
    // oscillation, oscillationSpeed) is confirmed public in the decompiled source, and confirmed
    // it's a PLAYBACK-side effect (audioSource.pitch), not a transmission one - the local player's
    // own PlayerVoiceChat.instance has audioSource.volume forced to 0 in Awake() (you don't play
    // your own voice back to yourself), so calling this on your OWN instance would be silent and
    // pointless. Calling it on someone ELSE's instance changes how YOU hear THEM - pure local
    // perception, touches no network state and can't be heard by anyone but you, unlike
    // GlitchyVoice (which chops the OUTGOING Recorder.TransmitEnabled - what THEY hear from you).
    // Same RunManager.instance.voiceChats enumeration as the mute loop above, not
    // PlayerTargets.AliveOthers()+reflection - this file already proved that's the reliable list of
    // every PlayerVoiceChat instance including remote ones. overrideTimer kept a little longer than
    // one RunSafe tick (~0.03s) so it never fully decays and re-triggers the timeIn ease between
    // calls - stays a smooth held pitch, not a shift-decay-shift stutter.
    public static void ApplyChipmunk()
    {
        if (!CheatState.ChipmunkVoices.Value) return;

        RunManager runManager = RunManager.instance;
        if (runManager == null) return;

        List<PlayerVoiceChat> voiceChats = GameReflect.Get<List<PlayerVoiceChat>>(runManager, "voiceChats");
        if (voiceChats == null) return;

        float pitch = CheatState.ChipmunkPitch.Value;
        foreach (PlayerVoiceChat voiceChat in voiceChats)
        {
            if (voiceChat == null || voiceChat == PlayerVoiceChat.instance) continue;
            voiceChat.OverridePitch(pitch, 0.15f, 0.15f, 0.2f);
        }
    }
}
