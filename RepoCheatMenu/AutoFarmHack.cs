// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace RepoCheatMenu;

// Auto-Farm: a self-driving looter, closer to a Minecraft "Baritone goto" than a full bot - it
// doesn't see or reason about the map, it just already knows exactly where every valuable and
// extraction point is (ValuableDirector.valuableList / RoundDirector.extractionPointList, the
// same god's-eye-view info every other World-tab hack already reads). Getting there is real
// though: it drives your actual character over the level's baked Unity NavMesh (confirmed present
// in the decompiled source - every EnemyNavMeshAgent rides the exact same static NavMesh data,
// generated identically on every client since level geometry isn't master-gated, only gameplay
// logic is) using the same two input channels a human uses:
//   - InputManager.GetMovementY is patched (see AutoFarmMovementPatch in Patches.cs) to report
//     "holding forward" while a path is active, so the game's own PlayerController.FixedUpdate
//     walks you exactly like a real W press would - sprint/crouch/slide/stairs/physics all still
//     the game's own code, nothing here reimplements movement physics.
//   - CameraAim.SetPlayerAim (confirmed public) steers your look direction each physics step -
//     PlayerController.FixedUpdate reads cameraGameObject's yaw to set the body's own facing
//     every tick (confirmed in the decompiled source), so turning the camera IS turning where you
//     walk. This means the bot visibly looks around as it navigates, same as Fly/Noclip already
//     take over full movement - intended for genuinely stepping away, not for running alongside
//     manual mouse control.
// There's no separate "flight path" trick here like LootMagnet's Teleport() - every meter is
// earned on foot, so it opens doors, climbs stairs, and can get stuck on stuff exactly like a
// real player would.
//
// Deliberately best-effort about what it can reach, same spirit as real Baritone: if
// NavMesh.CalculatePath comes back Partial/Invalid for a target, or the player doesn't visibly
// make progress for a few seconds (stuck on a closed secret door, a mechanism it can't operate,
// geometry the mesh disagrees with), that target gets benched for a while and it moves on to the
// next reachable one instead of grinding forever against something it can't solve.
internal static class AutoFarmHack
{
    private enum State { Idle, MovingToItem, MovingToExtraction }

    private const float PlanInterval = 0.3f;
    private const float RepathInterval = 1.5f;
    private const float CornerArriveDistance = 0.7f;
    private const float GrabRange = 2.3f;
    private const float DropDistance = 1.6f;
    private const float SearchRadius = 80f;
    private const float NavSampleRange = 4f;
    private const int MaxCandidatesTried = 3;
    private const float StuckCheckInterval = 2.5f;
    private const float StuckDistance = 0.5f;
    private const float BlacklistTime = 25f;
    private const float ExtractionActivateRetry = 3f;
    private const float TurnSpeedDegPerSec = 260f;

    private static State _state = State.Idle;
    private static PhysGrabObject _targetItem;
    private static ExtractionPoint _targetExtraction;
    private static NavMeshPath _path;
    private static int _corner;
    private static float _nextRepath;
    private static float _nextPlan;
    private static float _nextStuckCheck;
    private static float _nextExtractionActivateTry;
    private static Vector3 _stuckAnchor;
    private static readonly Dictionary<int, float> _blacklist = new();

    // Read every physics step by AutoFarmMovementPatch (Patches.cs) to fake "holding forward".
    internal static bool WantForward;

    // NavMeshSurface.BuildNavMesh() (confirmed in the decompiled LevelGenerator.NavMeshSetupRPC)
    // only ever runs behind "if (SemiFunc.MasterOnlyRPC(_info))" - so a guest's own client NEVER
    // bakes a NavMesh at all, CalculatePath silently returns nothing, and the bot just stands
    // there forever. The fix: the NavMeshSurface component itself, and the level colliders it
    // bakes from, exist locally on every client regardless of host status (collision/walking
    // already works normally for a guest) - so we just call BuildNavMesh() ourselves, directly,
    // client-side, same loophole as every other "only master normally does this, but nothing
    // stops us from doing it locally" trick in this mod. Works for guest or host.
    private static NavMeshSurface _bakedSurface;

    // World-space line along the current path, updated whenever it changes - same idea as
    // Baritone's path render.
    private static LineRenderer _pathLine;

