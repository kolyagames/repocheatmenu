using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;

namespace RepoCheatMenu;

internal static class CheatState
{
    // PanicHack's own persisted state - deliberately NOT part of what the kill switch zeroes
    // (PanicHack.CollectToggles skips these two by reference). Before this existed the "what was
    // on before I panicked" list lived only in memory, while the forced-false values went
    // straight to disk like any other ConfigEntry write - so quitting while panicked silently
    // threw away the whole toggle layout with no way back.
    public static ConfigEntry<bool> PanicHidden;
    public static ConfigEntry<string> PanicSnapshot;

    // Stats tab
    public static ConfigEntry<bool> GodMode;
    public static ConfigEntry<bool> HighStrength;
    public static ConfigEntry<int> StrengthLevel;
    public static ConfigEntry<bool> StrengthOverrideMode;
    public static ConfigEntry<float> StrengthOverrideValue;
    public static ConfigEntry<bool> UnlimitedGrabRange;
    public static ConfigEntry<bool> GrabThroughWalls;
    public static ConfigEntry<int> GrabRangeLevel;
    public static ConfigEntry<bool> NoOvercharge;
    public static ConfigEntry<bool> InfiniteBatteries;
    public static ConfigEntry<bool> PowerGrab;
    public static ConfigEntry<bool> GentleDrop;

    // Speed tab
    public static ConfigEntry<bool> InfiniteStamina;
    public static ConfigEntry<float> SpeedMultiplier;
    public static ConfigEntry<float> SprintSpeedMultiplier;
    public static ConfigEntry<float> CrouchSpeedMultiplier;
    public static ConfigEntry<bool> InfiniteJumps;
    public static ConfigEntry<float> JumpMultiplier;
    public static ConfigEntry<bool> NoKnockdown;
    public static ConfigEntry<bool> NoFallDamage;
    public static ConfigEntry<bool> AntiKnockback;
    public static ConfigEntry<bool> Strafe;
    public static ConfigEntry<float> StrafeMultiplier;
    public static ConfigEntry<bool> Fly;
    public static ConfigEntry<float> FlySpeed;
    public static ConfigEntry<bool> Noclip;
    public static ConfigEntry<bool> FlySprintBoost;
    public static ConfigEntry<bool> DeadFly;
    public static ConfigEntry<float> DeadFlySpeed;

    // Cosmetic tab
    public static ConfigEntry<bool> ForceWings;
    public static ConfigEntry<bool> WingsPink;
    public static ConfigEntry<bool> EyeColor;
    public static ConfigEntry<string> EyeColorName;
    public static ConfigEntry<bool> BigPupils;
    public static ConfigEntry<string> NameChangerText;
    public static ConfigEntry<bool> MimicHostName;
    public static ConfigEntry<bool> MimicHostSkin;

    // Entities tab
    public static ConfigEntry<bool> NoGrabMaxTime;

    // ESP tab
    public static ConfigEntry<bool> EspPlayers;
    public static ConfigEntry<bool> EspEnemies;
    public static ConfigEntry<bool> EspValuables;
    public static ConfigEntry<bool> EspCarts;
    public static ConfigEntry<bool> EspExtractionPoints;
    public static ConfigEntry<bool> EspLoadout;
    public static ConfigEntry<bool> AutoActivateExtraction;
    public static ConfigEntry<string> ItemSpawnerName;
    public static ConfigEntry<int> EspMaxDistance;
    public static ConfigEntry<bool> EspShaderMode;
    public static ConfigEntry<string> EspColor;
    public static ConfigEntry<bool> EspRainbow;

    // Vision tab
    public static ConfigEntry<bool> BetterVision;
    public static ConfigEntry<bool> ThirdPerson;
    public static ConfigEntry<float> ThirdPersonDistance;
    public static ConfigEntry<float> ThirdPersonHeight;
    public static ConfigEntry<float> ThirdPersonHorizontal;
    public static ConfigEntry<float> ThirdPersonSmoothness;
    public static ConfigEntry<float> ThirdPersonModelScale;
    public static ConfigEntry<int> Fov;
    public static ConfigEntry<int> FlashlightIntensity;
    public static ConfigEntry<int> FlashAngle;
    public static ConfigEntry<bool> FlashInCrouch;
    public static ConfigEntry<bool> FlashlightColorEnabled;
    public static ConfigEntry<string> FlashlightColorHex;
    public static ConfigEntry<bool> FlashlightColorRainbow;
    public static ConfigEntry<bool> MaxHeadBattery;
    public static ConfigEntry<bool> LoadingForceSkip;
    public static ConfigEntry<bool> KolobokMode;

    // Valuables tab
    public static ConfigEntry<bool> Unbreakable;
    public static ConfigEntry<bool> FreeQuota;

    // Unbreakable Items - per SemiFunc.itemType (16 real categories the game itself uses,
    // confirmed in the decompiled source), separate from Unbreakable above which only covers
    // actual valuables.
    public static ConfigEntry<bool> UnbreakableDrone;
    public static ConfigEntry<bool> UnbreakableOrb;
    public static ConfigEntry<bool> UnbreakableCart;
    public static ConfigEntry<bool> UnbreakableItemUpgrade;
    public static ConfigEntry<bool> UnbreakablePlayerUpgrade;
    public static ConfigEntry<bool> UnbreakablePowerCrystal;
    public static ConfigEntry<bool> UnbreakableGrenade;
    public static ConfigEntry<bool> UnbreakableMelee;
    public static ConfigEntry<bool> UnbreakableHealthPack;
    public static ConfigEntry<bool> UnbreakableGun;
    public static ConfigEntry<bool> UnbreakableTracker;
    public static ConfigEntry<bool> UnbreakableMine;
    public static ConfigEntry<bool> UnbreakablePocketCart;
    public static ConfigEntry<bool> UnbreakableTool;
    public static ConfigEntry<bool> UnbreakableVehicle;
    public static ConfigEntry<bool> UnbreakableLauncher;
    public static ConfigEntry<bool> QuotaZeroRace;
    public static ConfigEntry<bool> AutoSkipToShop;
    public static ConfigEntry<bool> CursedHands;
    public static ConfigEntry<bool> SeatShuffle;
    public static ConfigEntry<string> LevelLoaderName;
    public static ConfigEntry<string> ModuleLoaderName;
    public static ConfigEntry<bool> MonsterBait;
    public static ConfigEntry<bool> RemovePillarLimit;
    public static ConfigEntry<int> PillarDropMultiplier;
    public static ConfigEntry<bool> PillarDropRemoveLimit;
    public static ConfigEntry<float> SpawnRateMultiplier;
    public static ConfigEntry<bool> HealSpam;
    public static ConfigEntry<bool> CartRam;
    public static ConfigEntry<bool> MasterRpcSelfBypass;
    public static ConfigEntry<bool> InfiniteChargingStation;
    public static ConfigEntry<bool> ChargingStationHeal;
    public static ConfigEntry<bool> DeathAutoToTruck;
    public static ConfigEntry<bool> DeathAutoToExtraction;
    public static ConfigEntry<bool> TeammateDeathDropBead;
    public static ConfigEntry<bool> AutoReviveTeammates;

    // Items tab
    public static ConfigEntry<bool> UseLaser;
    public static ConfigEntry<bool> MeleeVibrate;
    public static ConfigEntry<bool> StealHeldItem;
    public static ConfigEntry<string> StealHeldItemName;
    public static ConfigEntry<string> SpawnValuableName;

    // Voice tab
    public static ConfigEntry<string> VoiceFilter;
    public static ConfigEntry<bool> GlitchyVoice;
    public static ConfigEntry<float> GlitchyVoiceInterval;
    public static ConfigEntry<bool> ChipmunkVoices;
    public static ConfigEntry<float> ChipmunkPitch;

    // Repo Debug tab
    public static ConfigEntry<bool> AlwaysInteract;
    public static ConfigEntry<bool> SkipMoonWait;
    public static ConfigEntry<bool> SkipResultsScreen;

    // Troll tab
    public static ConfigEntry<bool> DestroyAllValuables;
    public static ConfigEntry<bool> ShopDestroyer;
    public static ConfigEntry<bool> LootMagnet;
    public static ConfigEntry<bool> ValuableGodMode;
    public static ConfigEntry<bool> CartPuller;
    public static ConfigEntry<bool> AutoLoot;
    public static ConfigEntry<bool> AutoFarm;
    public static ConfigEntry<bool> ExtractionRadar;
    public static ConfigEntry<bool> KnockdownAll;
    public static ConfigEntry<bool> KnockdownBanish;
    public static ConfigEntry<bool> KnockdownBanishKill;
    public static ConfigEntry<bool> AntiAimSpin;
    public static ConfigEntry<bool> DinnerBone;
    public static ConfigEntry<float> DinnerBoneAngle;
    public static ConfigEntry<bool> DinnerBoneShowSelf;
    public static ConfigEntry<bool> FlipEnabled;
    public static ConfigEntry<string> FlipAxis;
    public static ConfigEntry<float> FlipSpeed;
    public static ConfigEntry<bool> FlipShowSelf;
    public static ConfigEntry<float> FlipOrbitRadius;
    public static ConfigEntry<bool> TPose;
    public static ConfigEntry<bool> GhostCrawl;
    public static ConfigEntry<bool> Invisible;
    public static ConfigEntry<float> InvisibleHeight;
    public static ConfigEntry<bool> InvisibleRevealWhileTalking;
    public static ConfigEntry<bool> GlitchyFlashlight;
    public static ConfigEntry<float> GlitchyFlashlightRadius;
    public static ConfigEntry<bool> GhostRider;
    public static ConfigEntry<bool> ChatSpam;
    public static ConfigEntry<string> ChatSpamText;
    public static ConfigEntry<float> ChatSpamInterval;
    public static ConfigEntry<bool> ChatSpamRichText;
    public static ConfigEntry<string> ChatSpamColor;
    public static ConfigEntry<int> ChatSpamFontSize;

    // Always-on gameplay HUD (PlayerHud.cs) - separate from both menus, drawn every frame
    // regardless of whether either is open. One ConfigEntry group per widget instead of one field
    // each (Enabled/PosX/PosY/Color) so adding a future widget is one BindHudWidget call, not four
    // new fields - see HudWidgetConfig below.
    public sealed class HudWidgetConfig
    {
        public ConfigEntry<bool> Enabled;
        public ConfigEntry<float> PosX;
        public ConfigEntry<float> PosY;
        public ConfigEntry<string> Color;
    }

    public static ConfigEntry<bool> HudEnabled;
    public static ConfigEntry<bool> HudEditMode;
    public static ConfigEntry<bool> HudMediaPlayerUseSpotify;
    public static ConfigEntry<bool> HudAutoStack;
    public static ConfigEntry<string> HudMediaPlayerTitle;
    public static HudWidgetConfig HudPlayerStats;
    public static HudWidgetConfig HudTargetInfo;
    public static HudWidgetConfig HudMediaPlayer;
    public static HudWidgetConfig HudKeybinds;

    public static ConfigEntry<string> FakeSoundTargetName;
    public static ConfigEntry<bool> PlayerCage;
    public static ConfigEntry<bool> PlayerCageAll;
    public static ConfigEntry<bool> FreezeAll;
    public static ConfigEntry<bool> MassDisarm;
    public static ConfigEntry<bool> YeetStorm;
    public static ConfigEntry<bool> ExtractionCamperPunish;
    public static ConfigEntry<bool> ShopExecution;
    public static ConfigEntry<bool> AutoShopExecute;
    public static ConfigEntry<bool> CarryThrustStorm;
    public static ConfigEntry<bool> GrabLockTarget;
    public static ConfigEntry<bool> GrabLockAll;
    public static ConfigEntry<bool> UnequipStorm;
    public static ConfigEntry<bool> WeaponDrainAll;
    public static ConfigEntry<bool> DisarmOnGrab;
    public static ConfigEntry<bool> ExtractionSnipe;
    public static ConfigEntry<bool> AutoRevealValuables;
    public static ConfigEntry<bool> ProjectileStrikeStorm;
    public static ConfigEntry<bool> VehicleWobbleStorm;
    public static ConfigEntry<bool> ArenaSabotage;
    public static ConfigEntry<bool> ArenaWeaponsSabotage;
    public static ConfigEntry<bool> ArenaLegitSabotage;
    public static ConfigEntry<bool> SwarmStorm;
    public static ConfigEntry<bool> LockAllDoors;
    public static ConfigEntry<bool> TrapRoom;
    public static ConfigEntry<bool> EnemyDropKill;
    public static ConfigEntry<bool> FreezeEnemies;
    public static ConfigEntry<float> FreezeEnemiesRange;
    public static ConfigEntry<bool> UnlimitedRerolls;
    public static ConfigEntry<bool> StealAllHeldItems;

    // Misc tab - networking scaffold
    public static ConfigEntry<bool> SendTestPacket;
    public static ConfigEntry<int> PacketTestIntervalMs;
    public static ConfigEntry<bool> DebugBridgeEnabled;
    public static ConfigEntry<bool> EventLogging;
    public static ConfigEntry<bool> NetworkDiagnostics;

    // Misc tab - anti-crash (defensive, on by default)
    public static ConfigEntry<bool> AntiCrash;
    public static ConfigEntry<bool> AntiCrashQuarantine;
    public static ConfigEntry<int> AntiCrashMaxRpcsPerSecond;
    public static ConfigEntry<int> AntiCrashMaxInstantiatesPerSecond;
    public static ConfigEntry<int> AntiCrashMaxArgStringLength;
    public static ConfigEntry<int> AntiCrashQuarantineSeconds;
    public static ConfigEntry<int> MultiRoomExtraCount;
    public static ConfigEntry<int> MultiRoomMaxPlayers;

