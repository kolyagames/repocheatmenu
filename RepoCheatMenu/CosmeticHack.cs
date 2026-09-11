using System.Collections.Generic;
using System.Linq;
using ExitGames.Client.Photon;
using Photon.Pun;
using UnityEngine;

namespace RepoCheatMenu;

// Force Wings: shows the wings cosmetic regardless of actual TumbleWings upgrade level, purely
// visual - no lift/thrust force, that stays gated to the real upgrade level inside
// ItemUpgradePlayerTumbleWingsLogic.FixedUpdate (master-gated, untouched here) so this can't be
// used to actually fly.
//
// PlayerAvatar.UpgradeTumbleWingsVisualsActive(bool, bool) (confirmed public in the decompiled
// source) RPCs to UpgradeTumbleWingsVisualsActiveRPC, gated SemiFunc.MasterAndOwnerOnlyRPC - you
// always own your own PlayerAvatar, so calling this yourself passes that gate regardless of host
// status, and it broadcasts RpcTarget.All. Its second bool argument IS the pink/original choice
// (confirmed reading the RPC handler's own body - it calls WingsSetPinkColors()/
// WingsSetOriginalColors() identically on every receiving client based on that argument), so
// unlike eye color below, wings genuinely sync for real through the game's own channel - no need
// to build anything extra. Confirmed the game has exactly these two presets and nothing else -
// no generic color field/method exists anywhere in ItemUpgradePlayerTumbleWingsLogic.
//
// Eye color has no equivalent in the game at all - no field, no RPC, nothing (confirmed absent
// from PlayerEyes.cs/PlayerAvatar.cs). Built from scratch instead: local material write for your
// own immediate feedback, plus Photon's own per-player custom properties (Player.
// SetCustomProperties - a real Photon SDK sync channel, sent to everyone in the room
// automatically, nothing R.E.P.O.-specific about it) to broadcast the pick and a per-tick scan
// that applies whatever any OTHER player has broadcast to their own eyes - so it only actually
// shows up for teammates who are also running this mod, same idea GameTools uses for the same
// gap. Restricted to a small fixed palette instead of arbitrary hex/rainbow, since there's no
// "real" eye color to imitate the way wings has two real presets - a fixed list at least reads
// as a deliberate set of choices instead of a made-up gradient.
internal static class CosmeticHack
{
    private const string EyeColorPropertyKey = "RCM_EyeColor";
    private const string BigPupilsPropertyKey = "RCM_BigPupils";

    private static readonly Dictionary<string, Color> EyePalette = new()
    {
        ["Red"] = new Color(0.9f, 0.1f, 0.1f),
        ["Orange"] = new Color(1f, 0.5f, 0.05f),
        ["Yellow"] = new Color(0.95f, 0.9f, 0.1f),
        ["Green"] = new Color(0.15f, 0.85f, 0.2f),
        ["Blue"] = new Color(0.15f, 0.4f, 0.95f),
        ["Purple"] = new Color(0.6f, 0.15f, 0.9f),
        ["White"] = Color.white,
    };

    public static readonly string[] EyeColorNames = { "Red", "Orange", "Yellow", "Green", "Blue", "Purple", "White" };

    public static void Apply()
    {
        PlayerAvatar local = PlayerAvatar.instance;
        if (local == null) return;

        ApplyWings(local);
        ApplyEyeColor(local);
        ApplyOthersEyeColor(local);
        ApplyBigPupils(local);
        ApplyOthersBigPupils(local);
        ApplyMimicHostSkin(local);
        ApplyDinnerBoneSelfPreview(local);
        ApplyFlipSelfPreview(local);
    }

    private static bool _dinnerBoneSelfActive;
    private static Quaternion _dinnerBoneMeshBackupRotation;

    // Lets you preview Dinner Bone on yourself without a second person to check for you - see
    // CheatState.DinnerBoneShowSelf's description for the technique (confirmed from GameTools'
    // own FlipShowToSelf). Only tilts meshParent, the visual mesh root - a completely separate
    // GameObject from PlayerController.transform, so this has zero effect on movement/collision/
    // camera, unlike touching the movement transform the way the network-facing patch briefly
    // does. Requires Third Person so there's actually a camera angle to see your own model from.
    private static void ApplyDinnerBoneSelfPreview(PlayerAvatar local)
    {
        bool want = CheatState.DinnerBone.Value && CheatState.DinnerBoneShowSelf.Value && CheatState.ThirdPerson.Value;

        PlayerAvatarVisuals visuals = local.playerAvatarVisuals;
        Transform mesh = visuals != null && visuals.meshParent != null ? visuals.meshParent.transform : null;

        if (!want)
        {
            if (_dinnerBoneSelfActive && mesh != null)
                mesh.localRotation = _dinnerBoneMeshBackupRotation;
            _dinnerBoneSelfActive = false;
            return;
        }

        if (mesh == null) return;

        if (!_dinnerBoneSelfActive)
        {
            _dinnerBoneMeshBackupRotation = mesh.localRotation;
            _dinnerBoneSelfActive = true;
        }

        mesh.localRotation = _dinnerBoneMeshBackupRotation * Quaternion.Euler(0f, 0f, CheatState.DinnerBoneAngle.Value);
    }

