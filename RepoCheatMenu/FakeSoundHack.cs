// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RepoCheatMenu;

// There's no single global "play monster sound at point" API in the decompiled source - each
// enemy type's own xxxAnim script (BirthdayBoyAnim, EnemyRunnerAnim, etc - all separate classes,
// no shared base) carries its own public Sound fields, and Sound.Play(Vector3) (confirmed
// public, plain serializable class, no network/master check anywhere near it) just spawns a
// one-shot positional AudioSource - always purely local playback. Rather than hardcoding field
// names per enemy type, this reflects over every MonoBehaviour on whichever enemy is currently
// spawned and grabs the first Sound-typed field whose name suggests something scary
// (scream/notice/hurt/aggro/attack/chase), falling back to any populated Sound field if none
// match. Best effort - not independently confirmed against every enemy type in the game - but
// it means we're not shipping or guessing at any audio assets of our own, just reusing whatever
// the level already loaded.
internal static class FakeSoundHack
{
    private static readonly string[] PreferredNameHints = { "scream", "notice", "hurt", "aggro", "attack", "chase" };

    public static List<string> ScanPlayerNames()
    {
        List<string> names = new();
        PlayerAvatar local = PlayerAvatar.instance;
        GameDirector director = GameDirector.instance;
        if (director == null) return names;

        List<PlayerAvatar> players = GameReflect.Get<List<PlayerAvatar>>(director, "PlayerList");
        if (players == null) return names;

        foreach (PlayerAvatar p in players)
        {
            if (p == null || p == local) continue;
            string name = GameReflect.Get<string>(p, "playerName");
            if (!string.IsNullOrEmpty(name) && !names.Contains(name))
                names.Add(name);
        }
        return names;
    }

    public static void PlaySelected()
    {
        string targetName = CheatState.FakeSoundTargetName.Value;
        if (string.IsNullOrEmpty(targetName) || targetName == "None")
        {
            Plugin.Log.LogInfo("Fake Monster Sound had no effect - no target player selected.");
            return;
        }

        GameDirector director = GameDirector.instance;
        List<PlayerAvatar> players = director != null ? GameReflect.Get<List<PlayerAvatar>>(director, "PlayerList") : null;
        if (players == null) return;

        PlayerAvatar target = null;
        foreach (PlayerAvatar p in players)
        {
            if (p == null) continue;
            if (GameReflect.Get<string>(p, "playerName") == targetName) { target = p; break; }
        }

        if (target == null)
        {
            Plugin.Log.LogInfo($"Fake Monster Sound had no effect - couldn't find player '{targetName}'.");
            return;
        }

        Sound sound = FindScarySound();
        if (sound == null)
        {
            Plugin.Log.LogInfo("Fake Monster Sound had no effect - no spawned enemy to borrow a sound from yet.");
            return;
        }

        sound.Play(target.transform.position);
    }

    private static Sound FindScarySound()
    {
        EnemyDirector director = EnemyDirector.instance;
        if (director == null) return null;

        List<EnemyParent> enemies = GameReflect.Get<List<EnemyParent>>(director, "enemiesSpawned");
        if (enemies == null) return null;

        Sound fallback = null;
        foreach (EnemyParent parent in enemies)
        {
            if (parent == null) continue;

            foreach (MonoBehaviour behaviour in parent.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;

                foreach (FieldInfo field in behaviour.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (field.FieldType != typeof(Sound)) continue;

                    Sound candidate = (Sound)field.GetValue(behaviour);
                    if (candidate?.Sounds == null || candidate.Sounds.Length == 0) continue;

                    fallback ??= candidate;

                    string nameLower = field.Name.ToLowerInvariant();
                    foreach (string hint in PreferredNameHints)
                        if (nameLower.Contains(hint)) return candidate;
                }
            }
        }
        return fallback;
    }
}
