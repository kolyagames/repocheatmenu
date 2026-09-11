using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace RepoCheatMenu;

// GodMode/NoOvercharge/InfiniteStamina used to be Harmony prefixes here, but the decompiled
// source shows the game already ships built-in debug fields that gate this exact logic
// directly (PlayerHealth.Hurt() checks "godMode", PhysGrabber.PhysGrabOverCharge() checks
// "DebugDisableOvercharge", stamina drain checks "DebugEnergy") - setting those fields each
// tick in TimeTickPatch.ApplyStats is simpler and can't race with anything, so the patches
// were removed in favor of that.

[HarmonyPatch(typeof(PhysGrabObjectImpactDetector), "Break")]
internal static class ImpactDetectorBreakPatch
{
    [HarmonyPrefix]
    private static bool Prefix(PhysGrabObjectImpactDetector __instance, bool ___isValuable)
    {
        if (CheatState.Unbreakable.Value && ___isValuable) return false;
        return !UnbreakableItemsHack.IsProtected(__instance);
    }
}

// Anti-Knockback: PlayerController.ForceImpulse (confirmed in the decompiled source) is the
// single method every kind of external hit-force routes through - HurtCollider calls it for
// thrown/exploding objects and enemy hits ("ForceImpulse(normalized * playerHitForce)"), and
// nothing else applies knockback to the player a different way. Skipping the whole method
// blocks all of them uniformly instead of chasing each source separately. Purely local - only
// ever runs on your own PlayerController - so it works the same for guest or host.
[HarmonyPatch(typeof(PlayerController), "ForceImpulse")]
internal static class AntiKnockbackPatch
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        return !CheatState.AntiKnockback.Value;
    }
}

// Gentle Drop: PhysGrabObject.Throw() (confirmed private in the decompiled source) is called
// unconditionally by GrabEnded() every time an object is released - not just on some dedicated
// throw button - with force scaled by how far the aim point had drifted from the object times
// (0.5 + throwStrength), never actually zero. That's plain vanilla behavior (nothing to do with
// Power Grab or High Strength), confirmed by tracing the actual call graph rather than guessing.
// Zeroing the object's velocity right after Throw() applies its impulse turns "always throws a
// little" into "just drops". Throw() itself only ever runs where the real physics authority is
// (host/singleplayer - GrabEndedRPC targets the master client, and GrabEnded() only calls Throw()
// directly in singleplayer per GameManager.instance.gameMode == 0), so this patch firing at all
// already implies we're the one with authority - no extra guest/host check needed.
[HarmonyPatch(typeof(PhysGrabObject), "Throw")]
internal static class GentleDropPatch
{
    [HarmonyPostfix]
    private static void Postfix(PhysGrabObject __instance)
    {
        if (!CheatState.GentleDrop.Value) return;
        if (__instance == null || __instance.rb == null) return;

        __instance.rb.velocity = Vector3.zero;
        __instance.rb.angularVelocity = Vector3.zero;
    }
}

[HarmonyPatch(typeof(PhysGrabObjectImpactDetector), "DestroyObject")]
internal static class ImpactDetectorDestroyPatch
{
    [HarmonyPrefix]
    private static bool Prefix(PhysGrabObjectImpactDetector __instance, bool ___isValuable)
    {
        if (CheatState.Unbreakable.Value && ___isValuable) return false;
        return !UnbreakableItemsHack.IsProtected(__instance);
    }
}

// Bypasses the game's own 10-item cap on enemy-death valuables (EnemyDirector.AddEnemyValuable,
// confirmed in the decompiled source - normally destroys the oldest one past 10) by adding
// straight to the list ourselves and skipping the original method entirely.
[HarmonyPatch(typeof(EnemyDirector), "AddEnemyValuable")]
internal static class RemovePillarLimitPatch
{
    [HarmonyPrefix]
    private static bool Prefix(EnemyValuable _newValuable, ref System.Collections.Generic.List<EnemyValuable> ___enemyValuables)
    {
        if (!CheatState.RemovePillarLimit.Value) return true;

        ___enemyValuables.RemoveAll(v => v == null);
        ___enemyValuables.Add(_newValuable);
        return false;
    }
}

[HarmonyPatch(typeof(EnemyParent), "Despawn")]
internal static class PillarDropMultiplierPatch
{
    [HarmonyPostfix]
    private static void Postfix(EnemyParent __instance)
    {
        PillarDropHack.ApplyDespawn(__instance);
    }
}

// Spawn Rate Multiplier - see CheatState.SpawnRateMultiplier's own comment for the full mechanism.
// EnemyDirector.AmountSetup() (confirmed public) computes amountCurve1/2/3Value (private - via
// GameReflect) and calls its own private PickEnemies(tierList) that many times per tier to stock
// enemyListCurrent/enemyList, the pools GetEnemy() draws from all level long. This postfix pads
// each tier proportionally to its own real count instead of a flat bump per tier, so the game's own
// tier-rarity balance (tier 3 always rarer than tier 1) holds at any multiplier.
//
// Bug caught live via the debug bridge before this ever reached the player: padding the pool alone
// did nothing observable - confirmed enemyListCurrent.Count actually going from 2 to 10 (5x, as
// asked), but LevelGenerator.cs's own spawn coroutine (the real thing that turns pool entries into
// spawned enemies) loops "for (i = 0; i < EnemyDirector.instance.totalAmount; i++)", and totalAmount
// (also internal, also via GameReflect) is computed by AmountSetup() itself BEFORE this postfix
// ever runs - stayed at the original vanilla count the whole time, so the spawn loop never iterated
// far enough to reach any of the padding. Fixed by bumping totalAmount by the same extra count this
// postfix just added to the pool, so the loop that actually calls EnemySpawn sees the real total.
[HarmonyPatch(typeof(EnemyDirector), "AmountSetup")]
internal static class SpawnRateMultiplierPatch
{
    [HarmonyPostfix]
    private static void Postfix(EnemyDirector __instance)
    {
        float multiplier = CheatState.SpawnRateMultiplier.Value;
        if (multiplier <= 1f) return;
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;

        int totalExtra = 0;
        totalExtra += AddExtra(__instance, "amountCurve1Value", __instance.enemiesDifficulty1, multiplier);
        totalExtra += AddExtra(__instance, "amountCurve2Value", __instance.enemiesDifficulty2, multiplier);
        totalExtra += AddExtra(__instance, "amountCurve3Value", __instance.enemiesDifficulty3, multiplier);

        if (totalExtra > 0)
        {
            int totalAmount = GameReflect.Get<int>(__instance, "totalAmount");
            GameReflect.Set(__instance, "totalAmount", totalAmount + totalExtra);
        }
    }

    private static int AddExtra(EnemyDirector director, string curveValueField, System.Collections.Generic.List<EnemySetup> tier, float multiplier)
    {
        if (tier == null || tier.Count == 0) return 0;

        int baseCount = GameReflect.Get<int>(director, curveValueField);
        int extra = Mathf.RoundToInt(baseCount * (multiplier - 1f));
        for (int i = 0; i < extra; i++)
            GameReflect.Invoke(director, "PickEnemies", tier);
        return extra;
    }
}

// Confirmed from RepoHax's Hooked__PhysGrabObject_PhysicsGrabbingManipulation: grabStrength
// isn't a value you can just leave forced - something recomputes it back to the real
// (dict-derived) upgrade value before each grab-physics pass, so EntityHack's background
// tick write usually loses the race. RepoHax wins by boosting grabStrength immediately before
// the manipulation call and restoring it right after, every single call. We do the same here.
[HarmonyPatch(typeof(PhysGrabObject), "PhysicsGrabbingManipulation")]
internal static class PhysicsGrabManipulationPatch
{
    private static PhysGrabber _grabber;
    private static float _backupStrength;

    [HarmonyPrefix]
    private static void Prefix()
    {
        _grabber = null;
        if (!CheatState.HighStrength.Value) return;

        PlayerAvatar avatar = PlayerAvatar.instance;
        if (avatar == null) return;

        PhysGrabber grabber = GameReflect.Get<PhysGrabber>(avatar, "physGrabber");
        if (grabber == null) return;

        _backupStrength = GameReflect.Get<float>(grabber, "grabStrength");
        if (_backupStrength < 100f)
        {
            _grabber = grabber;
            GameReflect.Set(grabber, "grabStrength", 100f);
        }
    }

    [HarmonyPostfix]
    private static void Postfix()
    {
        if (_grabber != null)
            GameReflect.Set(_grabber, "grabStrength", _backupStrength);
    }
}

// Same backup/restore trick as above, this time for grabRange - confirmed from RepoHax's
// Hooked__PhysGrabber_PhysGrabLogic and Hooked__PhysGrabber_RayCheck. Both wrap the local
// player's PhysGrabber specifically (compared by reference), so range hacks on other players'
// grabbers are left untouched.
[HarmonyPatch(typeof(PhysGrabber), "PhysGrabLogic")]
internal static class PhysGrabLogicRangePatch
{
    private static PhysGrabber _grabber;
    private static float _backup;

    [HarmonyPrefix]
    private static void Prefix(PhysGrabber __instance, ref float ___grabRange)
    {
        _grabber = null;
        if (!CheatState.UnlimitedGrabRange.Value) return;
        if (PlayerAvatar.instance == null || __instance != GameReflect.Get<PhysGrabber>(PlayerAvatar.instance, "physGrabber")) return;

        _grabber = __instance;
        _backup = ___grabRange;
        ___grabRange = 999999f;
    }