    public static void Apply()
    {
        if (!CheatState.AutoFarm.Value || !GameState.IsInGame())
        {
            Reset();
            return;
        }
        // Ghost: same hard block as AutoLoot - we can't actually pick anything up, so running the
        // path-plan + teleport-step logic every tick accomplishes nothing. Reset and bail.
        if (GhostObserverHack.LocalRpcIsMuted)
        {
            Reset();
            return;
        }

        PlayerAvatar local = PlayerAvatar.instance;
        PlayerController controller = PlayerController.instance;
        if (local == null || controller == null || GameReflect.Get<bool>(local, "deadSet"))
        {
            Reset();
            return;
        }

        if (Time.unscaledTime < _nextPlan) return;
        _nextPlan = Time.unscaledTime + PlanInterval;

        EnsureNavMeshBaked();

        PhysGrabber grabber = local.physGrabber;
        if (grabber == null) return;

        CheckStuck(controller);

        switch (_state)
        {
            case State.Idle:
                if (GameReflect.Get<bool>(grabber, "grabbed"))
                    StartMovingToExtraction(); // already holding something (manual grab, or leftover) - deliver it
                else
                    PickNextItem(controller);
                break;

            case State.MovingToItem:
                TickMovingToItem(controller, grabber);
                break;

            case State.MovingToExtraction:
                TickMovingToExtraction(controller, grabber);
                break;
        }
    }

    // Called every FixedUpdate (PlayerControllerFixedUpdatePatch in Patches.cs) - purely steers
    // toward the current path corner, all target/state decisions happen on the slower Plan tick.
    public static void ApplyFixed(PlayerController controller)
    {
        if (!CheatState.AutoFarm.Value || _state == State.Idle || _path == null || _path.corners.Length == 0 || controller == null)
        {
            WantForward = false;
            return;
        }

        Vector3 pos = controller.transform.position;
        Vector3 corner = _path.corners[Mathf.Min(_corner, _path.corners.Length - 1)];
        Vector3 flat = corner - pos;
        flat.y = 0f;

        if (flat.magnitude < CornerArriveDistance)
        {
            if (_corner < _path.corners.Length - 1)
            {
                _corner++;
                corner = _path.corners[_corner];
                flat = corner - pos;
                flat.y = 0f;
            }
        }

        if (flat.sqrMagnitude < 0.01f)
        {
            WantForward = false;
            return;
        }

        Transform camTransform = controller.cameraGameObject != null ? controller.cameraGameObject.transform : null;
        CameraAim aim = CameraAim.Instance;
        if (camTransform != null && aim != null)
        {
            Quaternion desired = Quaternion.LookRotation(flat.normalized, Vector3.up);
            Quaternion smoothed = Quaternion.RotateTowards(camTransform.rotation, desired, TurnSpeedDegPerSec * Time.fixedDeltaTime);
            aim.SetPlayerAim(smoothed, true);
        }

        WantForward = true;
    }

    private static void Reset()
    {
        _state = State.Idle;
        _targetItem = null;
        _targetExtraction = null;
        _path = null;
        WantForward = false;
        HidePathLine();
    }

    // Level geometry (and therefore the NavMeshSurface component sitting on LevelGenerator) is
    // torn down and rebuilt fresh each level, so _bakedSurface going stale/null or pointing at a
    // destroyed object both mean "new level, need to bake again".
    //
    // BuildNavMesh() is a heavyweight synchronous main-thread bake - suspected culprit behind a
    // reported hang. Object.FindFirstObjectByType<NavMeshSurface>() has no guarantee of returning
    // the SAME instance every call if two momentarily coexist (old level's LevelGenerator not yet
    // destroyed while the new one has already spawned its own during a level transition) - if
    // that ever flip-flops, _bakedSurface's null-check alone would happily re-trigger a full
    // rebake every single Plan tick (every 0.3s) forever, which is exactly the kind of sustained
    // main-thread stall that would look like a freeze. _nextBakeAttempt is a hard floor
    // independent of that logic - even in the worst case, a rebake can't fire more than once
    // every BakeCooldown seconds.
    private static float _nextBakeAttempt;
    private const float BakeCooldown = 5f;

    private static void EnsureNavMeshBaked()
    {
        if (_bakedSurface != null) return;
        if (Time.unscaledTime < _nextBakeAttempt) return;
        _nextBakeAttempt = Time.unscaledTime + BakeCooldown;

        NavMeshSurface surface = Object.FindFirstObjectByType<NavMeshSurface>();
        if (surface == null) return;

        try
        {
            surface.BuildNavMesh();
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogError("[AutoFarm] BuildNavMesh threw, backing off: " + e);
            return;
        }

        _bakedSurface = surface;
        Plugin.Log.LogInfo("[AutoFarm] Baked local NavMesh client-side (bypassing the master-only NavMeshSetupRPC gate).");
    }

