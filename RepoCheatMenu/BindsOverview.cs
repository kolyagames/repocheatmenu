// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace RepoCheatMenu;

// All key bindings in one place: what is bound, which keys are bound to more than one thing (pressing such
// a key fires all of them at once - easy to do by accident with middle-click binding), and a "clear all"
// that first saves a config backup (binds live in the .cfg, so ConfigBackup can bring them back).
internal static class BindsOverview
{
    private static float _clearArmedUntil;

    public static string Info()
    {
        var bound = KeybindManager.All.Where(b => b.Vk != null && b.Vk.Value != 0).ToList();
        if (bound.Count == 0) return "Ни одной клавиши не назначено (средний клик по строке меню назначает).";

        var sb = new StringBuilder();
        sb.Append("Назначено: ").Append(bound.Count);

        foreach (var group in bound.GroupBy(b => b.Vk.Value).Where(g => g.Count() > 1))
            sb.Append("\nКонфликт ").Append(Win32.VkName(group.Key)).Append(": ")
              .Append(string.Join(", ", group.Select(b => b.Label)));

        const int MaxListed = 12; // a live-info row is not a scroll view
        foreach (Keybind b in bound.OrderBy(b => b.Label).Take(MaxListed))
            sb.Append('\n').Append(Win32.VkName(b.Vk.Value)).Append(" - ").Append(b.Label)
              .Append(b.Toggle != null ? (b.Toggle.Value ? " (вкл)" : " (выкл)") : "");
        if (bound.Count > MaxListed) sb.Append("\n... и ещё ").Append(bound.Count - MaxListed);
        return sb.ToString();
    }

    // Two presses within 6 seconds.
    public static void ClearAll()
    {
        if (Time.unscaledTime > _clearArmedUntil)
        {
            _clearArmedUntil = Time.unscaledTime + 6f;
            DynamicIsland.Notify("Бинды", "Нажмите ещё раз, чтобы снять все клавиши", false);
            return;
        }
        _clearArmedUntil = 0f;
        ConfigBackup.BackupNow();
        int n = 0;
        foreach (Keybind b in KeybindManager.All)
        {
            if (b.Vk == null || b.Vk.Value == 0) continue;
            b.Vk.Value = 0;
            n++;
        }
        DynamicIsland.Notify("Бинды", $"Снято клавиш: {n}. Вернуть - «Вернуть последнюю копию»", true);
    }
}