    [HarmonyPostfix]
    private static void Postfix(ref float ___grabRange)
    {
        if (_grabber != null)
            ___grabRange = _backup;
    }
}

[HarmonyPatch(typeof(PhysGrabber), "RayCheck")]
internal static class PhysGrabRayCheckRangePatch
{
    private static PhysGrabber _grabber;
    private static float _backup;

    // Grab Through Walls: PhysGrabber.mask (confirmed private in the decompiled source, set once
    // at Start to "SemiFunc.LayerMaskGetVisionObstruct() - LayerMask.GetMask('Player')") is what
    // RayCheck's own Physics.Raycast uses to find what to grab - since that mask includes solid
    // level geometry (walls/floor), the ray simply stops at the first wall in the way and never
    // reaches whatever's behind it. Temporarily narrowing the mask to ONLY the phys-grab-object
    // layers (confirmed real layer names from the decompiled source, e.g. PlayerDeathHead.cs's
    // own mask construction: "PhysGrabObject", "PhysGrabObjectCart", "PhysGrabObjectHinge") makes
    // the ray pass straight through walls, since they're not on any of those layers, and hit the
    // actual grabbable object directly. Same backup/restore-around-the-call trick as grabRange
    // above, and the SAME local-player-only guard.
    private static bool _maskActive;
    private static LayerMask _maskBackup;

    [HarmonyPrefix]
    private static void Prefix(PhysGrabber __instance, ref float ___grabRange, ref LayerMask ___mask)
    {
        _grabber = null;
        _maskActive = false;

        if (PlayerAvatar.instance == null || __instance != GameReflect.Get<PhysGrabber>(PlayerAvatar.instance, "physGrabber")) return;

        if (CheatState.UnlimitedGrabRange.Value)
        {
            _grabber = __instance;
            _backup = ___grabRange;
            ___grabRange = 999999f;
        }

        if (CheatState.GrabThroughWalls.Value)
        {
            _maskActive = true;
            _maskBackup = ___mask;
            ___mask = (LayerMask)LayerMask.GetMask("PhysGrabObject", "PhysGrabObjectCart", "PhysGrabObjectHinge");
        }
    }

    [HarmonyPostfix]
    private static void Postfix(ref float ___grabRange, ref LayerMask ___mask)
    {
        if (_grabber != null)
            ___grabRange = _backup;
        if (_maskActive)
            ___mask = _maskBackup;
    }
}

// Fly needs to win every physics step against the game's own movement force, not just our
// independent ~33/sec tick loop - applying it as a postfix on the same FixedUpdate that
// computes that force guarantees ours is the very last write before the step integrates.
[HarmonyPatch(typeof(PlayerController), "FixedUpdate")]
internal static class PlayerControllerFixedUpdatePatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerController __instance)
    {
        if (__instance != PlayerController.instance) return;

        // This runs on every physics step (much more often than the ~33/sec tick loop), so an
        // uncaught exception here would spam far harder than anywhere else - and would abort
        // AutoFarmHack.ApplyFixed for the rest of that step if KnockdownHack threw first. Same
        // isolate-each-call reasoning as TimeTickPatch.RunSafe.
        try { KnockdownHack.ApplyFixed(__instance); }
        catch (System.Exception e) { Plugin.Log.LogError("Exception in KnockdownHack.ApplyFixed: " + e); }

        try { AutoFarmHack.ApplyFixed(__instance); }
        catch (System.Exception e) { Plugin.Log.LogError("Exception in AutoFarmHack.ApplyFixed: " + e); }
    }
}

// Fly moved from a FixedUpdate/Rigidbody-driven approach to this Update/transform-driven one -
// see FlyHack's class comment for why.
[HarmonyPatch(typeof(PlayerController), "Update")]
internal static class PlayerControllerUpdatePatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerController __instance)
    {
        if (__instance != PlayerController.instance) return;

        try { FlyHack.ApplyUpdate(__instance); }
        catch (System.Exception e) { Plugin.Log.LogError("Exception in FlyHack.ApplyUpdate: " + e); }
    }
}

// Feeds Auto-Farm's steering into the exact same input channel a human W-press goes through
// (InputManager.GetMovementY, confirmed public in the decompiled source and the same method
// SemiFunc.InputMovementY wraps, which PlayerController.FixedUpdate reads to build
// InputDirectionRaw) - skip-the-original-and-return-1 while a path is active, otherwise pass
// real input through untouched. Only Y (forward) is spoofed; X (strafe) is left alone since
// AutoFarmHack.ApplyFixed already steers by turning the camera/body to face the waypoint, so
// pure forward is all that's needed - and leaving X alone means a manual strafe nudge still
// works if you touch the keyboard mid-run.
[HarmonyPatch(typeof(InputManager), "GetMovementY")]
internal static class AutoFarmMovementPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref float __result)
    {
        if (!AutoFarmHack.WantForward) return true;
        __result = 1f;
        return false;
    }
}

// Hotkey polling piggybacks on Time.deltaTime's getter - a core engine property read
// many times per frame by built-in Unity systems, regardless of this game's own script
// structure. Unlike the game-specific Update methods we tried first (PlayerController,
// RunManager - never observed firing), this is guaranteed to run in any live Unity game.
// Key state itself is read straight from Win32 (GetAsyncKeyState), bypassing
// UnityEngine.Input entirely - the same kind of OS-level approach RepoHax uses.
[HarmonyPatch(typeof(UnityEngine.Time), "deltaTime", HarmonyLib.MethodType.Getter)]
internal static class TimeTickPatch
{
    private static bool _menuKeyWasDown;
    private static bool _customMenuKeyWasDown;
    private static bool _escapeWasDown;
    private static bool _reactivateKeyWasDown;
    private static int _lastPollTick;
    private static int _tickCount;

    [HarmonyPostfix]
    private static void Postfix()
    {
        int now = System.Environment.TickCount;
        if (now - _lastPollTick < 30) return; // throttle to ~33 checks/sec, this getter fires far more often than that
        _lastPollTick = now;

        _tickCount++;
        if (_tickCount % 90 == 0)
            Plugin.Log.LogInfo($"Time.deltaTime tick heartbeat ({_tickCount}).");

        // ']' is the one thing still polled while PanicHack.Hidden - has to keep working no
        // matter what, it's the only way back in once everything below stops running entirely.
        bool reactivateKeyDown = Win32.IsKeyDown(UnityEngine.KeyCode.RightBracket);
        if (reactivateKeyDown && !_reactivateKeyWasDown)
        {
            try
            {
                PanicHack.Reactivate();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("Exception while reactivating PanicHack: " + e);
            }
        }
        _reactivateKeyWasDown = reactivateKeyDown;

        // Second thing still polled while hidden, for the same reason as ']' above: the debug
        // bridge is the only OTHER way back in. Found the hard way - panic now persists across a
        // restart (by design), and since the bridge ticks inside the ApplyStats block below, a
        // panic + quit left a session that could only be recovered by physically pressing ']'.
        // Runs only when its own toggle is explicitly on (off by default), renders nothing and
        // touches no gameplay, so it doesn't undermine what the kill switch is for.
        if (PanicHack.Hidden && CheatState.DebugBridgeEnabled.Value)
            RunSafe(nameof(DebugBridge), DebugBridge.Apply);

        // Deactivated: skip the menu hotkey, Escape, OverlayUI.Tick() and ApplyStats() entirely
        // - not just "everything reports off", nothing runs at all until reactivated.
        if (PanicHack.Hidden) return;

        bool menuKeyDown = Win32.IsKeyDown(Plugin.MenuKey.Value);
        if (menuKeyDown && !_menuKeyWasDown)
        {
            Plugin.Log.LogInfo("Menu hotkey detected via Time.deltaTime tick (Win32 GetAsyncKeyState) - toggling menu.");
            try
            {
                OverlayUI.Toggle();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("Exception while toggling menu: " + e);
            }
        }
        _menuKeyWasDown = menuKeyDown;

        // F6 - the second, CustomGui-based menu (CustomOverlayUI.cs), running alongside the
        // original on RightShift - both are complete and kept feature-equal, each with a button
        // in its own header to switch to the other. Hardcoded key, not CheatState-configurable.
        bool customMenuKeyDown = Win32.IsKeyDown(UnityEngine.KeyCode.F6);
        if (customMenuKeyDown && !_customMenuKeyWasDown)
            CustomOverlayUI.Toggle();
        _customMenuKeyWasDown = customMenuKeyDown;

        bool escapeDown = Win32.IsKeyDown(UnityEngine.KeyCode.Escape);
        if (escapeDown && !_escapeWasDown)
            OverlayUI.Close();
        _escapeWasDown = escapeDown;

        OverlayUI.Tick();
        HudRoot.Tick();

        // Stat application lives on this same confirmed-firing tick instead of
        // PlayerController.Update/StatsManager.PlayerAdd/ItemBattery.Update, none of which
        // we were able to confirm actually fire in this game (no heartbeat ever observed there).
        ApplyStats();
    }