    private static void UpdatePathLine()
    {
        if (_path == null || _path.corners.Length < 2)
        {
            HidePathLine();
            return;
        }

        if (_pathLine == null)
        {
            var go = new GameObject("RepoCheatMenu_AutoFarmPath");
            Object.DontDestroyOnLoad(go);
            _pathLine = go.AddComponent<LineRenderer>();
            _pathLine.material = new Material(Shader.Find("Unlit/Color")) { color = Color.magenta };
            _pathLine.startColor = Color.magenta;
            _pathLine.endColor = Color.magenta;
            _pathLine.startWidth = 0.08f;
            _pathLine.endWidth = 0.08f;
            _pathLine.useWorldSpace = true;
        }

        _pathLine.gameObject.SetActive(true);
        _pathLine.positionCount = _path.corners.Length;
        for (int i = 0; i < _path.corners.Length; i++)
            _pathLine.SetPosition(i, _path.corners[i] + Vector3.up * 0.1f);
    }

    private static void HidePathLine()
    {
        if (_pathLine != null)
            _pathLine.gameObject.SetActive(false);
    }

    private static void CheckStuck(PlayerController controller)
    {
        if (Time.unscaledTime < _nextStuckCheck) return;
        _nextStuckCheck = Time.unscaledTime + StuckCheckInterval;

        Vector3 pos = controller.transform.position;
        if (_state != State.Idle && Vector3.Distance(pos, _stuckAnchor) < StuckDistance)
        {
            Plugin.Log.LogInfo($"[AutoFarm] Stuck in {_state}, abandoning current target.");
            if (_state == State.MovingToItem && _targetItem != null)
                _blacklist[_targetItem.GetInstanceID()] = Time.unscaledTime + BlacklistTime;

            Reset();
        }
        _stuckAnchor = pos;
    }

    // If a whole scan turns up nothing pathable, back off instead of immediately retrying next
    // Plan tick (0.3s later) - otherwise a level with everything currently blacklisted/unreachable
    // would hammer NavMesh.CalculatePath at that same 0.3s rate forever, which is exactly the kind
    // of sustained main-thread load that could look like a freeze on a slow/degenerate mesh.
    private const float NoTargetBackoff = 4f;
    private static float _nextPickAttempt;

    private static void PickNextItem(PlayerController controller)
    {
        if (Time.unscaledTime < _nextPickAttempt) return;

        ValuableDirector director = ValuableDirector.instance;
        if (director == null) return;

        List<ValuableObject> valuables = director.valuableList;
        if (valuables == null || valuables.Count == 0)
        {
            _nextPickAttempt = Time.unscaledTime + NoTargetBackoff;
            return;
        }

        Vector3 pos = controller.transform.position;
        var candidates = new List<(float dist, PhysGrabObject obj)>();

        foreach (ValuableObject valuable in valuables)
        {
            if (valuable == null) continue;

            PhysGrabObject physObject = GameReflect.Get<PhysGrabObject>(valuable, "physGrabObject");
            if (physObject == null) continue;

            if (_blacklist.TryGetValue(physObject.GetInstanceID(), out float expires))
            {
                if (Time.unscaledTime < expires) continue;
                _blacklist.Remove(physObject.GetInstanceID());
            }

            List<PhysGrabber> playerGrabbing = GameReflect.Get<List<PhysGrabber>>(physObject, "playerGrabbing");
            if (playerGrabbing != null && playerGrabbing.Count > 0) continue; // someone's holding it

            float d = Vector3.Distance(pos, physObject.transform.position);
            if (d > SearchRadius) continue;

            candidates.Add((d, physObject));
        }

        if (candidates.Count == 0)
        {
            _nextPickAttempt = Time.unscaledTime + NoTargetBackoff;
            return;
        }
        candidates.Sort((a, b) => a.dist.CompareTo(b.dist));

        int tried = 0;
        foreach (var (_, obj) in candidates)
        {
            if (tried++ >= MaxCandidatesTried) break;

            if (!TryComputePath(pos, obj.transform.position, out NavMeshPath path))
            {
                _blacklist[obj.GetInstanceID()] = Time.unscaledTime + BlacklistTime;
                continue;
            }

            _targetItem = obj;
            _path = path;
            _corner = 0;
            _nextRepath = Time.unscaledTime + RepathInterval;
            _state = State.MovingToItem;
            _stuckAnchor = pos;
            UpdatePathLine();
            Plugin.Log.LogInfo($"[AutoFarm] Targeting valuable at {obj.transform.position} ({tried} candidates checked).");
            return;
        }

        // Every candidate we tried this pass failed to path - back off instead of immediately
        // re-scanning next tick.
        _nextPickAttempt = Time.unscaledTime + NoTargetBackoff;
    }

