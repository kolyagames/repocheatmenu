// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

namespace RepoCheatMenu;

// Real melee damage, like grab/throw strength, is computed wherever the weapon's swing
// physics actually run authoritatively - confirmed in the decompiled source,
// EnemyHealth.Hurt() and the player-hurt path both gate on
// SemiFunc.IsMasterClientOrSingleplayer() with no RPC bridge letting a guest request a hit, so
// only the master's own simulation of a swing ever lands real damage. Same authority model as
// HighStrength - this only does anything while hosting or in singleplayer.
//
// HurtCollider.enemyDamageCooldown/playerDamageCooldown (confirmed public fields, not synced
// anywhere) gate how often the SAME target can be hit again via HurtCollider.CanHit()'s
// per-target cooldown list - 0.25s by default regardless of how fast you're actually swinging.
// The hurt-collider hitbox itself is only active while ItemMelee.currentState == Swinging,
// which naturally times out back to Idle after ~0.2s without continued motion. Forcing the
// state machine to stay in Swinging and cutting the cooldown keeps that hitbox permanently
// live and able to register hits dozens of times a second instead of 4 - "vibrating" the
// weapon into dealing far more damage per second than any real swing could.
internal static class MeleeVibrateHack
{
    private const float FastCooldown = 0.02f;

    private static HurtCollider _activeHurtCollider;
    private static float _backupEnemyCooldown;
    private static float _backupPlayerCooldown;

    public static void Apply()
    {
        bool masterOrSolo = !GameManager.Multiplayer() || Photon.Pun.PhotonNetwork.IsMasterClient;
        if (!CheatState.MeleeVibrate.Value || !masterOrSolo)
        {
            Restore();
            return;
        }

        PlayerAvatar avatar = PlayerAvatar.instance;
        PhysGrabber grabber = avatar != null ? GameReflect.Get<PhysGrabber>(avatar, "physGrabber") : null;
        bool grabbed = grabber != null && GameReflect.Get<bool>(grabber, "grabbed");
        PhysGrabObject held = grabbed ? GameReflect.Get<PhysGrabObject>(grabber, "grabbedPhysGrabObject") : null;
        ItemMelee melee = held != null ? held.GetComponent<ItemMelee>() : null;

        if (melee == null)
        {
            Restore();
            return;
        }

        HurtCollider hurt = held.GetComponentInChildren<HurtCollider>();
        if (hurt != _activeHurtCollider)
        {
            Restore();
            if (hurt != null)
            {
                _activeHurtCollider = hurt;
                _backupEnemyCooldown = hurt.enemyDamageCooldown;
                _backupPlayerCooldown = hurt.playerDamageCooldown;
            }
        }

        if (hurt != null)
        {
            hurt.enemyDamageCooldown = FastCooldown;
            hurt.playerDamageCooldown = FastCooldown;
        }

        GameReflect.Set(melee, "currentState", ItemMelee.State.Swinging);
        GameReflect.Set(melee, "stateTimer", 0f);
        GameReflect.Set(melee, "hitBoxTimer", 0.2f);

        UnityEngine.Transform hurtTransform = GameReflect.Get<UnityEngine.Transform>(melee, "hurtColliderTransform");
        if (hurtTransform != null)
            hurtTransform.gameObject.SetActive(true);
    }

    private static void Restore()
    {
        if (_activeHurtCollider == null) return;

        _activeHurtCollider.enemyDamageCooldown = _backupEnemyCooldown;
        _activeHurtCollider.playerDamageCooldown = _backupPlayerCooldown;
        _activeHurtCollider = null;
    }
}
