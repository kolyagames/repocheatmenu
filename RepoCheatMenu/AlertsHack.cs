// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace RepoCheatMenu;

// Local-only alerts on the Dynamic Island - nothing is sent to the network. Three independent
// toggles, all reading state the game already replicates to every client:
//   LobbyAlerts  - a teammate joined / left / died / was revived (PlayerList + PlayerAvatar.deadSet)
//   EnemyAlert   - a spawned enemy came within EnemyAlertRange metres (once per enemy per approach,
//                  re-armed when it leaves 1.5x the range, so a monster pacing at the edge does not spam)
//   HaulAlert    - the extraction haul reached the quota (RoundDirector.currentHaul >= haulGoal)
//   LowHealthAlert - your own or a teammate's health fell to LowHealthPercent of max (PlayerHealth.health)
// Shown through DynamicIsland.Notify, so the island has to be on (HudIsland) to see them.
internal static class AlertsHack
{
    private static readonly Dictionary<int, bool> Players = new();    // instance id -> dead
    private static readonly Dictionary<int, string> Names = new();
    private static readonly HashSet<int> Alerted = new();
    private static readonly List<int> Scratch = new();
    private static bool _lobbyPrimed;
    private static bool _haulNotified;
    private static readonly HashSet<int> LowHealth = new();
    private static float _next;

    public static void Apply()
    {
        if (!CheatState.LobbyAlerts.Value && !CheatState.EnemyAlert.Value && !CheatState.HaulAlert.Value && !CheatState.LowHealthAlert.Value)
        {
            ResetRoster();
            return;
        }
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.25f;

        // Only leaving the room forgets the roster; a scene change (no local avatar for a moment) must not.
        if (!Photon.Pun.PhotonNetwork.InRoom) ResetRoster();

        PlayerAvatar local = PlayerTargets.Local;
        if (local == null) return;

        if (CheatState.LobbyAlerts.Value)
        {
            CheckPlayers(local);
            _lobbyPrimed = true;
        }
        else
        {
            ResetRoster();
        }
        if (CheatState.EnemyAlert.Value) CheckEnemies(local);
        if (CheatState.HaulAlert.Value) CheckHaul();
        if (CheatState.LowHealthAlert.Value) CheckHealth(local);
        else LowHealth.Clear();
    }

    private static void ResetRoster()
    {
        _lobbyPrimed = false;
        Players.Clear();
        Names.Clear();
        Avatars.Clear();
    }

    // Keyed by Photon actor number, not by the avatar object: the game destroys and re-creates every avatar
    // on each level change, and ours appears before the others - keying by instance id announced every
    // teammate as "joined" (and the dead ones as "revived") on every scene load. "Left" is decided by the
    // Photon room itself, so an avatar missing during loading is not a departure.
    private static readonly Dictionary<int, int> Avatars = new();   // actor -> avatar instance id

    private static void CheckPlayers(PlayerAvatar local)
    {
        GameDirector director = GameDirector.instance;
        List<PlayerAvatar> list = director != null ? GameReflect.Get<List<PlayerAvatar>>(director, "PlayerList") : null;
        if (list == null || !GameManager.Multiplayer() || !Photon.Pun.PhotonNetwork.InRoom) return;

        foreach (PlayerAvatar p in list)
        {
            if (p == null || p == local || p.photonView == null) continue;
            int id = p.photonView.OwnerActorNr;
            int instance = p.GetInstanceID();
            bool dead = GameReflect.Get<bool>(p, "deadSet");
            string name = PlayerTargets.DisplayName(p);

            if (!Players.TryGetValue(id, out bool wasDead))
            {
                if (_lobbyPrimed) DynamicIsland.Notify(name, "Зашёл в лобби", true);
            }
            else if (Avatars.TryGetValue(id, out int known) && known != instance)
            {
                // Same player, new avatar after a scene change: take the new state silently.
            }
            else if (!wasDead && dead)
            {
                // DeathRecap already announces it, with the cause - one notification per death is enough.
                if (!CheatState.DeathRecap.Value) DynamicIsland.Notify(name, "Погиб", false);
            }
            else if (wasDead && !dead)
            {
                DynamicIsland.Notify(name, "Возродился", true);
            }
            Players[id] = dead;
            Names[id] = name;
            Avatars[id] = instance;
        }

        Scratch.Clear();
        foreach (int id in Players.Keys)
            if (Photon.Pun.PhotonNetwork.CurrentRoom.GetPlayer(id) == null) Scratch.Add(id);
        foreach (int id in Scratch)
        {
            if (_lobbyPrimed) DynamicIsland.Notify(Names.TryGetValue(id, out string n) ? n : "Игрок", "Вышел из лобби", false);
            Players.Remove(id);
            Names.Remove(id);
            Avatars.Remove(id);
        }
    }