    private static void TickMovingToItem(PlayerController controller, PhysGrabber grabber)
    {
        if (_targetItem == null)
        {
            Reset();
            return;
        }

        List<PhysGrabber> playerGrabbing = GameReflect.Get<List<PhysGrabber>>(_targetItem, "playerGrabbing");
        if (playerGrabbing != null && playerGrabbing.Count > 0 && !playerGrabbing.Contains(grabber))
        {
            // someone else grabbed it while we were en route - give up on it, pick something else
            Reset();
            return;
        }

        Vector3 pos = controller.transform.position;
        Vector3 targetPos = _targetItem.transform.position;
        float dist = Vector3.Distance(pos, targetPos);

        if (dist <= GrabRange)
        {
            grabber.OverrideGrab(_targetItem);
            Plugin.Log.LogInfo("[AutoFarm] Grabbed valuable, heading to extraction.");
            StartMovingToExtraction();
            return;
        }

        MaybeRepath(pos, targetPos);
    }

    private static void StartMovingToExtraction()
    {
        _targetItem = null;
        _state = State.MovingToExtraction;
        _path = null;
        _corner = 0;
        _stuckAnchor = PlayerController.instance != null ? PlayerController.instance.transform.position : Vector3.zero;
    }

    private static void TickMovingToExtraction(PlayerController controller, PhysGrabber grabber)
    {
        if (!GameReflect.Get<bool>(grabber, "grabbed"))
        {
            // lost/dropped/stolen mid-carry - go pick another
            Reset();
            return;
        }

        Vector3 pos = controller.transform.position;

        if (_targetExtraction == null || GameReflect.Get<ExtractionPoint.State>(_targetExtraction, "currentState") != ExtractionPoint.State.Active)
        {
            _targetExtraction = EconomyHack.FindActiveExtractionPoint(pos);
            _path = null;

            if (_targetExtraction == null)
            {
                if (Time.unscaledTime >= _nextExtractionActivateTry)
                {
                    _nextExtractionActivateTry = Time.unscaledTime + ExtractionActivateRetry;
                    TryActivateExtraction();
                }
                return; // stand still (holding the item) until one comes online
            }
        }

        if (_targetExtraction.extractionArea == null) return;

        Vector3 targetPos = _targetExtraction.extractionArea.transform.position;
        float dist = Vector3.Distance(pos, targetPos);

        if (dist <= DropDistance)
        {
            grabber.ReleaseObject(-1);
            Plugin.Log.LogInfo("[AutoFarm] Delivered valuable to extraction.");
            Reset();
            return;
        }

        MaybeRepath(pos, targetPos);
    }

    private static void TryActivateExtraction()
    {
        PlayerAvatar local = PlayerAvatar.instance;
        if (local != null) ExtractionActivatorHack.RequestNearestIdle(local.transform.position);
    }

    private static void MaybeRepath(Vector3 from, Vector3 to)
    {
        if (Time.unscaledTime < _nextRepath && _path != null && _path.corners.Length > 0) return;
        _nextRepath = Time.unscaledTime + RepathInterval;

        if (!TryComputePath(from, to, out NavMeshPath path))
        {
            if (_state == State.MovingToItem && _targetItem != null)
                _blacklist[_targetItem.GetInstanceID()] = Time.unscaledTime + BlacklistTime;
            Reset();
            return;
        }

        _path = path;
        _corner = 0;
        UpdatePathLine();
    }

    private static bool TryComputePath(Vector3 from, Vector3 to, out NavMeshPath path)
    {
        path = new NavMeshPath();
        if (!NavMesh.SamplePosition(to, out NavMeshHit hit, NavSampleRange, NavMesh.AllAreas)) return false;
        if (!NavMesh.CalculatePath(from, hit.position, NavMesh.AllAreas, path)) return false;
        return path.status == NavMeshPathStatus.PathComplete && path.corners.Length > 0;
    }
}
