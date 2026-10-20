// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace RepoCheatMenu;

// Ghost observer: keep your (local-only) body next to the player picked in "Игроки", so you can watch
// someone without flying by hand. Only while GhostObserver is really active - your position is not sent
// to anyone then, so nobody sees you trailing them. FlyHack does the moving (kinematic body, collider
// off, the same state Noclip uses); this only decides where to.
//
// Two views:
//   behind - behind and above them, you look around freely;
//   eyes   - at their eye point, the camera turned where THEY look. Their PlayerVisionTarget.VisionTransform
//            is synced over the network (position = eyes, rotation = their camera), and the camera is turned
//            with CameraAim.AimTargetSet - the call the game itself uses to turn a dead player's view.
internal static class GhostFollowHack
{
    private const float FollowSharpness = 8f;
    private const float EyesForward = 0.3f;   // just in front of their face, so their own head mesh is not in view

    public static PlayerAvatar Target()
    {
        if (!CheatState.GhostFollow.Value || !GhostObserverHack.SessionGhost) return null;
        PlayerAvatar picked = PlayerTargets.Picked();
        // Unity's overloaded == spots a destroyed PlayerAvatar even while a C# reference is still live
        // (picked left the room between the Picked() cache and the follow tick); without this guard
        // GameReflect would NRE on reading deadSet from a dead instance and the follow would never
        // recover for the current session.
        if (picked == null || (object)picked == null || picked == PlayerAvatar.instance) return null;
        if (picked.transform == null) return null;
        return picked;
    }

    public static void Move(PlayerController controller, PlayerAvatar target)
    {
        if (target == null || target.transform == null) return;
        bool dead = GameReflect.Get<bool>(target, "deadSet");
        Transform eyes = target.PlayerVisionTarget != null ? target.PlayerVisionTarget.VisionTransform : null;

        Vector3 desired;
        if (CheatState.GhostFollowEyes.Value && !dead && eyes != null)
        {
            // Our camera rests at body + CameraPosition.playerOffset (CameraPosition.Update), so put the body that
            // far below their eyes. The live camera position lags behind the body and would drift the result.
            desired = eyes.position + eyes.forward * EyesForward - CameraOffsetWorld(controller);
            if (CameraAim.Instance != null)
                CameraAim.Instance.AimTargetSet(eyes.position + eyes.forward * 10f, 0.1f, 30f, target.gameObject, 0);
            controller.transform.position = desired; // eyes view must not lag behind their head
            // The camera eases toward the body (CameraPosition.Update slerp); speed that up so the view stays on
            // their head. NOT CameraPosition.OverridePosition: held every tick it once sent the player into an
            // endless fall (see VisionHack.cs).
            if (CameraPosition.instance != null) CameraPosition.instance.OverrideSpeed(60f, 0.1f);
            return;
        }

        Vector3 anchor = PlayerTargets.WorldPosition(target);
        Vector3 forward = target.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        forward.Normalize();

        float distance = Mathf.Clamp(CheatState.GhostFollowDistance.Value, 1f, 10f);
        desired = anchor - forward * distance + Vector3.up * (distance * 0.5f);
        float t = 1f - Mathf.Exp(-FollowSharpness * Time.deltaTime);
        controller.transform.position = Vector3.Lerp(controller.transform.position, desired, t);
    }

    // The camera's resting offset from the body in world space; a standing eye height if it is not there yet.
    private static Vector3 CameraOffsetWorld(PlayerController controller)
    {
        CameraPosition cp = CameraPosition.instance;
        if (cp == null) return Vector3.up * 1.5f;
        Transform parent = cp.transform.parent;
        return parent != null ? parent.TransformVector(cp.playerOffset) : cp.playerOffset;
    }

    // Default hotkeys (arrows - the game binds none of them). They only act while the ghost is really active:
    // the picked player is shared with other features (player catcher, mimic), and an arrow pressed in normal
    // play must not quietly change it. Pressing one also turns following on.
    public static void KeyNext() { if (GhostObserverHack.SessionGhost) { CheatState.GhostFollow.Value = true; Cycle(1); } }
    public static void KeyPrev() { if (GhostObserverHack.SessionGhost) { CheatState.GhostFollow.Value = true; Cycle(-1); } }

    public static void KeyEyes()
    {
        if (!GhostObserverHack.SessionGhost) return;
        CheatState.GhostFollow.Value = true;
        CheatState.GhostFollowEyes.Value = !CheatState.GhostFollowEyes.Value;
        DynamicIsland.Notify("Слежка", CheatState.GhostFollowEyes.Value ? "Его глазами" : "Сзади", true);
    }

