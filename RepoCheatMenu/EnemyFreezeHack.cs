// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace RepoCheatMenu;

// Found by auditing the game's own RPC surface for the same shape as ReviveRPC, not ported from
// GameTools or anyone else - Enemy.FreezeRPC (confirmed [PunRPC], public) is gated ONLY by
// "SemiFunc.MasterOnlyRPC(_info)", the exact same shallow gate ReviveRPC has, so it's already
// exploitable through the MasterRpcSelfBypassPatch this mod installs for Revive Self - no new
// patch needed. The vanilla caller (Enemy's own internal Freeze(float) wrapper, a few lines above
// FreezeRPC in the decompiled source) additionally checks
// SemiFunc.IsMasterClientOrSingleplayer() before even sending the RPC, but that's the CALLER's
// own courtesy check, not enforced inside FreezeRPC itself - calling the RPC directly (skipping
// that wrapper) works for guest or host exactly like Revive Self does.
//
// FreezeTimer (confirmed public on Enemy) counts down via Time.deltaTime on Enemy's own per-frame
// tick - EnemyRigidbody.cs reads it in two places (confirmed in the decompiled source), zeroing
// velocity/angularVelocity and bailing out of movement/chase logic entirely while it's positive.
// A real, complete freeze, not just a visual - re-applied periodically here (not one huge value)
// so it never actually lapses while the toggle's on.
internal static class EnemyFreezeHack
{
    private const float ReapplyInterval = 1f;
    private const float FreezeDuration = 2f; // > ReapplyInterval so it never counts down to 0 between ticks

    private static float _nextApply;

    public static void Apply()
    {
        if (!CheatState.FreezeEnemies.Value) return;
        if (Time.unscaledTime < _nextApply) return;
        _nextApply = Time.unscaledTime + ReapplyInterval;

        EnemyDirector director = EnemyDirector.instance;
        if (director == null) return;

        List<EnemyParent> enemies = GameReflect.Get<List<EnemyParent>>(director, "enemiesSpawned");
        if (enemies == null) return;

        PlayerController controller = PlayerController.instance;
        Vector3 origin = controller != null ? controller.transform.position : Vector3.zero;
        float range = CheatState.FreezeEnemiesRange.Value;
        bool multiplayer = GameManager.Multiplayer();

        foreach (EnemyParent parent in enemies)
        {
            if (parent == null) continue;

            Enemy enemy = GameReflect.Get<Enemy>(parent, "Enemy");
            if (enemy == null) continue;

            if (range > 0f)
            {
                Transform center = GameReflect.Get<Transform>(enemy, "CenterTransform");
                if (center != null && Vector3.Distance(origin, center.position) > range) continue;
            }

            if (multiplayer)
            {
                // Enemy's own field is "PhotonView" (capital P, set from GetComponent in Awake); the lower-case
                // "photonView" is MonoBehaviourPun's PROPERTY, which GameReflect (fields only) never finds -
                // this used to read null and skip every enemy, so the freeze never ran in multiplayer.
                PhotonView view = GameReflect.Get<PhotonView>(enemy, "PhotonView") ?? enemy.GetComponent<PhotonView>();
                if (view == null) continue;
                view.RPC("FreezeRPC", RpcTarget.All, FreezeDuration);
            }
            else
            {
                // MethodInfo.Invoke on Mono does NOT fill in a trailing optional parameter's
                // default value the way a normal C# call site does - it requires an argument for
                // every declared parameter or throws TargetParameterCountException (confirmed live
                // via the debug bridge: this fired on every single tick until the explicit
                // default(PhotonMessageInfo) was added). Same gotcha UtilityHolesHack.cs and
                // WorldHolesHack2.cs already work around for their own RPC calls - worth checking
                // any other GameReflect.Invoke(..., "XxxRPC", ...) call that only ever exercises
                // its singleplayer branch in solo testing, since guest/host multiplayer calls take
                // the real photonView.RPC(...) path instead and never hit this at all.
                GameReflect.Invoke(enemy, "FreezeRPC", FreezeDuration, default(PhotonMessageInfo));
            }
        }
    }
}
