using System.Collections.Generic;
using UnityEngine;

namespace RepoCheatMenu;

// "Teleporter" - move yourself to a player/enemy, or pull one to you.
//
// Self-teleport (TP to Player / TP to Enemy) is a plain local transform+Rigidbody write, same
// as LateJoinFixHack.TeleportToNearestPlayer - no network authority needed at all, works for
// guest or host.
//
// Pulling a PLAYER to you needs the same tumble-state dance as RaidHack's Cage/Yeet
// (KnockdownHack.TriggerImpact first, then PhysGrabObject.Teleport only once isTumbling
// actually flips) - confirmed in KnockdownHack.ApplyFixed's comment, a non-tumbling player's
// PlayerTumble.rb just follows their own PlayerController transform every Update, so teleporting
// it directly would get silently overwritten the very next frame.
//
// Pulling an ENEMY to you has no such restriction - confirmed in the decompiled
// EnemyRigidbody.Teleport(): enemies carry their own PhysGrabObject (the same component every
// grabbable prop/valuable has - it's literally how players grab and throw enemies in vanilla),
// and the AI's own reposition logic already rides PhysGrabObject.Teleport() directly, no tumble
// equivalent involved. So the SetPositionRPC loophole (applied by the master with zero
// validation, same as LootMagnet/PowerGrab/Banish) works immediately - no setup RPC, no waiting.
internal static class TeleportHack
{
    private const float PullTimeout = 1.5f;

    private static PlayerAvatar _pendingPull;
    private static float _pullDeadline;

    public static void TeleportToPlayer()
    {
        PlayerAvatar target = FindPlayerTarget();
        PlayerController controller = PlayerController.instance;
        if (target == null || controller == null || controller.rb == null)
        {
            Plugin.Log.LogInfo("TP to Player had no effect - no target player selected.");
            return;
        }

        Vector3 from = controller.transform.position;
        Vector3 pos = target.transform.position + target.transform.forward * -1.5f + Vector3.up * 0.2f;
        controller.rb.velocity = Vector3.zero;
        controller.rb.position = pos;
        controller.transform.position = pos;

        EventLog.Write("teleport", $"self -> player {PlayerTargets.NameOf(target)}: " +
            $"{EventLog.Pos(from)} -> {EventLog.Pos(pos)} (dist {Vector3.Distance(from, pos):0.#}m)");
    }

    public static void PullPlayerToMe()
    {
        PlayerAvatar local = PlayerAvatar.instance;
        PlayerAvatar target = FindPlayerTarget();
        if (local == null || target == null)
        {
            Plugin.Log.LogInfo("Pull Player had no effect - no target player selected.");
            return;
        }

        KnockdownHack.TriggerImpact(local, target);
        _pendingPull = target;
        _pullDeadline = Time.unscaledTime + PullTimeout;
    }

    // Ticked every frame (RunSafe in Patches.cs) - same wait pattern as RaidHack.ApplyYeetPending,
    // just pulls to our own current position once the impact actually lands instead of flinging away.
    public static void ApplyPullPending()
    {
        if (_pendingPull == null) return;

        if (Time.unscaledTime >= _pullDeadline)
        {
            _pendingPull = null;
            return;
        }

        if (!GameReflect.Get<bool>(_pendingPull, "isTumbling")) return;

        PlayerAvatar local = PlayerAvatar.instance;
        if (local != null)
        {
            PlayerTumble tumble = GameReflect.Get<PlayerTumble>(_pendingPull, "tumble");
            PhysGrabObject body = tumble != null ? GameReflect.Get<PhysGrabObject>(tumble, "physGrabObject") : null;
            Vector3 from = _pendingPull.transform.position;
            Vector3 to = local.transform.position + local.transform.forward * 1.5f + Vector3.up * 0.2f;
            body?.Teleport(to, local.transform.rotation);

            EventLog.Write("teleport", $"pull player {PlayerTargets.NameOf(_pendingPull)} -> me: " +
                $"{EventLog.Pos(from)} -> {EventLog.Pos(to)}" + (body == null ? " (NO physGrabObject - no-op)" : ""));
        }
        _pendingPull = null;
    }