    // Each hack runs behind its own try/catch instead of one big block around the whole method.
    // A single try/catch around everything meant one hack throwing (e.g. a field name that
    // doesn't resolve on some game patch) silently killed every hack listed AFTER it for that
    // entire tick, forever, with nothing but a log line to notice by - VoiceFilter/RepoDebug/
    // PacketTest/Region sit at the end of the list and would just stop running completely while
    // e.g. Knockdown was misbehaving. Isolating each call means a break in one hack can't take
    // any of the others down with it, and the log now points at exactly which one failed.
    // Lets PanicHack push one more real tick of state through every hack right after zeroing
    // CheatState, so things that only undo themselves "next time they run" (ESP pooled labels,
    // ThirdPerson camera offset, DebugEnergy/godMode fields, ...) actually go back to vanilla
    // instead of freezing mid-effect the instant the tick loop stops.
    internal static void RunApplyStatsOnce() => ApplyStats();

    private static void RunSafe(string name, System.Action action)
    {
        try
        {
            action();
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogError($"Exception in {name}: {e}");
        }
    }

    private static void ApplyStats()
    {

        RunSafe(nameof(KeybindManager), KeybindManager.Apply);

        RunSafe("CoreStats", () =>
        {
            PlayerController controller = PlayerController.instance;
            if (controller != null)
            {
                // Both are official built-in debug toggles (PlayerController.DebugEnergy/
                // DebugDisableOvercharge) that gate the drain/overcharge logic directly at its
                // source, confirmed in the decompiled source - cleaner and race-free compared to
                // resetting EnergyCurrent every tick, which visibly snapped the stamina bar.
                controller.DebugEnergy = CheatState.InfiniteStamina.Value;
                controller.DebugDisableOvercharge = CheatState.NoOvercharge.Value;
                // Noclip/Fly also force this - confirmed root cause of "noclip snaps back to the
                // floor" reports: our noclip only moves PlayerController.rb, but tumbling/ragdoll
                // is a SEPARATE PlayerTumble.rb that keeps colliding with the real floor normally
                // (unaffected by our kinematic trick) - PlayerController.cs:632 then re-syncs
                // transform.position to wherever that ragdoll settled the moment isTumbling ends
                // (confirmed in the decompiled source), overriding whatever fake sunken/flying
                // position Noclip/Fly had reached. Suppressing tumble entirely while either is on
                // removes the only thing that was ever forcing you back.
                controller.DebugNoTumble = CheatState.NoKnockdown.Value || CheatState.Fly.Value || CheatState.Noclip.Value;

                // No Fall Damage: confirmed in the decompiled PlayerAvatar.cs (the "isLocal"
                // block reading CollisionController.fallDistance) - damage only applies once
                // fallDistance >= 8f, computed entirely client-side. fallDistance itself is
                // internal, so this needs GameReflect. Keeping it pinned at 0 every tick means
                // the threshold is never reached at all - simpler and race-free compared to
                // patching the damage-tier logic itself.
                if (CheatState.NoFallDamage.Value && controller.CollisionController != null)
                    GameReflect.Set(controller.CollisionController, "fallDistance", 0f);

                InvisibleSafetyNet.Apply(controller);

                MovementHack.Apply(controller);
            }

            if (CheatState.HighStrength.Value)
            {
                if (CheatState.StrengthOverrideMode.Value)
                {
                    PhysGrabber grabber = PlayerAvatar.instance != null ? PlayerAvatar.instance.physGrabber : null;
                    grabber?.OverrideGrabStrength(CheatState.StrengthOverrideValue.Value);
                }
                else
                {
                    StatsHack.ForceStat("playerUpgradeStrength", CheatState.StrengthLevel.Value);
                }
            }

            if (CheatState.UnlimitedGrabRange.Value)
                StatsHack.ForceStat("playerUpgradeRange", CheatState.GrabRangeLevel.Value);

            if (PlayerAvatar.instance != null)
            {
                PlayerHealth health = PlayerAvatar.instance.playerHealth;
                // PlayerHealth.Hurt() checks this built-in field directly and returns before
                // applying any damage - same effect as our old Harmony prefix, no patch needed.
                if (health != null)
                    GameReflect.Set(health, "godMode", CheatState.GodMode.Value);
            }

            // RoundDirector.debugInfiniteBattery is a built-in toggle that ItemBattery.Update()
            // checks directly each frame ("if (... || RoundDirector.instance.debugInfiniteBattery)
            // batteryLife = 100f"), with no master-client gating around that check - so it's local
            // and guest-safe, and replaces our old per-tick FindObjectsOfType<ItemBattery> loop.
            if (RoundDirector.instance != null)
                GameReflect.Set(RoundDirector.instance, "debugInfiniteBattery", CheatState.InfiniteBatteries.Value);

            if (CheatState.MaxHeadBattery.Value && SpectateCamera.instance != null)
            {
                GameReflect.Set(SpectateCamera.instance, "headEnergy", 9999f);
                GameReflect.Set(SpectateCamera.instance, "headEnergyEnough", true);
            }
        });

        RunSafe(nameof(EntityHack), EntityHack.Apply);
        RunSafe(nameof(VisionHack), VisionHack.Apply);
        RunSafe(nameof(FlashlightHack), FlashlightHack.Apply);
        RunSafe(nameof(LaserHack), LaserHack.Apply);
        RunSafe(nameof(MeleeVibrateHack), MeleeVibrateHack.Apply);
        RunSafe(nameof(EspOverlay), EspOverlay.Apply);
        RunSafe(nameof(PowerGrabHack), PowerGrabHack.Apply);
        RunSafe(nameof(TrollHack), TrollHack.Apply);
        RunSafe(nameof(ChatSpamHack), ChatSpamHack.Apply);
        RunSafe(nameof(QuotaHack), QuotaHack.Apply);
        RunSafe("QuotaHack.ApplyZeroRace", QuotaHack.ApplyZeroRace);
        RunSafe("QuotaHack.ApplyAutoSkipToShop", QuotaHack.ApplyAutoSkipToShop);
        RunSafe(nameof(UpgradeStandHack), UpgradeStandHack.Apply);
        RunSafe("RaidHack.ApplyCursedHands", RaidHack.ApplyCursedHands);
        RunSafe("RaidHack.ApplySeatShuffle", RaidHack.ApplySeatShuffle);
        RunSafe("LevelLoaderHack.ApplyModuleInjection", LevelLoaderHack.ApplyModuleInjection);
        RunSafe(nameof(MonsterBaitHack), MonsterBaitHack.Apply);
        RunSafe(nameof(EnemyFreezeHack), EnemyFreezeHack.Apply);
        RunSafe("HealHack.ApplyHealSpam", HealHack.ApplyHealSpam);
        RunSafe("ChargingStationHack.ApplyHeal", ChargingStationHack.ApplyHeal);
        RunSafe("ReviveHack.ApplyAutoRevive", ReviveHack.ApplyAutoRevive);
        RunSafe(nameof(ShopDestroyerHack), ShopDestroyerHack.Apply);
        RunSafe(nameof(LootMagnetHack), LootMagnetHack.Apply);
        RunSafe(nameof(ValuableGodModeHack), ValuableGodModeHack.Apply);
        RunSafe("CartHack.PullAllToExtraction", CartHack.PullAllToExtraction);
        RunSafe("GhostRiderHack.Apply", GhostRiderHack.Apply);
        RunSafe("NameChangerHack.ApplyMimicHost", NameChangerHack.ApplyMimicHost);
        RunSafe("CartHack.ApplyRam", CartHack.ApplyRam);
        RunSafe(nameof(DeadFlyHack), DeadFlyHack.Apply);
        RunSafe(nameof(AutoLootHack), AutoLootHack.Apply);
        RunSafe(nameof(AutoFarmHack), AutoFarmHack.Apply);
        RunSafe(nameof(ExtractionRadarHack), ExtractionRadarHack.Apply);
        RunSafe(nameof(ExtractionActivatorHack), ExtractionActivatorHack.Apply);
        RunSafe(nameof(CursorLockHack), CursorLockHack.Apply);
        RunSafe("KnockdownHack.Apply", KnockdownHack.Apply);
        RunSafe("KnockdownHack.ApplyBanish", KnockdownHack.ApplyBanish);
        RunSafe("RaidHack.ApplyCage", RaidHack.ApplyCage);
        RunSafe("RaidHack.ApplyYeetPending", RaidHack.ApplyYeetPending);
        RunSafe("RaidHack.ApplyCageAll", RaidHack.ApplyCageAll);
        RunSafe("RaidHack.ApplyFreezeAll", RaidHack.ApplyFreezeAll);
        RunSafe("MassRaidHack.ApplyMassDisarm", MassRaidHack.ApplyMassDisarm);
        RunSafe("MassRaidHack.ApplyYeetStorm", MassRaidHack.ApplyYeetStorm);
        RunSafe("MassRaidHack.ApplyExtractionCamperPunish", MassRaidHack.ApplyExtractionCamperPunish);
        RunSafe("ShopExecutionHack.Apply", ShopExecutionHack.Apply);
        RunSafe("RuckusFrameHack.ApplyAutoExecute", RuckusFrameHack.ApplyAutoExecute);
        RunSafe("RuckusFrameHack.ApplyCarryThrustStorm", RuckusFrameHack.ApplyCarryThrustStorm);
        RunSafe("GrabDenialHack.ApplyGrabLockTarget", GrabDenialHack.ApplyGrabLockTarget);
        RunSafe("GrabDenialHack.ApplyGrabLockAll", GrabDenialHack.ApplyGrabLockAll);
        RunSafe("GrabDenialHack.ApplyUnequipStorm", GrabDenialHack.ApplyUnequipStorm);
        RunSafe("GrabDenialHack.ApplyWeaponDrainAll", GrabDenialHack.ApplyWeaponDrainAll);
        RunSafe("GrabDenialHack.ApplyDisarmOnGrab", GrabDenialHack.ApplyDisarmOnGrab);
        RunSafe("WorldHolesHack2.ApplyExtractionSnipe", WorldHolesHack2.ApplyExtractionSnipe);
        RunSafe("WorldHolesHack2.ApplyAutoReveal", WorldHolesHack2.ApplyAutoReveal);
        RunSafe("WorldHolesHack2.ApplyStrikeStorm", WorldHolesHack2.ApplyStrikeStorm);
        RunSafe("WorldHolesHack2.ApplyWobbleStorm", WorldHolesHack2.ApplyWobbleStorm);
        RunSafe("MassRaidHack.ApplyGroupKnockdownPending", MassRaidHack.ApplyGroupKnockdownPending);
        RunSafe("MassRaidHack.ApplyArenaSabotage", MassRaidHack.ApplyArenaSabotage);
        RunSafe("MassRaidHack.ApplyArenaWeaponsSabotage", MassRaidHack.ApplyArenaWeaponsSabotage);
        RunSafe("MassRaidHack.ApplyArenaLegitSabotage", MassRaidHack.ApplyArenaLegitSabotage);
        RunSafe("TeleportHack.ApplySwarmStorm", TeleportHack.ApplySwarmStorm);
        RunSafe("UtilityHolesHack.ApplyLockAllDoors", UtilityHolesHack.ApplyLockAllDoors);
        RunSafe("TeleportHack.ApplyTrapRoom", TeleportHack.ApplyTrapRoom);
        RunSafe("TeleportHack.ApplyEnemyDropKill", TeleportHack.ApplyEnemyDropKill);
        RunSafe("RaidHack.ApplyYeetAllPending", RaidHack.ApplyYeetAllPending);
        RunSafe("TeleportHack.ApplyPullPending", TeleportHack.ApplyPullPending);
        // Was never wired in at all despite being fully implemented - StealHeldItem/StealAllHeldItems
        // silently did nothing until now regardless of what the config said.
        RunSafe(nameof(ItemStealHack), ItemStealHack.Apply);
        RunSafe(nameof(VoiceFilterHack), VoiceFilterHack.Apply);
        RunSafe("VoiceFilterHack.ApplyChipmunk", VoiceFilterHack.ApplyChipmunk);
        RunSafe(nameof(RepoDebugHack), RepoDebugHack.Apply);
        RunSafe(nameof(PacketTestHack), PacketTestHack.Apply);
        RunSafe(nameof(NetworkDiagnostics), NetworkDiagnostics.Apply);
        RunSafe(nameof(KolobokHack), KolobokHack.Apply);
        RunSafe(nameof(CosmeticHack), CosmeticHack.Apply);
        RunSafe(nameof(DebugBridge), DebugBridge.Apply);
        RunSafe(nameof(EventLog), EventLog.Apply);
    }
}