    public static void Init(ConfigFile config)
    {
        PanicHidden = config.Bind("Panic", "PanicHidden", false,
            "True while the kill switch is engaged. Persisted so ']' still restores everything " +
            "after quitting and relaunching while panicked, instead of leaving every toggle " +
            "stuck off with no way back.");
        PanicSnapshot = config.Bind("Panic", "PanicSnapshot", "",
            "Semicolon-separated FieldName=0/1 list of what was enabled when the kill switch was " +
            "last engaged. Written by PanicHack.Deactivate, consumed by Reactivate.");

        GodMode = config.Bind("Stats", "GodMode", false,
            "Ignore all incoming damage.");
        HighStrength = config.Bind("Stats", "HighStrength", false,
            "Forces the Strength upgrade level and boosts grab/throw force. The real physics " +
            "boost only takes effect when you're hosting or in singleplayer - the game computes " +
            "grab/throw strength exclusively on whoever's the master client, so a guest can't " +
            "affect it locally no matter what.");
        StrengthLevel = config.Bind("Stats", "StrengthLevel", 20,
            "Upgrade level forced when High Strength is on.");
        StrengthOverrideMode = config.Bind("Stats", "StrengthOverrideMode", false,
            "Alternate way to apply High Strength: instead of forcing the Strength upgrade level " +
            "in StatsManager's dictionary, calls PhysGrabber.OverrideGrabStrength(value) directly " +
            "every tick - a dedicated override field the game itself reads in place of the real " +
            "grabStrength whenever it's set (confirmed in the decompiled source), so it's not " +
            "capped by whatever the upgrade level system normally allows. Same host/singleplayer-" +
            "only wall as plain High Strength - the physics still only run authoritatively on the " +
            "master client.");
        StrengthOverrideValue = config.Bind("Stats", "StrengthOverrideValue", 5f,
            "Grab strength value used when Strength Override Mode is on.");
        UnlimitedGrabRange = config.Bind("Stats", "UnlimitedGrabRange", false,
            "Forces the Grab Range upgrade level without buying it. Unlike Strength, this works for guests too.");
        GrabRangeLevel = config.Bind("Stats", "GrabRangeLevel", 20,
            "Upgrade level forced when Unlimited Grab Range is on.");

        GrabThroughWalls = config.Bind("Stats", "GrabThroughWalls", false,
            "Lets your grab raycast pass straight through walls/floors to whatever's behind them, " +
            "instead of stopping at the first wall in the way - PhysGrabber.RayCheck's own raycast " +
            "mask normally includes solid level geometry, this narrows it to just the phys-grab-" +
            "object layers while the raycast runs. Local-only, works for guest or host, same as " +
            "Unlimited Grab Range.");
        NoOvercharge = config.Bind("Stats", "NoOvercharge", false,
            "Blocks the grab-overcharge penalty entirely.");
        InfiniteBatteries = config.Bind("Stats", "InfiniteBatteries", false,
            "Flashlight and death-head batteries never drain.");
        PowerGrab = config.Bind("Stats", "PowerGrab", false,
            "Snaps whatever you're holding straight to your aim point, ignoring weight. Unlike " +
            "High Strength, this works as a guest too - it rides the same position-request RPC " +
            "the game itself uses (e.g. for item drones), which the host applies unconditionally.");

        GentleDrop = config.Bind("Stats", "GentleDrop", false,
            "Stops releasing a held item from launching it - confirmed in the decompiled source, " +
            "PhysGrabObject.GrabEnded() unconditionally calls Throw() on every release " +
            "(force scaled by how far your aim point had drifted from the object, times " +
            "0.5+throwStrength - never actually zero), completely vanilla behavior, nothing to " +
            "do with Power Grab or High Strength. This zeroes the object's velocity right after " +
            "that throw impulse is applied, so it just drops instead. Only takes effect where " +
            "Throw() actually runs authoritatively - host/singleplayer, same wall as High " +
            "Strength; a guest's own release never runs Throw() locally at all (it just asks the " +
            "master to), so there's nothing for this to zero out there.");

        InfiniteStamina = config.Bind("Speed", "InfiniteStamina", false,
            "Stamina never depletes.");
        SpeedMultiplier = config.Bind("Speed", "SpeedMultiplier", 1f,
            "Multiplier on base walk speed (1 = vanilla).");
        SprintSpeedMultiplier = config.Bind("Speed", "SprintSpeedMultiplier", 1f,
            "Multiplier on sprint speed (1 = vanilla).");
        CrouchSpeedMultiplier = config.Bind("Speed", "CrouchSpeedMultiplier", 1f,
            "Multiplier on crouch/crawl speed (1 = vanilla).");
        InfiniteJumps = config.Bind("Speed", "InfiniteJumps", false,
            "Forces the extra-jump counter to a high value each frame.");
        JumpMultiplier = config.Bind("Speed", "JumpMultiplier", 1f,
            "Multiplier on PlayerController.JumpForce (public field, set directly - 1 = vanilla). " +
            "Purely local, works for guest or host.");

        NoKnockdown = config.Bind("Speed", "NoKnockdown", false,
            "Never tumble/ragdoll, including from being hit by thrown objects.");

        NoFallDamage = config.Bind("Speed", "NoFallDamage", false,
            "Blocks fall damage by pinning PlayerCollisionController.fallDistance at 0 every " +
            "tick, so it never reaches the 8m threshold PlayerAvatar's own client-authoritative " +
            "damage check requires. Purely local, works for guest or host.");

        AntiKnockback = config.Bind("Speed", "AntiKnockback", false,
            "Blocks PlayerController.ForceImpulse entirely - confirmed in the decompiled source, " +
            "this is the single method every kind of external hit-force routes through " +
            "(HurtCollider on thrown/exploding objects, enemy hits, etc.), so this stops ALL of " +
            "them uniformly instead of chasing each source separately. Purely local - only " +
            "affects your own PlayerController, works for guest or host. Bind a key on the " +
            "Keybinds tab to toggle it on demand.");
        Strafe = config.Bind("Speed", "Strafe", false,
            "Vanilla movement is already omnidirectional in the decompiled source - " +
            "InputDirection is applied to MoveSpeed/SprintSpeed/CrouchSpeed uniformly no " +
            "matter which way you're pressing, there's no built-in backward/strafe penalty to " +
            "remove. What this does instead: clears PlayerController.MoveMult() every tick " +
            "(the temporary slow multiplier hazards/enemies apply, which is the actual thing " +
            "that can make any direction feel slower) and layers a flat extra speed " +
            "multiplier on top of walk/sprint/crouch speed in every direction.");
        StrafeMultiplier = config.Bind("Speed", "StrafeMultiplier", 1.2f,
            "Flat extra speed multiplier applied in every direction when Strafe is on " +
            "(1 = no extra boost, stacks multiplicatively with the Move/Sprint/Crouch Speed x sliders).");

        Fly = config.Bind("Speed", "Fly", false,
            "Disables gravity and moves you freely along the camera's full look direction " +
            "(forward/right/up, including pitch - look up and fly up) instead of the normal " +
            "flat ground movement. Writes transform.position directly from a PlayerController." +
            "Update postfix (Time.deltaTime-based) rather than driving the Rigidbody through " +
            "FixedUpdate - the earlier FixedUpdate/MovePosition approach produced no visible " +
            "movement in practice. Rigidbody goes kinematic and its Collider gets disabled while " +
            "flying, so this also passes through walls on its own now (no separate Noclip needed " +
            "for that anymore). Purely local movement, nothing networked here, works identically " +
            "for guest or host.");
        FlySpeed = config.Bind("Speed", "FlySpeed", 15f,
            "Fly movement speed in units/sec.");
        Noclip = config.Bind("Speed", "Noclip", false,
            "Goes kinematic and lets you sink straight through the floor (gravity simulated by " +
            "hand, since a kinematic Rigidbody ignores real Physics.gravity) - useful on its own " +
            "as a quick way to fall through geometry without flying around. Fly already passes " +
            "through walls by itself now, so Noclip isn't required to combine with it anymore. " +
            "Purely local, works for guest or host.");
        FlySprintBoost = config.Bind("Speed", "FlySprintBoost", false,
            "Doubles Fly speed while holding Sprint, on top of the normal Fly Speed value.");
        DeadFly = config.Bind("Speed", "DeadFly", false,
            "Free movement while dead, before being revived. The vanilla spectate camera only " +
            "orbits a fixed point (SpectateCamera.StateDeath, confirmed in the decompiled source - " +
            "no free movement input anywhere in that state). Instead of touching the camera, this " +
            "moves your own death head - PlayerDeathHead has its own PhysGrabObject like any " +
            "grabbable prop, and the camera's follow point is a child transform ON that head, so " +
            "relocating it via Teleport() (SetPositionRPC to the master, zero validation, works " +
            "for guest or host - same loophole used everywhere else in this mod) drags the whole " +
            "vanilla follow-cam along for free.");
        DeadFlySpeed = config.Bind("Speed", "DeadFlySpeed", 10f,
            "Movement speed while using Dead Fly, units/sec.");

        ForceWings = config.Bind("Cosmetic", "ForceWings", false,
            "Shows the wings cosmetic regardless of your actual TumbleWings upgrade level - " +
            "purely visual, no lift/thrust (that stays tied to the real upgrade). Rides " +
            "PlayerAvatar.UpgradeTumbleWingsVisualsActive(), owner-gated not master-gated, so " +
            "teammates see the wings appear on you too, guest or host.");
        WingsPink = config.Bind("Cosmetic", "WingsPink", false,
            "The game only has two real wing colors, confirmed in the decompiled source - " +
            "ItemUpgradePlayerTumbleWingsLogic.WingsSetOriginalColors()/WingsSetPinkColors(), " +
            "no generic color channel exists. UpgradeTumbleWingsVisualsActiveRPC takes the pink " +
            "choice as one of its own RPC arguments (RpcTarget.All), so - unlike the old fake-hex " +
            "version this replaced - teammates genuinely see whichever one you pick, not just you.");
        EyeColor = config.Bind("Cosmetic", "EyeColor", false,
            "Recolors your eyes from a small fixed palette (EyeColorName below) - the game has no " +
            "eye-color system of its own at all (no field, no RPC, confirmed absent from the " +
            "decompiled source), so this is built from scratch: applies the color to your own " +
            "renderer locally, and broadcasts your pick via Photon's own per-player custom " +
            "properties (Player.SetCustomProperties, a real synced channel, not tied to any " +
            "R.E.P.O.-specific RPC) so every other player running this same mod sees it too and " +
            "applies it on their end - same approach GameTools uses for the same problem.");
        EyeColorName = config.Bind("Cosmetic", "EyeColorName", "Red",
            "Which fixed palette color Eye Color uses. Cycle with the menu's button.");

        BigPupils = config.Bind("Cosmetic", "BigPupils", false,
            "PlayerAvatar.OverridePupilSize(multiplier, prio, ...) - confirmed public, correctly-" +
            "protected (MasterAndOwnerOnlyRPC, checks the RPC sender - not a hole, you always own " +
            "your own avatar) real game API, the same one ChatManager/GumballValuable/EnemyCeilingEye " +
            "use to make eyes look scared. Calling it ourselves every tick just repurposes it as a " +
            "toggle instead of a one-shot scare. No native sync for arbitrary callers though - same " +
            "gap Eye Color already worked around - so this rides the same RCM_-prefixed Photon " +
            "custom-properties broadcast (Player.SetCustomProperties, real Photon SDK channel, not " +
            "R.E.P.O.-specific) to show up for OTHER players running this mod too, not just locally.");

        NameChangerText = config.Bind("Cosmetic", "NameChangerText", "",
            "New display name to broadcast - rides PlayerAvatar.AddToStatsManagerRPC " +
            "(confirmed [PunRPC], gated by SemiFunc.OwnerOnlyRPC which you always pass on your " +
            "own avatar, no bypass needed). Real for everyone in the room, host or guest, and " +
            "persists for anyone who joins later (RpcTarget.AllBuffered). Your actual steamID is " +
            "kept untouched so StatsManager still finds your real upgrade progress - only the " +
            "displayed name changes. Press the menu's button to apply.");

        MimicHostName = config.Bind("Cosmetic", "MimicHostName", false,
            "Auto-impersonation - finds whichever PlayerAvatar's photonView.Owner is " +
            "PhotonNetwork.MasterClient (the room host) and copies their current playerName onto " +
            "yours via the same NameChangerHack.SetName() the manual field above uses - same real, " +
            "everyone-sees-it rename, just triggered automatically instead of typed. Ticks " +
            "continuously (cheap - early-out unless something changed) rather than once at join, so " +
            "it also re-syncs if the host later renames or master migrates to someone else. No-op " +
            "if you're the host yourself (nothing to mimic) or in true singleplayer (no host " +
            "concept). Works for guest or host - only reads the host's own already-public name, no " +
            "authority needed.");

        MimicHostSkin = config.Bind("Cosmetic", "MimicHostSkin", false,
            "Same auto-mimic idea as MimicHostName, on appearance instead of the name - finds the " +
            "host's PlayerAvatar (PlayerTargets.Host()) and copies their playerCosmetics." +
            "cosmeticEquippedRaw (List<int>, internal) and colorsEquipped (int[], internal) onto " +
            "your own PlayerCosmetics via its own public SetupCosmetics(synced:true, forced:true, " +
            "list)/SetupColors(synced:true, colors) methods (confirmed public in the decompiled " +
            "source) - the exact same calls the game's own cosmetics menu makes when you change an " +
            "outfit, just fed the host's loadout instead of your own MetaManager selection. Both " +
            "RPCs they trigger (SetupCosmeticsRPC/SetupColorsRPC) are gated OwnerOnlyRPC - real, " +
            "everyone-sees-it, no bypass needed since it's always your own avatar's PhotonView. " +
            "Ticks continuously, re-applies only when the host's own loadout actually changes (own " +
            "static snapshot compared each tick), so it also catches the host swapping cosmetics " +
            "mid-run. Same no-op conditions as MimicHostName (host yourself, true singleplayer).");

        NoGrabMaxTime = config.Bind("Entities", "NoGrabMaxTime", false,
            "Enemies grabbing you never reach their shake-free time limit (host/singleplayer only).");

        EspPlayers = config.Bind("Esp", "EspPlayers", false,
            "Shows other players' names through walls.");
        EspEnemies = config.Bind("Esp", "EspEnemies", false,
            "Shows enemy names through walls.");
        EspValuables = config.Bind("Esp", "EspValuables", false,
            "Shows valuable names and prices through walls.");
        EspCarts = config.Bind("Esp", "EspCarts", false,
            "Shows carts through walls (no director list tracks carts, so this scans the scene " +
            "once a second - cheap enough at that rate). Use the Cart Teleport button (Items " +
            "tab) to pull the nearest one straight to you.");

        EspExtractionPoints = config.Bind("Esp", "EspExtractionPoints", false,
            "Shows every extraction point through walls, with its current state " +
            "(Idle/Active/etc).");

        EspLoadout = config.Bind("Esp", "EspLoadout", false,
            "Adds what each teammate is currently holding to their Player ESP label (requires " +
            "Player ESP to also be on). Purely informational, local rendering only.");

        AutoActivateExtraction = config.Bind("World", "AutoActivateExtraction", false,
            "Auto-activates the extraction sequence the instant none is active, skipping the " +
            "need to walk up and press the button yourself. Rides " +
            "RoundDirector.RequestExtractionPointActivation (RPC to master, applied " +
            "unconditionally regardless of who asked) - works for guest or host.");

        ItemSpawnerName = config.Bind("World", "ItemSpawnerName", "None",
            "Item type to spawn for free - pick from the game's full item registry " +
            "(StatsManager.itemDictionary, loaded locally off disk so it's the same list " +
            "regardless of host/guest status - NOT the shop's current rotation, which is " +
            "master-gated and empty for a guest) using the cycle button, then hit Spawn. Same " +
            "PhotonNetwork.InstantiateRoomObject trick as Gold Coins - works for guest or host.");
        EspMaxDistance = config.Bind("Esp", "EspMaxDistance", 25,
            "Maximum distance (meters) to show ESP labels at - keeps distant clutter off screen.");
        EspShaderMode = config.Bind("Esp", "EspShaderMode", false,
            "Fills targeted players/enemies/valuables with a solid-color silhouette visible " +
            "through walls, in addition to the text label.");
        EspColor = config.Bind("Esp", "EspColor", "#00FF00",
            "Shader Mode highlight color (hex). Cycle presets with the menu's Color button.");
        EspRainbow = config.Bind("Esp", "EspRainbow", false,
            "Cycles the Shader Mode highlight color through the rainbow instead of using a fixed color.");

        BetterVision = config.Bind("Vision", "BetterVision", false,
            "Extends draw distance and disables the level's darkness post-processing.");
        ThirdPerson = config.Bind("Vision", "ThirdPerson", false,
            "Pulls the camera back behind your character.");
        ThirdPersonDistance = config.Bind("Vision", "ThirdPersonDistance", 3f,
            "How far back the camera pulls (max reach - still clamped closer by the existing " +
            "wall-avoidance SphereCast so it never clips through geometry behind you).");
        ThirdPersonHeight = config.Bind("Vision", "ThirdPersonHeight", 0f,
            "Vertical camera offset in Third Person, local space (+up/-down).");
        ThirdPersonHorizontal = config.Bind("Vision", "ThirdPersonHorizontal", 0f,
            "Horizontal camera offset in Third Person, local space (+right/-left) - over-the-" +
            "shoulder instead of dead-center behind you.");
        ThirdPersonSmoothness = config.Bind("Vision", "ThirdPersonSmoothness", 8f,
            "How eased/laggy the Third Person camera follow is, in units/sec (the 'smooth camera' " +
            "style from Minecraft visual clients - the render camera trails the target offset " +
            "instead of snapping to it every frame). LOWER = smoother/laggier, HIGHER = snappier. " +
            "Also what fixed the reported jitter: the wall-avoidance SphereCast below is a fresh " +
            "independent cast every tick, and at longer distances indoors it can flip between " +
            "'clear' and 'blocked' on consecutive frames as the look direction shifts by fractions " +
            "of a degree - easing toward the target instead of snapping to it absorbs exactly that " +
            "single-frame flip-flop.");
        ThirdPersonModelScale = config.Bind("Vision", "ThirdPersonModelScale", 1f,
            "GameTools' 'Third Person Model Scale' - shrinks/grows your own visible mesh while " +
            "you're looking at yourself in Third Person (1 = normal size). Purely cosmetic self-" +
            "view, scales playerAvatarVisuals.transform (confirmed public, its own visuals-only " +
            "component) - never touches movement/collision.");
        Fov = config.Bind("Vision", "Fov", 60,
            "Field of view (60-140).");
        FlashlightIntensity = config.Bind("Vision", "FlashlightIntensity", 10,
            "Flashlight intensity, 10 = vanilla, 20 = double brightness.");
        FlashAngle = config.Bind("Vision", "FlashAngle", 60,
            "Flashlight cone angle in degrees.");
        FlashInCrouch = config.Bind("Vision", "FlashInCrouch", false,
            "Keep the flashlight on while crouching/crawling.");
        MaxHeadBattery = config.Bind("Vision", "MaxHeadBattery", false,
            "Death-head flight energy never depletes.");

        FlashlightColorEnabled = config.Bind("Vision", "FlashlightColorEnabled", false,
            "Recolors your flashlight beam. Local rendering only - Light.color, nobody else sees it.");
        FlashlightColorHex = config.Bind("Vision", "FlashlightColorHex", "#FFFFFF",
            "Flashlight color (hex). Cycle presets with the menu's Color button.");
        FlashlightColorRainbow = config.Bind("Vision", "FlashlightColorRainbow", false,
            "Cycles the flashlight color through the rainbow instead of using a fixed color.");

        KolobokMode = config.Bind("Vision", "KolobokMode", false,
            "While spectating as your death head (dead, holding Interact to leave your body), " +
            "automatically cycles the game's own charge-jump hop (hold Jump / release to launch " +
            "forward, PlayerDeathHead.SpectatedJumpLocalInput) instead of you doing it manually - " +
            "keeps hopping in whatever direction you're looking. Rides an owner-gated RPC (not " +
            "master-gated), so it works for guest or host. Still discrete hops on the game's own " +
            "~2s cooldown, not smooth rolling - nothing can bypass that without master authority.");
        LoadingForceSkip = config.Bind("Vision", "LoadingForceSkip", false,
            "Changes the loading-screen Skip button to an instant force-skip instead of the " +
            "normal safe one. Normal mode just marks you ready and waits for the rest of the " +
            "group like the game itself would. Force mode jumps your own client into the level " +
            "immediately without telling anyone else - confirmed to desync other players " +
            "(they can get stuck waiting on you forever) if they genuinely weren't done loading " +
            "yet. Only use Force when Normal is already stuck and waiting won't help.");

        Unbreakable = config.Bind("Valuables", "Unbreakable", false,
            "Valuables never break from impacts.");

        UnbreakableDrone = config.Bind("Items", "UnbreakableDrone", false, "Never break: drones.");
        UnbreakableOrb = config.Bind("Items", "UnbreakableOrb", false, "Never break: orbs.");
        UnbreakableCart = config.Bind("Items", "UnbreakableCart", false, "Never break: carts.");
        UnbreakableItemUpgrade = config.Bind("Items", "UnbreakableItemUpgrade", false, "Never break: item upgrade stations.");
        UnbreakablePlayerUpgrade = config.Bind("Items", "UnbreakablePlayerUpgrade", false, "Never break: player upgrade stations.");
        UnbreakablePowerCrystal = config.Bind("Items", "UnbreakablePowerCrystal", false, "Never break: power crystals (charging station fuel).");
        UnbreakableGrenade = config.Bind("Items", "UnbreakableGrenade", false, "Never break: grenades.");
        UnbreakableMelee = config.Bind("Items", "UnbreakableMelee", false, "Never break: melee weapons.");
        UnbreakableHealthPack = config.Bind("Items", "UnbreakableHealthPack", false, "Never break: health packs.");
        UnbreakableGun = config.Bind("Items", "UnbreakableGun", false, "Never break: guns.");
        UnbreakableTracker = config.Bind("Items", "UnbreakableTracker", false, "Never break: trackers.");
        UnbreakableMine = config.Bind("Items", "UnbreakableMine", false, "Never break: mines.");
        UnbreakablePocketCart = config.Bind("Items", "UnbreakablePocketCart", false, "Never break: pocket carts.");
        UnbreakableTool = config.Bind("Items", "UnbreakableTool", false, "Never break: tools.");
        UnbreakableVehicle = config.Bind("Items", "UnbreakableVehicle", false, "Never break: vehicles.");
        UnbreakableLauncher = config.Bind("Items", "UnbreakableLauncher", false, "Never break: launchers.");

        FreeQuota = config.Bind("Valuables", "FreeQuota", false,
            "Completes the nearest active extraction point's haul goal for free. Host/singleplayer: " +
            "zeroes the point's own haulGoal directly (QuotaHack.cs) - genuinely public field, " +
            "real authority, instant. Guest: no direct authority over that, so instead repeatedly " +
            "spawns real valuables into the zone (same guest-safe path as Spawn Valuable) until the " +
            "master's own running total crosses the goal - works, just not instant.");

        QuotaZeroRace = config.Bind("Valuables", "QuotaZeroRace", false,
            "Standalone from Free Quota above - targets the brief window right after a level " +
            "loads, before RoundDirector's own haulGoal has finished syncing (still reads 0). A " +
            "point activated in that window computes its own goal as 0, completing instantly with " +
            "zero dollars collected (QuotaHack.ApplyZeroRace). Works for guest or host - same " +
            "RequestExtractionPointActivation RPC everything else here uses. Window closes once " +
            "the real goal syncs, a few seconds into the level.");

        AutoSkipToShop = config.Bind("Valuables", "AutoSkipToShop", false,
            "Host/singleplayer only. Same zero-race trigger as Quota Zero Race above, then follows " +
            "through into TruckScreenText.GotoNextLevel() once the point's success animation has " +
            "had time to finish - the whole level 1 gets skipped for the room straight to the shop, " +
            "as early as the loading screen. No-op for guests: RunManager.ChangeLevel's own first " +
            "line checks SemiFunc.IsMasterClientOrSingleplayer() before doing anything at all.");

        UnlimitedRerolls = config.Bind("Valuables", "UnlimitedRerolls", false,
            "Removes UpgradeStand's random 1-3 reroll cap (confirmed in the decompiled source: " +
            "rolled once per stand as Random.Range(1, 4) on first use, then locks into State.Broken " +
            "once you hit it) by forcing maxRerollCount up before the stand's own logic can act on " +
            "the real roll. Host/singleplayer only - unlike Revive/Freeze this isn't a simple RPC " +
            "gate, the entire press-processing state machine (cost deduction, the roll itself) only " +
            "runs under SemiFunc.IsMasterClientOrSingleplayer(), a guest can't trigger any of it " +
            "themselves at all. Still costs the real, escalating RerollCost (5 + rerollCount*5) in " +
            "currency each time - this only removes the attempt cap, not the price.");

        CursedHands = config.Bind("Troll", "CursedHands", false,
            "Repeatedly force-equips a random unequipped item onto the picked target - " +
            "ItemEquippable.RPC_RequestEquip has no sender check at all, works for guest or host.");

        SeatShuffle = config.Bind("Troll", "SeatShuffle", false,
            "Repeatedly force-seats the picked target into a random seat on a random nearby " +
            "vehicle - ItemVehicle.RequestSeatRPC checks IsMasterClientOrSingleplayer, not the " +
            "sender, works for guest or host.");

        LevelLoaderName = config.Bind("Misc", "LevelLoaderName", "None",
            "Host/singleplayer only - loads any level from RunManager.levels directly, same as " +
            "the built-in dev console's /level command, without needing that console unlocked.");

        ModuleLoaderName = config.Bind("Misc", "ModuleLoaderName", "None",
            "Host/singleplayer only - forces a specific room module from the CURRENT level's own " +
            "pool into the next generation and restarts the scene, same as the dev console's " +
            "/module command (LevelGenerator.DebugModule etc., injected every tick via " +
            "LevelLoaderHack.ApplyModuleInjection while regenerating).");

        MonsterBait = config.Bind("Troll", "MonsterBait", false,
            "Every few seconds, teleports the nearest musical valuable to the picked target and " +
            "fires MusicalValuableLogic.MusicKeyPressedRPC (no sender check at all), which calls " +
            "SemiFunc.EnemyInvestigate at that spot - draws real enemy attention to the target.");

        RemovePillarLimit = config.Bind("Troll", "RemovePillarLimit", false,
            "The game caps enemy-death valuables at 10 total (EnemyDirector.AddEnemyValuable, " +
            "confirmed in the decompiled source - destroys the oldest one once an 11th is added). " +
            "This skips that cap, so older enemy drops stop getting despawned to make room for " +
            "new ones.");
        PillarDropMultiplier = config.Bind("Troll", "PillarDropMultiplier", 1,
            "How many enemy-death valuables spawn per kill instead of the normal 1 (up to each " +
            "enemy's own spawnValuableMax unless Pillar Drop Remove Limit below is also on). Only " +
            "actually spawns anything on whichever machine is master/singleplayer - " +
            "PhotonNetwork.InstantiateRoomObject (what the game itself uses here) is master-only, " +
            "confirmed in NetSpawn.cs.");
        PillarDropRemoveLimit = config.Bind("Troll", "PillarDropRemoveLimit", false,
            "GameTools' 'remove pillar limit' - EnemyHealth.spawnValuableMax (confirmed public, " +
            "default 3) caps the TOTAL valuables any one enemy can ever drop across its whole " +
            "lifetime, not just this session's own extras, so a high Pillar Drop Multiplier alone " +
            "still silently caps out once an enemy's already dropped 3. This forces that cap up " +
            "before the loop runs.");

        SpawnRateMultiplier = config.Bind("Monsters", "SpawnRateMultiplier", 1f,
            "GameTools calls this 'Level Spawn Multiplier' - own implementation, same technique: " +
            "EnemyDirector.AmountSetup() (confirmed public in the decompiled source) computes how " +
            "many enemies of each of its 3 difficulty tiers to stock the level with by calling its " +
            "own private PickEnemies() N times per tier (N itself read from private " +
            "amountCurve1/2/3Value fields via GameReflect - each call weighted-picks one enemy type " +
            "and adds it to the shared pool GetEnemy() draws from throughout the level). A postfix " +
            "on AmountSetup calls that same private PickEnemies() extra times per tier, scaled by " +
            "(multiplier - 1) against each tier's own real count, so higher tiers stay " +
            "proportionally rarer at any multiplier instead of every tier getting an equal flat " +
            "bump. Host/singleplayer only (SemiFunc.IsMasterClientOrSingleplayer) - a guest's own " +
            "call would only pad their own local copy of a list the guest doesn't actually spawn " +
            "from. 1 = vanilla, no-op.");

        HealSpam = config.Bind("Troll", "HealSpam", false,
            "Every second, heals the picked target for 25 via PlayerHealth.HealOtherRPC (checks " +
            "photonView.IsMine, not the sender) - harmless, but retriggers the game's own heal " +
            "camera-glitch effect over and over. No effect once they're already at full health.");

        CartRam = config.Bind("Troll", "CartRam", false,
            "Grabs the nearest cart (PhysGrabber.OverrideGrab, same auto-equip path drones use) " +
            "and repeatedly snaps it onto the picked target's position (PhysGrabObject.Teleport - " +
            "SetPositionRPC to the master, zero validation, works for guest or host, same as Power " +
            "Grab). Whether this actually registers as a hit depends on PhysGrabObjectImpactDetector's " +
            "own collision/playerGrabbing checks at runtime - not something verifiable from source " +
            "alone, since cart's isValuable/hasHealth flags may be set on the prefab itself.");

        MasterRpcSelfBypass = config.Bind("Valuables", "MasterRpcSelfBypass", false,
            "Patches SemiFunc.MasterOnlyRPC itself: on YOUR OWN client, any RPC gated by that " +
            "check now passes whenever the message claims to come from you - regardless of " +
            "whether you're actually master. Only affects what runs locally on your own machine " +
            "(other clients still run the real, unpatched check on their end), so this unlocks " +
            "guest-side self-effects on RPCs that were previously silently dropped for a guest - " +
            "e.g. Revive Self below (PlayerAvatar.ReviveRPC has no other guard). Does nothing for " +
            "effects that need to land on someone ELSE's machine.");

        InfiniteChargingStation = config.Bind("Valuables", "InfiniteChargingStation", false,
            "Pins the charging station's own private charge fields (chargeInt/chargeTotal/" +
            "chargeFloat/chargeSegmentCurrent) at full every tick and blocks its " +
            "OutOfCrystalsShutdown/ChargingStationCrystalBrokenRPC from running - it never runs " +
            "out and never breaks down. Purely local field writes, works for guest or host.");

        ChargingStationHeal = config.Bind("Valuables", "ChargingStationHeal", false,
            "Heals you 5 HP/sec while standing within range of the charging station. Rides " +
            "PlayerHealth.Heal() (your own avatar, no RPC needed) - works for guest or host.");

        DeathAutoToTruck = config.Bind("Valuables", "DeathAutoToTruck", false,
            "When you die, your death head's landing spot is overridden to the truck's safety " +
            "spawn point instead of where you actually died. Purely local (your own " +
            "PlayerDeathHead.triggeredPosition) - works for guest or host.");

        DeathAutoToExtraction = config.Bind("Valuables", "DeathAutoToExtraction", false,
            "Same as Death Auto To Truck, but targets the nearest extraction point instead. Only " +
            "one of these two should be on at a time - whichever runs its Trigger patch last wins.");

        TeammateDeathDropBead = config.Bind("Valuables", "TeammateDeathDropBead", false,
            "Whenever ANY player's death head triggers (including yours), spawns a small valuable " +
            "shard at their position via PhotonNetwork.InstantiateRoomObject - same guest-safe " +
            "spawn path as Spawn Valuable. Works for guest or host.");

        AutoReviveTeammates = config.Bind("Valuables", "AutoReviveTeammates", false,
            "Every couple seconds, calls Revive() on every dead teammate currently in the level. " +
            "Same wall as the manual Revive Target button above: real for host/singleplayer only, " +
            "a local-only illusion on your own screen as a guest.");

        UseLaser = config.Bind("Items", "UseLaser", false,
            "Adds a laser sight to guns while held.");

        MeleeVibrate = config.Bind("Items", "MeleeVibrate", false,
            "Host/singleplayer only: while a melee weapon is held, forces its swing hitbox to " +
            "stay permanently active and slashes its per-target hit cooldown, so it lands many " +
            "more hits per second than a real swing. Real damage is computed wherever the " +
            "weapon's swing physics actually run authoritatively (same as throw/grab strength), " +
            "so as a guest this has no effect on enemies or other players.");

        StealHeldItem = config.Bind("Items", "StealHeldItem", false,
            "Whenever another player is holding the item type picked below, it gets teleported " +
            "to you and your own grabber tries to auto-pick it up (PhysGrabber.OverrideGrab() - " +
            "confirmed public, the same method drones use to put something straight into a " +
            "player's hands). The yank rides the same Teleport()/SetPositionRPC loophole " +
            "PowerGrab uses, so it works for guest or host; the auto-pickup afterward is " +
            "best-effort. Keeps re-stealing it every time they pick it back up while this is on.");
        StealHeldItemName = config.Bind("Items", "StealHeldItemName", "None",
            "Item type to steal - pick from the list (built from whatever's currently in the " +
            "level) using the cycle button, or hit Refresh after entering a level to rescan.");
        SpawnValuableName = config.Bind("Items", "SpawnValuableName", "None",
            "Which valuable to spawn - pick from the list (same names/scope the game's own " +
            "\"/spawn valuable <name>\" debug command uses, searched across every level, not " +
            "just the current one) using the cycle button.");

        VoiceFilter = config.Bind("Voice", "VoiceFilter", "All",
            "Which players you hear, regardless of distance. Vanilla voice chat (confirmed in " +
            "the decompiled PlayerVoiceChat source) has no built-in dead/living separation - " +
            "it's purely positional audio - so this works by muting/unmuting each " +
            "PlayerVoiceChat's own AudioSource (mic) and TTS AudioSource locally on your " +
            "client. 'All' = normal vanilla hearing. 'Living' = mutes dead/spectating " +
            "players. 'Dead' = mutes everyone still alive, so you only hear ghosts. " +
            "Listen-only - what you transmit yourself is never affected, and nobody else's " +
            "hearing changes either.");

        GlitchyVoice = config.Bind("Voice", "GlitchyVoice", false,
            "Troll: chops your OWN outgoing voice transmission on and off on a fast timer " +
            "(Photon Voice Recorder.TransmitEnabled, re-forced in a Postfix after " +
            "PlayerVoiceChat.Update's own per-frame write), so everyone else hears you stuttering " +
            "and cutting in and out like a broken connection. Everything else about you stays " +
            "completely normal for them - position, model, animation are never touched, unlike " +
            "Invisible. Only ever forces transmission OFF, never ON, so it can't open your mic " +
            "when you're muted or not holding push-to-talk.");
        GlitchyVoiceInterval = config.Bind("Voice", "GlitchyVoiceInterval", 0.15f,
            "How long each on/off half of the chop lasts, in seconds - lower is a faster, harsher " +
            "stutter, higher sounds more like whole words dropping out.");

        ChipmunkVoices = config.Bind("Voice", "ChipmunkVoices", false,
            "Different direction from GlitchyVoice - this is about what YOU hear, not what others " +
            "hear from you. PlayerVoiceChat.OverridePitch(multiplier, ...) is confirmed public and " +
            "confirmed a PLAYBACK-side effect (audioSource.pitch) - your own PlayerVoiceChat." +
            "instance has that AudioSource forced silent (volume 0) since you never play your own " +
            "voice back to yourself, so calling this on your own instance does nothing. Calling it " +
            "on everyone ELSE's instance (RunManager.instance.voiceChats, same list VoiceFilter " +
            "already uses) re-pitches how their voice sounds through your speakers only - nothing " +
            "sent over the network, nobody else notices anything changed. Ticks continuously so it " +
            "re-applies to anyone who starts talking after the toggle went on.");
        ChipmunkPitch = config.Bind("Voice", "ChipmunkPitch", 1.6f,
            "Pitch multiplier ChipmunkVoices applies - above 1 is higher/faster (chipmunk), below " +
            "1 is lower/slower (demon voice). 1 = no change.");

        // These three are the only built-in debug fields confirmed guest-safe by reading
        // their call sites in the decompiled source - no IsMasterClient/IsMasterClientOrSingleplayer
        // gate anywhere near where they're read. Most of the game's other debug fields
        // (EnemyDirector.debugEasyGrab/debugSpawnClose/debugDespawnClose/debugNoSpawnedPause/
        // debugShortActionTimer/debugInvestigate) sit inside EnemyRigidbody/EnemyParent methods
        // that start with "if (!SemiFunc.IsMasterClientOrSingleplayer()) return;" - same
        // authority model as NoGrabMaxTime above, so a guest setting them does nothing. Left
        // out rather than included with a caveat, since the point of this tab is specifically
        // "works without hosting".
        AlwaysInteract = config.Bind("RepoDebug", "AlwaysInteract", false,
            "Built-in debug field (ToolController.DebugAlwaysInteract) - treats the interact " +
            "button as permanently held for whatever tool/interactable you're currently " +
            "aiming at. Purely local input simulation, no networking involved - works for guest or host.");

        SkipMoonWait = config.Bind("RepoDebug", "SkipMoonWait", false,
            "Built-in debug field (MoonUI.debugNoWait) - cuts the moon-selection screen's " +
            "forced wait from 1s to 0.1s. Local UI timer only - works for guest or host.");

        SkipResultsScreen = config.Bind("RepoDebug", "SkipResultsScreen", false,
            "Built-in debug field (ResultScreenUI.debugSkip) - finishes the end-of-run results " +
            "screen immediately instead of waiting for it to play out. Local UI flow control " +
            "only - works for guest or host.");

        DestroyAllValuables = config.Bind("Troll", "DestroyAllValuables", false,
            "Continuously destroys every valuable on the map, except $100 ones (the cheapest " +
            "tier - left alone on purpose). Host/singleplayer: destroys them outright via the " +
            "game's own DestroyObject(). Guest: DestroyObject() and BreakRPC are both " +
            "master-gated server-side with no client override, so this falls back to the same " +
            "SetPositionRPC trick PowerGrab uses - teleporting each valuable to a random nearby " +
            "point hard enough for the host's own physics to register a real impact and break " +
            "it there instead.");

        ShopDestroyer = config.Bind("Troll", "ShopDestroyer", false,
            "Shop Destroyer 3000 - separate from Destroy All Valuables above. Only runs while " +
            "you're in the shop level (SemiFunc.RunIsShop()). Scans for every ItemAttributes in " +
            "the shop (the same generic item component ItemStealHack scans for - covers guns, " +
            "melee weapons, health packs, batteries, valuables/crystals, anything spawned there), " +
            "not just valuables/carts/melee weapons. Same host/guest split as Destroy All " +
            "Valuables: DestroyObject() outright when hosting/solo, SetPositionRPC slam-teleport " +
            "as a guest.");

        LootMagnet = config.Bind("World", "LootMagnet", false,
            "Pulls every valuable in the level toward the nearest active extraction point instead " +
            "of destroying anything - nothing gets broken or removed. Rides " +
            "PhysGrabObject.Teleport() (SetPositionRPC to the master, applied with zero " +
            "validation, same loophole as PowerGrab/ItemSteal) so it works for guest or host.");

        CartPuller = config.Bind("World", "CartPuller", false,
            "Same idea as Loot Magnet, for carts - pulls every cart on the map toward the active " +
            "extraction point via the same fly-above-obstacles Teleport() approach. Works for " +
            "guest or host.");

        ValuableGodMode = config.Bind("World", "ValuableGodMode", false,
            "GameTools' 'Valuable God Mode' - stops every valuable in the level from breaking or " +
            "losing value on impact. Forces PhysGrabObjectImpactDetector.isIndestructible true on " +
            "every valuable each tick (confirmed in the decompiled source: Break() only marks " +
            "value lost when that flag AND destroyDisable are both false, and the game's own " +
            "impact-processing chain skips entirely while it's true) - the same field the game " +
            "already drives itself as a short per-object grace timer after spawning/throwing, so " +
            "this just coexists with that. Host/singleplayer only, same wall as most break/destroy " +
            "logic in this mod: a guest's own client never runs the collision handler that reads " +
            "this flag for someone else's object, so it has no effect there at all, not even a " +
            "local-only illusion.");

        AutoLoot = config.Bind("World", "AutoLoot", false,
            "Automatically picks up any loose item within ~2m instead of needing to aim and grab " +
            "yourself - only unheld items, never yanks something out of a teammate's hands. Rides " +
            "PhysGrabber.OverrideGrab(), purely local, works for guest or host.");

        ExtractionRadar = config.Bind("World", "ExtractionRadar", false,
            "Small on-screen readout of distance/direction to the nearest active extraction " +
            "point. Purely local UI, no networking.");

        AutoFarm = config.Bind("World", "AutoFarm", false,
            "Self-driving looter (like a Minecraft Baritone 'goto'): walks your actual character " +
            "over the level's real NavMesh to the nearest valuable, grabs it, carries it to the " +
            "active extraction point (auto-activating one if none is running), drops it, and " +
            "repeats - no teleporting, it genuinely walks there and can open doors/climb stairs " +
            "like a real player, including into rooms you haven't been to yet, since it already " +
            "knows where every valuable is. Takes over movement AND camera look while active (it " +
            "steers by turning to face where it's walking) - meant for actually stepping away, " +
            "not for running alongside manual control. If a target turns out unreachable (path " +
            "blocked, stuck for a few seconds) it gives up on that one for a while and moves on " +
            "to the next instead of grinding forever. Purely local movement input, works for " +
            "guest or host.");

        KnockdownAll = config.Bind("Troll", "KnockdownAll", false,
            "Alive: freezes you in the air (no gravity/velocity), forces you into the game's " +
            "own tumbling/ragdoll state, and smoothly homes in on the nearest other player " +
            "(via Rigidbody.MovePosition, not a raw teleport - direct transform writes were " +
            "getting snapped back by the physics engine resyncing the Transform from the " +
            "Rigidbody). Arriving tries to put THEM into real tumbling too and shove them for " +
            "real - host/singleplayer only, forcing someone else's tumble state needs master " +
            "authority, no way around that. As guest it falls back to disarming whatever " +
            "they're holding plus a hurt-flash on contact instead - the same hit a real " +
            "tumbling ragdoll gives someone it bumps into, just without the actual ragdoll on " +
            "them. Works for guest or host, just a stronger effect while hosting. " +
            "Dead/spectating: press Jump to fire one knockdown pulse at every living player on " +
            "the map directly - host/singleplayer only, since a guest has no body to touch " +
            "anyone with while dead and no authority to force someone else's tumble state " +
            "remotely.");

        KnockdownBanish = config.Bind("Troll", "KnockdownBanish", false,
            "Mode 2 of Knockdown All - alive only. Smoothly homes in on the nearest other " +
            "player the same way mode 1 does, freezes there for 2s once it arrives (same " +
            "disarm/shove/hurt-flash contact hit as mode 1), then grabs that player and " +
            "teleports them away from you and deep under the floor before homing in on " +
            "whoever's next-nearest. Rides PhysGrabObject.Teleport() the same way PowerGrab " +
            "does, but that teleport only actually sticks while the target is really tumbling - " +
            "otherwise the game resyncs their position back every frame - so the under-floor " +
            "banish itself is host/singleplayer only, same wall as the Kill option below. As " +
            "guest this still homes in and hits/disarms them on contact like mode 1, it just " +
            "never banishes anyone. Don't enable at the same time as Knockdown All, they'll " +
            "fight over your position/tumble state.");

        KnockdownBanishKill = config.Bind("Troll", "KnockdownBanishKill", false,
            "Makes Knockdown + Banish actually kill the target on top of teleporting them under " +
            "the floor, instead of just stranding them there. Real damage to someone else's " +
            "character needs master authority (PlayerHealth.HurtOtherRPC checks who sent the " +
            "RPC, not who called it) - host/singleplayer only, same wall as HighStrength/" +
            "MeleeVibrate. As a guest, the banish teleport still happens but the kill silently " +
            "does nothing.");

        AntiAimSpin = config.Bind("Troll", "AntiAimSpin", false,
            "Visual-only troll: everyone else's game renders your head/body spinning " +
            "continuously, by spoofing only the camera rotation your client broadcasts over " +
            "the network for the instant it's sent. Your own camera, aim, and view are never " +
            "touched - works for guest or host, since you're always the one broadcasting your " +
            "own camera data.");

        DinnerBone = config.Bind("Troll", "DinnerBone", false,
            "Visual-only troll: flips/tilts how everyone else sees your character by spoofing " +
            "the body rotation your client broadcasts over the network the same way. Your own " +
            "movement, collision, and camera stay completely normal locally - only what gets " +
            "sent to other players is tilted. Works for guest or host.");

        DinnerBoneAngle = config.Bind("Troll", "DinnerBoneAngle", 180f,
            "Roll angle Dinner Bone broadcasts, in degrees around your own forward axis " +
            "(0 = normal, 90 = sideways/leaning, 180 = fully upside down, Minecraft-style).");

        DinnerBoneShowSelf = config.Bind("Troll", "DinnerBoneShowSelf", false,
            "Also tilts your own visual mesh (PlayerAvatarVisuals.meshParent, confirmed public in " +
            "the decompiled source - a separate GameObject from the movement/camera transforms) so " +
            "you can preview what everyone else sees without needing a second person to check for " +
            "you. Needs Third Person on too, same as you'd need to look at your own model from " +
            "outside either way - technique confirmed from GameTools' own FlipShowToSelf, which " +
            "only ever applies when their equivalent of Third Person is also active.");

        FlipEnabled = config.Bind("Troll", "FlipEnabled", false,
            "Continuous spin, distinct from Dinner Bone's static tilt - same broadcast-rotation " +
            "trick (PlayerAvatar.OnPhotonSerializeView), but the angle keeps advancing over time " +
            "instead of holding one fixed offset, on whichever axis FlipAxis picks. Same as " +
            "Dinner Bone: only what other players' clients render for you changes, your own " +
            "movement/collision/camera never sees it. Don't run both at once - they fight over " +
            "the same broadcast rotation.");
        FlipAxis = config.Bind("Troll", "FlipAxis", "Roll",
            "Which axis Flip spins around - Roll (forward axis, upside-down-but-facing-same-way, " +
            "same look Dinner Bone's 180 does), Pitch (forward/backward somersault), or Yaw " +
            "(spin in place, same idea as Anti-Aim Spin but on the body broadcast instead of the " +
            "aim target). Cycle with the menu's button.");
        FlipSpeed = config.Bind("Troll", "FlipSpeed", 360f,
            "Flip spin speed, degrees/sec.");
        FlipShowSelf = config.Bind("Troll", "FlipShowSelf", false,
            "Same self-preview trick as Dinner Bone Show Self - spins your own visual mesh too, " +
            "needs Third Person on.");
        FlipOrbitRadius = config.Bind("Troll", "FlipOrbitRadius", 0f,
            "GameTools calls this 'spin radius': while Flip is running, also broadcasts your " +
            "position tracing a horizontal circle of this size around your real spot, synced to " +
            "the same spin phase as the rotation - so to everyone else you spin AND drift around " +
            "in a loop instead of spinning in place. 0 disables it (spin-in-place only). Same " +
            "broadcast-only trick as the rotation: PlayerAvatar.OnPhotonSerializeView sends " +
            "PlayerController.instance.transform.position as its own stream field right next to " +
            "rotation (confirmed in the decompiled source), so it backs up/restores around the " +
            "call exactly like FlipPatch already does for rotation - your own position, movement " +
            "and collision are never touched.");

        TPose = config.Bind("Troll", "TPose", false,
            "Visual-only troll, same broadcast-spoofing trick as Anti-Aim/Dinner Bone: zeroes " +
            "out the movement/animation flags (moving, sprinting, crouching, crawling, sliding, " +
            "velocity) your client broadcasts, for the instant they're sent. Everyone else's " +
            "Animator locally derives your pose from those flags, so with all of them false they " +
            "settle into the idle/rest pose - you'll look frozen standing still while your real " +
            "position keeps gliding across the map to them. Not a literal arms-out T-pose (no " +
            "synced field forces that specific pose), but the closest broadcastable approximation. " +
            "Your own movement and animation stay completely normal locally. Works for guest or host.");

        GhostCrawl = config.Bind("Troll", "GhostCrawl", false,
            "Opposite spirit of T-Pose - same broadcast-spoofing trick, but forces the Crawling " +
            "flag TRUE (and Crouching/Sprinting/Sliding false so they don't fight it on the " +
            "Animator) instead of everything false. Everyone else's Animator locally derives your " +
            "pose from those flags (confirmed in the decompiled PlayerAvatarVisuals.AnimationLogic " +
            "- Crawling+Moving drives the same 'Crawl Move' state the game's own crawl uses), so " +
            "to them you're endlessly crawling around the map - standing still crawls in place, " +
            "moving crawls at full speed, no slowdown, since your real movement is completely " +
            "untouched locally. Works for guest or host. Don't run alongside T-Pose - both mutate " +
            "the same broadcast flags, whichever Prefix runs last wins.");

        Invisible = config.Bind("Troll", "Invisible", false,
            "Same broadcast-position trick Flip Orbit Radius uses - spoofs the position your " +
            "client sends in PlayerAvatar.OnPhotonSerializeView, holding it at InvisibleHeight " +
            "(only Y changes, X/Z stay real) instead of a per-frame offset. Everyone else's screen " +
            "renders your character shot straight up into the sky instead of where you actually " +
            "are. Your own real position/movement/collision/camera never sees anything but the " +
            "restored real value - only changes what OTHER clients render. Works for guest or " +
            "host. Not independently verified whether this also affects enemy AI targeting on " +
            "whichever machine is master (their vision/chase logic may read the same broadcast " +
            "position) - could help or do nothing, untested either way. Voice stays audible: " +
            "PlayerVoiceChat is a child of PlayerAvatar, so this same spoofed position would also " +
            "relocate your voice for listeners while talking - the spoof is skipped for the " +
            "instant your mic is actually open (PlayerVoiceChat.instance.isTalking), broadcasting " +
            "your real position only while talking, sky position again the moment you stop.");
        InvisibleHeight = config.Bind("Troll", "InvisibleHeight", 5000f,
            "World-space Y coordinate Invisible broadcasts instead of your real height - high " +
            "enough to clear any level's real ceiling by a wide margin.");
        InvisibleRevealWhileTalking = config.Bind("Troll", "InvisibleRevealWhileTalking", false,
            "Reported live 2026-09-11: with this on, other players could see you at your real " +
            "position basically the whole time you were talking with friends - broadcasting the " +
            "real position while talking (so voice doesn't sound distant/muted) and staying " +
            "genuinely hidden are in direct tension, same broadcast field, can't have both at " +
            "once. Off by default - Invisible now means invisible no matter how much you talk. " +
            "Turn this on only if you'd rather sound normal while talking and accept being visible " +
            "for those moments (plus a 1.5s grace tail after each utterance).");

        GlitchyFlashlight = config.Bind("Troll", "GlitchyFlashlight", false,
            "Same broadcast-spoofing trick as everything else here, on a field nothing in this mod " +
            "touched yet: PlayerAvatar.OnPhotonSerializeView also sends flashlightLightAim." +
            "clientAimPoint (confirmed public in the decompiled source) whenever it moved more than " +
            "0.2m since the last send. Confirmed in FlashlightLightAim.cs: for every OTHER client " +
            "watching you, that received point is the ONLY input driving where your flashlight " +
            "fixture visually points (Quaternion.LookRotation toward it, clamped to 45 degrees off " +
            "your own forward - so whatever we send, it can only ever swing within that cone, never " +
            "point somewhere physically impossible). Sending a point that circles around the real " +
            "one instead of the real one itself makes your beam sweep and jitter unpredictably to " +
            "everyone else while you look completely normal, since this path is explicitly " +
            "!isLocal-only - your own flashlight direction is untouched. Works for guest or host, " +
            "no gameplay side effect at all (purely what a light fixture points at).");
        GlitchyFlashlightRadius = config.Bind("Troll", "GlitchyFlashlightRadius", 3f,
            "How far the fake aim point wanders from the real one, in world units - GlitchyFlashlight's " +
            "own sweep radius, not related to the light's actual range/intensity.");

        GhostRider = config.Bind("Troll", "GhostRider", false,
            "Same broadcast-spoofing trick, on a field nothing else here touches: PlayerAvatar." +
            "OnPhotonSerializeView also sends PlayerController.CollisionGrounded.physRiding/" +
            "physRidingID/physRidingPosition (all public, confirmed in the decompiled " +
            "PlayerCollisionGrounded) - the game's own 'am I standing on a physics prop' state. " +
            "Confirmed in PlayerAvatarVisuals.cs: on every OTHER client, receiving physRiding=true " +
            "spawns a point under the referenced object's transform and lerps YOUR visual body " +
            "toward it instead of your real broadcast position - literally how riding a cart/" +
            "elevator already looks in vanilla play. Spoofing it points that same mechanism at the " +
            "nearest networked PhysGrabObject (rescanned every 1.5s, see GhostRiderHack.cs) instead " +
            "of whatever you're actually standing on, so to everyone else you look glued to and " +
            "riding around on some random valuable/prop, tracking it wherever it goes/gets thrown - " +
            "your own real position and movement are completely untouched. If no PhysGrabObject " +
            "exists nearby yet this session, this is a no-op until one does. Works for guest or host.");

        ChatSpam = config.Bind("Troll", "ChatSpam", false,
            "Continuously sends phrases from the list below into the real in-game chat, one every " +
            "ChatSpamInterval seconds - rides ChatManager.instance.ForceSendMessage, the same " +
            "public method vanilla chat itself calls once you hit Enter. The game's own chat has " +
            "a built-in 1-message-per-second guard (ChatManager's private spamTimer) that no " +
            "client-side call can bypass, so that's the real floor no matter how low the interval " +
            "below is set. Works for guest or host - it's just sending a normal chat message.");

        ChatSpamText = config.Bind("Troll", "ChatSpamText", "привет|как дела|гг",
            "Phrases to spam, separated by | - cycled in order, looping back to the start.");

        ChatSpamInterval = config.Bind("Troll", "ChatSpamInterval", 2f,
            "Seconds between send attempts (clamped to a 1s minimum - see ChatSpam above for why " +
            "going lower wouldn't help).");
        ChatSpamRichText = config.Bind("Troll", "ChatSpamRichText", false,
            "Wraps the sent phrase in <color=..><size=..> tags before it goes to ForceSendMessage. " +
            "The chat message string goes straight into a Text.text with no stripping anywhere in " +
            "ChatManager.cs (confirmed in the decompiled source) - the game's own HUD elements " +
            "(HealthUI/EnergyUI/GoalUI/HaulUI) freely use the exact same <color>/<size>/<b> tags, " +
            "so this rides Unity's own Rich Text support on the Text component other players' " +
            "chat renders with. Not independently confirmed for the chat window's own Text " +
            "component specifically (that's an Inspector-set toggle, invisible from source), but " +
            "nothing in code strips it either way.");
        ChatSpamColor = config.Bind("Troll", "ChatSpamColor", "#FF3B30",
            "Chat spam text color when Rich Text is on. Cycle with the menu's button.");
        ChatSpamFontSize = config.Bind("Troll", "ChatSpamFontSize", 30,
            "Chat spam text size (pixels, same units the game's own HaulUI/GoalUI use for their " +
            "own <size=..> tags) when Rich Text is on.");

        HudEnabled = config.Bind("Hud", "HudEnabled", true,
            "Master switch for the always-on gameplay HUD (separate from both menus - shows " +
            "whether or not F6/RightShift is open).");
        HudEditMode = config.Bind("Hud", "HudEditMode", false,
            "Shows drag handles, a remove button and a recolor button on every enabled HUD widget, " +
            "plus a palette strip to add back any removed one. Toggle from the HUD tab in the menu.");
        HudAutoStack = config.Bind("Hud", "HudAutoStack", true,
            "Stacks the enabled HUD widgets into one tight column under the first one, keeping the "
            + "spacing constant as cards grow and shrink with their content. Off lets you place each "
            + "card independently by dragging in edit mode.");
        HudMediaPlayerUseSpotify = config.Bind("Hud", "HudMediaPlayerUseSpotify", true,
            "Reads the real currently-playing track from Spotify's desktop client window title " +
            "(Win32.GetSpotifyWindowTitle - plain user32.dll P/Invoke, not the WinRT now-playing " +
            "API, which doesn't run reliably under Unity's Mono runtime). Off falls back to the " +
            "manual decorative text below.");
        HudMediaPlayerTitle = config.Bind("Hud", "HudMediaPlayerTitle", "Даже моя бэйби не знает",
            "Manual/decorative \"now playing\" text, used when Spotify integration above is off " +
            "or Spotify isn't running - no real audio hook behind it in that case, just a static " +
            "prop like the game's own HUD chrome.");
        HudPlayerStats = BindHudWidget(config, "HudPlayerStats", "Игрок", true, 20f, 20f, "#0A84FF");
        HudTargetInfo = BindHudWidget(config, "HudTargetInfo", "Цель", true, 20f, 122f, "#FF3B30");
        HudMediaPlayer = BindHudWidget(config, "HudMediaPlayer", "Плеер", true, 20f, 244f, "#34C759");
        HudKeybinds = BindHudWidget(config, "HudKeybinds", "Бинды", false, 20f, 372f, "#FF9500");

        FakeSoundTargetName = config.Bind("Troll", "FakeSoundTargetName", "None",
            "Target player, shared by Scare Sound, Cage and Yeet below - pick from the list " +
            "using the cycle button, or hit Refresh to rescan who's in the lobby. Scare Sound: " +
            "borrows a scary sound from whatever enemy's currently spawned and plays it right at " +
            "that player's position - purely local audio, only you hear it. Cage/Yeet: works at " +
            "any range, no need to be near them - both ride KnockdownHack's TumbleImpactRPC " +
            "trick to put the target into real tumble state first (that RPC has no proximity " +
            "check, and every client including the real master runs it, so the master's own " +
            "copy applies the real force regardless of who sent it), then move their body via " +
            "PhysGrabObject.Teleport() same as PowerGrab/Banish. Works for guest or host.");

        PlayerCage = config.Bind("Troll", "PlayerCage", false,
            "Continuously teleports the target player (picked above) to a fixed point far " +
            "under the map, re-triggering tumble state if it ever lapses - holds them there " +
            "until turned off or the target changes. Works for guest or host.");

        PlayerCageAll = config.Bind("Troll", "PlayerCageAll", false,
            "Same as Player Cage, applied to every other player in the lobby simultaneously - " +
            "each gets their own spread-out point under the map so they don't pile on each " +
            "other. Locks down the whole room until turned off. Works for guest or host.");

        FreezeAll = config.Bind("Troll", "FreezeAll", false,
            "Pins every other player exactly where they already are - not under the map, not " +
            "anywhere else, just frozen in place - for as long as this is on. Same " +
            "TriggerImpact-then-Teleport mechanic as Player Cage, just targeting each player's " +
            "own current position instead of a shared point. Releases on its own once turned " +
            "off (tumbling just lapses). Works for guest or host.");

        MassDisarm = config.Bind("Raid", "MassDisarm", false,
            "Continuously rips whatever anyone's currently holding out of their hands, every " +
            "tick - nobody keeps a tool or weapon the whole time this is on. Works for guest or host.");

        YeetStorm = config.Bind("Raid", "YeetStorm", false,
            "Automatically picks a random other player every few seconds and yeets them into " +
            "the air - no target picker needed, just chaos on a timer. Works for guest or host.");

        ExtractionCamperPunish = config.Bind("Raid", "ExtractionCamperPunish", false,
            "Watches every active extraction point - anyone standing within ~8m gets caged in " +
            "place until they back off (or this is turned off). Punishes camping the exit " +
            "instead of playing. Works for guest or host.");

        ShopExecution = config.Bind("Raid", "ShopExecution", false,
            "Shop only. Kills everyone in the lobby one at a time using the shop keeper - " +
            "frames the first player, waits until they actually die, then moves on to the next. " +
            "Sequential on purpose: the keeper drops every new request while it's already busy " +
            "with someone, so framing everyone at once only ever punishes one of them. Skips a " +
            "target that survives 60s (out of the keeper's reach) and turns itself off when the " +
            "list is empty. Needs the keeper awake. Works for guest or host.");

        AutoShopExecute = config.Bind("Raid", "AutoShopExecute", false,
            "Shop only. Keeps every other player's shop-keeper ruckus score pinned above the " +
            "execution threshold via PlayerDamagingPlayerRPC, so anyone who survives gets picked " +
            "up again a few seconds later. Works for guest or host.");

        CarryThrustStorm = config.Bind("Raid", "CarryThrustStorm", false,
            "Shop only. Continuously reports fake carry-thrusts between players, keeping the " +
            "keeper permanently annoyed at the whole lobby. Separate counter from the damage " +
            "route, so it stacks with it. Works for guest or host.");

        GrabLockTarget = config.Bind("Raid", "GrabLockTarget", false,
            "Keeps the picked target unable to pick anything up at all - re-sends the game's own " +
            "grab-disable every few seconds. Works for guest or host.");

        GrabLockAll = config.Bind("Raid", "GrabLockAll", false,
            "Same, applied to the whole lobby: nobody can hold anything while this is on. " +
            "Works for guest or host.");

        UnequipStorm = config.Bind("Raid", "UnequipStorm", false,
            "Continuously unequips everyone's inventory slots - nothing stays equipped. " +
            "Works for guest or host.");

        WeaponDrainAll = config.Bind("Raid", "WeaponDrainAll", false,
            "Continuously burns the durability/battery of every melee weapon in the level, " +
            "without touching their owners. Works for guest or host.");

        DisarmOnGrab = config.Bind("Raid", "DisarmOnGrab", false,
            "Reactive: the moment anyone picks anything up, it's knocked straight back out of " +
            "their hands. Quieter than a blanket grab lock. Works for guest or host.");

        ExtractionSnipe = config.Bind("Raid", "ExtractionSnipe", false,
            "Repeatedly requests activation of the extraction point FURTHEST from you, so the " +
            "round commits to the worst one before anyone can choose (the host's own handler " +
            "locks all the others once one activates). Works for guest or host.");

        AutoRevealValuables = config.Bind("World", "AutoRevealValuables", false,
            "Marks every valuable as discovered on everyone's map, repeatedly as the level " +
            "streams more in. Works for guest or host.");

        ProjectileStrikeStorm = config.Bind("Raid", "ProjectileStrikeStorm", false,
            "Repeatedly detonates any live slow projectile in the level on top of each player. " +
            "Needs a projectile to actually exist. Works for guest or host.");

        VehicleWobbleStorm = config.Bind("Raid", "VehicleWobbleStorm", false,
            "Continuously shakes every vehicle's BODY MESH and plays its impact sound. Cosmetic " +
            "only - confirmed in ItemVehicle.ApplyWobbleTrigger, the wobble springs drive " +
            "meshTransform, never the rigidbody, and both offsets are clamped (0.3m / 40 degrees), " +
            "so it does not affect handling or throw anyone off. Purely an annoyance. " +
            "Works for guest or host.");

        ArenaSabotage = config.Bind("Raid", "ArenaSabotage", false,
            "Arena race only - continuously drops every other player's kart off the track via " +
            "the same Teleport() loophole carts/valuables use (ItemVehicle has its own " +
            "PhysGrabObject). Never touches your own vehicle. Works for guest or host.");

        ArenaWeaponsSabotage = config.Bind("Raid", "ArenaWeaponsSabotage", false,
            "The falling-floor weapons Arena mode only (Arena.instance, not the kart race) - " +
            "every ~3s, knocks every other player into real tumble state (same TriggerImpact " +
            "trick Cage/Yeet use) and drops them into the pit below the platform instead of " +
            "letting them fight it out. Never touches you. Works for guest or host.");

        ArenaLegitSabotage = config.Bind("Raid", "ArenaLegitSabotage", false,
            "Arena race, subtler version of Auto-Win - never touches an opponent's kart directly. " +
            "Every ~2.5s, drops a real prop (any PhysGrabObject found near the track) a few " +
            "meters ahead of each opponent along their own heading, so they plow into \"something " +
            "that was just there\" instead of visibly flying off the track. Needs movable props " +
            "to actually exist near the track to work. Works for guest or host.");

        SwarmStorm = config.Bind("Raid", "SwarmStorm", false,
            "Every ~8s, finds whichever cluster of players is currently biggest and sends the " +
            "nearest available enemy straight there via Teleport() - punishes grouping up " +
            "instead of splitting off. Ignores a lone player. Works for guest or host.");

        LockAllDoors = config.Bind("Raid", "LockAllDoors", false,
            "Jams every door/cabinet/lid (PhysGrabHinge) on the level exactly where it was the " +
            "moment this turned on - via the same Teleport(position, rotation) loophole every " +
            "other grabbable prop uses. Turn on while doors are closed to actually lock people " +
            "in/out; turning it on mid-open just jams it open instead. Works for guest or host.");

        TrapRoom = config.Bind("Raid", "TrapRoom", false,
            "Swarm the Group's targeting, plus rips every door within ~15m of that cluster off " +
            "wherever it's hinged and arranges them in a ring facing inward around the group - " +
            "an improvised wall built from the doors themselves, held there every tick against " +
            "their own hinge physics (not a guaranteed permanent barrier). Works for guest or host.");

        EnemyDropKill = config.Bind("Raid", "EnemyDropKill", false,
            "Every ~4s, teleports every spawned enemy straight up and lets gravity do the rest - " +
            "the real fall speed on landing triggers EnemyHealth's own impact-damage system for " +
            "real (same master-side-execution trick as everything else here). Not a guaranteed " +
            "one-shot kill, depends on the enemy type's own health - keeps repeating until they " +
            "die. Works for guest or host.");

        FreezeEnemies = config.Bind("Raid", "FreezeEnemies", false,
            "Freezes every enemy in place - a genuine mechanical freeze (EnemyRigidbody zeroes " +
            "velocity/angular velocity and skips movement/chase logic entirely while " +
            "Enemy.FreezeTimer is positive, confirmed in the decompiled source), not just a " +
            "visual. Found by auditing the game's own RPC surface for the same shape ReviveRPC " +
            "has: Enemy.FreezeRPC is gated only by SemiFunc.MasterOnlyRPC(_info), no extra sender " +
            "check, so it's already exploitable through the same MasterRpcSelfBypassPatch this mod " +
            "installs for Revive Self - no separate patch needed. Works for guest or host, " +
            "re-applied every second so it never actually lapses.");
        FreezeEnemiesRange = config.Bind("Raid", "FreezeEnemiesRange", 0f,
            "Only freezes enemies within this many meters of you - 0 means no limit, freeze " +
            "everything spawned in the level.");

        StealAllHeldItems = config.Bind("Items", "StealAllHeldItems", false,
            "\"Global Item Yoink\" - ignores the name filter below and yanks whatever every " +
            "other player is currently holding, every tick, straight to you. Same " +
            "Teleport()/OverrideGrab loophole as Steal Held Item, just unfiltered. Works for " +
            "guest or host.");

        SendTestPacket = config.Bind("Misc", "SendTestPacket", false,
            "Checkbox wired to PacketTestHack.OnToggled(): turning it on starts a bounded burst " +
            "(a fixed packet count at a capped rate, drained by PacketTestHack.Apply() on the " +
            "per-tick loop) of empty custom Photon room events (RaiseEvent) to the master " +
            "client, rather than relying on an existing PunRPC method on some game prefab, so " +
            "it works standalone. Turning it off early cancels whatever's left of the burst. " +
            "Scaffold only for now - extend PacketTestHack's payload/handler once there's a " +
            "real effect (e.g. movement) to drive from it.");
        PacketTestIntervalMs = config.Bind("Misc", "PacketTestIntervalMs", 500,
            "How many milliseconds PacketTestHack.Apply() waits between sends while the toggle " +
            "above is on (was a hardcoded 500ms/~2 per second before this).");
        EventLogging = config.Bind("Misc", "EventLogging", true,
            "Writes structured, per-category event logs (what spawned where, who got teleported " +
            "from where to where, what was taken out of whose hands) to RepoCheatMenu_Logs/" +
            "<session>/<category>.log next to this plugin's DLL - separate from BepInEx's single " +
            "shared LogOutput.log. Buffered and flushed every couple of seconds, not per event.");
        DebugBridgeEnabled = config.Bind("Misc", "DebugBridgeEnabled", false,
            "Lets a local development session run C# through DebugBridge.cs (RepoCheatMenu_Bridge/" +
            "command.txt -> result.txt, next to this plugin's DLL) for autonomous testing - same " +
            "ScriptEngine the Scripts tab uses, full access to this mod and the game. Off by " +
            "default: this is arbitrary code execution, only turn it on for a session you're " +
            "actively directing.");

        NetworkDiagnostics = config.Bind("Misc", "NetworkDiagnostics", false,
            "Read-only network readout (NetworkDiagnostics.Apply on the per-tick loop): ping, " +
            "cloud region, players in room and real incoming/outgoing traffic taken from the " +
            "Photon peer's own traffic counters (PhotonNetwork.NetworkStatisticsEnabled). " +
            "Sends nothing itself - it only samples counters the client already keeps.");

        AntiCrash = config.Bind("AntiCrash", "AntiCrash", true,
            "Защита от сетевого краша (по умолчанию ВКЛ - это не чит, а щит). Гасит исключения " +
            "из входящих RPC и событий Photon, чтобы кривой пакет ронял сам себя, а не весь " +
            "клиент, режет гигантские строки/массивы в аргументах RPC и ограничивает частоту " +
            "RPC и сетевых Instantiate по каждому отправителю. Клиент может лагать под флудом, " +
            "но остаётся в игре.");

        AntiCrashQuarantine = config.Bind("AntiCrash", "AntiCrashQuarantine", false,
            "Карантин: игрок, который раз за разом пробивает лимиты, на несколько секунд " +
            "полностью игнорируется (все его RPC/Instantiate отбрасываются). Максимальная " +
            "защита, но повышает шанс временного рассинхрона с этим игроком - поэтому выключено " +
            "по умолчанию.");

        AntiCrashMaxRpcsPerSecond = config.Bind("AntiCrash", "AntiCrashMaxRpcsPerSecond", 200,
            "Сколько входящих RPC в секунду от ОДНОГО игрока пропускать до отбрасывания лишних. " +
            "Обычная игра в этот потолок не упирается.");

        AntiCrashMaxInstantiatesPerSecond = config.Bind("AntiCrash", "AntiCrashMaxInstantiatesPerSecond", 60,
            "Сколько сетевых Instantiate (спавн объектов) в секунду от одного игрока пропускать. " +
            "Тысячи спавнов в секунду - типичная краш-атака.");

        AntiCrashMaxArgStringLength = config.Bind("AntiCrash", "AntiCrashMaxArgStringLength", 2000,
            "Максимальная длина строки в аргументах входящего RPC. Более длинные RPC " +
            "отбрасываются целиком - защита от 'мегабайтной строки', убивающей текст-меш Unity.");

        AntiCrashQuarantineSeconds = config.Bind("AntiCrash", "AntiCrashQuarantineSeconds", 5,
            "На сколько секунд отправляется в карантин игрок-нарушитель (если карантин включён).");

        MultiRoomExtraCount = config.Bind("Misc", "MultiRoomExtraCount", 2,
            "Sibling Rooms: how many EXTRA independent rooms to create alongside the one you're " +
            "already hosting (so 2 here means 3 real rooms total, including your own). Each is " +
            "a genuinely separate, independently-joinable room - not the same room re-listed.");

        MultiRoomMaxPlayers = config.Bind("Misc", "MultiRoomMaxPlayers", 40,
            "Sibling Rooms: max players for each of the extra rooms created above. Vanilla cap " +
            "is 6 - going well past that (30-50-ish) has never been tested for how the game's " +
            "own physics/enemy/level sync holds up with that many people actually in one room.");

        RegisterKeybinds(config);
    }