    public static void TeleportToEnemy()
    {
        EnemyParent enemy = FindNearestEnemy();
        PlayerController controller = PlayerController.instance;
        if (enemy == null || controller == null || controller.rb == null)
        {
            Plugin.Log.LogInfo("TP to Enemy had no effect - no enemy currently spawned.");
            return;
        }

        Vector3 from = controller.transform.position;
        Vector3 pos = enemy.transform.position + Vector3.up * 0.2f;
        controller.rb.velocity = Vector3.zero;
        controller.rb.position = pos;
        controller.transform.position = pos;

        EventLog.Write("teleport", $"self -> enemy {GameReflect.Get<string>(enemy, "enemyName")}: " +
            $"{EventLog.Pos(from)} -> {EventLog.Pos(pos)} (dist {Vector3.Distance(from, pos):0.#}m)");
    }

    public static void PullEnemyToMe()
    {
        PlayerAvatar local = PlayerAvatar.instance;
        EnemyParent enemyParent = FindNearestEnemy();
        if (local == null || enemyParent == null)
        {
            Plugin.Log.LogInfo("Pull Enemy had no effect - no enemy currently spawned.");
            return;
        }

        PhysGrabObject body = GetEnemyBody(enemyParent);
        if (body == null) return;

        Vector3 pos = local.transform.position + local.transform.forward * 2f + Vector3.up * 0.5f;
        body.Teleport(pos, local.transform.rotation);
    }

    // "Swarm the Group" - finds whichever cluster of players is currently biggest (most players
    // within ClusterRadius of each other - e.g. 4 people camped in one room) and sends the
    // nearest available enemy straight there via the same PhysGrabObject.Teleport() loophole
    // PullEnemyToMe already confirmed. Bigger gathering always wins over a closer-but-smaller one -
    // that's the whole point, punish grouping up instead of splitting the enemy's attention evenly.
    private const float ClusterRadius = 12f;
    private const float SwarmInterval = 8f;
    private static float _swarmNextTime;

    public static void SwarmTheGroup()
    {
        Vector3? cluster = FindBiggestClusterCenter(out int count);
        if (cluster == null)
        {
            Plugin.Log.LogInfo("Swarm the Group had no effect - no players found.");
            return;
        }

        EnemyParent enemy = FindNearestEnemyTo(cluster.Value);
        if (enemy == null)
        {
            Plugin.Log.LogInfo("Swarm the Group had no effect - no enemy currently spawned.");
            return;
        }

        PhysGrabObject body = GetEnemyBody(enemy);
        if (body == null) return;

        body.Teleport(cluster.Value, enemy.transform.rotation);
        Plugin.Log.LogInfo($"Swarm the Group: sent an enemy to a group of {count} player(s).");
    }

    // Toggle version - re-sends an enemy to whatever the biggest cluster currently is every
    // few seconds, so people can't just group up once and feel safe for the rest of the level.
    public static void ApplySwarmStorm()
    {
        if (!CheatState.SwarmStorm.Value) return;
        if (Time.unscaledTime < _swarmNextTime) return;
        _swarmNextTime = Time.unscaledTime + SwarmInterval;

        Vector3? cluster = FindBiggestClusterCenter(out int count);
        if (cluster == null || count < 2) return; // don't bother chasing a single lone player

        EnemyParent enemy = FindNearestEnemyTo(cluster.Value);
        PhysGrabObject body = enemy != null ? GetEnemyBody(enemy) : null;
        body?.Teleport(cluster.Value, enemy.transform.rotation);
    }

    // Trap Room - Swarm the Group's targeting, but also rips every door within TrapDoorRadius of
    // the cluster off wherever it's hinged and arranges them in a ring around the group, facing
    // inward - an improvised wall built out of the doors themselves, not just each one jammed
    // shut in its own frame. Same PhysGrabObject.Teleport(position, rotation) loophole as
    // everything else; nothing disables the door's own HingeJoint, so this is winning a tug of
    // war against it every tick the same way Player Cage already does against a body's physics -
    // holds as long as the toggle stays on, not a guaranteed permanent barrier, and multiple
    // doors sharing tight quarters can clip into each other or the room's own geometry since nothing
    // here checks for overlap the way LootMagnet's SafeFlightHelper does for a slower pull.
    private const float TrapDoorRadius = 15f;
    private const float TrapSwarmInterval = 8f;
    private const float WallRingRadius = 3f;
    private static float _trapNextSwarm;