// "Anti-aim": PlayerLocalCamera was the wrong target - that component (and CameraAim) feeds
// aim-assist/targeting logic, not what other clients render. The actual broadcast that drives
// the visible head/eye-look for everyone else is PlayerVisionTarget.OnPhotonSerializeView,
// confirmed in the decompiled source: its Update() sets "TargetRotation = MainCamera.transform
// .rotation" for the local player and sends that one field over the network; PlayerAvatarVisuals
// reads "playerAvatar.PlayerVisionTarget.VisionTransform" to drive the head-look system for
// every other client. So this patches TargetRotation directly (never touching the real
// MainCamera/CameraAim at all) for the instant it's read into the outgoing stream, then restores
// it immediately after - our own aim and view are never touched.
//
// Re-verified end to end against the now-complete decompiled source (previously only had
// Assembly-CSharp itself, not the Photon assemblies): the field name/type ("private Quaternion
// TargetRotation") is exact, and OnPhotonSerializeView's own gate -
// "SemiFunc.MasterAndOwnerOnlyRPC(info, base.photonView)" - passes for our own outgoing write
// regardless of host/guest (it's an OR: master OR owner, and we always own our own
// PlayerVisionTarget). Nothing here needed fixing. If this still looks like it's doing nothing,
// the likely explanation isn't the patch - PUN only invokes OnPhotonSerializeView at all while
// actually inside a Photon room (GameManager.Multiplayer()); it is never called by the engine in
// pure singleplayer, same reason other hacks in this mod branch on Multiplayer() to call their
// RPC methods directly instead. And since this only changes what OTHER clients receive, your own
// screen is never the right place to check it - you need a second client watching you.
[HarmonyPatch(typeof(PlayerVisionTarget), "OnPhotonSerializeView")]
internal static class AntiAimSpinPatch
{
    private static bool _active;
    private static Quaternion _backup;
    private static float _yaw;

    private static bool _loggedOnce;

    [HarmonyPrefix]
    private static void Prefix(PhotonStream stream, ref Quaternion ___TargetRotation)
    {
        _active = false;
        if (!CheatState.AntiAimSpin.Value) return;

        if (!_loggedOnce)
        {
            _loggedOnce = true;
            Plugin.Log.LogInfo("Anti-Aim Spin: only changes what OTHER players' clients render " +
                "for you - your own camera/view is untouched by design, so check on a second " +
                "client (or ask someone in the lobby) rather than your own screen.");
        }

        if (!stream.IsWriting) return;

        _active = true;
        _backup = ___TargetRotation;
        _yaw = (_yaw + 720f * Time.deltaTime) % 360f;
        ___TargetRotation = Quaternion.Euler(0f, _yaw, 0f);
    }

    [HarmonyPostfix]
    private static void Postfix(ref Quaternion ___TargetRotation)
    {
        if (_active)
            ___TargetRotation = _backup;
    }
}

// "Dinner bone": same trick, this time on PlayerAvatar's own OnPhotonSerializeView, which
// broadcasts PlayerController.instance.transform.rotation as "clientRotation" - confirmed in
// the decompiled source, remote clients feed that straight into "bodySpringTarget =
// playerAvatar.clientRotationCurrent" in PlayerAvatarVisuals.Update(), which the visual mesh
// springs toward. Tilting around the LOCAL FORWARD (Z) axis - roll, not pitch - is what actually
// produces the Minecraft-Dinnerbone "upside down but still facing the same way" look; an X-axis
// tilt instead turns the broadcast rotation into facing-backward-and-upside-down, which is a
// much smaller-looking change at small angles and easy to mistake for "nothing happened". Our
// own real PlayerController transform - and therefore our own movement, collision, and camera -
// never sees anything but the restored real rotation.
[HarmonyPatch(typeof(PlayerAvatar), "OnPhotonSerializeView")]
internal static class DinnerBonePatch
{
    private static bool _active;
    private static Quaternion _backup;
    private static bool _loggedOnce;

    [HarmonyPrefix]
    private static void Prefix(PhotonStream stream)
    {
        _active = false;
        if (!CheatState.DinnerBone.Value) return;

        PlayerController controller = PlayerController.instance;

        // Re-verified against the now-complete decompiled source: PlayerAvatar.OnPhotonSerializeView
        // reads "PlayerController.instance.transform.rotation" directly (confirmed at the exact
        // stream.SendNext call, not this.transform / PlayerAvatar's own transform), so mutating that
        // same Transform here, before the original method runs, changes exactly what gets sent - the
        // patch target and field were both already correct. If this still looks like it's doing
        // nothing, see AntiAimSpinPatch's comment above: OnPhotonSerializeView is never invoked by
        // PUN in pure singleplayer, and the effect only ever shows on OTHER clients' screens, never
        // your own.
        if (!_loggedOnce)
        {
            _loggedOnce = true;
            Plugin.Log.LogInfo("Dinner Bone: only changes what OTHER players' clients render for " +
                "you - your own movement/collision/camera is untouched by design, check on a " +
                "second client rather than your own screen.");
        }

        if (!stream.IsWriting || controller == null) return;

        _active = true;
        Transform t = controller.transform;
        _backup = t.rotation;
        t.rotation = t.rotation * Quaternion.Euler(0f, 0f, CheatState.DinnerBoneAngle.Value);
    }

    [HarmonyPostfix]
    private static void Postfix()
    {
        if (_active)
            PlayerController.instance.transform.rotation = _backup;
    }
}

// "Flip": same broadcast-rotation trick as Dinner Bone above (same field, same
// OnPhotonSerializeView target), but the offset keeps advancing every call instead of holding
// one fixed angle - a continuous spin instead of a static tilt. FlipAxis picks which local axis
// it spins around: Roll (forward axis - same "upside down but still facing the same way" look
// Dinner Bone's 180 gives, just spinning instead of held), Pitch (forward/backward somersault),
// Yaw (spin in place). Don't enable alongside Dinner Bone - both mutate the same
// PlayerController.transform.rotation right before the same broadcast, so whichever Prefix runs
// last wins for that tick.
//
// FlipOrbitRadius adds GameTools' "spin radius" on top: PlayerAvatar.OnPhotonSerializeView sends
// PlayerController.instance.transform.position as its own SendNext right next to rotation
// (confirmed in the decompiled source, same method) - untouched by every other Troll toggle in
// this file (TPose's own comment above notes position is "sent separately, untouched"), so this
// is the first one to spoof it. Same backup/restore-around-the-call shape as rotation: offset the
// real transform's position for the single call that reads it, put it back in the postfix. The
// circle is plain world XZ, phase-locked to the same _angle driving the rotation so orbit speed
// visually tracks spin speed the way GameTools' single shared phase does - not an independent
// timer, so there's nothing new to desync.
[HarmonyPatch(typeof(PlayerAvatar), "OnPhotonSerializeView")]
internal static class FlipPatch
{
    private static bool _active;
    private static Quaternion _backupRotation;
    private static Vector3 _backupPosition;
    private static float _angle;

