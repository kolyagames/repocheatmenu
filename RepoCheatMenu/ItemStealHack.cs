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

// "Steal Held Item": every tick, checks every other player's currently-held item, and if its
// type (ItemAttributes.item.itemName - both confirmed public fields on the real runtime
// classes, no reflection needed) matches the one picked from the list, it teleports that
// specific item to you and tries to auto-grab it via PhysGrabber.OverrideGrab() - confirmed
// public in the decompiled source, the same method drones/other auto-equip effects use to put
// something straight into a player's hands. The yank itself rides PhysGrabObject.Teleport()
// (SetPositionRPC to the master, applied with zero validation - same loophole used elsewhere in
// this mod), so it works for guest or host; OverrideGrab is purely local (just sets fields your
// own PhysGrabber.Update() reads to acquire the grab), so the actual pickup afterward is
// best-effort, not independently confirmed against every possible timing.
//
// StealAllHeldItems ("Global Item Yoink") skips the name filter entirely and yanks whatever
// everyone is holding, every tick - only the last one grabbed each pass actually ends up in your
// hands (OverrideGrab replaces the previous grab), the rest just land at your feet, but every
// target still loses whatever they were holding regardless.
internal static class ItemStealHack
{
    public static void Apply()
    {
        bool stealAll = CheatState.StealAllHeldItems.Value;
        if (!stealAll && !CheatState.StealHeldItem.Value) return;

        string targetName = CheatState.StealHeldItemName.Value;
        if (!stealAll && (string.IsNullOrEmpty(targetName) || targetName == "None")) return;

        PlayerAvatar local = PlayerAvatar.instance;
        if (local == null) return;

        GameDirector director = GameDirector.instance;
        if (director == null) return;

        List<PlayerAvatar> players = GameReflect.Get<List<PlayerAvatar>>(director, "PlayerList");
        if (players == null) return;

        foreach (PlayerAvatar p in players)
        {
            if (p == null || p == local) continue;

            PhysGrabber grabber = GameReflect.Get<PhysGrabber>(p, "physGrabber");
            if (grabber == null) continue;
            if (!GameReflect.Get<bool>(grabber, "grabbed")) continue;

            PhysGrabObject held = GameReflect.Get<PhysGrabObject>(grabber, "grabbedPhysGrabObject");
            if (held == null) continue;

            if (!stealAll)
            {
                ItemAttributes attributes = held.GetComponent<ItemAttributes>();
                if (attributes == null || attributes.item == null) continue;
                if (attributes.item.itemName != targetName) continue;
            }

            Vector3 from = held.transform.position;
            Vector3 pos = local.transform.position + local.transform.forward * 1.5f + Vector3.up * 0.5f;
            GameReflect.TeleportSafe(held, pos, local.transform.rotation, "ItemStealHack");

            // Under a session ghost our PhysGrabber.OverrideGrab would be vetoed every frame by
            // GhostObserverHack.Tick's own OverrideGrabDisable, and the GrabStartedRPC the game
            // would send next is filtered anyway - so the yank itself still works (SetPositionRPC
            // on the item's scene view goes through), but we don't pretend to grab it on top.
            if (!GhostObserverHack.LocalRpcIsMuted)
            {
                PhysGrabber ownGrabber = GameReflect.Get<PhysGrabber>(local, "physGrabber");
                ownGrabber?.OverrideGrab(held);
            }

            ItemAttributes stolen = held.GetComponent<ItemAttributes>();
            EventLog.Write("items", $"stole '{(stolen != null && stolen.item != null ? stolen.item.itemName : held.name)}' " +
                $"from {PlayerTargets.NameOf(p)}: {EventLog.Pos(from)} -> {EventLog.Pos(pos)}" +
                (stealAll ? " (steal-all)" : ""));
        }
    }

    // Distinct item names currently present anywhere in the level, for the picker list.
    public static List<string> ScanItemNames()
    {
        List<string> names = new();
        foreach (ItemAttributes attributes in Object.FindObjectsByType<ItemAttributes>(FindObjectsSortMode.None))
        {
            if (attributes == null || attributes.item == null) continue;
            string name = attributes.item.itemName;
            if (string.IsNullOrEmpty(name) || names.Contains(name)) continue;
            names.Add(name);
        }
        names.Sort();
        return names;
    }
}
