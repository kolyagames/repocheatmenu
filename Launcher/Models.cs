// ============================================================================
// RepoLauncher - лаунчер модпаков RepoCheatMenu
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// ============================================================================

using System.Text.Json;

namespace RepoLauncher;

// modpacks.json lives next to the exe and is meant to be edited: which packs exist, which packages
// (folders under packages/) each one installs, and which config values it sets. The defaults
// below are written out on first run.
internal sealed class ModpackFile
{
    public List<Pack> Packs { get; set; } = new();
    public List<Package> Packages { get; set; } = new();
}

internal sealed class Pack
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string Version { get; set; }
    public List<string> Packages { get; set; } = new();
    public List<ConfigEdit> Config { get; set; } = new();
}

// A folder under packages/ that is copied to BepInEx/plugins/<Target>/ of the pack's profile.
internal sealed class Package
{
    public string Id { get; set; }
    public string Title { get; set; }
    public string Note { get; set; }
    public string Dir { get; set; }
    public string Target { get; set; }
}

// One value in a BepInEx .cfg file of the profile (BepInEx/config/<File>).
internal sealed class ConfigEdit
{
    public string File { get; set; }
    public string Section { get; set; }
    public string Key { get; set; }
    public string Value { get; set; }
}

internal static class Modpacks
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private const string DefaultJson = """
    {
      "packs": [
        {
          "id": "default",
          "name": "DEFAULT",
          "description": "Только мод RepoCheatMenu, ничего лишнего",
          "version": "1.0.37",
          "packages": [ "repocheatmenu" ],
          "config": [
            { "file": "local.repocheatmenu.cfg", "section": "Misc", "key": "Optimization", "value": "false" }
          ]
        },
        {
          "id": "optimization",
          "name": "OPTIMIZATION",
          "description": "Макс. FPS: без теней, сглаживания и блума",
          "version": "1.0.37",
          "packages": [ "repocheatmenu" ],
          "config": [
            { "file": "local.repocheatmenu.cfg", "section": "Misc", "key": "Optimization", "value": "true" }
          ]
        },
        {
          "id": "hd",
          "name": "REPO HD",
          "description": "HD-графика: DLSS / FSR / SMAA, ultrawide",
          "version": "1.0.37",
          "packages": [ "repocheatmenu", "menulib", "repofidelity" ],
          "config": [
            { "file": "local.repocheatmenu.cfg", "section": "Misc", "key": "Optimization", "value": "false" }
          ]
        }
      ],
      "packages": [
        { "id": "repocheatmenu", "title": "RepoCheatMenu", "note": "Ядро мода и горячая перезагрузка", "dir": "RepoCheatMenu", "target": "local-RepoCheatMenu" },
        { "id": "menulib", "title": "MenuLib", "note": "Библиотека меню (нужна REPOFidelity)", "dir": "nickklmao-MenuLib", "target": "nickklmao-MenuLib" },
        { "id": "repofidelity", "title": "REPOFidelity", "note": "HD-графика, DLSS, FSR, SMAA", "dir": "Vippy-REPOFidelity", "target": "Vippy-REPOFidelity" }
      ]
    }
    """;

    public static ModpackFile Load(string path)
    {
        if (!File.Exists(path)) File.WriteAllText(path, DefaultJson);
        ModpackFile file = JsonSerializer.Deserialize<ModpackFile>(File.ReadAllText(path), Json);
        if (file == null || file.Packs.Count == 0) throw new InvalidDataException("modpacks.json has no packs");
        return file;
    }
}

// Remembers which pack was chosen last.
internal sealed class Settings
{
    public string ActivePack { get; set; }
    public string GameDir { get; set; }

    public static Settings Load(string path)
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Modpacks.Json) ?? new Settings(); }
        catch { return new Settings(); }
    }

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, Modpacks.Json));
        }
        catch { /* losing the remembered choice is not worth failing over */ }
    }
}