    [HarmonyPrefix]
    private static void Prefix(PhotonStream stream)
    {
        _active = false;
        if (!CheatState.FlipEnabled.Value) return;

        PlayerController controller = PlayerController.instance;
        if (!stream.IsWriting || controller == null) return;

        _active = true;
        _angle = (_angle + CheatState.FlipSpeed.Value * Time.deltaTime) % 360f;
        Vector3 axis = CheatState.FlipAxis.Value switch
        {
            "Pitch" => Vector3.right,
            "Yaw" => Vector3.up,
            _ => Vector3.forward, // Roll
        };

        _backupRotation = controller.transform.rotation;
        controller.transform.rotation = _backupRotation * Quaternion.AngleAxis(_angle, axis);

        float radius = CheatState.FlipOrbitRadius.Value;
        _backupPosition = controller.transform.position;
        if (radius > 0.01f)
        {
            float rad = _angle * Mathf.Deg2Rad;
            Vector3 orbitPos = _backupPosition +
                new Vector3(Mathf.Cos(rad) * radius, 0f, Mathf.Sin(rad) * radius);
            controller.transform.position = orbitPos;
            // Same fix InvisiblePatch needed after being reported stranding the player in the sky
            // during sliding/Tumble: PlayerController.rb isn't kinematic during those physics-
            // active states, and a bare transform.position write doesn't reliably sync with the
            // physics engine on a non-kinematic Rigidbody - mirror onto rb.position too so
            // Transform and Rigidbody never disagree about where you are, even for one frame.
            if (controller.rb != null) controller.rb.position = orbitPos;
        }
    }

    [HarmonyPostfix]
    private static void Postfix()
    {
        if (!_active) return;
        PlayerController controller = PlayerController.instance;
        if (controller == null) return;
        controller.transform.rotation = _backupRotation;
        controller.transform.position = _backupPosition;
        if (controller.rb != null)
        {
            controller.rb.position = _backupPosition;
            // Backported from InvisiblePatch's own Postfix after auditing every other broadcast-
            // position spoof for the same shape (2026-09-11): writing raw .position twice in one
            // frame on a non-kinematic Rigidbody (grounded/walking - Fly/Noclip's kinematic path
            // never had this problem) can leave PhysX inferring an implied velocity from the
            // position delta between the two writes, the same mechanism that stranded a player for
            // real while Invisible was on. FlipOrbitRadius does the exact same double-write shape
            // every single tick it's enabled (not just once, like the trigger that hit Invisible) -
            // its small radius (capped at 5m) means any resulting drift is easy to mistake for
            // "just the orbit," not proof the underlying issue isn't present here too. Zeroing
            // after every restore cancels it before it can accumulate, same as Invisible's fix.
            controller.rb.velocity = Vector3.zero;
            controller.rb.angularVelocity = Vector3.zero;
        }
    }
}

// "Invisible": same broadcast-position trick FlipOrbitRadius uses (PlayerAvatar.OnPhotonSerializeView
// sends PlayerController.instance.transform.position directly, confirmed in the decompiled
// source), but held at a fixed absurd height instead of a per-frame offset - every other client's
// screen renders your character way up in the sky, off in a corner nobody looks, instead of where
// you actually are. X/Z stay exactly where you really are (only Y is swapped) so it reads as "shot
// straight up", matching what was asked for, not teleported off to some random point. Same
// backup/restore-around-the-call shape as every other Troll broadcast-spoof here: only the single
// instant this method reads the transform is touched, your own real position/movement/collision/
// camera never sees anything but the restored real value.
//
// Confirmed effect: what OTHER players' clients RENDER for you. Not independently verified whether
// this also throws off enemy AI targeting on whichever machine is master (enemy vision/chase logic
// there might read the same broadcast position) - could go either way as a side effect, not
// promising it either way without checking EnemyVision's own source first.
//
// Reported live: worked normally while standing/walking, but sliding or Tumble (voluntary ragdoll,
// PlayerTumble.cs) actually stranded the player at the fake sky height instead of just broadcasting
// it. Root cause: PlayerController.rb (confirmed public Rigidbody) isn't kinematic during those
// physics-active states, and Unity's own guidance is explicit that writing transform.position
// directly on a non-kinematic Rigidbody doesn't reliably sync with the physics engine the way it
// does for a kinematic one (FlyHack's rb.isKinematic=true path never had this problem for exactly
// that reason). Restoring transform.position alone in the Postfix wasn't enough - the physics
// engine had already latched onto the fake position internally and re-applied it on the next
// FixedUpdate regardless of what the Transform said a script-frame later. Fix: mirror every write
// onto rb.position too (Rigidbody's own explicit position setter goes through the physics engine
// properly), so there's never a frame where Transform and Rigidbody disagree about where you are.
[HarmonyPatch(typeof(PlayerAvatar), "OnPhotonSerializeView")]
internal static class InvisiblePatch
{
    // Reported live twice now: "isTalking" alone (VAD is momentary - it drops false between
    // words/breaths, not just at the end of a sentence) meant most of a normal conversation still
    // caught the network send tick in a gap and re-broadcast the fake sky position anyway -
    // listeners' 3D voice attenuation then computes distance against a target thousands of units
    // away (InvisibleHeight defaults 500-20000) and goes silent, which reads as "can't hear me at
    // all near them," not just "sounds a bit off." Fix at the time: once isTalking goes true, hold
    // the real position broadcast for a short grace window after it drops back to false too, so
    // short pauses mid-sentence don't immediately snap back to the spoofed height.
    //
    // THIRD live report, 2026-09-11: that fix went too far the other way - "players could see me
    // while invisible" turned out to just be normal co-op voice chat. Someone talking on and off
    // through a whole session keeps re-extending a 1.5s window almost continuously, so the real
    // position was broadcasting nearly the entire time for anyone who talks a lot with friends -
    // "stay hidden" and "sound positionally correct while talking" are in direct tension (same
    // broadcast field, can't have both simultaneously), and this mod's actual point is staying
    // hidden. Gated behind CheatState.InvisibleRevealWhileTalking now, default OFF - Invisible
    // means invisible regardless of how much you talk unless explicitly opted into the trade-off.
    private const float TalkGraceSeconds = 1.5f;

    private static bool _active;
    private static Vector3 _backup;
    private static float _talkGraceUntil;

    [HarmonyPrefix]
    private static void Prefix(PhotonStream stream)
    {
        _active = false;
        if (!CheatState.Invisible.Value) return;

        PlayerController controller = PlayerController.instance;
        if (!stream.IsWriting || controller == null) return;

        // "Being heard while invisible": PlayerVoiceChat is a child of PlayerAvatar - its own
        // transform is never independently repositioned for the normal talking case (confirmed in
        // the decompiled source: the only transform.position writes there are for an unrelated
        // lobby-mixer/disabled-avatar special case) - so the SAME broadcast position spoofed below
        // also relocates the voice audio source on every listener's machine, meaning talking while
        // "in the sky" would sound distant/wrong to them. Skip the spoof (broadcast the real
        // position) while actually talking AND for TalkGraceSeconds after, so a normal back-and-
        // forth conversation keeps a stable real position the whole time instead of flickering
        // back to the sky between every word.
        if (CheatState.InvisibleRevealWhileTalking.Value)
        {
            if (PlayerVoiceChat.instance != null && GameReflect.Get<bool>(PlayerVoiceChat.instance, "isTalking"))
                _talkGraceUntil = Time.unscaledTime + TalkGraceSeconds;
            if (Time.unscaledTime < _talkGraceUntil)
                return;
        }

        _active = true;
        _backup = controller.transform.position;
        Vector3 fake = new Vector3(_backup.x, CheatState.InvisibleHeight.Value, _backup.z);
        controller.transform.position = fake;
        if (controller.rb != null) controller.rb.position = fake;
    }

    [HarmonyPostfix]
    private static void Postfix()
    {
        if (!_active) return;
        PlayerController controller = PlayerController.instance;
        if (controller == null) return;
        controller.transform.position = _backup;
        if (controller.rb != null)
        {
            controller.rb.position = _backup;
            // Defensive addition after a second live report of being genuinely yeeted upward (not
            // just visually, for real) despite the position/rb.position mirror already in place -
            // writing raw .position twice in one frame on a non-kinematic Rigidbody (grounded/
            // walking, isKinematic is false here - Fly/Noclip's own kinematic path never had this
            // problem) can leave PhysX inferring a large implied velocity from the position delta
            // between the two writes. Zeroing velocity right after the restore, same defensive
            // pattern Fly already uses for its own direct-position-write approach, cancels
            // whatever spurious impulse that round trip may have produced instead of letting it
            // carry into the next physics step.
            controller.rb.velocity = Vector3.zero;
            controller.rb.angularVelocity = Vector3.zero;
        }
    }
}