    private static void CheckEnemies(PlayerAvatar local)
    {
        EnemyDirector director = EnemyDirector.instance;
        List<EnemyParent> enemies = director != null ? GameReflect.Get<List<EnemyParent>>(director, "enemiesSpawned") : null;
        if (enemies == null) return;

        float range = Mathf.Max(3f, CheatState.EnemyAlertRange.Value);
        foreach (EnemyParent parent in enemies)
        {
            if (parent == null || !GameReflect.Get<bool>(parent, "Spawned")) continue; // despawned: not on the map
            Enemy enemy = GameReflect.Get<Enemy>(parent, "Enemy");
            Transform center = enemy != null ? GameReflect.Get<Transform>(enemy, "CenterTransform") : null;
            if (center == null) continue;

            int id = parent.GetInstanceID();
            float d = Vector3.Distance(local.transform.position, center.position);
            if (d <= range)
            {
                if (!Alerted.Add(id)) continue;
                string name = GameReflect.Get<string>(parent, "enemyName");
                DynamicIsland.Notify(string.IsNullOrEmpty(name) ? "Враг" : name, $"Рядом: {d:0} м", false);
            }
            else if (d > range * 1.5f)
            {
                Alerted.Remove(id);
            }
        }
    }

    // Own health and every teammate's: one notification when it falls to the threshold, re-armed once it
    // is 20 points above it again, so a player hovering around the line does not spam.
    private static void CheckHealth(PlayerAvatar local)
    {
        GameDirector director = GameDirector.instance;
        List<PlayerAvatar> list = director != null ? GameReflect.Get<List<PlayerAvatar>>(director, "PlayerList") : null;
        if (list == null) return;

        float percent = Mathf.Clamp(CheatState.LowHealthPercent.Value, 5, 90) / 100f;
        foreach (PlayerAvatar p in list)
        {
            if (p == null || GameReflect.Get<bool>(p, "deadSet")) continue;
            PlayerHealth ph = GameReflect.Get<PlayerHealth>(p, "playerHealth");
            if (ph == null) continue;
            int hp = GameReflect.Get<int>(ph, "health");
            int max = Mathf.Max(1, GameReflect.Get<int>(ph, "maxHealth"));

            int id = p.GetInstanceID();
            if (hp <= max * percent)
            {
                if (!LowHealth.Add(id)) continue;
                DynamicIsland.Notify(p == local ? "Вы" : PlayerTargets.DisplayName(p), $"Мало здоровья: {hp} HP", false);
            }
            else if (hp > max * percent + 20f)
            {
                LowHealth.Remove(id);
            }
        }
    }

    private static void CheckHaul()
    {
        RoundDirector rd = RoundDirector.instance;
        if (rd == null) { _haulNotified = false; return; }
        int goal = GameReflect.Get<int>(rd, "haulGoal");
        int haul = GameReflect.Get<int>(rd, "currentHaul");
        if (goal <= 0) { _haulNotified = false; return; }

        bool reached = haul >= goal;
        if (reached && !_haulNotified)
            DynamicIsland.Notify("Квота", "Собрана - можно эвакуироваться", true);
        _haulNotified = reached;
    }
}
