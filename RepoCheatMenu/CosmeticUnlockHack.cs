// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System;
using System.IO;
using UnityEngine;

namespace RepoCheatMenu;

// Unlock every cosmetic in the game for your account via the game's own public
// MetaManager.CosmeticUnlockAll() (the developer-menu path: adds every cosmeticAssets index to
// cosmeticUnlocks and calls Save()). Equipping checks cosmeticUnlocks only locally, and what you
// wear is then synced to the room through the normal SetupCosmetics path (the same one
// MimicHostSkin uses), so everyone sees the item - it is the real thing, not a local skin.
//
// This permanently edits MetaSave.es3, so it is two-step on purpose: the first click only arms the
// button (and says so on the island), the second click within ArmSeconds does it. A timestamped
// copy of the current save is written next to it first (<save>.es3.rcm-<yyyyMMdd-HHmmss>.bak) -
// to undo, close the game and copy that file back over MetaSave.es3.
internal static class CosmeticUnlockHack
{
    private const float ArmSeconds = 6f;
    private static float _armedUntil;

    public static void UnlockAll()
    {
        MetaManager meta = MetaManager.instance;
        if (meta == null)
        {
            DynamicIsland.Notify("Косметика", "Меню мета-прогресса ещё не загружено", false);
            Plugin.Log.LogInfo("CosmeticUnlockAll: MetaManager not ready.");
            return;
        }

        if (Time.unscaledTime > _armedUntil)
        {
            _armedUntil = Time.unscaledTime + ArmSeconds;
            DynamicIsland.Notify("Все косметики", "Нажмите ещё раз за 6 с - правит MetaSave.es3 (бэкап будет)", true);
            Plugin.Log.LogInfo("CosmeticUnlockAll armed - click again within " + ArmSeconds + "s to confirm.");
            return;
        }
        _armedUntil = 0f;

        string backup = BackupSave(meta);
        if (backup == null)
        {
            DynamicIsland.Notify("Все косметики", "Бэкап сейва не удался - отменено", false);
            return;
        }
        bool changed;
        try { changed = meta.CosmeticUnlockAll(); }
        catch (Exception e)
        {
            Plugin.Log.LogError("CosmeticUnlockAll failed: " + e);
            DynamicIsland.Notify("Все косметики", "Ошибка, см. лог", false);
            return;
        }

        DynamicIsland.Notify("Все косметики", changed ? "Разблокировано" : "Уже всё открыто", changed);
        Plugin.Log.LogInfo("CosmeticUnlockAll: changed=" + changed + (backup.Length > 0 ? ", backup=" + backup : ", no save file yet, nothing to back up"));
    }

    private static string BackupSave(MetaManager meta)
    {
        try
        {
            string name = GameReflect.Get<string>(meta, "savePath") ?? "MetaSave";
            string src = Path.Combine(Application.persistentDataPath, name + ".es3");
            if (!File.Exists(src)) return "";
            string dst = src + ".rcm-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak";
            File.Copy(src, dst, false);
            return dst;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("CosmeticUnlockAll: backup failed: " + e.Message);
            return null;
        }
    }
}
