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

// "Ловец игроков": пока кто-то лежит в режиме кувырка (Q, PlayerAvatar.isTumbling) - его тело это
// обычный PhysGrabObject (PlayerTumble.physGrabObject), и игра сама разрешает его брать и носить.
// Ловец сам хватает ближайшего такого игрока в радиусе через PhysGrabber.OverrideGrab (public, тот
// же вход, что у AutoLoot и ItemStealHack), без ручного наведения.
//   * ForceGrabPhysObject игры не требует прямой видимости: RaycastAll по слою PhysGrabObject берёт
//     попадание именно в нужный объект, стены не мешают;
//   * не трогает ваш хват, пока вы держите что-то НЕ из игроков (лут не бросается);
//   * держится за пойманного, пока он кувыркается; встал или умер - хват спадает сам, как в игре;
//   * Release-таймер: через N секунд отпускает, чтобы не держать вечно (0 = пока не отпустите сами).
// Работает у гостя и у хоста: сеть идёт штатным путём хвата игры, никаких RPC мы не подделываем.
internal static class PlayerCatcherHack
{
    private const float Interval = 0.15f;

    private static float _last;
    private const float GrabGrace = 0.6f;     // столько ждём, пока хват игры установится
    private const float RecatchCooldown = 3f;  // отпущенного по таймеру не ловим сразу же снова

    private static PhysGrabObject _caught;
    private static float _caughtSince;
    private static PhysGrabObject _released;
    private static float _releasedAt;

    internal static string CaughtName { get; private set; } = "";

    public static void Apply()
    {
        if (!CheatState.PlayerCatcher.Value)
        {
            if (_caught != null) Reset();
            return;
        }
        // Under a session ghost: our PhysGrabber view is filtered, so the GrabStartedRPC never
        // reaches the host - the catch is purely a local visual on our screen, nobody actually
        // gets pulled. Also GhostObserverHack.Tick holds OverrideGrabDisable every frame, which
        // would veto this anyway. Skip quietly.
        if (GhostObserverHack.LocalRpcIsMuted)
        {
            if (_caught != null) Reset();
            return;
        }
        if (Time.unscaledTime - _last < Interval) return;
        _last = Time.unscaledTime;

        PlayerAvatar local = PlayerAvatar.instance;
        if (local == null || GameState.IsInGame() == false) return;

        PhysGrabber grabber = GameReflect.Get<PhysGrabber>(local, "physGrabber");
        if (grabber == null) return;

        bool grabbed = GameReflect.Get<bool>(grabber, "grabbed");
        PhysGrabObject holding = grabbed ? GameReflect.Get<PhysGrabObject>(grabber, "grabbedPhysGrabObject") : null;

        // Пойманный: держим, пока он кувыркается, и отпускаем по таймеру.
        if (_caught != null)
        {
            bool stillHeld = holding == _caught;
            float held = Time.unscaledTime - _caughtSince;
            if (!stillHeld)
            {
                // Хват ставится не в тот же кадр - не бросаем цель, пока не прошло время на установку.
                if (held < GrabGrace) return;
                Reset();
                return;
            }
            float limit = CheatState.PlayerCatcherHoldSeconds.Value;
            if (limit > 0f && held > limit)
            {
                grabber.OverrideGrab(null, 0.1f, true);
                _released = _caught;
                _releasedAt = Time.unscaledTime;
                Reset();
            }
            return;
        }

        // Чужой груз в руках не отнимаем.
        if (grabbed) return;

        PlayerAvatar target = FindTarget(local);
        if (target == null) return;

        PlayerTumble tumble = GameReflect.Get<PlayerTumble>(target, "tumble");
        PhysGrabObject body = tumble != null ? GameReflect.Get<PhysGrabObject>(tumble, "physGrabObject") : null;
        if (body == null) return;
        if (body == _released && Time.unscaledTime - _releasedAt < RecatchCooldown) return;

        grabber.OverrideGrab(body, 0.3f);
        _caught = body;
        _caughtSince = Time.unscaledTime;
        CaughtName = PlayerTargets.DisplayName(target);
    }

    private static void Reset()
    {
        _caught = null;
        CaughtName = "";
    }

    private static PlayerAvatar FindTarget(PlayerAvatar local)
    {
        GameDirector director = GameDirector.instance;
        List<PlayerAvatar> players = director != null ? GameReflect.Get<List<PlayerAvatar>>(director, "PlayerList") : null;
        if (players == null) return null;

        PlayerAvatar only = CheatState.PlayerCatcherTargetOnly.Value ? PlayerTargets.Picked() : null;
        if (CheatState.PlayerCatcherTargetOnly.Value && only == null) return null;

        float range = CheatState.PlayerCatcherRange.Value;
        PlayerAvatar best = null;
        float bestDist = range;

        foreach (PlayerAvatar p in players)
        {
            if (p == null || p == local) continue;
            if (only != null && p != only) continue;
            if (GameReflect.Get<bool>(p, "deadSet") || GameReflect.Get<bool>(p, "isDisabled")) continue;
            if (!GameReflect.Get<bool>(p, "isTumbling")) continue;

            float d = Vector3.Distance(local.transform.position, p.transform.position);
            if (d < bestDist) { bestDist = d; best = p; }
        }
        return best;
    }

    internal static string Status()
    {
        if (!CheatState.PlayerCatcher.Value) return "  -> Выключено";
        return CaughtName.Length > 0
            ? "  -> Держите: " + CaughtName
            : "  -> Ждёт, пока кто-то упадёт в кувырок (Q) в радиусе";
    }
}
