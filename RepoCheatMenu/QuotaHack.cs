using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace RepoCheatMenu;

// "Free quota": completes the current extraction goal for free.
//
// Host/singleplayer: ExtractionPoint recomputes its own haulCurrent from RoundDirector.currentHaul
// every tick (confirmed in the decompiled source - HaulInternalStatsUpdate sets
// "haulCurrent = RoundDirector.instance.currentHaul + RoundDirector.instance.extractionPointSurplus"),
// so forcing haulCurrent itself would just get overwritten next frame. haulGoal is what the
// success check actually compares against ("if (haulGoal - haulCurrent <= 0 && haulGoalFetched)"
// in HaulChecker) and is genuinely public (not just "publicized" for the compiler) - zeroing it
// directly is enough; we ARE the authority here, so the point's own per-tick check does the rest.
//
// Guest: no authority over that field, so this instead deliberately reproduces a real bug already
// observed in play - a freshly-activated extraction point gets its OWN goal set to
// "RoundDirector.haulGoal / RoundDirector.extractionPoints" (confirmed in the decompiled
// ExtractionPoint - a per-point slice of the run's total), computed from whatever
// RoundDirector.currentHaul the real master already has banked AT THE MOMENT OF ACTIVATION. If
// currentHaul already covers that slice (typical once you're a couple of points into a run and
// carrying surplus), the point reads as already met the instant it activates - and because
// activation itself routes through RequestExtractionPointActivation, the same master-side "do it
// unconditionally, regardless of who asked" RPC ExtractionActivatorHack already relies on, this
// is a REAL master-computed result visible to everyone, not a local illusion. Checking the
// condition first instead of hoping to catch the same accidental timing window is what turns the
// "1 quota randomly bugs and completes itself" bug into something reliable on purpose.
internal static class QuotaHack
{
    private static float _nextTick;
    private static RoundDirector _raceDirectorSeen;
    private static float _raceEarliestFireTime;
    private const float RaceMinDelay = 2f;

    private static bool RaceWindowReady(RoundDirector director)
    {
        if (director != _raceDirectorSeen)
        {
            _raceDirectorSeen = director;
            _raceEarliestFireTime = Time.unscaledTime + RaceMinDelay;
        }
        return Time.unscaledTime >= _raceEarliestFireTime;
    }

    public static void Apply()
    {
        if (!CheatState.FreeQuota.Value) return;
        if (PlayerController.instance == null) return;
        if (Time.unscaledTime < _nextTick) return;
        _nextTick = Time.unscaledTime + 0.3f;

        RoundDirector director = RoundDirector.instance;
        if (director == null) return;

        if (SemiFunc.IsMasterClientOrSingleplayer())
        {
            ExtractionPoint point = EconomyHack.FindActiveExtractionPoint(PlayerController.instance.transform.position);
            if (point != null && point.haulGoal > 0)
                point.haulGoal = 0;
            return;
        }

        if (GameReflect.Get<bool>(director, "extractionPointActive"))
        {
            // A point's already running - no real authority over ITS already-fetched goal as a
            // guest, so fall back to actually building currentHaul the legitimate way (same path
            // Spawn Valuable uses) until the master's own tally clears it. Not instant, but real.
            EconomyHack.SpawnValuable();
            return;
        }

        int extractionPointCount = GameReflect.Get<int>(director, "extractionPoints");
        if (extractionPointCount <= 0) return;

        int currentHaul = GameReflect.Get<int>(director, "currentHaul");
        int perPointGoal = GameReflect.Get<int>(director, "haulGoal") / extractionPointCount;
        if (currentHaul < perPointGoal) return; // not enough banked yet to clear a fresh point's slice - wait for one to activate normally

        List<GameObject> points = GameReflect.Get<List<GameObject>>(director, "extractionPointList");
        if (points == null) return;

        foreach (GameObject go in points)
        {
            if (go == null) continue;
            PhotonView view = go.GetComponent<PhotonView>();
            if (view == null) continue;

            director.RequestExtractionPointActivation(view.ViewID);
            return; // one is enough - the fresh goal is already covered, this triggers the real completion
        }
    }