    private static bool _flipSelfActive;
    private static Quaternion _flipMeshBackupRotation;
    private static float _flipSelfAngle;

    // Same self-preview technique as Dinner Bone above, continuous instead of static - see
    // FlipPatch's comment for the axis mapping.
    private static void ApplyFlipSelfPreview(PlayerAvatar local)
    {
        bool want = CheatState.FlipEnabled.Value && CheatState.FlipShowSelf.Value && CheatState.ThirdPerson.Value;

        PlayerAvatarVisuals visuals = local.playerAvatarVisuals;
        Transform mesh = visuals != null && visuals.meshParent != null ? visuals.meshParent.transform : null;

        if (!want)
        {
            if (_flipSelfActive && mesh != null)
                mesh.localRotation = _flipMeshBackupRotation;
            _flipSelfActive = false;
            return;
        }

        if (mesh == null) return;

        if (!_flipSelfActive)
        {
            _flipMeshBackupRotation = mesh.localRotation;
            _flipSelfActive = true;
        }

        _flipSelfAngle = (_flipSelfAngle + CheatState.FlipSpeed.Value * Time.deltaTime) % 360f;
        Vector3 axis = CheatState.FlipAxis.Value switch
        {
            "Pitch" => Vector3.right,
            "Yaw" => Vector3.up,
            _ => Vector3.forward,
        };

        mesh.localRotation = _flipMeshBackupRotation * Quaternion.AngleAxis(_flipSelfAngle, axis);
    }

    private static bool _wingsActive;
    private static bool _wingsPink;

    private static void ApplyWings(PlayerAvatar local)
    {
        bool want = CheatState.ForceWings.Value;
        bool pink = CheatState.WingsPink.Value;
        if (want == _wingsActive && (!want || pink == _wingsPink)) return;

        local.UpgradeTumbleWingsVisualsActive(want, pink);
        _wingsActive = want;
        _wingsPink = pink;
    }

    private static string _lastSyncedEyeColor;

    private static void ApplyEyeColor(PlayerAvatar local)
    {
        if (!CheatState.EyeColor.Value) return;

        string name = CheatState.EyeColorName.Value;
        if (!EyePalette.TryGetValue(name, out Color color)) color = Color.white;

        PlayerAvatarVisuals visuals = local.playerAvatarVisuals;
        PlayerEyes eyes = visuals != null ? visuals.playerEyes : null;
        if (eyes != null)
        {
            SetMaterialColor(eyes.eyeLeft != null ? eyes.eyeLeft.GetComponentInChildren<Renderer>() : null, color, "_Color", "_BaseColor", "_EmissionColor");
            SetMaterialColor(eyes.eyeRight != null ? eyes.eyeRight.GetComponentInChildren<Renderer>() : null, color, "_Color", "_BaseColor", "_EmissionColor");
        }

        if (name == _lastSyncedEyeColor || !PhotonNetwork.InRoom || local.photonView == null || local.photonView.Owner == null) return;
        _lastSyncedEyeColor = name;

        Hashtable props = new() { [EyeColorPropertyKey] = name };
        local.photonView.Owner.SetCustomProperties(props);
    }

    // PlayerHud's target widget uses this as its "identity color" swatch - the closest real,
    // network-synced-per-player visual data this mod tracks (there's no exposed player skin/color
    // field anywhere in the decompiled source, confirmed by grep - a live 3D headshot would need
    // its own tracking camera per target, not attempted here). Only resolves for players running
    // this same mod (RCM_EyeColor is our own custom property, nobody else writes it).
    public static Color? GetSyncedEyeColor(PlayerAvatar player)
    {
        if (player == null || player.photonView == null || player.photonView.Owner == null) return null;
        if (!player.photonView.Owner.CustomProperties.TryGetValue(EyeColorPropertyKey, out object raw)) return null;
        if (raw is not string name || !EyePalette.TryGetValue(name, out Color color)) return null;
        return color;
    }

    private static void ApplyOthersEyeColor(PlayerAvatar local)
    {
        if (!CheatState.EyeColor.Value) return;

        foreach (PlayerAvatar player in PlayerTargets.AliveOthers())
        {
            if (player == null || player.photonView == null || player.photonView.Owner == null) continue;

            if (!player.photonView.Owner.CustomProperties.TryGetValue(EyeColorPropertyKey, out object raw)) continue;
            if (raw is not string name || !EyePalette.TryGetValue(name, out Color color)) continue;

            PlayerAvatarVisuals visuals = player.playerAvatarVisuals;
            PlayerEyes eyes = visuals != null ? visuals.playerEyes : null;
            if (eyes == null) continue;

            SetMaterialColor(eyes.eyeLeft != null ? eyes.eyeLeft.GetComponentInChildren<Renderer>() : null, color, "_Color", "_BaseColor", "_EmissionColor");
            SetMaterialColor(eyes.eyeRight != null ? eyes.eyeRight.GetComponentInChildren<Renderer>() : null, color, "_Color", "_BaseColor", "_EmissionColor");
        }
    }

