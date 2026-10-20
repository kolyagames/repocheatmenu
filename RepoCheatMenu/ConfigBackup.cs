// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace RepoCheatMenu;

// Backups of the whole mod config file (all settings AND keybinds - unlike presets, which leave binds alone).
// One automatic copy per day at game start, the last MaxBackups kept, plus a manual "copy now". Restore puts
// the chosen copy over the .cfg and asks BepInEx to Reload() it, so the change applies immediately; the file
// it replaces is kept as before-restore.cfg first, so a restore can itself be undone by restoring that.
// Folder: RepoCheatMenu_Presets\backups next to the plugins.
internal static class ConfigBackup
{
    private const int MaxBackups = 7;
    private static readonly TimeSpan AutoInterval = TimeSpan.FromHours(20);
    private static float _restoreArmedUntil;
    public static string LastMessage = "";

    private static string Dir => Path.Combine(BepInEx.Paths.PluginPath, "RepoCheatMenu_Presets", "backups");
    private static string BeforeRestorePath => Path.Combine(Dir, "before-restore.cfg");

    private static ConfigFile Config => Plugin.Instance != null ? Plugin.Instance.Config : null;

    private static FileInfo[] Backups()
    {
        try
        {
            if (!Directory.Exists(Dir)) return Array.Empty<FileInfo>();
            return new DirectoryInfo(Dir).GetFiles("cfg_*.cfg").OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
        }
        catch (Exception) { return Array.Empty<FileInfo>(); }
    }

    // Plugin.Awake, right after the settings are bound (so the file on disk is complete).
    public static void AutoBackup()
    {
        try
        {
            FileInfo[] existing = Backups();
            if (existing.Length > 0 && DateTime.UtcNow - existing[0].LastWriteTimeUtc < AutoInterval) return;
            MakeCopy();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("ConfigBackup: automatic backup failed: " + e.Message);
        }
    }

    private static bool MakeCopy()
    {
        ConfigFile cfg = Config;
        if (cfg == null || !File.Exists(cfg.ConfigFilePath)) return false;
        cfg.Save();
        Directory.CreateDirectory(Dir);
        string target = Path.Combine(Dir, $"cfg_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.cfg");
        File.Copy(cfg.ConfigFilePath, target, overwrite: true);

        FileInfo[] all = Backups();
        for (int i = MaxBackups; i < all.Length; i++)
        {
            try { all[i].Delete(); } catch (Exception) { /* locked or gone - next time */ }
        }
        return true;
    }

    public static void BackupNow()
    {
        try
        {
            Report(MakeCopy() ? "Резервная копия сохранена" : "Файл настроек не найден", true);
        }
        catch (Exception e) { Report("Не удалось: " + e.Message, false); }
    }

    // Two presses within 6 seconds: restores the newest copy.
    public static void RestoreLatest()
    {
        FileInfo[] all = Backups();
        if (all.Length == 0) { Report("Копий пока нет", false); return; }
        if (Time.unscaledTime > _restoreArmedUntil)
        {
            _restoreArmedUntil = Time.unscaledTime + 6f;
            Report($"Нажмите ещё раз, чтобы вернуть копию от {all[0].LastWriteTime:dd.MM HH:mm}", false);
            return;
        }
        _restoreArmedUntil = 0f;
        RestoreFrom(all[0].FullName, $"Возвращена копия от {all[0].LastWriteTime:dd.MM HH:mm}");
    }

    public static void UndoRestore()
    {
        if (!File.Exists(BeforeRestorePath)) { Report("Нечего отменять", false); return; }
        string temp = BeforeRestorePath + ".tmp";
        try { File.Copy(BeforeRestorePath, temp, overwrite: true); }
        catch (Exception e) { Report("Не удалось: " + e.Message, false); return; }
        RestoreFrom(temp, "Настройки до восстановления возвращены");
        try { File.Delete(temp); } catch (Exception) { }
    }

    private static void RestoreFrom(string source, string doneText)
    {
        ConfigFile cfg = Config;
        if (cfg == null) { Report("Настройки ещё не загружены", false); return; }
        try
        {
            cfg.Save();
            Directory.CreateDirectory(Dir);
            File.Copy(cfg.ConfigFilePath, BeforeRestorePath, overwrite: true);
            File.Copy(source, cfg.ConfigFilePath, overwrite: true);
            bool wasMuted = DynamicIsland.Muted;
            DynamicIsland.Muted = true; // a restore flips many toggles; no pill for each
            try { cfg.Reload(); }
            finally { DynamicIsland.Muted = wasMuted; }
            Report(doneText, true);
        }
        catch (Exception e)
        {
            Report("Не удалось: " + e.Message, false);
        }
    }

    public static string Info()
    {
        FileInfo[] all = Backups();
        if (all.Length == 0) return "Копий пока нет (первая появится при следующем запуске или кнопкой).";
        string s = $"Копий: {all.Length}, последняя {all[0].LastWriteTime:dd.MM HH:mm}";
        return LastMessage.Length > 0 ? s + "\nПоследнее: " + LastMessage : s;
    }

    private static void Report(string text, bool good)
    {
        LastMessage = text;
        Plugin.Log.LogInfo("ConfigBackup: " + text);
        DynamicIsland.Notify("Резервная копия", text, good);
    }
}