    // Separate, standalone toggle - not tied to AutoActivateExtraction/FreeQuota above. Targets a
    // narrower, more reliable version of the same underlying race: RoundDirector.Start() runs a
    // coroutine that waits for level generation before computing the run's real haulGoal and only
    // THEN RPCs it out (confirmed in the decompiled source - "while (!LevelGenerator.Instance.Generated
    // && haulGoalMax == 0) { ... }" then "photonView.RPC('StartRoundRPC', RpcTarget.All, haulGoal)").
    // Until that finishes, RoundDirector.instance.haulGoal reads as its default, 0. A point
    // activated in that exact window computes its own goal as "0 / extractionPoints" = 0, so its
    // own success check ("haulGoal - haulCurrent <= 0") is true immediately, before a single
    // dollar has been collected - completing for free regardless of currentHaul, host or guest,
    // the instant the level loads. Same RequestExtractionPointActivation RPC as everything else
    // in this file - once the real haulGoal syncs (a few seconds into the level, well before
    // anyone normally reaches an extraction point), the window's gone and this stops firing.
    public static void ApplyZeroRace()
    {
        if (!CheatState.QuotaZeroRace.Value) return;

        RoundDirector director = RoundDirector.instance;
        if (director == null) return;
        if (!RaceWindowReady(director)) return;
        if (GameReflect.Get<bool>(director, "extractionPointActive")) return; // something's already active - not this race
        if (GameReflect.Get<int>(director, "haulGoal") != 0) return; // real goal already synced - window's closed for this level

        List<GameObject> points = GameReflect.Get<List<GameObject>>(director, "extractionPointList");
        if (points == null) return;

        foreach (GameObject go in points)
        {
            if (go == null) continue;
            PhotonView view = go.GetComponent<PhotonView>();
            if (view == null) continue;

            director.RequestExtractionPointActivation(view.ViewID);
            return;
        }
    }

    // Host/singleplayer only - self-contained (doesn't require QuotaZeroRace above to also be
    // on), same zero-race trigger, then follows through into TruckScreenText.GotoNextLevel()
    // once the point's own success animation has had time to actually finish. GotoNextLevel just
    // calls RunManager.instance.ChangeLevel(true, false) - confirmed in the decompiled RunManager
    // that ChangeLevel's very first line is "if ((!SemiFunc.MenuLevel() &&
    // !SemiFunc.IsMasterClientOrSingleplayer()) || restarting) return;", a real authority check
    // with no exploitable gap - a guest calling this (or physically pressing the real button)
    // gets a silent no-op, not even a local illusion, so this doesn't attempt it as a guest at all.
    private static float _shopTransitionAt = -1f;

    public static void ApplyAutoSkipToShop()
    {
        if (!CheatState.AutoSkipToShop.Value) { _shopTransitionAt = -1f; return; }
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return; // confirmed no-op for guests above - don't bother

        if (_shopTransitionAt >= 0f)
        {
            if (Time.unscaledTime < _shopTransitionAt) return;
            _shopTransitionAt = -1f;
            if (TruckScreenText.instance != null)
                TruckScreenText.instance.GotoNextLevel();
            CheatState.AutoSkipToShop.Value = false;
            return;
        }

        RoundDirector director = RoundDirector.instance;
        if (director == null) return;
        if (!RaceWindowReady(director)) return;
        if (GameReflect.Get<bool>(director, "extractionPointActive")) return;
        if (GameReflect.Get<int>(director, "haulGoal") != 0) return;

        List<GameObject> points = GameReflect.Get<List<GameObject>>(director, "extractionPointList");
        if (points == null) return;

        foreach (GameObject go in points)
        {
            if (go == null) continue;
            PhotonView view = go.GetComponent<PhotonView>();
            if (view == null) continue;

            director.RequestExtractionPointActivation(view.ViewID);
            _shopTransitionAt = Time.unscaledTime + 4f;
            return;
        }
    }
}