    public static void ApplyTrapRoom()
    {
        if (!CheatState.TrapRoom.Value) return;

        Vector3? cluster = FindBiggestClusterCenter(out int count);
        if (cluster == null || count < 2) return;
        Vector3 center = cluster.Value;

        if (Time.unscaledTime >= _trapNextSwarm)
        {
            EnemyParent enemy = FindNearestEnemyTo(center);
            PhysGrabObject enemyBody = enemy != null ? GetEnemyBody(enemy) : null;
            enemyBody?.Teleport(center, enemy.transform.rotation);
            _trapNextSwarm = Time.unscaledTime + TrapSwarmInterval;
        }

        List<PhysGrabHinge> nearbyDoors = new();
        foreach (PhysGrabHinge hinge in Object.FindObjectsByType<PhysGrabHinge>(FindObjectsSortMode.None))
        {
            if (hinge == null) continue;
            if (Vector3.Distance(hinge.transform.position, center) > TrapDoorRadius) continue;
            nearbyDoors.Add(hinge);
        }
        if (nearbyDoors.Count == 0) return;

        // Each door needs the SAME ring slot every tick, or the wall reshuffles itself every frame.
        // The original approach handed out "slot = number of doors registered so far" and kept a
        // dictionary of it, which broke in two ways: the slot index was never reduced modulo the
        // current door count, so once more doors had ever been registered than are currently
        // nearby, two of them mapped to the same angle and stacked inside each other; and the
        // dictionary held onto destroyed hinges from previous levels forever, which is what kept
        // inflating that count in the first place.
        // Sorting by instance ID gives the same stable order every tick from the current set
        // alone - no state to keep, nothing to leak, and no two doors can share a slot.
        nearbyDoors.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));

        for (int i = 0; i < nearbyDoors.Count; i++)
        {
            PhysGrabHinge hinge = nearbyDoors[i];
            float angle = 360f / nearbyDoors.Count * i * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * WallRingRadius;
            Vector3 wallPos = center + offset + Vector3.up * 1f;
            Quaternion facing = Quaternion.LookRotation((center - wallPos).normalized, Vector3.up);

            PhysGrabObject physObject = GameReflect.Get<PhysGrabObject>(hinge, "physGrabObject");
            if (physObject == null) continue;

            if ((physObject.transform.position - wallPos).sqrMagnitude > 0.25f)
                physObject.Teleport(wallPos, facing);
        }
    }

    // Enemy Drop Kill - confirmed in the decompiled EnemyHealth.cs: LightImpact/MediumImpact/
    // HeavyImpact all route into Hurt(), which is gated the exact same way TumbleImpactRPC's
    // real force application is - "SemiFunc.IsMasterClientOrSingleplayer()", a check on whichever
    // machine is EXECUTING it, not on who caused it. These aren't even RPCs - they're plain local
    // methods PhysGrabObjectImpactDetector calls when it detects a hard enough physical impact,
    // and that detector runs independently on every client's own physics simulation. So: we can't
    // just teleport an enemy into geometry (Teleport() is a position snap, not a push - no
    // velocity means no real impact reading), but teleporting it straight up and letting gravity
    // do the accelerating gives it genuine fall speed. When it lands, the REAL master's own
    // client independently simulates that same collision with real velocity, its own impact
    // detector fires for real, and Hurt() actually applies damage there. Not a guaranteed one-shot
    // kill - depends on that enemy type's own health/impactHeavyDamage/damageResistance - so this
    // keeps dropping every spawned enemy again every few seconds instead of once.
    private const float DropHeight = 50f;
    private const float DropInterval = 4f;
    private static float _nextEnemyDrop;

    public static void ApplyEnemyDropKill()
    {
        if (!CheatState.EnemyDropKill.Value) return;
        if (Time.unscaledTime < _nextEnemyDrop) return;
        _nextEnemyDrop = Time.unscaledTime + DropInterval;

        EnemyDirector director = EnemyDirector.instance;
        List<EnemyParent> enemies = director != null ? GameReflect.Get<List<EnemyParent>>(director, "enemiesSpawned") : null;
        if (enemies == null) return;

        int count = 0;
        foreach (EnemyParent parent in enemies)
        {
            if (parent == null) continue;
            PhysGrabObject body = GetEnemyBody(parent);
            if (body == null) continue;

            Vector3 pos = body.transform.position + Vector3.up * DropHeight;
            body.Teleport(pos, body.transform.rotation);
            count++;
        }

        if (count > 0) Plugin.Log.LogInfo($"Enemy Drop Kill: dropped {count} enemy(ies) from height.");
    }

    private static Vector3? FindBiggestClusterCenter(out int bestCount)
    {
        bestCount = 0;
        GameDirector director = GameDirector.instance;
        List<PlayerAvatar> players = director != null ? GameReflect.Get<List<PlayerAvatar>>(director, "PlayerList") : null;
        if (players == null) return null;

        List<PlayerAvatar> alive = new();
        foreach (PlayerAvatar p in players)
        {
            if (p == null || GameReflect.Get<bool>(p, "deadSet")) continue;
            alive.Add(p);
        }
        if (alive.Count == 0) return null;

        Vector3 bestCenter = alive[0].transform.position;
        foreach (PlayerAvatar center in alive)
        {
            Vector3 pos = center.transform.position;
            int count = 0;
            foreach (PlayerAvatar other in alive)
                if (Vector3.Distance(pos, other.transform.position) <= ClusterRadius) count++;

            if (count > bestCount)
            {
                bestCount = count;
                bestCenter = pos;
            }
        }
        return bestCenter;
    }

    private static EnemyParent FindNearestEnemyTo(Vector3 origin)
    {
        EnemyDirector director = EnemyDirector.instance;
        List<EnemyParent> enemies = director != null ? GameReflect.Get<List<EnemyParent>>(director, "enemiesSpawned") : null;
        if (enemies == null) return null;

        EnemyParent nearest = null;
        float nearestDist = float.MaxValue;
        foreach (EnemyParent e in enemies)
        {
            if (e == null) continue;
            float d = Vector3.Distance(origin, e.transform.position);
            if (d < nearestDist) { nearestDist = d; nearest = e; }
        }
        return nearest;
    }

    // Shared with RaidHack's target picker (CheatState.FakeSoundTargetName) - one player pick
    // serves Sound/Cage/Yeet/Frame/Seat/Teleport all at once, no reason for a separate field.
    private static PlayerAvatar FindPlayerTarget()
    {
        string targetName = CheatState.FakeSoundTargetName.Value;
        if (string.IsNullOrEmpty(targetName) || targetName == "None") return null;

        GameDirector director = GameDirector.instance;
        List<PlayerAvatar> players = director != null ? GameReflect.Get<List<PlayerAvatar>>(director, "PlayerList") : null;
        if (players == null) return null;

        foreach (PlayerAvatar p in players)
        {
            if (p == null) continue;
            if (GameReflect.Get<string>(p, "playerName") == targetName) return p;
        }
        return null;
    }

    private static EnemyParent FindNearestEnemy()
    {
        PlayerAvatar local = PlayerAvatar.instance;
        EnemyDirector director = EnemyDirector.instance;
        if (local == null || director == null) return null;

        List<EnemyParent> enemies = GameReflect.Get<List<EnemyParent>>(director, "enemiesSpawned");
        if (enemies == null) return null;

        EnemyParent nearest = null;
        float nearestDist = float.MaxValue;
        foreach (EnemyParent e in enemies)
        {
            if (e == null) continue;
            float d = Vector3.Distance(local.transform.position, e.transform.position);
            if (d < nearestDist) { nearestDist = d; nearest = e; }
        }
        return nearest;
    }

    private static PhysGrabObject GetEnemyBody(EnemyParent enemyParent)
    {
        Enemy enemy = GameReflect.Get<Enemy>(enemyParent, "Enemy");
        EnemyRigidbody rigid = enemy != null ? GameReflect.Get<EnemyRigidbody>(enemy, "Rigidbody") : null;
        return rigid != null ? GameReflect.Get<PhysGrabObject>(rigid, "physGrabObject") : null;
    }
}