// Invisible safety net - a general fallback instead of chasing each new physics trigger for the
// same underlying bug one at a time. InvisiblePatch's rb.position mirror + velocity zeroing above
// already fixed two live-reported triggers (sliding/Tumble, then a second unspecified one) for
// the "real position dragged toward the fake broadcast height, not just the visual" failure mode -
// a THIRD live report (2026-09-11: yeeted/thrown while Invisible, landed ~600 units up) shows that
// fix still isn't airtight against every non-kinematic state the game's physics can put
// PlayerController.rb through. Rather than find and patch each new trigger individually forever,
// this runs every tick completely independently of whether OnPhotonSerializeView happens to fire
// this frame (Photon's own network-send tick is throttled, not every-frame - the drag can happen
// and compound across several ticks in between), and snaps back to the last known-good position
// whenever the real position ends up implausibly high while Invisible is on, regardless of what
// specific interaction caused it this time.
internal static class InvisibleSafetyNet
{
    // Comfortably below InvisibleHeight's own minimum (500, see its slider in OverlayUI/
    // CustomOverlayUI) - the real position only reaches anywhere near this while being dragged
    // toward the fake broadcast height, never during ordinary grounded play (the game's own
    // out-of-bounds catch fires at y < -100, confirmed in the decompiled PlayerAvatar.cs - there's
    // no comparably extreme case on the way up in any normal level). Catching the drag early, well
    // before it reaches the actual spoofed height, also means less physics velocity to cancel.
    private const float SafeCeiling = 150f;

    private static Vector3 _lastGoodPosition;
    private static bool _hasGoodPosition;

    public static void Apply(PlayerController controller)
    {
        if (!CheatState.Invisible.Value)
        {
            _hasGoodPosition = false;
            return;
        }

        // Fly/Noclip/DeadFly can legitimately take you well above SafeCeiling on purpose - this
        // net exists to catch the UNINTENDED strand, not to fight a feature doing exactly what
        // it's supposed to. Treat wherever they take you as "good" so the net doesn't immediately
        // yank you back down the instant one of them is turned back off near a real high point.
        if (CheatState.Fly.Value || CheatState.Noclip.Value || CheatState.DeadFly.Value)
        {
            _lastGoodPosition = controller.transform.position;
            _hasGoodPosition = true;
            return;
        }

        Vector3 current = controller.transform.position;

        if (_hasGoodPosition && current.y > SafeCeiling)
        {
            controller.transform.position = _lastGoodPosition;
            if (controller.rb != null)
            {
                controller.rb.position = _lastGoodPosition;
                // Same defensive zeroing InvisiblePatch's own Postfix already needed - cancels
                // whatever velocity PhysX inferred from the position jump instead of letting a
                // spurious impulse carry into the next physics step and drag you right back up.
                controller.rb.velocity = Vector3.zero;
                controller.rb.angularVelocity = Vector3.zero;
            }
            return; // don't overwrite _lastGoodPosition with the bad height we just corrected
        }

        _lastGoodPosition = current;
        _hasGoodPosition = true;
    }
}

// "Broken Flashlight": a field nothing else in this file touches yet on the same broadcast call -
// PlayerAvatar.OnPhotonSerializeView also sends flashlightLightAim.clientAimPoint (public, confirmed
// in the decompiled source) whenever it's moved more than 0.2m since the last send. Confirmed in
// FlashlightLightAim.cs's own Update(): for every OTHER client watching you (the whole method is
// gated !playerAvatar.isLocal for this branch), that's the ONLY input steering where your flashlight
// fixture visually points - Quaternion.LookRotation toward it, clamped to 45 degrees off your own
// forward, smoothed with a Lerp/Slerp on their end. Whatever point we send, the receiving client's
// own clamp keeps the beam physically plausible (can't point backward through your skull) - it just
// can't tell a real aim point from a fake one, so circling the real point instead of sending it
// straight makes your beam sweep/jitter unpredictably for everyone else while yours looks completely
// normal (your own local flashlight direction comes from a totally separate isLocal-only branch of
// the same Update(), never touched here). Purely cosmetic - no position/physics/gameplay involved,
// same category as GlitchyVoice.
[HarmonyPatch(typeof(PlayerAvatar), "OnPhotonSerializeView")]
internal static class GlitchyFlashlightPatch
{
    private static bool _active;
    private static Vector3 _backup;
    private static float _phase;

    [HarmonyPrefix]
    private static void Prefix(PhotonStream stream, PlayerAvatar __instance)
    {
        _active = false;
        if (!CheatState.GlitchyFlashlight.Value) return;
        if (!stream.IsWriting || __instance.flashlightLightAim == null) return;

        _active = true;
        _phase += Time.deltaTime * 6f;
        float radius = CheatState.GlitchyFlashlightRadius.Value;

        _backup = __instance.flashlightLightAim.clientAimPoint;
        // Three different frequencies per axis so the sweep doesn't trace a flat circle/ellipse -
        // reads as an erratic wobble instead of an obviously mechanical loop.
        Vector3 offset = new(
            Mathf.Sin(_phase) * radius,
            Mathf.Sin(_phase * 1.7f + 1.3f) * radius,
            Mathf.Cos(_phase * 1.3f) * radius);
        __instance.flashlightLightAim.clientAimPoint = _backup + offset;
    }

    [HarmonyPostfix]
    private static void Postfix(PlayerAvatar __instance)
    {
        if (!_active) return;
        if (__instance.flashlightLightAim != null)
            __instance.flashlightLightAim.clientAimPoint = _backup;
    }
}

// "Ghost Rider" - see GhostRiderHack.cs's own comment for the full mechanism (physRiding/
// physRidingID/physRidingPosition, the same fields the game uses for standing on a moving cart).
// This patch is only the broadcast-spoof half - GhostRiderHack.Apply() (ticked separately from
// Patches.cs's CoreStats) does the actual "which object is nearest" scan and hands this patch a
// ready target, so the scan doesn't run on every OnPhotonSerializeView call.
[HarmonyPatch(typeof(PlayerAvatar), "OnPhotonSerializeView")]
internal static class GhostRiderPatch
{
    private static bool _active;
    private static bool _backupRiding;
    private static int _backupRidingId;
    private static Vector3 _backupRidingPos;

    [HarmonyPrefix]
    private static void Prefix(PhotonStream stream)
    {
        _active = false;
        if (!CheatState.GhostRider.Value || !GhostRiderHack.HasTarget) return;

        PlayerController controller = PlayerController.instance;
        if (!stream.IsWriting || controller == null || controller.CollisionGrounded == null) return;

        _active = true;
        _backupRiding = controller.CollisionGrounded.physRiding;
        _backupRidingId = controller.CollisionGrounded.physRidingID;
        _backupRidingPos = controller.CollisionGrounded.physRidingPosition;

        controller.CollisionGrounded.physRiding = true;
        controller.CollisionGrounded.physRidingID = GhostRiderHack.TargetViewId;
        controller.CollisionGrounded.physRidingPosition = GhostRiderHack.RidingLocalOffset;
    }

    [HarmonyPostfix]
    private static void Postfix()
    {
        if (!_active) return;
        PlayerController controller = PlayerController.instance;
        if (controller == null || controller.CollisionGrounded == null) return;

        controller.CollisionGrounded.physRiding = _backupRiding;
        controller.CollisionGrounded.physRidingID = _backupRidingId;
        controller.CollisionGrounded.physRidingPosition = _backupRidingPos;
    }
}

// "T-pose": there's no synced field anyone else's Animator reads that means "force the literal
// bind/rest pose" - animation for every player is independently recomputed on each observing
// client from a handful of synced movement flags (PlayerAvatar.OnPhotonSerializeView, confirmed
// in the decompiled source: isCrouching/isSprinting/isCrawling/isSliding/isMoving/rbVelocity/
// rbVelocityRaw are sent there, while position/rotation are sent as separate fields in the same
// stream). Zeroing all of those for the instant they're serialized - same backup/restore-around-
// the-call trick as AntiAimSpin/DinnerBone above - means every other client's
// PlayerAvatarVisuals.AnimationLogic() sees "not moving, not sprinting, not crouching, not
// crawling, not sliding" and settles its Animator into the idle/rest state, while your position
// (sent separately, untouched) keeps updating normally - so you visibly glide across the map
// frozen in a standing pose to everyone else. It's not literally arms-out T-pose (no synced
// channel can force that specific pose), but it's the closest broadcastable equivalent. Your own
// local simulation/animation never sees any of this - only the outgoing stream is touched.
[HarmonyPatch(typeof(PlayerAvatar), "OnPhotonSerializeView")]
internal static class TPosePatch
{
    private static bool _active;
    private static bool _backupCrouching, _backupSprinting, _backupCrawling, _backupSliding, _backupMoving;
    private static Vector3 _backupVelocity, _backupVelocityRaw;
    private static bool _loggedOnce;

