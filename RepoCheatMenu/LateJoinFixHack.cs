using UnityEngine;

namespace RepoCheatMenu;

// Mitigations for the third-party LateJoin mod's own known sync gaps (Rebateman/LateJoin issue #2:
// "objects also do not get synced with other players sometimes... missing energy crystal sucker,
// or your ambience is set to the wrong one"). That mod replays level state to a joiner purely via
// Photon's buffered-RPC replay, which is order-sensitive and sometimes drops objects - the real
// fix lives in that mod's own sync logic, not here. These two buttons only cover what's actually
// fixable from our side without master authority:
//
//  - Rescan: forces the same lighting/particle-distance/audio-distance refresh the game's own
//    SpectateCamera uses internally after abrupt teleports (LightManager.UpdateInstant() +
//    LevelGenerator.RestartParticleDistances() + AudioManager.RestartAudioLoopDistances(), all
//    confirmed public in the decompiled source) - helps when geometry/audio DID load but the
//    lighting/ambience/culling-distance systems just never got (re-)applied for a client that
//    joined mid-sequence. Doesn't create anything that's actually missing.
//  - Teleport To Nearest Player: pure local move of your own transform/Rigidbody (no network
//    authority needed, same as Fly/Noclip). Doesn't fix missing collision - if a room's collider
//    genuinely never got instantiated on your client (the "walk = fall through, thrown by master
//    = doesn't fall through" symptom - your client has no local collider there at all, but the
//    master's own authoritative copy of that room does), no client-side trick can conjure that
//    collider from nothing. This is just an escape hatch so falling into a phantom room doesn't
//    strand you in the void indefinitely - moves you next to whichever other player is already
//    standing on real geometry.
internal static class LateJoinFixHack
{
    public static void RescanNow()
    {
        if (LightManager.instance != null)
            LightManager.instance.UpdateInstant();

        if (LevelGenerator.Instance != null)
            LevelGenerator.Instance.RestartParticleDistances();

        if (AudioManager.instance != null)
            AudioManager.instance.RestartAudioLoopDistances();

        if (CameraGlitch.Instance != null)
            CameraGlitch.Instance.PlayShort();

        Plugin.Log.LogInfo("LateJoinFix: rescanned lighting/particles/audio.");
    }

    public static void TeleportToNearestPlayer()
    {
        PlayerAvatar local = PlayerAvatar.instance;
        PlayerController controller = PlayerController.instance;
        if (local == null || controller == null || controller.rb == null) return;

        GameDirector director = GameDirector.instance;
        if (director == null) return;

        PlayerAvatar nearest = null;
        float nearestDist = float.MaxValue;
        foreach (PlayerAvatar p in director.PlayerList)
        {
            // isDisabled is internal (confirmed in the decompiled source) - our mod is a separate
            // assembly, so this needs GameReflect same as everything else non-public. Direct
            // access here was throwing FieldAccessException every call, which is exactly why the
            // button appeared to do nothing.
            if (p == null || p == local || GameReflect.Get<bool>(p, "isDisabled")) continue;
            float d = Vector3.Distance(local.transform.position, p.transform.position);
            if (d < nearestDist) { nearestDist = d; nearest = p; }
        }

        if (nearest == null)
        {
            Plugin.Log.LogInfo("LateJoinFix: Teleport To Nearest Player had no effect - no other players found.");
            return;
        }

        Vector3 pos = nearest.transform.position + Vector3.up * 1f;
        controller.rb.velocity = Vector3.zero;
        controller.rb.position = pos;
        controller.transform.position = pos;
    }
}
