// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace RepoCheatMenu;

// Settings presets: the whole mod setup (every CheatState setting, HUD widget positions included) saved to
// one of three slots and loaded back in one click or one key, copied to the clipboard to hand to a friend,
// pasted back, or reset to defaults. Every load / paste / reset first writes the current setup to undo.txt,
// so "Отменить" always returns to how things were a moment ago.
//
// Files: RepoCheatMenu_Presets/preset<N>.txt next to the plugins (same place as the Scripts folder), plain
// "Name=value" lines. Names are CheatState field names (that class is never renamed by the obfuscator, see
// tools/obfuscar.template.xml), widget settings are "Widget.<config key>". Values use BepInEx's own serialisation
// (ConfigEntryBase.GetSerializedValue / SetSerializedValue), the exact text the .cfg file holds. Keybinds
// live in the Keybinds section, not in CheatState, so presets leave them alone.
internal static class PresetsHack
{
    public const int SlotCount = 3;
    private const string Header = "# RepoCheatMenu preset";

    // Internal state that must never travel with a preset.
    private static readonly HashSet<string> Skip = new()
    {
        nameof(CheatState.PanicHidden),
        nameof(CheatState.PanicSnapshot),
        nameof(CheatState.DebugBridgeEnabled),
    };

    private static string Dir => Path.Combine(Paths.PluginPath, "RepoCheatMenu_Presets");
    private static string SlotPath(int slot) => Path.Combine(Dir, $"preset{slot}.txt");
    private static string UndoPath => Path.Combine(Dir, "undo.txt");

    private static float _resetArmedUntil;
    public static string LastMessage = "";

    // --- the settings themselves -------------------------------------------------------------------

    private static List<(string Name, ConfigEntryBase Entry)> Entries()
    {
        var list = new List<(string, ConfigEntryBase)>();
        foreach (FieldInfo field in typeof(CheatState).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (Skip.Contains(field.Name)) continue;
            object value = field.GetValue(null);
            if (value is ConfigEntryBase entry)
            {
                list.Add((field.Name, entry));
            }
            else if (value is CheatState.HudWidgetConfig widget)
            {
                // Named by the config key (e.g. "HudLootPosX"), not the nested field name: the obfuscator
                // may rename HudWidgetConfig's fields between builds, the .cfg key never changes.
                foreach (FieldInfo sub in typeof(CheatState.HudWidgetConfig).GetFields(BindingFlags.Public | BindingFlags.Instance))
                    if (sub.GetValue(widget) is ConfigEntryBase subEntry)
                        list.Add(("Widget." + subEntry.Definition.Key, subEntry));
            }
        }
        return list;
    }

    private static string Serialize()
    {
        var sb = new StringBuilder();
        sb.AppendLine(Header);
        sb.AppendLine($"# {DateTime.Now:yyyy-MM-dd HH:mm}");
        foreach (var (name, entry) in Entries())
            sb.Append(name).Append('=').AppendLine(entry.GetSerializedValue());
        return sb.ToString();
    }