    // Every bindable toggle/action shown on the Keybinds tab. Bind itself (which key/mouse
    // button, if any) is a separate per-entry ConfigEntry<int> stored under the "Keybinds"
    // section - see KeybindManager.
    private static void RegisterKeybinds(ConfigFile config)
    {
        KeybindManager.RegisterToggle(config, "GodMode", GodMode);
        KeybindManager.RegisterToggle(config, "HighStrength", HighStrength);
        KeybindManager.RegisterToggle(config, "StrengthOverrideMode", StrengthOverrideMode);
        KeybindManager.RegisterToggle(config, "UnlimitedGrabRange", UnlimitedGrabRange);
        KeybindManager.RegisterToggle(config, "GrabThroughWalls", GrabThroughWalls);
        KeybindManager.RegisterToggle(config, "NoOvercharge", NoOvercharge);
        KeybindManager.RegisterToggle(config, "InfiniteBatteries", InfiniteBatteries);
        KeybindManager.RegisterToggle(config, "PowerGrab", PowerGrab);
        KeybindManager.RegisterToggle(config, "GentleDrop", GentleDrop);

        KeybindManager.RegisterToggle(config, "InfiniteStamina", InfiniteStamina);
        KeybindManager.RegisterToggle(config, "InfiniteJumps", InfiniteJumps);
        KeybindManager.RegisterToggle(config, "NoKnockdown", NoKnockdown);
        KeybindManager.RegisterToggle(config, "NoFallDamage", NoFallDamage);
        KeybindManager.RegisterToggle(config, "AntiKnockback", AntiKnockback);
        KeybindManager.RegisterToggle(config, "Strafe", Strafe);
        KeybindManager.RegisterToggle(config, "Fly", Fly);
        KeybindManager.RegisterToggle(config, "Noclip", Noclip);
        KeybindManager.RegisterToggle(config, "FlySprintBoost", FlySprintBoost);
        KeybindManager.RegisterToggle(config, "DeadFly", DeadFly);

        KeybindManager.RegisterToggle(config, "EspPlayers", EspPlayers);
        KeybindManager.RegisterToggle(config, "EspEnemies", EspEnemies);
        KeybindManager.RegisterToggle(config, "EspValuables", EspValuables);
        KeybindManager.RegisterToggle(config, "EspExtractionPoints", EspExtractionPoints);
        KeybindManager.RegisterToggle(config, "EspLoadout", EspLoadout);
        KeybindManager.RegisterToggle(config, "AutoActivateExtraction", AutoActivateExtraction);
        KeybindManager.RegisterToggle(config, "EspShaderMode", EspShaderMode);
        KeybindManager.RegisterToggle(config, "EspRainbow", EspRainbow);
        KeybindManager.RegisterToggle(config, "ForceWings", ForceWings);
        KeybindManager.RegisterToggle(config, "WingsPink", WingsPink);
        KeybindManager.RegisterToggle(config, "EyeColor", EyeColor);
        KeybindManager.RegisterToggle(config, "MimicHostName", MimicHostName);
        KeybindManager.RegisterToggle(config, "MimicHostSkin", MimicHostSkin);
        KeybindManager.RegisterToggle(config, "BigPupils", BigPupils);

        KeybindManager.RegisterToggle(config, "BetterVision", BetterVision);
        KeybindManager.RegisterToggle(config, "ThirdPerson", ThirdPerson);
        KeybindManager.RegisterToggle(config, "FlashInCrouch", FlashInCrouch);
        KeybindManager.RegisterToggle(config, "MaxHeadBattery", MaxHeadBattery);
        KeybindManager.RegisterToggle(config, "KolobokMode", KolobokMode);

        KeybindManager.RegisterToggle(config, "Unbreakable", Unbreakable);
        KeybindManager.RegisterToggle(config, "UnbreakableDrone", UnbreakableDrone);
        KeybindManager.RegisterToggle(config, "UnbreakableOrb", UnbreakableOrb);
        KeybindManager.RegisterToggle(config, "UnbreakableCart", UnbreakableCart);
        KeybindManager.RegisterToggle(config, "UnbreakableItemUpgrade", UnbreakableItemUpgrade);
        KeybindManager.RegisterToggle(config, "UnbreakablePlayerUpgrade", UnbreakablePlayerUpgrade);
        KeybindManager.RegisterToggle(config, "UnbreakablePowerCrystal", UnbreakablePowerCrystal);
        KeybindManager.RegisterToggle(config, "UnbreakableGrenade", UnbreakableGrenade);
        KeybindManager.RegisterToggle(config, "UnbreakableMelee", UnbreakableMelee);
        KeybindManager.RegisterToggle(config, "UnbreakableHealthPack", UnbreakableHealthPack);
        KeybindManager.RegisterToggle(config, "UnbreakableGun", UnbreakableGun);
        KeybindManager.RegisterToggle(config, "UnbreakableTracker", UnbreakableTracker);
        KeybindManager.RegisterToggle(config, "UnbreakableMine", UnbreakableMine);
        KeybindManager.RegisterToggle(config, "UnbreakablePocketCart", UnbreakablePocketCart);
        KeybindManager.RegisterToggle(config, "UnbreakableTool", UnbreakableTool);
        KeybindManager.RegisterToggle(config, "UnbreakableVehicle", UnbreakableVehicle);
        KeybindManager.RegisterToggle(config, "UnbreakableLauncher", UnbreakableLauncher);
        KeybindManager.RegisterToggle(config, "FreeQuota", FreeQuota);
        KeybindManager.RegisterToggle(config, "QuotaZeroRace", QuotaZeroRace);
        KeybindManager.RegisterToggle(config, "AutoSkipToShop", AutoSkipToShop);
        KeybindManager.RegisterToggle(config, "UnlimitedRerolls", UnlimitedRerolls);
        KeybindManager.RegisterToggle(config, "CursedHands", CursedHands);
        KeybindManager.RegisterToggle(config, "SeatShuffle", SeatShuffle);
        KeybindManager.RegisterToggle(config, "MonsterBait", MonsterBait);
        KeybindManager.RegisterToggle(config, "RemovePillarLimit", RemovePillarLimit);
        KeybindManager.RegisterToggle(config, "HealSpam", HealSpam);
        KeybindManager.RegisterToggle(config, "CartRam", CartRam);
        KeybindManager.RegisterToggle(config, "MasterRpcSelfBypass", MasterRpcSelfBypass);
        KeybindManager.RegisterToggle(config, "InfiniteChargingStation", InfiniteChargingStation);
        KeybindManager.RegisterToggle(config, "ChargingStationHeal", ChargingStationHeal);
        KeybindManager.RegisterToggle(config, "DeathAutoToTruck", DeathAutoToTruck);
        KeybindManager.RegisterToggle(config, "DeathAutoToExtraction", DeathAutoToExtraction);
        KeybindManager.RegisterToggle(config, "TeammateDeathDropBead", TeammateDeathDropBead);
        KeybindManager.RegisterToggle(config, "AutoReviveTeammates", AutoReviveTeammates);
        KeybindManager.RegisterToggle(config, "FlashlightColorEnabled", FlashlightColorEnabled);
        KeybindManager.RegisterToggle(config, "FlashlightColorRainbow", FlashlightColorRainbow);
        KeybindManager.RegisterToggle(config, "UseLaser", UseLaser);
        KeybindManager.RegisterToggle(config, "MeleeVibrate", MeleeVibrate);

        KeybindManager.RegisterToggle(config, "AlwaysInteract", AlwaysInteract);
        KeybindManager.RegisterToggle(config, "SkipMoonWait", SkipMoonWait);
        KeybindManager.RegisterToggle(config, "SkipResultsScreen", SkipResultsScreen);

        KeybindManager.RegisterToggle(config, "DestroyAllValuables", DestroyAllValuables);
        KeybindManager.RegisterToggle(config, "ShopDestroyer", ShopDestroyer);
        KeybindManager.RegisterToggle(config, "LootMagnet", LootMagnet);
        KeybindManager.RegisterToggle(config, "ValuableGodMode", ValuableGodMode);
        KeybindManager.RegisterToggle(config, "CartPuller", CartPuller);
        KeybindManager.RegisterToggle(config, "AutoLoot", AutoLoot);
        KeybindManager.RegisterToggle(config, "AutoFarm", AutoFarm);
        KeybindManager.RegisterToggle(config, "ExtractionRadar", ExtractionRadar);
        KeybindManager.RegisterToggle(config, "KnockdownAll", KnockdownAll);
        KeybindManager.RegisterToggle(config, "KnockdownBanish", KnockdownBanish);
        KeybindManager.RegisterToggle(config, "KnockdownBanishKill", KnockdownBanishKill);
        KeybindManager.RegisterToggle(config, "PillarDropRemoveLimit", PillarDropRemoveLimit);
        KeybindManager.RegisterToggle(config, "AntiAimSpin", AntiAimSpin);
        KeybindManager.RegisterToggle(config, "DinnerBone", DinnerBone);
        KeybindManager.RegisterToggle(config, "DinnerBoneShowSelf", DinnerBoneShowSelf);
        KeybindManager.RegisterToggle(config, "FlipEnabled", FlipEnabled);
        KeybindManager.RegisterToggle(config, "FlipShowSelf", FlipShowSelf);
        KeybindManager.RegisterToggle(config, "TPose", TPose);
        KeybindManager.RegisterToggle(config, "GhostCrawl", GhostCrawl);
        KeybindManager.RegisterToggle(config, "Invisible", Invisible);
        KeybindManager.RegisterToggle(config, "InvisibleRevealWhileTalking", InvisibleRevealWhileTalking);
        KeybindManager.RegisterToggle(config, "GlitchyFlashlight", GlitchyFlashlight);
        KeybindManager.RegisterToggle(config, "GhostRider", GhostRider);
        KeybindManager.RegisterToggle(config, "GlitchyVoice", GlitchyVoice);
        KeybindManager.RegisterToggle(config, "ChipmunkVoices", ChipmunkVoices);
        KeybindManager.RegisterToggle(config, "ChatSpam", ChatSpam);
        KeybindManager.RegisterToggle(config, "ChatSpamRichText", ChatSpamRichText);
        KeybindManager.RegisterToggle(config, "PlayerCage", PlayerCage);
        KeybindManager.RegisterToggle(config, "PlayerCageAll", PlayerCageAll);
        KeybindManager.RegisterToggle(config, "FreezeAll", FreezeAll);
        KeybindManager.RegisterToggle(config, "MassDisarm", MassDisarm);
        KeybindManager.RegisterToggle(config, "YeetStorm", YeetStorm);
        KeybindManager.RegisterToggle(config, "ExtractionCamperPunish", ExtractionCamperPunish);
        KeybindManager.RegisterToggle(config, "ShopExecution", ShopExecution);
        KeybindManager.RegisterToggle(config, "AutoShopExecute", AutoShopExecute);
        KeybindManager.RegisterToggle(config, "CarryThrustStorm", CarryThrustStorm);
        KeybindManager.RegisterToggle(config, "GrabLockTarget", GrabLockTarget);
        KeybindManager.RegisterToggle(config, "GrabLockAll", GrabLockAll);
        KeybindManager.RegisterToggle(config, "UnequipStorm", UnequipStorm);
        KeybindManager.RegisterToggle(config, "WeaponDrainAll", WeaponDrainAll);
        KeybindManager.RegisterToggle(config, "DisarmOnGrab", DisarmOnGrab);
        KeybindManager.RegisterToggle(config, "ExtractionSnipe", ExtractionSnipe);
        KeybindManager.RegisterToggle(config, "AutoRevealValuables", AutoRevealValuables);
        KeybindManager.RegisterToggle(config, "ProjectileStrikeStorm", ProjectileStrikeStorm);
        KeybindManager.RegisterToggle(config, "VehicleWobbleStorm", VehicleWobbleStorm);

        KeybindManager.RegisterAction(config, "InstantExecute", RuckusFrameHack.InstantExecute);
        KeybindManager.RegisterAction(config, "InstantExecuteAll", RuckusFrameHack.InstantExecuteAll);
        KeybindManager.RegisterAction(config, "FrameForAttack", RuckusFrameHack.FrameForAttack);
        KeybindManager.RegisterAction(config, "FrameEveryoneCrossfire", RuckusFrameHack.FrameEveryoneCrossfire);
        KeybindManager.RegisterAction(config, "FrameAsKiller", RuckusFrameHack.FrameAsKiller);
        KeybindManager.RegisterAction(config, "ScoreBomb", RuckusFrameHack.ScoreBomb);
        KeybindManager.RegisterAction(config, "CarryThrustFrame", RuckusFrameHack.CarryThrustFrame);
        KeybindManager.RegisterAction(config, "DisarmTarget", GrabDenialHack.DisarmTarget);
        KeybindManager.RegisterAction(config, "DisarmAll", GrabDenialHack.DisarmAll);
        KeybindManager.RegisterAction(config, "ForceUnequipTarget", GrabDenialHack.ForceUnequipTarget);
        KeybindManager.RegisterAction(config, "ForceUnequipAll", GrabDenialHack.ForceUnequipAll);
        KeybindManager.RegisterAction(config, "ForceEquipOnTarget", GrabDenialHack.ForceEquipOnTarget);
        KeybindManager.RegisterAction(config, "ForceEquipJunkAll", GrabDenialHack.ForceEquipJunkAll);
        KeybindManager.RegisterAction(config, "DrainTargetWeapon", GrabDenialHack.DrainTargetWeapon);
        KeybindManager.RegisterAction(config, "DrainAllWeaponsNow", GrabDenialHack.DrainAllWeaponsNow);
        KeybindManager.RegisterAction(config, "StripTarget", GrabDenialHack.StripTarget);
        KeybindManager.RegisterAction(config, "ActivateNearestExtraction", WorldHolesHack2.ActivateNearestExtraction);
        KeybindManager.RegisterAction(config, "ActivateFarthestExtraction", WorldHolesHack2.ActivateFarthestExtraction);
        KeybindManager.RegisterAction(config, "RevealAllValuables", WorldHolesHack2.RevealAllValuables);
        KeybindManager.RegisterAction(config, "StrikeTarget", WorldHolesHack2.StrikeTarget);
        KeybindManager.RegisterAction(config, "StrikeEveryone", WorldHolesHack2.StrikeEveryone);
        KeybindManager.RegisterAction(config, "WobbleAllVehicles", WorldHolesHack2.WobbleAllVehicles);
        KeybindManager.RegisterAction(config, "WobbleTargetVehicle", WorldHolesHack2.WobbleTargetVehicle);
        KeybindManager.RegisterAction(config, "ResetAllPhysPushers", WorldHolesHack2.ResetAllPhysPushers);
        KeybindManager.RegisterAction(config, "ResetTargetPhysPusher", WorldHolesHack2.ResetTargetPhysPusher);
        KeybindManager.RegisterAction(config, "DestroyAllTrapsFully", WorldHolesHack2.DestroyAllTrapsFully);
        KeybindManager.RegisterAction(config, "TriggerAllMusicBoxes", WorldHolesHack2.TriggerAllMusicBoxes);
        KeybindManager.RegisterAction(config, "StopAllChompBooks", WorldHolesHack2.StopAllChompBooks);
        KeybindManager.RegisterAction(config, "WorldSweep", WorldHolesHack2.WorldSweep);
        KeybindManager.RegisterToggle(config, "ArenaSabotage", ArenaSabotage);
        KeybindManager.RegisterToggle(config, "ArenaWeaponsSabotage", ArenaWeaponsSabotage);
        KeybindManager.RegisterToggle(config, "ArenaLegitSabotage", ArenaLegitSabotage);
        KeybindManager.RegisterToggle(config, "SwarmStorm", SwarmStorm);
        KeybindManager.RegisterToggle(config, "LockAllDoors", LockAllDoors);
        KeybindManager.RegisterToggle(config, "TrapRoom", TrapRoom);
        KeybindManager.RegisterToggle(config, "EnemyDropKill", EnemyDropKill);
        KeybindManager.RegisterToggle(config, "FreezeEnemies", FreezeEnemies);
        KeybindManager.RegisterToggle(config, "StealHeldItem", StealHeldItem);
        KeybindManager.RegisterToggle(config, "StealAllHeldItems", StealAllHeldItems);

        KeybindManager.RegisterAction(config, "HealFull", HealHack.HealFull);
        KeybindManager.RegisterAction(config, "ReviveSelf", ReviveHack.ReviveSelf);
        KeybindManager.RegisterAction(config, "ReviveOther", ReviveHack.ReviveOther);
        KeybindManager.RegisterAction(config, "KillSelf", InstantKillHack.KillSelf);
        KeybindManager.RegisterAction(config, "KillOther", InstantKillHack.KillOther);
        KeybindManager.RegisterAction(config, "SpawnValuable", EconomyHack.SpawnValuable);
        KeybindManager.RegisterAction(config, "PlaySelectedFakeSound", FakeSoundHack.PlaySelected);
        KeybindManager.RegisterAction(config, "Yeet", RaidHack.Yeet);
        KeybindManager.RegisterAction(config, "YeetAll", RaidHack.YeetAll);
        KeybindManager.RegisterAction(config, "FrameForShopKeeper", RaidHack.FrameForShopKeeper);
        KeybindManager.RegisterAction(config, "ForceSeat", RaidHack.ForceSeat);
        KeybindManager.RegisterAction(config, "MassSeatKidnap", MassRaidHack.MassSeatKidnap);
        KeybindManager.RegisterAction(config, "FrameAllForShopKeeper", MassRaidHack.FrameAllForShopKeeper);
        KeybindManager.RegisterAction(config, "ItemPurge", MassRaidHack.ItemPurge);
        KeybindManager.RegisterAction(config, "CartYeetAll", MassRaidHack.CartYeetAll);
        KeybindManager.RegisterAction(config, "ValuableBlackhole", MassRaidHack.ValuableBlackhole);
        KeybindManager.RegisterAction(config, "InstantGroupKnockdown", MassRaidHack.InstantGroupKnockdown);
        KeybindManager.RegisterAction(config, "Nuke", MassRaidHack.Nuke);
        KeybindManager.RegisterAction(config, "DisarmAllTraps", UtilityHolesHack.DisarmAllTraps);
        KeybindManager.RegisterAction(config, "ToggleAllSwitches", UtilityHolesHack.ToggleAllSwitches);
        KeybindManager.RegisterAction(config, "SwarmTheGroup", TeleportHack.SwarmTheGroup);
        KeybindManager.RegisterAction(config, "TeleportToPlayer", TeleportHack.TeleportToPlayer);
        KeybindManager.RegisterAction(config, "PullPlayerToMe", TeleportHack.PullPlayerToMe);
        KeybindManager.RegisterAction(config, "TeleportToEnemy", TeleportHack.TeleportToEnemy);
        KeybindManager.RegisterAction(config, "PullEnemyToMe", TeleportHack.PullEnemyToMe);
        KeybindManager.RegisterAction(config, "ForceDropAll", RaidHack.ForceDropAll);
        KeybindManager.RegisterAction(config, "LateJoinRescan", LateJoinFixHack.RescanNow);
        KeybindManager.RegisterAction(config, "TeleportToNearestPlayer", LateJoinFixHack.TeleportToNearestPlayer);
        KeybindManager.RegisterAction(config, "CreateSiblingRooms", MultiRoomHack.CreateSiblingRooms);

        KeybindManager.RegisterToggle(config, "HudEnabled", HudEnabled);
        KeybindManager.RegisterToggle(config, "HudEditMode", HudEditMode);
        KeybindManager.RegisterToggle(config, "DebugBridgeEnabled", DebugBridgeEnabled);
    }