    [HarmonyPrefix]
    private static void Prefix(PhotonStream stream, ref bool ___isCrouching, ref bool ___isSprinting,
        ref bool ___isCrawling, ref bool ___isSliding, ref bool ___isMoving,
        ref Vector3 ___rbVelocity, ref Vector3 ___rbVelocityRaw)
    {
        _active = false;
        if (!CheatState.TPose.Value) return;

        if (!_loggedOnce)
        {
            _loggedOnce = true;
            Plugin.Log.LogInfo("T-Pose: only changes what OTHER players' clients render for you - " +
                "your own animation/movement is untouched by design, check on a second client " +
                "rather than your own screen.");
        }

        if (!stream.IsWriting) return;

        _active = true;
        _backupCrouching = ___isCrouching;
        _backupSprinting = ___isSprinting;
        _backupCrawling = ___isCrawling;
        _backupSliding = ___isSliding;
        _backupMoving = ___isMoving;
        _backupVelocity = ___rbVelocity;
        _backupVelocityRaw = ___rbVelocityRaw;

        ___isCrouching = false;
        ___isSprinting = false;
        ___isCrawling = false;
        ___isSliding = false;
        ___isMoving = false;
        ___rbVelocity = Vector3.zero;
        ___rbVelocityRaw = Vector3.zero;
    }

    [HarmonyPostfix]
    private static void Postfix(ref bool ___isCrouching, ref bool ___isSprinting,
        ref bool ___isCrawling, ref bool ___isSliding, ref bool ___isMoving,
        ref Vector3 ___rbVelocity, ref Vector3 ___rbVelocityRaw)
    {
        if (!_active) return;

        ___isCrouching = _backupCrouching;
        ___isSprinting = _backupSprinting;
        ___isCrawling = _backupCrawling;
        ___isSliding = _backupSliding;
        ___isMoving = _backupMoving;
        ___rbVelocity = _backupVelocity;
        ___rbVelocityRaw = _backupVelocityRaw;
    }
}

// "Ghost Crawl": T-Pose's mirror image, same broadcast-flag trick. Confirmed in the decompiled
// PlayerAvatarVisuals.AnimationLogic() - Crawling forces the Animator's "Crawling" bool, and
// combined with Moving (left at its real value, not touched) that's the exact same condition the
// game's own crawl uses to reach the "Crawl Move" state (the method literally checks
// animator.GetCurrentAnimatorStateInfo(0).IsName("Crawl Move") a few lines below where it sets
// these bools). Crouching/Sprinting/Sliding are force-false alongside it purely so their own
// Animator bools don't contend with Crawling for the same state on the observer's machine - real
// values for those are restored in the Postfix like everything else here. Only the outgoing
// stream is touched: your own local Animator/movement/collision reads the real flags the whole
// time, so you run around completely normally while everyone else's screen shows you endlessly
// crawling - standing still crawls in place, sprinting crawls at full speed, no distance/speed
// side effect since position is a separate, untouched field in the same stream.
[HarmonyPatch(typeof(PlayerAvatar), "OnPhotonSerializeView")]
internal static class GhostCrawlPatch
{
    private static bool _active;
    private static bool _backupCrouching, _backupSprinting, _backupCrawling, _backupSliding;

    [HarmonyPrefix]
    private static void Prefix(PhotonStream stream, ref bool ___isCrouching, ref bool ___isSprinting,
        ref bool ___isCrawling, ref bool ___isSliding)
    {
        _active = false;
        if (!CheatState.GhostCrawl.Value) return;
        if (!stream.IsWriting) return;

        _active = true;
        _backupCrouching = ___isCrouching;
        _backupSprinting = ___isSprinting;
        _backupCrawling = ___isCrawling;
        _backupSliding = ___isSliding;

        ___isCrouching = false;
        ___isSprinting = false;
        ___isCrawling = true;
        ___isSliding = false;
    }

    [HarmonyPostfix]
    private static void Postfix(ref bool ___isCrouching, ref bool ___isSprinting,
        ref bool ___isCrawling, ref bool ___isSliding)
    {
        if (!_active) return;

        ___isCrouching = _backupCrouching;
        ___isSprinting = _backupSprinting;
        ___isCrawling = _backupCrawling;
        ___isSliding = _backupSliding;
    }
}

// Third Person's grab/aim fix: PlayerLocalCamera.GetOverrideTransform() (confirmed via a grep
// across the whole decompiled source) is THE canonical "where is the player aiming from" accessor
// - not something grab-specific, every tool, weapon, vehicle seat, and revive item reads it too.
// Normally (override inactive) it returns PlayerLocalCamera's own transform, which tracks
// CameraNoise.Instance every frame (PlayerLocalCamera.Update(), confirmed in the decompiled
// source) - and CameraNoise.Instance sits on the same object Camera.main does, so VisionHack's
// third-person pull-back directly drags this "aim origin" back with it. Reported live: items got
// grabbed from wherever the pulled-back camera was pointing instead of the crosshair. Fix:
// substitute a small persistent proxy Transform positioned at VisionHack.TrueAimPosition/
// TrueAimRotation (the real first-person pose, reconstructed independently of the displaced
// camera - see VisionHack.cs's own comment) whenever Third Person is active, so grabbing/aiming
// keeps working exactly like normal first person while the camera itself stays pulled back.
// Instance-checked against PlayerAvatar.instance so this only ever substitutes OUR OWN aim origin
// - every other player's/vehicle occupant's GetOverrideTransform() call (this method runs for
// them too, same class) is left completely alone. Read-only substitution, touches no player
// physics - the safe alternative to the CameraPosition.OverridePosition() native-API attempt
// VisionHack.cs's own comment documents as reverted after it sent the player into freefall.
[HarmonyPatch(typeof(PlayerLocalCamera), "GetOverrideTransform")]
internal static class ThirdPersonAimPatch
{
    private static Transform _aimProxy;

    [HarmonyPostfix]
    private static void Postfix(PlayerLocalCamera __instance, ref Transform __result)
    {
        if (!VisionHack.TrueAimPosition.HasValue || !VisionHack.TrueAimRotation.HasValue) return;
        if (__instance == null || __instance.playerAvatar != PlayerAvatar.instance) return;

        if (_aimProxy == null)
        {
            GameObject go = new GameObject("RepoCheatMenu_ThirdPersonAimProxy");
            Object.DontDestroyOnLoad(go);
            _aimProxy = go.transform;
        }
        _aimProxy.SetPositionAndRotation(VisionHack.TrueAimPosition.Value, VisionHack.TrueAimRotation.Value);
        __result = _aimProxy;
    }
}

[HarmonyPatch(typeof(PlayerVoiceChat), "Update")]
internal static class DeadVoiceHearPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerVoiceChat __instance, AudioSource ___audioSource, PlayerAvatar ___playerAvatar)
    {
        if (CheatState.VoiceFilter.Value != "Dead") return;
        if (___audioSource == null || ___playerAvatar == null) return;
        if (__instance == PlayerVoiceChat.instance) return;
        if (!GameReflect.Get<bool>(___playerAvatar, "deadSet")) return;

        if (___audioSource.outputAudioMixerGroup != __instance.mixerMicrophoneSound)
            ___audioSource.outputAudioMixerGroup = __instance.mixerMicrophoneSound;
        ___audioSource.mute = false;
        if (___audioSource.volume < 0.01f)
            ___audioSource.volume = 1f;
    }
}

// "Glitchy voice": chops your OWN outgoing Photon Voice transmission on and off on a fast timer,
// so everyone else hears you stuttering/cutting in and out like a broken connection - while your
// position, model, animation and everything else stay completely normal. Unlike Invisible this
// touches no broadcast position at all, so there's nothing to give you away visually.
// PlayerVoiceChat.Update() assigns recorder.TransmitEnabled from its own push-to-talk/mute logic
// every frame (confirmed in the decompiled source), so this has to run as a Postfix, re-forcing
// the value after the game's own write - a Prefix or a plain per-tick write from ApplyStats would
// just get overwritten again on the very same frame.
//
// Only ever forces transmission OFF, never ON: if you're legitimately muted or not holding
// push-to-talk, the game's own logic already set it false and this leaves that alone rather than
// forcing your mic open against your intent.
[HarmonyPatch(typeof(PlayerVoiceChat), "Update")]
internal static class GlitchyVoicePatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerVoiceChat __instance, Photon.Voice.Unity.Recorder ___recorder)
    {
        if (!CheatState.GlitchyVoice.Value) return;
        if (___recorder == null || __instance != PlayerVoiceChat.instance) return;

        float halfPeriod = Mathf.Max(0.05f, CheatState.GlitchyVoiceInterval.Value);
        bool transmitWindow = Mathf.Repeat(Time.unscaledTime, halfPeriod * 2f) < halfPeriod;
        if (!transmitWindow)
            ___recorder.TransmitEnabled = false;
    }
}

