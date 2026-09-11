using UnityEngine;

namespace RepoCheatMenu;

// Extra copies of whatever valuable an enemy would already drop on death - the game's own spawn
// logic (EnemyParent.Despawn, confirmed in the decompiled source) picks the prefab from
// AssetManager.instance.enemyValuableSmall/Medium/Big by difficulty tier and spawns exactly one
// via PhotonNetwork.InstantiateRoomObject (or Object.Instantiate in singleplayer), gated on
// Enemy.Health.spawnValuable && spawnValuableCurrent < spawnValuableMax. This runs as a Postfix
// on that same method and spawns (PillarDropMultiplier - 1) more of the SAME tier at small
// offsets around the same point, riding NetSpawn.Spawn for the same guest/host handling
// everything else in this mod uses - master-only in practice, same as the vanilla spawn itself.
//
// GameTools separates "pillar drop multiplier" from a second toggle, "remove pillar limit" -
// distinct because spawnValuableMax (confirmed public on EnemyHealth, default 3) caps the total
// valuables ANY enemy can ever drop across its whole lifetime (it counts every spawnValuable call,
// not just this Postfix's own extras), so a multiplier alone still hits that ceiling once the
// enemy has dropped 3 total. PillarDropRemoveLimit forces the cap up before the loop runs so the
// multiplier isn't silently capped by a field this class doesn't otherwise touch.
internal static class PillarDropHack
{
    private const int RemovedLimitValue = 999;

    public static void ApplyDespawn(EnemyParent enemyParent)
    {
        int extra = CheatState.PillarDropMultiplier.Value - 1;
        if (extra <= 0 || enemyParent == null) return;

        Enemy enemy = GameReflect.Get<Enemy>(enemyParent, "Enemy");
        if (enemy == null) return;
        if (!GameReflect.Get<bool>(enemy, "HasHealth")) return;

        EnemyHealth health = GameReflect.Get<EnemyHealth>(enemy, "Health");
        if (health == null || health.healthCurrent > 0) return;
        if (!health.spawnValuable) return;

        if (CheatState.PillarDropRemoveLimit.Value && health.spawnValuableMax < RemovedLimitValue)
            health.spawnValuableMax = RemovedLimitValue;

        GameObject prefab = enemyParent.difficulty switch
        {
            EnemyParent.Difficulty.Difficulty2 => AssetManager.instance.enemyValuableMedium,
            EnemyParent.Difficulty.Difficulty3 => AssetManager.instance.enemyValuableBig,
            _ => AssetManager.instance.enemyValuableSmall,
        };
        if (prefab == null) return;

        Transform spawnPoint = enemy.CustomValuableSpawnTransform != null ? enemy.CustomValuableSpawnTransform : enemy.CenterTransform;
        if (spawnPoint == null) return;

        for (int i = 0; i < extra; i++)
        {
            if (GameReflect.Get<int>(health, "spawnValuableCurrent") >= health.spawnValuableMax) break;

            Vector3 offset = new Vector3(Random.Range(-0.5f, 0.5f), 0f, Random.Range(-0.5f, 0.5f));
            NetSpawn.Spawn("Valuables/" + prefab.name, prefab, spawnPoint.position + offset, Quaternion.identity);
            GameReflect.Set(health, "spawnValuableCurrent", GameReflect.Get<int>(health, "spawnValuableCurrent") + 1);
        }
    }
}
