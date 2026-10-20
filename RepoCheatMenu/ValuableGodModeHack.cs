// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System.Collections.Generic;

namespace RepoCheatMenu;

// GameTools calls this "Valuable God Mode" - keeps every valuable in the level from breaking/
// losing value on impact. Confirmed in the decompiled PhysGrabObjectImpactDetector.cs:
// Break() only marks value actually lost when "!isIndestructible && !destroyDisable" (line 794),
// and the entire Impact*/Break* call chain in PhysGrabObject.ProcessImpact is skipped outright
// when isIndestructible is true (PhysGrabObject.cs:1624, "if (hasImpactDetector &&
// !impactDetector.isIndestructible)") - so forcing that one field is enough, no need to also
// touch destroyDisable. isIndestructible is internal, reached via GameReflect same as everywhere
// else in this mod.
//
// This is the SAME field the game already drives itself as a temporary per-object grace timer
// (PhysGrabObject.timerAlterIndestructible, e.g. right after a valuable spawns/gets thrown) - our
// per-tick forcing while the toggle is on just coexists with that, same shape as every other
// "write every frame while active" hack in this mod. No restore-on-toggle-off write needed: once
// we stop forcing true, the game's own timer (already not running for most objects most of the
// time) settles it back to false through ResetIndestructible() on its own, so there's nothing of
// ours left to undo.
//
// Master-authoritative like every break/destroy path in this file: PhysGrabObject's own collision
// handler (ProcessImpact) bails out early for a non-master client in multiplayer before ever
// reaching isIndestructible, so this only actually prevents breakage as host or in singleplayer -
// same "host/singleplayer only" wall as HighStrength/KnockdownBanishKill. As a guest, forcing the
// flag on your own local copy has literally no effect (not even a local-only illusion) because
// Break() never runs on a guest's client at all for someone else's object.
internal static class ValuableGodModeHack
{
    public static void Apply()
    {
        if (!CheatState.ValuableGodMode.Value) return;

        ValuableDirector director = ValuableDirector.instance;
        if (director == null) return;

        List<ValuableObject> valuables = GameReflect.Get<List<ValuableObject>>(director, "valuableList");
        if (valuables == null) return;

        foreach (ValuableObject valuable in valuables)
        {
            if (valuable == null) continue;

            PhysGrabObject physObject = GameReflect.Get<PhysGrabObject>(valuable, "physGrabObject");
            if (physObject == null) continue;

            PhysGrabObjectImpactDetector detector = GameReflect.Get<PhysGrabObjectImpactDetector>(physObject, "impactDetector");
            if (detector == null) continue;

            GameReflect.Set(detector, "isIndestructible", true);
        }
    }
}