    // Big Pupils: OverridePupilSize's own priority+timer system (decompiled PlayerAvatar.cs) means
    // there's nothing to explicitly turn off LOCALLY - calling it every tick keeps refreshing
    // overridePupilSizeTimer, and the moment we stop calling it the game's own spring naturally
    // eases the multiplier back to 1 within _time seconds on its own. The BROADCAST side still
    // needs an explicit off, though: Photon custom properties persist in the room until something
    // overwrites them, so a one-directional "only ever send true" sync (which is what Eye Color
    // above actually does - noted here, not fixed there in this pass) leaves everyone else stuck
    // seeing your last-sent color/state forever, even after you turn the toggle off locally. Synced
    // in both directions here so turning Big Pupils off actually clears it for the room too.
    private const float PupilMultiplier = 3f;
    private const int PupilPriority = 4;
    private static bool? _lastSyncedBigPupils;

    private static void ApplyBigPupils(PlayerAvatar local)
    {
        bool want = CheatState.BigPupils.Value;
        if (want) local.OverridePupilSize(PupilMultiplier, PupilPriority, 1f, 1f, 5f, 0.5f, 0.5f);

        if (_lastSyncedBigPupils == want || !PhotonNetwork.InRoom || local.photonView == null || local.photonView.Owner == null) return;
        _lastSyncedBigPupils = want;

        Hashtable props = new() { [BigPupilsPropertyKey] = want };
        local.photonView.Owner.SetCustomProperties(props);
    }

    private static void ApplyOthersBigPupils(PlayerAvatar local)
    {
        foreach (PlayerAvatar player in PlayerTargets.AliveOthers())
        {
            if (player == null || player.photonView == null || player.photonView.Owner == null) continue;
            if (!player.photonView.Owner.CustomProperties.TryGetValue(BigPupilsPropertyKey, out object raw)) continue;
            if (raw is not bool enabled || !enabled) continue;

            player.OverridePupilSize(PupilMultiplier, PupilPriority, 1f, 1f, 5f, 0.5f, 0.5f);
        }
    }

    // Mimic Host Skin - see CheatState.MimicHostSkin's own comment for the full mechanism. Own
    // snapshot of what was last copied (not compared against MetaManager's selection, which is
    // OUR OWN loadout, not the host's) so this only re-fires SetupCosmetics/SetupColors - each one
    // a real RPC broadcast - when the host's actual loadout changes, not every tick.
    private static List<int> _lastMimickedHostCosmetics;
    private static int[] _lastMimickedHostColors;

    private static void ApplyMimicHostSkin(PlayerAvatar local)
    {
        if (!CheatState.MimicHostSkin.Value) return;
        if (!GameManager.Multiplayer()) return; // no separate "host" loadout to copy in true singleplayer
        if (local.photonView == null || local.photonView.Owner == PhotonNetwork.MasterClient) return; // we ARE the host

        PlayerAvatar host = PlayerTargets.Host();
        if (host == null || host.playerCosmetics == null || local.playerCosmetics == null) return;

        List<int> hostCosmetics = GameReflect.Get<List<int>>(host.playerCosmetics, "cosmeticEquippedRaw");
        int[] hostColors = GameReflect.Get<int[]>(host.playerCosmetics, "colorsEquipped");

        if (hostCosmetics != null && !hostCosmetics.SequenceEqual(_lastMimickedHostCosmetics ?? new List<int>()))
        {
            _lastMimickedHostCosmetics = new List<int>(hostCosmetics);
            local.playerCosmetics.SetupCosmetics(true, true, hostCosmetics);
        }

        if (hostColors != null && !hostColors.SequenceEqual(_lastMimickedHostColors ?? System.Array.Empty<int>()))
        {
            _lastMimickedHostColors = (int[])hostColors.Clone();
            local.playerCosmetics.SetupColors(true, hostColors);
        }
    }

    // Renderer.material (as opposed to sharedMaterial) instantiates a per-renderer clone the
    // first time it's accessed - calling the property getter 2-3 times per call, every tick
    // (~33/sec) this runs, was unnecessary interop overhead on a hot path even though Unity
    // caches the clone after that first access. Fetch it once and reuse the local.
    private static void SetMaterialColor(Renderer renderer, Color color, params string[] properties)
    {
        if (renderer == null) return;
        Material material = renderer.material;
        if (material == null) return;

        foreach (string property in properties)
        {
            if (material.HasProperty(property))
                material.SetColor(property, color);
        }
    }
}