    private static HudWidgetConfig BindHudWidget(ConfigFile config, string id, string label, bool defaultEnabled, float defaultX, float defaultY, string defaultColor)
    {
        return new HudWidgetConfig
        {
            Enabled = config.Bind("Hud", id + "Enabled", defaultEnabled, $"Показывать HUD-виджет «{label}»."),
            PosX = config.Bind("Hud", id + "PosX", defaultX, $"Позиция виджета «{label}» по X (перетаскивание в режиме редактирования сохраняет сюда)."),
            PosY = config.Bind("Hud", id + "PosY", defaultY, $"Позиция виджета «{label}» по Y."),
            Color = config.Bind("Hud", id + "Color", defaultColor, $"Акцентный цвет виджета «{label}» (цикл по той же палитре, что и в других местах меню)."),
        };
    }

    // By-name lookup over this class's own static fields - lets ScriptGlobals (and anything else
    // that only has a string, e.g. a saved script module) flip the exact same toggles the GUI
    // rows are bound to, without a giant hand-written switch statement that drifts out of sync
    // every time a toggle gets added. Only ever matches ConfigEntry<bool> fields, so a script
    // typing "StrengthLevel" (an int) gets a clean "not found" instead of an InvalidCastException.
    private static readonly Dictionary<string, ConfigEntry<bool>> _boolLookup = new();

    private static ConfigEntry<bool> FindBool(string name)
    {
        if (_boolLookup.TryGetValue(name, out ConfigEntry<bool> cached)) return cached;

        FieldInfo field = typeof(CheatState).GetField(name, BindingFlags.Public | BindingFlags.Static);
        ConfigEntry<bool> entry = field != null && field.FieldType == typeof(ConfigEntry<bool>)
            ? (ConfigEntry<bool>)field.GetValue(null)
            : null;
        _boolLookup[name] = entry; // caches misses too - a typo'd name in a script shouldn't re-reflect every call
        return entry;
    }

    public static bool TrySetBool(string name, bool value)
    {
        ConfigEntry<bool> entry = FindBool(name);
        if (entry == null) return false;
        entry.Value = value;
        return true;
    }

    public static bool TryGetBool(string name) => FindBool(name)?.Value ?? false;
}
