// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System.Collections.Generic;
using System.Linq;

namespace RepoCheatMenu;

// One-shot "become that player": copies the name AND the look of the player picked in "Игроки"
// onto you, through the same two legitimate paths the host-mimic toggles already use:
//   * name  - NameChangerHack.SetName (AddToStatsManagerRPC, owner-checked, you own your avatar);
//   * skin  - PlayerCosmetics.SetupCosmetics / SetupColors on your own avatar (synced:true), the exact
//             calls the game's own cosmetics menu makes, fed the target's equipped list and colours.
// Everyone in the room sees the result. Your real steamID is untouched, so upgrades and stats stay yours.
internal static class CopyIdentityHack
{
    public static void CopyPicked()
    {
        PlayerAvatar local = PlayerAvatar.instance;
        PlayerAvatar target = PlayerTargets.Picked();

        if (local == null)
        {
            DynamicIsland.Notify("Копирование", "Вы не в игре", false);
            return;
        }
        if (target == null || target == local)
        {
            DynamicIsland.Notify("Копирование", "Сначала выберите другого игрока", false);
            return;
        }

        string name = GameReflect.Get<string>(target, "playerName");
        bool nameOk = !string.IsNullOrWhiteSpace(name);
        if (nameOk) NameChangerHack.SetName(name);

        bool skinOk = false;
        if (target.playerCosmetics != null && local.playerCosmetics != null)
        {
            List<int> equipped = GameReflect.Get<List<int>>(target.playerCosmetics, "cosmeticEquippedRaw");
            int[] colors = GameReflect.Get<int[]>(target.playerCosmetics, "colorsEquipped");
            if (equipped != null)
            {
                local.playerCosmetics.SetupCosmetics(true, true, new List<int>(equipped));
                skinOk = true;
            }
            if (colors != null)
            {
                local.playerCosmetics.SetupColors(true, (int[])colors.Clone());
                skinOk = true;
            }
        }

        string shown = nameOk ? name : "игрок";
        DynamicIsland.Notify("Скопирован: " + shown,
            nameOk && skinOk ? "ник и скин" : nameOk ? "только ник" : skinOk ? "только скин" : "нечего копировать",
            nameOk || skinOk);
    }
}
