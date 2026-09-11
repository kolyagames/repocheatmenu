using UnityEngine;

namespace RepoCheatMenu;

// UpgradeStand.cs (decompiled): each stand rolls its own random 1-3 reroll cap the first time you
// press its button ("maxRerollCount = Random.Range(1, 4)"), then locks into State.Broken once
// rerollCount reaches it. Not a simple RPC-bypass like Revive/Freeze - StatePressSucceed()
// confirms the ENTIRE press-processing state machine (cost deduction, the rerollCount increment,
// the max-count roll itself) only runs under SemiFunc.IsMasterClientOrSingleplayer(), so a guest
// physically can't trigger any of it themselves - master authority is baked into the state
// machine, not just gated at an RPC boundary like most of this mod's other host/singleplayer-only
// tricks. Host/singleplayer only: forces maxRerollCount up before the stand's own Update() can
// ever act on the real roll, so it never reaches its lockout. Still costs the real, escalating
// RerollCost (5 + rerollCount*5, confirmed in the decompiled source) in currency per reroll - this
// only removes the random 1-3 attempt cap, not the price.
internal static class UpgradeStandHack
{
    private const int UnlockedMaxReroll = 999;

    public static void Apply()
    {
        if (!CheatState.UnlimitedRerolls.Value) return;
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;

        foreach (UpgradeStand stand in Object.FindObjectsByType<UpgradeStand>(FindObjectsSortMode.None))
        {
            if (stand == null) continue;

            int maxReroll = GameReflect.Get<int>(stand, "maxRerollCount");
            if (maxReroll >= 0 && maxReroll < UnlockedMaxReroll)
                GameReflect.Set(stand, "maxRerollCount", UnlockedMaxReroll);
        }
    }
}