[HarmonyPatch(typeof(SemiFunc), "MasterOnlyRPC")]
internal static class MasterOnlyRpcSelfBypassPatch
{
    [HarmonyPrefix]
    private static bool Prefix(PhotonMessageInfo _info, ref bool __result)
    {
        if (!CheatState.MasterRpcSelfBypass.Value) return true;
        if (PhotonNetwork.LocalPlayer != null && _info.Sender == PhotonNetwork.LocalPlayer)
        {
            __result = true;
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(ChargingStation), "Update")]
internal static class ChargingStationInfinitePatch
{
    [HarmonyPrefix]
    private static void Prefix(ChargingStation __instance, ref int ___chargeInt, ref int ___chargeTotal,
        ref float ___chargeFloat, ref int ___chargeSegmentCurrent)
    {
        if (!CheatState.InfiniteChargingStation.Value) return;
        ___chargeInt = 10;
        ___chargeTotal = 100;
        ___chargeFloat = 1f;
        ___chargeSegmentCurrent = __instance.chargeSegments;
    }
}

[HarmonyPatch(typeof(ChargingStation), "OutOfCrystalsShutdown")]
internal static class ChargingStationShutdownBlockPatch
{
    [HarmonyPrefix]
    private static bool Prefix() => !CheatState.InfiniteChargingStation.Value;
}

[HarmonyPatch(typeof(ChargingStation), "ChargingStationCrystalBrokenRPC")]
internal static class ChargingStationCrystalBrokenBlockPatch
{
    [HarmonyPrefix]
    private static bool Prefix() => !CheatState.InfiniteChargingStation.Value;
}

[HarmonyPatch(typeof(PlayerDeathHead), "Trigger")]
internal static class PlayerDeathHeadTriggerPatch
{
    [HarmonyPostfix]
    private static void Postfix(PlayerDeathHead __instance, ref Vector3 ___triggeredPosition)
    {
        PlayerAvatar avatar = __instance.playerAvatar;
        if (avatar == null) return;

        if (avatar == PlayerAvatar.instance)
        {
            if (CheatState.DeathAutoToTruck.Value && TruckSafetySpawnPoint.instance != null)
            {
                ___triggeredPosition = TruckSafetySpawnPoint.instance.transform.position;
            }
            else if (CheatState.DeathAutoToExtraction.Value)
            {
                ExtractionPoint point = SemiFunc.ExtractionPointGetNearest(___triggeredPosition);
                if (point != null) ___triggeredPosition = point.transform.position;
            }
        }

        if (CheatState.TeammateDeathDropBead.Value) DropBead(avatar.transform.position);
    }

    private static void DropBead(Vector3 position)
    {
        GameObject prefab = AssetManager.instance != null ? AssetManager.instance.enemyValuableSmall : null;
        if (prefab == null) return;

        if (GameManager.Multiplayer())
            PhotonNetwork.InstantiateRoomObject("Valuables/" + prefab.name, position, Quaternion.identity, 0);
        else
            UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
    }
}


// ── Anti-crash ───────────────────────────────────────────────────────────────
// Every inbound RPC funnels through PhotonNetwork.ExecuteRpc(Hashtable, Player) and every raw
// inbound event through PhotonNetwork.OnEvent(EventData) - confirmed in the decompiled PUN source.
// Patching those two chokepoints covers the whole attack surface from other clients at once. The
// prefix drops abusive traffic (oversized args, floods) before the game ever sees it; the finalizer
// swallows anything that still throws so one bad packet can't tear down Photon's dispatch loop -
// the client may lag under a flood but stays in the room instead of crashing.

// NOT attribute-patched (no [HarmonyPatch(typeof(PhotonNetwork), ...)] here) - PatchAll() runs
// in Plugin.Awake(), which on BepInEx fires before the game's own first touch of the PhotonNetwork
// type (DataDirector.PhotonSetAppId/SetRegion, called from NetworkConnect.Start - see decompiled
// source). Attribute-patching a method declared directly on PhotonNetwork forces that type's
// static constructor (which builds NetworkingClient) to run then, under our uninitialized
// conditions instead of the game's - same "premature LoadBalancingClient" failure already
// diagnosed for PacketTestHack.Init/NetworkDiagnostics.Apply, except this one couldn't be fixed
// by gating on a toggle since attribute-patch ATTACHMENT itself is what triggers it, unconditionally,
// regardless of CheatState.AntiCrash.Value. Confirmed the hard way: with this patched via PatchAll,
// the public server list spun forever loading with the mod installed, worked fine without it.
// Fix: patch these two manually, deferred until DeferredAntiCrashInstallPatch (below) fires -
// i.e. strictly after NetworkConnect.Start() has already run the game's own Photon setup.
internal static class AntiCrashExecuteRpcPatch
{
    private const int ArgArrayMax = 64;
    private const int QuarantineStrikes = 3;
    private static int _lastLogTick;

    private static bool Prefix(ExitGames.Client.Photon.Hashtable rpcData, Player sender)
    {
        if (!CheatState.AntiCrash.Value) return true;

        int actor = sender != null ? sender.ActorNumber : -1;

        int argCount = 0;
        string longest = null;
        if (rpcData != null && rpcData.ContainsKey((byte)4) && rpcData[(byte)4] is object[] args)
        {
            argCount = args.Length;
            foreach (object o in args)
                if (o is string str && (longest == null || str.Length > longest.Length))
                    longest = str;
        }

        return AntiCrashHack.CheckRpc(actor,
            CheatState.AntiCrashMaxArgStringLength.Value, ArgArrayMax,
            CheatState.AntiCrashMaxRpcsPerSecond.Value,
            CheatState.AntiCrashQuarantine.Value,
            CheatState.AntiCrashQuarantineSeconds.Value, QuarantineStrikes,
            System.Environment.TickCount, longest, argCount);
    }

    private static System.Exception Finalizer(System.Exception __exception)
    {
        if (__exception == null || !CheatState.AntiCrash.Value) return null;
        AntiCrashHack.ExceptionsSwallowed++;
        NoteSwallowed(__exception);
        return null; // swallow: a malformed RPC dies on its own, not the whole dispatch loop
    }

    internal static void NoteSwallowed(System.Exception ex)
    {
        int now = System.Environment.TickCount;
        if (now - _lastLogTick < 2000) return; // throttle: log spam under a flood is itself a slowdown
        _lastLogTick = now;
        Plugin.Log.LogWarning($"[AntiCrash] Swallowed a network exception (total {AntiCrashHack.ExceptionsSwallowed}): {ex.GetType().Name}: {ex.Message}");
    }
}

// See AntiCrashExecuteRpcPatch's comment above - same deferred-manual-patch reasoning, same fix.
internal static class AntiCrashOnEventPatch
{
    private const int QuarantineStrikes = 3;

    private static bool Prefix(ExitGames.Client.Photon.EventData photonEvent)
    {
        if (!CheatState.AntiCrash.Value || photonEvent == null) return true;

        // Code 200 (RPC) routes into ExecuteRpc, already gated above - don't double-count it here.
        // 202 is networked Instantiate, the classic spawn-flood crash vector.
        if (photonEvent.Code != 202) return true;

        return AntiCrashHack.CheckInstantiate(photonEvent.Sender,
            CheatState.AntiCrashMaxInstantiatesPerSecond.Value,
            CheatState.AntiCrashQuarantine.Value,
            CheatState.AntiCrashQuarantineSeconds.Value, QuarantineStrikes,
            System.Environment.TickCount);
    }

    private static System.Exception Finalizer(System.Exception __exception)
    {
        if (__exception == null || !CheatState.AntiCrash.Value) return null;
        AntiCrashHack.ExceptionsSwallowed++;
        AntiCrashExecuteRpcPatch.NoteSwallowed(__exception);
        return null;
    }
}

// Installs the two AntiCrash PhotonNetwork patches above, once, right after NetworkConnect.Start()
// has already run the game's own Photon setup (PhotonSetRegion/Version/AppId, then
// PhotonNetwork.Disconnect()) - see AntiCrashExecuteRpcPatch's comment for why this can't be
// attribute-patched via PatchAll instead.
[HarmonyPatch(typeof(NetworkConnect), "Start")]
internal static class DeferredAntiCrashInstallPatch
{
    private static bool _installed;

    [HarmonyPostfix]
    private static void Postfix()
    {
        if (_installed) return;
        _installed = true;

        Harmony harmony = Plugin.HarmonyInstance;
        harmony.Patch(AccessTools.Method(typeof(PhotonNetwork), "ExecuteRpc"),
            prefix: new HarmonyMethod(AccessTools.Method(typeof(AntiCrashExecuteRpcPatch), "Prefix")),
            finalizer: new HarmonyMethod(AccessTools.Method(typeof(AntiCrashExecuteRpcPatch), "Finalizer")));
        harmony.Patch(AccessTools.Method(typeof(PhotonNetwork), "OnEvent"),
            prefix: new HarmonyMethod(AccessTools.Method(typeof(AntiCrashOnEventPatch), "Prefix")),
            finalizer: new HarmonyMethod(AccessTools.Method(typeof(AntiCrashOnEventPatch), "Finalizer")));

        Plugin.Log.LogInfo("[AntiCrash] PhotonNetwork.ExecuteRpc/OnEvent patched (deferred, post NetworkConnect.Start).");
    }
}

// String-argument clamp for the two game RPCs that take player-supplied text straight to a Unity
// text mesh - a huge string here is a one-packet crash. The whole-RPC size drop above is the
// blanket guard; this trims a merely-long string down instead of dropping it, so a legit-but-long
// chat line/name still shows (truncated) rather than vanishing.
[HarmonyPatch(typeof(PlayerAvatar), "ChatMessageSendRPC")]
internal static class AntiCrashChatClampPatch
{
    [HarmonyPrefix]
    private static void Prefix(ref string _message)
    {
        if (!CheatState.AntiCrash.Value || _message == null) return;
        if (_message.Length > 200) _message = _message.Substring(0, 200);
    }
}

[HarmonyPatch(typeof(PlayerAvatar), "AddToStatsManagerRPC")]
internal static class AntiCrashNameClampPatch
{
    [HarmonyPrefix]
    private static void Prefix(ref string _playerName, ref string _steamID)
    {
        if (!CheatState.AntiCrash.Value) return;
        if (_playerName != null && _playerName.Length > 64) _playerName = _playerName.Substring(0, 64);
        if (_steamID != null && _steamID.Length > 64) _steamID = _steamID.Substring(0, 64);
    }
}
