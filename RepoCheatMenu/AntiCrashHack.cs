// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System.Collections.Generic;

namespace RepoCheatMenu;

// Anti-crash: keeps the local client standing when other players try to bring it down over the
// network. Three independent layers, all defensive - nothing here sends anything:
//   1. Exception swallowing (Finalizer patches in Patches.cs on PhotonNetwork.ExecuteRpc and
//      OnEvent) - a malformed RPC/event kills only itself instead of tearing down Photon's whole
//      dispatch loop. That alone means "лагать но не падать" for anything that throws.
//   2. Argument sanity (RpcGate.CheckRpc) - a single oversized string or array argument (the
//      classic one-packet killer: a multi-megabyte chat string that detonates Unity's text mesh)
//      is dropped before the RPC method ever runs.
//   3. Per-sender rate limiting (RpcGate) - a flood of RPCs or networked Instantiate from one
//      actor is throttled to a sane ceiling, with an optional quarantine that ignores a proven
//      flooder outright for a while.
// The gate logic below is deliberately free of any Unity/Photon type so it can be exercised by a
// plain test harness - the Harmony patches in Patches.cs are the only Unity-facing part.
internal static class AntiCrashHack
{
    public static long ExceptionsSwallowed;
    public static long RpcsDropped;
    public static long InstantiatesDropped;

    // One sliding-window counter per actor. Environment.TickCount (ms, wraps ~49 days) is fine
    // here - we only ever take differences over a 1s window, far shorter than the wrap period.
    private sealed class Window
    {
        public int WindowStartTick;
        public int RpcCount;
        public int InstantiateCount;
        public int StrikeCount;
        public int QuarantineUntilTick;
    }

    private static readonly Dictionary<int, Window> _byActor = new();

    public static void Reset()
    {
        _byActor.Clear();
        ExceptionsSwallowed = 0;
        RpcsDropped = 0;
        InstantiatesDropped = 0;
    }

    // Returns true if the RPC should be allowed through. actorNumber < 0 means "unknown sender" -
    // always allowed, since we can't attribute or rate-limit it and dropping it risks eating our
    // own local traffic. maxStringLen/maxArrayLen guard argument size; maxPerSecond guards rate.
    public static bool CheckRpc(int actorNumber, int argStringMax, int argArrayMax,
        int maxPerSecond, bool quarantineEnabled, int quarantineSeconds, int quarantineStrikes,
        int nowTick, string longestString, int argCount)
    {
        if (argCount > argArrayMax || (longestString != null && longestString.Length > argStringMax))
        {
            RpcsDropped++;
            RegisterStrike(actorNumber, quarantineEnabled, quarantineSeconds, quarantineStrikes, nowTick);
            return false;
        }

        if (actorNumber < 0) return true;

        Window w = Advance(actorNumber, nowTick);
        if (nowTick - w.QuarantineUntilTick < 0)
        {
            RpcsDropped++;
            return false;
        }

        w.RpcCount++;
        if (w.RpcCount > maxPerSecond)
        {
            RpcsDropped++;
            RegisterStrike(actorNumber, quarantineEnabled, quarantineSeconds, quarantineStrikes, nowTick, w);
            return false;
        }
        return true;
    }

    public static bool CheckInstantiate(int actorNumber, int maxPerSecond, bool quarantineEnabled,
        int quarantineSeconds, int quarantineStrikes, int nowTick)
    {
        if (actorNumber < 0) return true;

        Window w = Advance(actorNumber, nowTick);
        if (nowTick - w.QuarantineUntilTick < 0)
        {
            InstantiatesDropped++;
            return false;
        }

        w.InstantiateCount++;
        if (w.InstantiateCount > maxPerSecond)
        {
            InstantiatesDropped++;
            RegisterStrike(actorNumber, quarantineEnabled, quarantineSeconds, quarantineStrikes, nowTick, w);
            return false;
        }
        return true;
    }

    public static int QuarantinedCount(int nowTick)
    {
        int count = 0;
        foreach (var w in _byActor.Values)
            if (nowTick - w.QuarantineUntilTick < 0) count++;
        return count;
    }

    private static Window Advance(int actorNumber, int nowTick)
    {
        if (!_byActor.TryGetValue(actorNumber, out Window w))
        {
            w = new Window { WindowStartTick = nowTick };
            _byActor[actorNumber] = w;
        }
        if (nowTick - w.WindowStartTick >= 1000)
        {
            w.WindowStartTick = nowTick;
            w.RpcCount = 0;
            w.InstantiateCount = 0;
        }
        return w;
    }

    private static void RegisterStrike(int actorNumber, bool quarantineEnabled, int quarantineSeconds,
        int quarantineStrikes, int nowTick, Window known = null)
    {
        if (actorNumber < 0 || !quarantineEnabled) return;
        Window w = known ?? Advance(actorNumber, nowTick);
        w.StrikeCount++;
        if (w.StrikeCount >= quarantineStrikes)
        {
            w.QuarantineUntilTick = nowTick + quarantineSeconds * 1000;
            w.StrikeCount = 0;
        }
    }
}