    // Jump to the nearest valuable with dollarValueCurrent > 0, prioritising the most expensive one
    // inside a short "nearby" radius first, falling back to the biggest in the whole level if
    // nothing is close. Fast scout for the team - the ghost drops in on the next payday item.
    public static void JumpToTopValuable()
    {
        if (!GhostObserverHack.SessionGhost)
        {
            DynamicIsland.Notify("Прыжок", "Нужен активный Призрак-наблюдатель", false);
            return;
        }
        PlayerController pc = PlayerController.instance;
        if (pc == null) return;
        Vector3 me = pc.transform.position;

        ValuableObject best = null;
        float bestValue = 0f;
        foreach (ValuableObject v in UnityEngine.Object.FindObjectsByType<ValuableObject>(FindObjectsSortMode.None))
        {
            if (v == null) continue;
            float dollars = GameReflect.Get<float>(v, "dollarValueCurrent");
            if (dollars <= 0f) continue;
            // Score = raw value * 1.0 for nearby, * 0.4 for far - keeps "best in sight" over "huge
            // but across the level" by default.
            float dist = Vector3.Distance(me, v.transform.position);
            float score = dollars * (dist < 25f ? 1f : 0.4f);
            if (score > bestValue)
            {
                bestValue = score;
                best = v;
            }
        }
        if (best == null)
        {
            DynamicIsland.Notify("Прыжок", "Ценностей на уровне нет", false);
            return;
        }
        // Follow-mode would snap us back to the picked player on the very next FlyHack tick (Move
        // overwrites transform.position every frame while a target is set). Jumping to a valuable
        // only makes sense as a one-shot repositioning, so turn Follow off here.
        if (CheatState.GhostFollow.Value) CheatState.GhostFollow.Value = false;
        pc.transform.position = best.transform.position + Vector3.up * 1.5f;
        float finalValue = GameReflect.Get<float>(best, "dollarValueCurrent");
        DynamicIsland.Notify("Прыжок", "К ценности $" + finalValue.ToString("0"), true);
    }

    // Convenience jumps that most ghost sessions want: back to the truck (safe room), to the
    // next unactivated extraction point (where the team is going anyway). All of them turn the
    // Follow toggle off first - otherwise FlyHack.Move would snap us back to the picked player
    // on the very next tick.
    public static void JumpToTruck()
    {
        if (!GhostObserverHack.SessionGhost) { DynamicIsland.Notify("Прыжок", "Нужен активный Призрак-наблюдатель", false); return; }
        PlayerController pc = PlayerController.instance;
        if (pc == null) return;
        if (TruckScreenText.instance == null)
        {
            DynamicIsland.Notify("Прыжок", "Грузовик недоступен на этой сцене", false);
            return;
        }
        if (CheatState.GhostFollow.Value) CheatState.GhostFollow.Value = false;
        pc.transform.position = TruckScreenText.instance.transform.position + Vector3.up * 2f;
        DynamicIsland.Notify("Прыжок", "К грузовику", true);
    }

    public static void JumpToExtraction()
    {
        if (!GhostObserverHack.SessionGhost) { DynamicIsland.Notify("Прыжок", "Нужен активный Призрак-наблюдатель", false); return; }
        PlayerController pc = PlayerController.instance;
        if (pc == null) return;
        ExtractionPoint ep = SemiFunc.ExtractionPointGetNearestNotActivated(pc.transform.position)
                              ?? SemiFunc.ExtractionPointGetNearest(pc.transform.position);
        if (ep == null)
        {
            DynamicIsland.Notify("Прыжок", "Точек выхода не найдено", false);
            return;
        }
        if (CheatState.GhostFollow.Value) CheatState.GhostFollow.Value = false;
        pc.transform.position = ep.transform.position + Vector3.up * 2f;
        DynamicIsland.Notify("Прыжок", "К точке выхода", true);
    }

    // One-shot teleport to the selected player, no follow. The ghost body is kinematic (noclip
    // state), so we can just rewrite its position - no network side effects, nothing is broadcast.
    // Puts us roughly head-height above them so we land clear of their mesh rather than inside it.
    public static void JumpToPicked()
    {
        if (!GhostObserverHack.SessionGhost)
        {
            DynamicIsland.Notify("Прыжок", "Нужен активный Призрак-наблюдатель", false);
            return;
        }
        PlayerAvatar picked = PlayerTargets.Picked();
        if (picked == null || picked == PlayerAvatar.instance)
        {
            DynamicIsland.Notify("Прыжок", "Выберите игрока во вкладке «Игроки»", false);
            return;
        }
        PlayerController pc = PlayerController.instance;
        if (pc == null) return;
        Vector3 anchor = PlayerTargets.WorldPosition(picked);
        pc.transform.position = anchor + Vector3.up * 1.8f;
        DynamicIsland.Notify("Прыжок", "К " + PlayerTargets.DisplayName(picked), true);
    }

    // Button: switch to the next other player in the list (wraps around).
    public static void NextTarget() => Cycle(1);

    private static void Cycle(int step)
    {
        GameDirector director = GameDirector.instance;
        List<PlayerAvatar> players = director != null ? GameReflect.Get<List<PlayerAvatar>>(director, "PlayerList") : null;
        if (players == null) return;

        List<PlayerAvatar> others = new();
        foreach (PlayerAvatar p in players)
            if (p != null && p != PlayerAvatar.instance) others.Add(p);
        if (others.Count == 0)
        {
            DynamicIsland.Notify("Слежка", "Больше никого нет", false);
            return;
        }

        int current = others.IndexOf(PlayerTargets.Picked());
        if (current < 0 && step < 0) current = 0;
        PlayerAvatar next = others[((current + step) % others.Count + others.Count) % others.Count];
        CheatState.FakeSoundTargetName.Value = PlayerTargets.NameOf(next);
        DynamicIsland.Notify("Слежка", PlayerTargets.DisplayName(next), true);
    }