    // Applies "Name=value" lines; unknown names (a preset from an older/newer build) are skipped. Saving the
    // .cfg after every single change would rewrite the whole file a few hundred times, so autosave is paused
    // for the batch and the file is written once at the end.
    private static int Apply(string text, out int skipped)
    {
        skipped = 0;
        var map = new Dictionary<string, ConfigEntryBase>();
        foreach (var (name, entry) in Entries()) map[name] = entry;

        ConfigFile file = null;
        foreach (var kv in map) { file = kv.Value.ConfigFile; break; }
        bool autosave = file != null && file.SaveOnConfigSet;
        if (file != null) file.SaveOnConfigSet = false;

        int applied = 0;
        // A preset flips dozens of toggles at once; one pill each would bury the screen.
        bool wasMuted = DynamicIsland.Muted;
        DynamicIsland.Muted = true;
        try
        {
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) { skipped++; continue; }
                if (!map.TryGetValue(line.Substring(0, eq), out ConfigEntryBase entry)) { skipped++; continue; }
                try
                {
                    entry.SetSerializedValue(line.Substring(eq + 1));
                    applied++;
                }
                catch (Exception) { skipped++; }
            }
        }
        finally
        {
            DynamicIsland.Muted = wasMuted;
            if (file != null)
            {
                file.SaveOnConfigSet = autosave;
                file.Save();
            }
        }
        return applied;
    }

    private static bool WriteFile(string path, string text)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(path, text, new UTF8Encoding(false));
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Presets: could not write {path}: {e.Message}");
            return false;
        }
    }

    private static void Report(string title, string text, bool good)
    {
        LastMessage = text;
        Plugin.Log.LogInfo($"Presets: {title} - {text}");
        DynamicIsland.Notify(title, text, good);
    }

    private static bool BackupForUndo() => WriteFile(UndoPath, Serialize());

    // --- slots ----------------------------------------------------------------------------------------

    public static void Save(int slot)
    {
        if (WriteFile(SlotPath(slot), Serialize()))
            Report($"Пресет {slot}", $"Сохранён ({Entries().Count} настроек)", true);
        else
            Report($"Пресет {slot}", "Не удалось сохранить файл", false);
    }

    public static void Load(int slot)
    {
        string path = SlotPath(slot);
        if (!File.Exists(path)) { Report($"Пресет {slot}", "Пусто - сначала сохраните", false); return; }
        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception e) { Report($"Пресет {slot}", "Не читается: " + e.Message, false); return; }

        if (!BackupForUndo()) { Report($"Пресет {slot}", "Отмена: не удалось сохранить резервную копию", false); return; }
        int applied = Apply(text, out int skipped);
        Report($"Пресет {slot}", $"Загружен: {applied} настроек" + (skipped > 0 ? $", пропущено {skipped}" : ""), true);
    }

    public static string SlotInfo(int slot)
    {
        string path = SlotPath(slot);
        if (!File.Exists(path)) return $"Пресет {slot}: пусто";
        try { return $"Пресет {slot}: сохранён {File.GetLastWriteTime(path):dd.MM HH:mm}"; }
        catch (Exception) { return $"Пресет {slot}: есть"; }
    }

    // Separate parameterless methods: the keybind list tells actions apart by delegate equality.
    public static void Save1() => Save(1);
    public static void Save2() => Save(2);
    public static void Save3() => Save(3);
    public static void Load1() => Load(1);
    public static void Load2() => Load(2);
    public static void Load3() => Load(3);

    // --- undo, reset, clipboard ----------------------------------------------------------------------

    public static void Undo()
    {
        if (!File.Exists(UndoPath)) { Report("Отмена", "Нечего отменять", false); return; }
        string text;
        try { text = File.ReadAllText(UndoPath); }
        catch (Exception e) { Report("Отмена", "Не читается: " + e.Message, false); return; }

        // The current setup becomes the new undo, so pressing it twice swaps back and forth.
        string current = Serialize();
        int applied = Apply(text, out _);
        WriteFile(UndoPath, current);
        Report("Отмена", $"Возвращены прежние настройки ({applied})", true);
    }

    // Two presses within 6 seconds: the first arms, the second resets every setting to its default.
    public static void ResetAll()
    {
        if (Time.unscaledTime > _resetArmedUntil)
        {
            _resetArmedUntil = Time.unscaledTime + 6f;
            Report("Сброс", "Нажмите ещё раз в течение 6 с, чтобы сбросить всё", false);
            return;
        }
        _resetArmedUntil = 0f;
        if (!BackupForUndo()) { Report("Сброс", "Отмена: не удалось сохранить резервную копию", false); return; }

        var sb = new StringBuilder();
        foreach (var (name, entry) in Entries())
        {
            object def = entry.DefaultValue;
            sb.Append(name).Append('=').AppendLine(TomlTypeConverter.ConvertToString(def, entry.SettingType));
        }
        int applied = Apply(sb.ToString(), out _);
        Report("Сброс", $"Все настройки по умолчанию ({applied}). «Отменить» вернёт прежние", true);
    }

    public static void CopyToClipboard()
    {
        try
        {
            GUIUtility.systemCopyBuffer = Serialize();
            Report("Пресет", "Текущие настройки скопированы в буфер обмена", true);
        }
        catch (Exception e) { Report("Пресет", "Буфер обмена недоступен: " + e.Message, false); }
    }

    public static void PasteFromClipboard()
    {
        string text;
        try { text = GUIUtility.systemCopyBuffer; }
        catch (Exception e) { Report("Пресет", "Буфер обмена недоступен: " + e.Message, false); return; }

        // Only text that really is a preset: anything else in the clipboard would just be noise.
        if (string.IsNullOrEmpty(text) || !text.TrimStart().StartsWith(Header))
        {
            Report("Пресет", "В буфере нет пресета мода", false);
            return;
        }
        if (!BackupForUndo()) { Report("Пресет", "Отмена: не удалось сохранить резервную копию", false); return; }
        int applied = Apply(text, out int skipped);
        Report("Пресет", $"Из буфера: {applied} настроек" + (skipped > 0 ? $", пропущено {skipped}" : ""), true);
    }
}
