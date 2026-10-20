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

internal static class LevelLoaderHack
{
    private static IEnumerable<Level> AllLevels(RunManager manager) =>
        (manager.levels ?? new List<Level>())
        .Concat(manager.levelArena ?? new List<Level>())
        .Concat(manager.levelShop ?? new List<Level>());

    public static List<string> ScanLevelNames()
    {
        RunManager manager = RunManager.instance;
        if (manager == null) return new List<string>();

        List<string> names = new();
        foreach (Level level in AllLevels(manager))
        {
            if (level == null) continue;
            string name = level.name;
            if (!string.IsNullOrEmpty(name) && !names.Contains(name))
                names.Add(name);
        }
        names.Sort();
        return names;
    }

    public static void LoadSelected()
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer())
        {
            Plugin.Log.LogInfo("Load Level had no effect - only works while hosting or in singleplayer.");
            return;
        }

        RunManager manager = RunManager.instance;
        if (manager == null) return;

        string targetName = CheatState.LevelLoaderName.Value;
        if (string.IsNullOrEmpty(targetName) || targetName == "None") return;

        Level level = AllLevels(manager).FirstOrDefault(l => l != null && l.name == targetName);
        if (level == null)
        {
            Plugin.Log.LogInfo($"Load Level had no effect - '{targetName}' not found in the current level list.");
            return;
        }

        GameReflect.Set(manager, "debugLevel", level);
        manager.ChangeLevel(false, false);
        GameReflect.Set(manager, "debugLevel", null);
    }

    private static IEnumerable<PrefabRef> CurrentLevelModules(Level level) =>
        (level.ModulesNormal1 ?? new List<PrefabRef>())
        .Concat(level.ModulesNormal2 ?? new List<PrefabRef>())
        .Concat(level.ModulesNormal3 ?? new List<PrefabRef>())
        .Concat(level.ModulesPassage1 ?? new List<PrefabRef>())
        .Concat(level.ModulesPassage2 ?? new List<PrefabRef>())
        .Concat(level.ModulesPassage3 ?? new List<PrefabRef>())
        .Concat(level.ModulesDeadEnd1 ?? new List<PrefabRef>())
        .Concat(level.ModulesDeadEnd2 ?? new List<PrefabRef>())
        .Concat(level.ModulesDeadEnd3 ?? new List<PrefabRef>())
        .Concat(level.ModulesExtraction1 ?? new List<PrefabRef>())
        .Concat(level.ModulesExtraction2 ?? new List<PrefabRef>())
        .Concat(level.ModulesExtraction3 ?? new List<PrefabRef>())
        .Where(x => x != null);

    private static string ModuleDisplayName(PrefabRef prefab) =>
        string.Join(" - ", prefab.PrefabName.Split(" - ").Skip(2));

    public static List<string> ScanCurrentLevelModuleNames()
    {
        RunManager manager = RunManager.instance;
        Level level = manager != null ? manager.levelCurrent : null;
        if (level == null) return new List<string>();

        List<string> names = new();
        foreach (PrefabRef prefab in CurrentLevelModules(level))
        {
            if (!prefab.IsValid()) continue;
            string name = ModuleDisplayName(prefab);
            if (!string.IsNullOrEmpty(name) && !names.Contains(name))
                names.Add(name);
        }
        names.Sort();
        return names;
    }

    private static PrefabRef _pendingModule;
    private static Level _pendingModuleLevel;
    private static Module.Type _pendingModuleType;

    public static void ForceCurrentLevelModule()
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer())
        {
            Plugin.Log.LogInfo("Force Module had no effect - only works while hosting or in singleplayer.");
            return;
        }

        RunManager manager = RunManager.instance;
        Level level = manager != null ? manager.levelCurrent : null;
        if (level == null) return;

        string targetName = CheatState.ModuleLoaderName.Value;
        if (string.IsNullOrEmpty(targetName) || targetName == "None") return;

        PrefabRef match = CurrentLevelModules(level).FirstOrDefault(x => x.IsValid() && ModuleDisplayName(x) == targetName);
        if (match == null)
        {
            Plugin.Log.LogInfo($"Force Module had no effect - '{targetName}' not found in this level's module pool.");
            return;
        }

        Module.Type type = Module.Type.Normal;
        string[] parts = match.PrefabName.ToLower().Split(" - ");
        if (parts.Length > 2)
        {
            switch (parts[2])
            {
                case "p": type = Module.Type.Passage; break;
                case "de": type = Module.Type.DeadEnd; break;
                case "e": type = Module.Type.Extraction; break;
            }
        }

        _pendingModule = match;
        _pendingModuleLevel = level;
        _pendingModuleType = type;

        manager.RestartScene();
        Plugin.Log.LogInfo($"Force Module: regenerating this level with '{targetName}' forced in.");
    }

    public static void ApplyModuleInjection()
    {
        if (_pendingModule == null) return;

        LevelGenerator generator = LevelGenerator.Instance;
        RunManager manager = RunManager.instance;
        if (generator == null || generator.Generated || manager == null || manager.levelCurrent != _pendingModuleLevel)
            return;

        GameReflect.Set(generator, "DebugModule", _pendingModule);
        switch (_pendingModuleType)
        {
            case Module.Type.Normal:
                GameReflect.Set(generator, "DebugNormal", true);
                break;
            case Module.Type.Passage:
                GameReflect.Set(generator, "DebugPassage", true);
                GameReflect.Set(generator, "DebugAmount", 6);
                break;
            case Module.Type.DeadEnd:
            case Module.Type.Extraction:
                GameReflect.Set(generator, "DebugDeadEnd", true);
                GameReflect.Set(generator, "DebugAmount", 1);
                break;
        }
    }
}