    // Pick whoever the camera is pointed at as the Picked player (used by Follow / JumpToPicked /
    // PlayerCatcher / Spectate). Faster than opening the Players tab. Uses the player's PhotonView
    // or PlayerAvatar on hit, walks parent chain. Falls back to nearest player within 15° cone.
    public static void PickHoveredPlayer()
    {
        if (!GhostObserverHack.SessionGhost)
        {
            DynamicIsland.Notify("Выбор", "Нужен активный Призрак-наблюдатель", false);
            return;
        }
        Camera cam = Camera.main;
        if (cam == null) return;

        PlayerAvatar picked = null;
        if (Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, 80f, ~0, QueryTriggerInteraction.Ignore))
            picked = hit.collider != null ? hit.collider.GetComponentInParent<PlayerAvatar>() : null;

        if (picked == null || picked == PlayerAvatar.instance)
        {
            // Cone fallback: look at everyone, take the one closest to the camera-forward axis
            // within a small angle. Beats missing a player because the raycast landed on a prop.
            GameDirector director = GameDirector.instance;
            List<PlayerAvatar> players = director != null ? GameReflect.Get<List<PlayerAvatar>>(director, "PlayerList") : null;
            if (players != null)
            {
                float bestDot = Mathf.Cos(15f * Mathf.Deg2Rad);
                foreach (PlayerAvatar p in players)
                {
                    if (p == null || p == PlayerAvatar.instance) continue;
                    Vector3 toP = (PlayerTargets.WorldPosition(p) - cam.transform.position).normalized;
                    float d = Vector3.Dot(cam.transform.forward, toP);
                    if (d > bestDot) { bestDot = d; picked = p; }
                }
            }
        }
        if (picked == null || picked == PlayerAvatar.instance)
        {
            DynamicIsland.Notify("Выбор", "В прицеле никого нет", false);
            return;
        }
        CheatState.FakeSoundTargetName.Value = PlayerTargets.NameOf(picked);
        DynamicIsland.Notify("Выбор", PlayerTargets.DisplayName(picked), true);
    }

    // Panel-style LiveInfo for the ghost section: compact status of every other player (HP %, dead
    // marker, talking marker). PlayerHealth.health / maxHealth are both internal in REPO - read
    // through GameReflect so Mono's access check does not blow up. The talking signal comes from
    // PlayerVoiceChat.isTalking (what the HUD voice indicator already reads).
    public static string PlayersPanel()
    {
        if (!GhostObserverHack.SessionGhost) return "";
        GameDirector director = GameDirector.instance;
        List<PlayerAvatar> players = director != null ? GameReflect.Get<List<PlayerAvatar>>(director, "PlayerList") : null;
        if (players == null || players.Count == 0) return "";

        System.Text.StringBuilder sb = new();
        sb.Append("  -> Игроки: ");
        bool first = true;
        foreach (PlayerAvatar p in players)
        {
            if (p == null || p == PlayerAvatar.instance) continue;
            if (!first) sb.Append("  ·  ");
            first = false;

            string name = PlayerTargets.DisplayName(p);
            if (name.Length > 10) name = name.Substring(0, 10);
            sb.Append(name);

            PlayerHealth h = p.playerHealth;
            if (h != null)
            {
                int hp = GameReflect.Get<int>(h, "health");
                int max = GameReflect.Get<int>(h, "maxHealth");
                if (GameReflect.Get<bool>(p, "deadSet"))
                    sb.Append(" †");
                else if (max > 0)
                    sb.Append(" ").Append(hp * 100 / max).Append("%");
            }

            PlayerVoiceChat voice = GameReflect.Get<PlayerVoiceChat>(p, "voiceChat");
            if (voice != null && GameReflect.Get<bool>(voice, "isTalking")) sb.Append(" ●");
        }
        return first ? "" : sb.ToString();
    }

    public static string KeysHint()
    {
        string Key(Action a)
        {
            Keybind kb = KeybindManager.Find(a);
            return kb != null && kb.Vk.Value != 0 ? Win32.VkName(kb.Vk.Value) : "-";
        }
        return $"  -> Клавиши в режиме призрака: {Key(KeyPrev)} / {Key(KeyNext)} игрок, {Key(KeyEyes)} глазами/сзади";
    }

    public static string Status()
    {
        if (!CheatState.GhostFollow.Value) return "  -> Выключено";
        if (!GhostObserverHack.SessionGhost) return "  -> Ждёт активного «Призрака-наблюдателя»";
        PlayerAvatar target = Target();
        if (target == null) return "  -> Выберите игрока во вкладке «Игроки» или кнопкой «Следующий»";
        return "  -> Следую за: " + PlayerTargets.DisplayName(target)
            + (CheatState.GhostFollowEyes.Value ? " (его глазами)" : "");
    }
}
