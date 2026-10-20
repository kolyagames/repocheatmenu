// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

namespace RepoCheatMenu;

// "Scratch" for the mod: a script is a flat list of blocks (what the editor tab shows) that the runner
// executes top to bottom, one step per frame at most. Containers (Repeat / Forever / If...) own every
// block up to their matching "End" block, so nesting is just the order in the list - the editor indents it.
// "Else" splits the innermost If-type container into a then-part and an else-part.
//
// Everything here only drives things the player could do by hand: flip the mod's own toggles, press the
// game's own "join lobby" path (SteamManager.JoinSteamLobby, the same method the invite button calls),
// leave to the main menu the way the pause menu does. Joining is rate-limited and only ever targets public,
// open, non-password lobbies that the Steam lobby list itself returns.
internal sealed class Block
{
    public string Kind;
    public float N;       // main number (seconds / count / timeout)
    public float N2;      // second number (random-wait max / min players / "invert" flag)
    public int Mode;      // cycle parameter
    public string Text = "";

    public Block Clone() => new() { Kind = Kind, N = N, N2 = N2, Mode = Mode, Text = Text };
}

internal sealed class BlockDef
{
    public string Id, Title, Category;
    public bool Container;
    public BlockDef(string id, string title, string category, bool container = false)
    {
        Id = id; Title = title; Category = category; Container = container;
    }
}

internal static class BlockDefs
{
    // Ids are what the .blk files store - never rename one (Restore keeps its old id "alloff" for that reason).
    public const string Wait = "wait", WaitRandom = "waitrnd", SetFeature = "feature", JoinNew = "joinnew",
        WaitInRoom = "inroom", WaitLevel = "level", Leave = "leave", Notify = "notify", Repeat = "repeat",
        Forever = "forever", IfInRoom = "ifroom", IfOk = "ifok", End = "end", ClearVisited = "forget", Stop = "stop",
        SpawnItem = "spawnitem", SpawnValuable = "spawnval", WaitPlayers = "players", IfChance = "chance",
        Teleport = "tp", SetValue = "setval", IfScene = "ifscene", WaitScene = "waitscene", IfMoney = "ifmoney",
        IfHp = "ifhp", PickPlayer = "pickplayer", CallScript = "call", Else = "else", SetVar = "setvar", IfVar = "ifvar",
        Log = "log", Restore = "alloff", WaitDead = "waitdead", Break = "break";

    // Palette order: blocks of one category are listed together.
    public static readonly BlockDef[] All =
    {
        new(JoinNew, "Зайти на новый сервер", "Сеть"),
        new(Leave, "Выйти в главное меню", "Сеть"),
        new(ClearVisited, "Забыть посещённые серверы", "Сеть"),

        new(Wait, "Ждать", "Ожидание"),
        new(WaitRandom, "Ждать случайно", "Ожидание"),
        new(WaitInRoom, "Ждать, пока я в комнате", "Ожидание"),
        new(WaitLevel, "Ждать загрузки уровня", "Ожидание"),
        new(WaitPlayers, "Ждать игроков в комнате", "Ожидание"),
        new(WaitScene, "Ждать сцену", "Ожидание"),
        new(WaitDead, "Ждать, пока я умру", "Ожидание"),

        new(SetFeature, "Функция мода", "Мод"),
        new(SetValue, "Установить значение", "Мод"),
        new(Restore, "Вернуть настройки до скрипта", "Мод"),
        new(Teleport, "Телепорт", "Мод"),
        new(PickPlayer, "Выбрать игрока", "Мод"),
        new(SpawnItem, "Заспавнить предмет", "Мод"),
        new(SpawnValuable, "Заспавнить ценность", "Мод"),
        new(SetVar, "Переменная", "Мод"),
        new(CallScript, "Запустить скрипт", "Мод"),
        new(Notify, "Уведомление", "Мод"),
        new(Log, "Записать в лог", "Мод"),

        new(Repeat, "Повторить", "Управление", true),
        new(Forever, "Всегда", "Управление", true),
        new(Break, "Прервать цикл", "Управление"),
        new(IfInRoom, "Если", "Управление", true),
        new(IfOk, "Если прошлый блок", "Управление", true),
        new(IfChance, "С шансом", "Управление", true),
        new(IfScene, "Если сцена", "Управление", true),
        new(IfMoney, "Если деньги", "Управление", true),
        new(IfHp, "Если моё HP", "Управление", true),
        new(IfVar, "Если переменная", "Управление", true),
        new(Else, "Иначе", "Управление"),
        new(End, "Конец", "Управление"),
        new(Stop, "Остановить скрипт", "Управление"),
    };

    private static readonly Dictionary<string, BlockDef> ById = All.ToDictionary(d => d.Id);
    public static BlockDef Get(string id) => id != null && ById.TryGetValue(id, out BlockDef d) ? d : null;

    public static bool IsContainer(string id) => Get(id)?.Container == true;

    public static bool IsIf(string id) => id == IfInRoom || id == IfOk || id == IfChance ||
        id == IfScene || id == IfMoney || id == IfHp || id == IfVar;

    public static Block Create(string id)
    {
        var b = new Block { Kind = id };
        switch (id)
        {
            case Wait: b.N = 5f; break;
            case WaitRandom: b.N = 3f; b.N2 = 10f; break;
            case JoinNew: b.N = 40f; b.N2 = 1f; break;
            case WaitInRoom: b.N = 40f; break;
            case WaitLevel: b.N = 120f; break;
            case Repeat: b.N = 3f; break;
            case SetFeature: b.Text = "AutoLoot"; break;
            case SpawnItem: case SpawnValuable: b.N = 10f; b.N2 = 0.3f; b.Text = ""; break;
            case WaitPlayers: b.N = 2f; b.N2 = 120f; break;
            case IfChance: b.N = 50f; break;
            case Teleport: b.N = 5f; break;
            case WaitScene: b.Mode = 2; b.N = 120f; break;
            case IfScene: b.Mode = 2; break;
            case IfMoney: b.N = 100f; break;
            case IfHp: b.N = 50f; break;
            case SetVar: b.Text = "счёт"; b.N = 1f; b.Mode = 1; break;
            case IfVar: b.Text = "счёт"; b.N = 3f; break;
            case Log: b.Text = "метка"; break;
            case WaitDead: b.N = 300f; break;
            case SetValue: b.N = 1f; break;
            case Notify: b.Text = "Скрипт: привет"; break;
        }
        return b;
    }

    public static UnityEngine.Color CategoryColor(string category) => category switch
    {
        "Сеть" => new UnityEngine.Color(0.30f, 0.62f, 1f),
        "Мод" => new UnityEngine.Color(0.56f, 0.42f, 1f),
        "Ожидание" => new UnityEngine.Color(1f, 0.72f, 0.2f),
        _ => new UnityEngine.Color(1f, 0.45f, 0.35f),
    };
}

internal static class BlockScripts
{
    // ---- storage -----------------------------------------------------------------------------------------------

    internal static ConfigEntry<string> AutoRunName;
    private static readonly SortedDictionary<string, List<Block>> Saved = new(StringComparer.OrdinalIgnoreCase);
    internal static IEnumerable<string> SavedNames => Saved.Keys;

    // The editor's working copy. Autosaved every couple of seconds (see AutosaveDraft), so closing the game or
    // switching scripts never loses unsaved edits.
    internal static List<Block> Draft = new();
    internal static string DraftName = ExampleName;

    private const string ExampleName = "Новый сервер";
    private const string TourName = "Обход серверов";
    private const string ExportHeader = "REPOBLK1";

    private static string Folder => Path.Combine(Paths.ConfigPath, "RepoCheatMenu_Blocks");
    private static string VisitedFile => Path.Combine(Folder, "visited_lobbies.txt");
    private static string DraftFile => Path.Combine(Folder, "editor.draft");

    internal static void Init(ConfigFile config)
    {
        AutoRunName = config.Bind("Blocks", "AutoRunScript", "", "Name of a saved block script to run once, a while after the main menu appears. Empty = off.");
        KeybindManager.RegisterAction(config, "BlockScriptRunStop", ToggleDraftRun);
        try
        {
            Directory.CreateDirectory(Folder);
            foreach (string file in Directory.GetFiles(Folder, "*.blk"))
            {
                try { Saved[Path.GetFileNameWithoutExtension(file)] = Deserialize(File.ReadAllText(file)); }
                catch (Exception e) { Plugin.Log.LogWarning("[Blocks] bad script file " + file + ": " + e.Message); }
            }
            if (Saved.Count == 0)
            {
                Save(ExampleName, Example());
                Save(TourName, TourExample());
            }
            LoadDraft();
            LoadVisited();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[Blocks] init: " + e.Message);
        }
    }

    private static List<Block> Example() => new()
    {
        new Block { Kind = BlockDefs.Notify, Text = "Ищу сервер, где я ещё не был" },
        new Block { Kind = BlockDefs.JoinNew, N = 40f, N2 = 1f },
        new Block { Kind = BlockDefs.IfOk },
        new Block { Kind = BlockDefs.WaitLevel, N = 180f },
        new Block { Kind = BlockDefs.IfOk },
        new Block { Kind = BlockDefs.SetFeature, Text = "EspValuables" },
        new Block { Kind = BlockDefs.Notify, Text = "Уровень загружен, подсветка ценностей включена" },
        new Block { Kind = BlockDefs.Else },
        new Block { Kind = BlockDefs.Notify, Text = "Уровень так и не начался (хост ещё в лобби?)" },
        new Block { Kind = BlockDefs.End },
        new Block { Kind = BlockDefs.Else },
        new Block { Kind = BlockDefs.Notify, Text = "Новых серверов не нашлось" },
        new Block { Kind = BlockDefs.End },
    };

    private static List<Block> TourExample() => new()
    {
        new Block { Kind = BlockDefs.SetVar, Text = "заходов", Mode = 0, N = 0f },
        new Block { Kind = BlockDefs.Repeat, N = 3f },
        new Block { Kind = BlockDefs.JoinNew, N = 40f, N2 = 1f },
        new Block { Kind = BlockDefs.IfOk },
        new Block { Kind = BlockDefs.SetVar, Text = "заходов", Mode = 1, N = 1f },
        new Block { Kind = BlockDefs.Wait, N = 60f },
        new Block { Kind = BlockDefs.Leave },
        new Block { Kind = BlockDefs.Else },
        new Block { Kind = BlockDefs.Break },
        new Block { Kind = BlockDefs.End },
        new Block { Kind = BlockDefs.End },
        new Block { Kind = BlockDefs.IfVar, Text = "заходов", N = 1f },
        new Block { Kind = BlockDefs.Notify, Text = "Обход закончен" },
        new Block { Kind = BlockDefs.Else },
        new Block { Kind = BlockDefs.Notify, Text = "Ни одного нового сервера" },
        new Block { Kind = BlockDefs.End },
    };

    internal static List<Block> Clone(List<Block> list) => list.Select(b => b.Clone()).ToList();

    internal static List<Block> Load(string name) =>
        name != null && Saved.TryGetValue(name.Trim(), out List<Block> l) ? Clone(l) : null;

    internal static string SanitizeName(string name)
    {
        name = (name ?? "").Trim();
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    // Returns the name the script was actually saved under (invalid file-name characters replaced), or null.
    internal static string Save(string name, List<Block> blocks)
    {
        name = SanitizeName(name);
        if (name.Length == 0) return null;
        Saved[name] = Clone(blocks);
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, name + ".blk"), Serialize(blocks));
        }
        catch (Exception e) { Plugin.Log.LogWarning("[Blocks] save: " + e.Message); }
        return name;
    }

    internal static bool Delete(string name)
    {
        name = SanitizeName(name);
        if (name.Length == 0 || !Saved.Remove(name)) return false;
        try { File.Delete(Path.Combine(Folder, name + ".blk")); } catch (Exception) { }
        if (AutoRunName != null && string.Equals(AutoRunName.Value, name, StringComparison.OrdinalIgnoreCase)) AutoRunName.Value = "";
        return true;
    }

    // One line per block: id|N|N2|Mode|Text (text escaped so it can't break the line format).
    private static string Serialize(List<Block> blocks) => string.Join("\n", blocks.Select(b =>
        string.Join("|", b.Kind, b.N.ToString(CultureInfo.InvariantCulture), b.N2.ToString(CultureInfo.InvariantCulture),
            b.Mode.ToString(CultureInfo.InvariantCulture), (b.Text ?? "").Replace("\\", "\\\\").Replace("|", "\\p").Replace("\n", "\\n").Replace("\r", ""))));

    // Lines that are not a known block (comments, garbage pasted from elsewhere) are skipped, not fatal.
    private static List<Block> Deserialize(string text)
    {
        var list = new List<Block>();
        foreach (string line in (text ?? "").Split('\n'))
        {
            string[] p = line.TrimEnd('\r').Split('|');
            if (p.Length < 5 || BlockDefs.Get(p[0]) == null) continue;
            if (!float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float n)) continue;
            if (!float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float n2)) continue;
            if (!int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int mode)) continue;
            list.Add(new Block
            {
                Kind = p[0], N = n, N2 = n2, Mode = mode,
                Text = string.Join("|", p.Skip(4)).Replace("\\n", "\n").Replace("\\p", "|").Replace("\\\\", "\\"),
            });
        }
        return list;
    }

    // Clipboard format: a header line (with the script's name) + the same lines as a .blk file.
    internal static string Export(List<Block> blocks, string name) => ExportHeader + " " + (name ?? "").Trim() + "\n" + Serialize(blocks);

    internal static List<Block> Import(string text, out string name)
    {
        name = null;
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Replace("\r\n", "\n").Trim();
        if (text.StartsWith(ExportHeader, StringComparison.Ordinal))
        {
            int nl = text.IndexOf('\n');
            string head = nl >= 0 ? text.Substring(0, nl) : text;
            name = head.Substring(ExportHeader.Length).Trim();
            if (name.Length == 0) name = null;
            text = nl >= 0 ? text.Substring(nl + 1) : "";
        }
        List<Block> list = Deserialize(text);
        return list.Count > 0 ? list : null;
    }

    // ---- editor draft ------------------------------------------------------------------------------------------

    private static string _draftSaved;
    private static float _nextDraftSave;
    private static bool _draftSaveWarned;

    private static void LoadDraft()
    {
        if (File.Exists(DraftFile))
        {
            string text = File.ReadAllText(DraftFile);
            string first = text.Split('\n')[0].TrimEnd('\r');
            if (first.StartsWith("#name=", StringComparison.Ordinal)) DraftName = first.Substring(6);
            Draft = Deserialize(text);
            _draftSaved = text;
            return;
        }
        Draft = Load(ExampleName) ?? new List<Block>();
        DraftName = ExampleName;
    }

    private static string DraftText() => "#name=" + (DraftName ?? "").Replace("\n", " ").Replace("\r", "") + "\n" + Serialize(Draft);

    private static void AutosaveDraft()
    {
        if (Time.unscaledTime < _nextDraftSave) return;
        _nextDraftSave = Time.unscaledTime + 2f;
        string text = DraftText();
        if (text == _draftSaved) return;
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(DraftFile, text);
            _draftSaved = text;
        }
        catch (Exception e)
        {
            if (!_draftSaveWarned) Plugin.Log.LogWarning("[Blocks] draft autosave: " + e.Message);
            _draftSaveWarned = true;
        }
    }

    // ---- mod settings by name ----------------------------------------------------------------------------------

    private static List<string> _featureNames;

    internal static List<string> FeatureNames
    {
        get
        {
            if (_featureNames != null) return _featureNames;
            _featureNames = typeof(CheatState).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(ConfigEntry<bool>))
                .Select(f => f.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            return _featureNames;
        }
    }

    private static List<string> _numberNames;

    // Every numeric setting of the mod (ConfigEntry<float> / ConfigEntry<int> on CheatState), by field name.
    internal static List<string> NumberNames
    {
        get
        {
            if (_numberNames != null) return _numberNames;
            _numberNames = typeof(CheatState).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(ConfigEntry<float>) || f.FieldType == typeof(ConfigEntry<int>))
                .Select(f => f.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            return _numberNames;
        }
    }

    // Exact (case-insensitive) match wins, otherwise the first name that contains the typed text.
    internal static string ResolveFeature(string text) => (text ?? "").Trim().Length == 0 ? null : ResolveName(FeatureNames, text);

    internal static string ResolveName(List<string> names, string text)
    {
        text = (text ?? "").Trim();
        if (names == null || names.Count == 0) return null;
        if (text.Length == 0) return names[0];
        string exact = names.FirstOrDefault(n => string.Equals(n, text, StringComparison.OrdinalIgnoreCase));
        return exact ?? names.FirstOrDefault(n => n.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    internal static string NextName(List<string> names, string text)
    {
        if (names == null || names.Count == 0) return text;
        string cur = ResolveName(names, text);
        int i = cur == null ? -1 : names.IndexOf(cur);
        return names[(i + 1) % names.Count];
    }

    internal static string NextFeature(string text) => NextName(FeatureNames, text);

    private static bool TrySetNumber(string name, float value)
    {
        FieldInfo f = typeof(CheatState).GetField(name, BindingFlags.Public | BindingFlags.Static);
        object entry = f?.GetValue(null);
        if (entry is ConfigEntry<float> cf) { cf.Value = value; return true; }
        if (entry is ConfigEntry<int> ci) { ci.Value = Mathf.RoundToInt(value); return true; }
        return false;
    }

    // Every setting a script changes is remembered with its value from before the run, so "Вернуть настройки
    // до скрипта" (the block, or the button in the tab - it also works after the script has stopped) can put
    // things back exactly. A new run starts a new snapshot.
    private static readonly Dictionary<string, (ConfigEntryBase Entry, object Value)> Snapshot = new();
    internal static int SnapshotCount => Snapshot.Count;

    private static void Remember(string field)
    {
        if (string.IsNullOrEmpty(field) || Snapshot.ContainsKey(field)) return;
        FieldInfo f = typeof(CheatState).GetField(field, BindingFlags.Public | BindingFlags.Static);
        if (f?.GetValue(null) is ConfigEntryBase e) Snapshot[field] = (e, e.BoxedValue);
    }

    internal static int RestoreSnapshot()
    {
        int changed = 0;
        foreach (var kv in Snapshot)
        {
            try
            {
                if (Equals(kv.Value.Entry.BoxedValue, kv.Value.Value)) continue;
                kv.Value.Entry.BoxedValue = kv.Value.Value;
                changed++;
            }
            catch (Exception e) { Plugin.Log.LogWarning("[Blocks] restore " + kv.Key + ": " + e.Message); }
        }
        Snapshot.Clear();
        return changed;
    }

    // ---- game state --------------------------------------------------------------------------------------------

    internal static readonly string[] SceneNames = { "главное меню", "лобби перед забегом", "уровень", "магазин", "грузовик (лобби)" };
    internal static readonly string[] MoneySources = { "общий счёт", "добыча раунда" };
    internal static readonly string[] PlayerPicks = { "случайного", "ближайшего", "самого дальнего" };

    internal static readonly string[] TeleportDestinations =
    {
        "к грузовику", "к точке выхода", "к самой дорогой ценности", "к случайной ценности",
        "к случайному игроку", "к выбранному игроку", "вперёд на N м", "вверх на N м",
    };

    // GameManager.Multiplayer() first: gameMode only becomes 1 once the game's own NetworkConnect has set Photon
    // up, so PhotonNetwork is never touched before that (touching it earlier can hang the game's connect flow).
    private static bool InRoom()
    {
        try { return GameManager.instance != null && GameManager.Multiplayer() && Photon.Pun.PhotonNetwork.InRoom; }
        catch (Exception) { return false; }
    }

    private static int PlayersInRoom()
    {
        try { return InRoom() && Photon.Pun.PhotonNetwork.CurrentRoom != null ? Photon.Pun.PhotonNetwork.CurrentRoom.PlayerCount : 0; }
        catch (Exception) { return 0; }
    }

    private static bool SceneIs(int mode)
    {
        try
        {
            if (RunManager.instance == null) return false;
            switch (mode)
            {
                case 0: return SemiFunc.IsMainMenu();
                case 1: return SemiFunc.RunIsLobbyMenu();
                case 2: return SemiFunc.RunIsLevel();
                case 3: return SemiFunc.RunIsShop();
                case 4: return SemiFunc.RunIsLobby();
            }
        }
        catch (Exception) { }
        return false;
    }

    // Works in singleplayer too: only the level itself has to be generated.
    private static bool LevelReady()
    {
        try
        {
            if (!SemiFunc.RunIsLevel()) return false;
            LevelGenerator gen = LevelGenerator.Instance;
            return gen != null && gen.Generated;
        }
        catch (Exception) { return false; }
    }

    private static float MoneyNow(int source)
    {
        try
        {
            if (source == 1) return RoundDirector.instance != null ? GameReflect.Get<int>(RoundDirector.instance, "currentHaul") : 0f;
            return SemiFunc.StatGetRunCurrency();
        }
        catch (Exception) { return 0f; }
    }

    private static float MyHp()
    {
        PlayerAvatar me = PlayerAvatar.instance;
        if (me == null) return 0f;
        if (GameReflect.Get<bool>(me, "deadSet")) return 0f;
        PlayerHealth ph = me.playerHealth;
        return ph != null ? GameReflect.Get<int>(ph, "health") : 0f;
    }

    private static bool IsDead(PlayerAvatar p) => p == null || GameReflect.Get<bool>(p, "deadSet");

    private static List<PlayerAvatar> OtherLivingPlayers()
    {
        var result = new List<PlayerAvatar>();
        GameDirector gd = GameDirector.instance;
        List<PlayerAvatar> list = gd != null ? GameReflect.Get<List<PlayerAvatar>>(gd, "PlayerList") : null;
        if (list == null) return result;
        foreach (PlayerAvatar p in list)
            if (p != null && p != PlayerAvatar.instance && !IsDead(p)) result.Add(p);
        return result;
    }

    // World position for a "Телепорт" destination, or null when there is no such target right now.
    private static Vector3? TeleportTarget(Block b)
    {
        PlayerController pc = PlayerController.instance;
        if (pc == null) return null;
        Vector3 me = pc.transform.position;
        switch (b.Mode)
        {
            case 0:
                return TruckScreenText.instance != null ? TruckScreenText.instance.transform.position + Vector3.up * 2f : (Vector3?)null;
            case 1:
            {
                ExtractionPoint ep = SemiFunc.ExtractionPointGetNearestNotActivated(me) ?? SemiFunc.ExtractionPointGetNearest(me);
                return ep != null ? ep.transform.position + Vector3.up * 2f : (Vector3?)null;
            }
            case 2:
            case 3:
            {
                ValuableObject best = null;
                float bestValue = -1f;
                var all = new List<ValuableObject>();
                foreach (ValuableObject v in UnityEngine.Object.FindObjectsByType<ValuableObject>(FindObjectsSortMode.None))
                {
                    if (v == null) continue;
                    float dollars = GameReflect.Get<float>(v, "dollarValueCurrent");
                    if (dollars <= 0f) continue;
                    all.Add(v);
                    if (dollars > bestValue) { bestValue = dollars; best = v; }
                }
                if (all.Count == 0) return null;
                if (b.Mode == 3) best = all[UnityEngine.Random.Range(0, all.Count)];
                return best.transform.position + Vector3.up * 1.5f;
            }
            case 4:
            {
                List<PlayerAvatar> others = OtherLivingPlayers();
                if (others.Count == 0) return null;
                PlayerAvatar t = others[UnityEngine.Random.Range(0, others.Count)];
                return t.transform.position + t.transform.forward * -1.5f + Vector3.up * 0.2f;
            }
            case 6:
            {
                Camera cam = Camera.main;
                Vector3 fwd = cam != null ? cam.transform.forward : pc.transform.forward;
                return me + fwd * Mathf.Max(0f, b.N);
            }
            case 7:
                return me + Vector3.up * Mathf.Max(0f, b.N);
        }
        return null;
    }

    // Follow (ghost) would snap us straight back to the followed player on the next tick.
    private static void StopFollowing()
    {
        if (!CheatState.GhostFollow.Value) return;
        Remember(nameof(CheatState.GhostFollow));
        CheatState.GhostFollow.Value = false;
    }

    // Same three steps as the game's own "Quit to menu" button (MenuPageEsc.ButtonEventQuitToMenu): the game then
    // runs its normal leave flow (outro, Photon disconnect, Steam lobby leave, main menu).
    private static bool RequestLeave()
    {
        NetworkManager nm = NetworkManager.instance;
        if (nm == null) return false;
        try
        {
            if (GameDirector.instance != null) GameDirector.instance.OutroStart();
            if (ResultScreenUI.instance != null) ResultScreenUI.instance.Disable();
            GameReflect.Set(nm, "leavePhotonRoom", true);
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[Blocks] leave: " + e.Message);
            return false;
        }
    }

    // ---- visited lobbies ---------------------------------------------------------------------------------------

    private static readonly HashSet<ulong> Visited = new();
    internal static int VisitedCount => Visited.Count;

    private static void LoadVisited()
    {
        if (!File.Exists(VisitedFile)) return;
        foreach (string line in File.ReadAllLines(VisitedFile))
            if (ulong.TryParse(line.Trim(), out ulong id) && id != 0) Visited.Add(id);
    }

    private static void MarkVisited(ulong id)
    {
        if (id == 0 || !Visited.Add(id)) return;
        try
        {
            Directory.CreateDirectory(Folder);
            File.AppendAllText(VisitedFile, id + "\n");
        }
        catch (Exception) { }
    }

    internal static void ForgetVisited()
    {
        Visited.Clear();
        try { File.Delete(VisitedFile); } catch (Exception) { }
    }

    private static ulong CurrentLobbyId()
    {
        if (SteamManager.instance == null) return 0;
        Lobby cur = GameReflect.Get<Lobby>(SteamManager.instance, "currentLobby");
        return (ulong)cur.Id;
    }

    private static float _nextObserve;

    // Any lobby the player is in counts as visited - also the ones joined by hand, not just by a script.
    private static void ObserveCurrentLobby()
    {
        if (Time.unscaledTime < _nextObserve) return;
        _nextObserve = Time.unscaledTime + 2f;
        if (!InRoom()) return;
        MarkVisited(CurrentLobbyId());
    }

    // ---- runner ------------------------------------------------------------------------------------------------

    private sealed class WaitFor
    {
        public float Until;
        public Func<bool> Cond;
    }

    private sealed class Node
    {
        public Block B;
        public List<Node> Children;
        public List<Node> Else;
    }

    internal static bool Running { get; private set; }
    internal static string RunningName { get; private set; } = "";
    // The block being executed in the top-level script (blocks of a called script do not move it), compared by
    // reference so the editor highlights the right row even after rows were moved.
    internal static Block CurrentBlock { get; private set; }

    private const int HistoryMax = 8;
    private static readonly List<string> History = new();
    private static string _status = "Не запущен.";

    internal static string Status
    {
        get => _status;
        private set
        {
            value ??= "";
            if (value == _status) return;
            _status = value;
            History.Add("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + value);
            if (History.Count > HistoryMax) History.RemoveAt(0);
        }
    }

    internal static string HistoryText() => History.Count == 0 ? "Журнал пуст." : string.Join("\n", History);

    private static IEnumerator _root;
    private static WaitFor _wait;
    private static bool _lastOk;
    private static bool _break;
    private static int _callDepth;
    private static float _startedAt;
    private static float _lastJoinTime = -100f;
    private static bool _autoFired;
    private static readonly Dictionary<string, float> Vars = new(StringComparer.OrdinalIgnoreCase);

    private static float Var(string name) => Vars.TryGetValue((name ?? "").Trim(), out float v) ? v : 0f;

    internal static string StatusLine()
    {
        if (!Running) return "■ " + _status;
        int s = Mathf.Max(0, Mathf.FloorToInt(Time.unscaledTime - _startedAt));
        return "▶ " + (s / 60).ToString("00") + ":" + (s % 60).ToString("00") + " · " + _status;
    }

    internal static void Start(List<Block> blocks, string name)
    {
        if (Running) Stop("перезапущен");
        _lastOk = false;
        _break = false;
        _wait = null;
        _callDepth = 0;
        Vars.Clear();
        Snapshot.Clear();
        RunningName = (name ?? "").Trim();
        _root = Exec(Parse(blocks));
        _startedAt = Time.unscaledTime;
        CurrentBlock = null;
        Running = true;
        Status = "Скрипт «" + RunningName + "» запущен";
        EventLog.Write("blocks", "start «" + RunningName + "»");
    }

    internal static void Stop(string why = "остановлен")
    {
        if (Running)
        {
            Status = "Скрипт «" + RunningName + "» " + why;
            EventLog.Write("blocks", "stop «" + RunningName + "»: " + why);
        }
        Running = false;
        _root = null;
        _wait = null;
        CurrentBlock = null;
    }

    // Keybind action: runs the editor's script, or stops whatever is running.
    internal static void ToggleDraftRun()
    {
        if (Running)
        {
            Stop("остановлен клавишей");
            RockNotify.Push("Скрипт", "остановлен", false, false);
            return;
        }
        Start(Draft, DraftName);
        RockNotify.Push("Скрипт", "«" + RunningName + "» запущен", true, false);
    }

    private static List<Node> Parse(List<Block> blocks)
    {
        var parents = new Stack<List<Node>>();
        var owners = new Stack<Node>();
        List<Node> cur = new();
        List<Node> top = cur;
        foreach (Block b in blocks)
        {
            if (b == null || BlockDefs.Get(b.Kind) == null) continue;
            if (b.Kind == BlockDefs.End)
            {
                if (parents.Count > 0) { cur = parents.Pop(); owners.Pop(); }
                continue;
            }
            if (b.Kind == BlockDefs.Else)
            {
                if (owners.Count > 0 && owners.Peek().Else == null && BlockDefs.IsIf(owners.Peek().B.Kind))
                {
                    owners.Peek().Else = new List<Node>();
                    cur = owners.Peek().Else;
                }
                continue;
            }
            var node = new Node { B = b };
            cur.Add(node);
            if (BlockDefs.IsContainer(b.Kind))
            {
                node.Children = new List<Node>();
                parents.Push(cur);
                owners.Push(node);
                cur = node.Children;
            }
        }
        return top;
    }

    private static WaitFor Seconds(float s) => new() { Until = Time.unscaledTime + Mathf.Max(0f, s) };
    private static WaitFor Until(Func<bool> cond) => new() { Cond = cond };

    private static void MarkCurrent(Node n)
    {
        if (_callDepth == 0) CurrentBlock = n.B;
    }

    // N2 >= 0.5 inverts the comparison ("не является" / "<").
    private static bool EvalCondition(Block b)
    {
        bool inv = b.N2 >= 0.5f;
        switch (b.Kind)
        {
            case BlockDefs.IfInRoom: return InRoom() != (b.Mode == 1);
            case BlockDefs.IfOk: return _lastOk != (b.Mode == 1);
            case BlockDefs.IfChance: return UnityEngine.Random.value * 100f < b.N;
            case BlockDefs.IfScene: return SceneIs(b.Mode) != inv;
            case BlockDefs.IfVar:
            {
                float v = Var(b.Text);
                Status = "Переменная «" + (b.Text ?? "").Trim() + "» = " + v.ToString("0.##");
                return (v >= b.N) != inv;
            }
            case BlockDefs.IfMoney:
            {
                float now = MoneyNow(b.Mode);
                Status = "Деньги (" + MoneySources[Mathf.Clamp(b.Mode, 0, MoneySources.Length - 1)] + "): " + now.ToString("0");
                return (now >= b.N) != inv;
            }
            case BlockDefs.IfHp:
            {
                float hp = MyHp();
                Status = "Моё HP: " + hp.ToString("0");
                return (hp >= b.N) != inv;
            }
        }
        return false;
    }

    // Each node yields WaitFor (wait) or null (next frame). After every node the list stops early when the script
    // was stopped (Stop block / button) or a "Прервать цикл" is travelling up to its loop.
    private static IEnumerator Exec(List<Node> nodes)
    {
        foreach (Node n in nodes)
        {
            if (!Running || _break) yield break;
            MarkCurrent(n);
            Block b = n.B;
            switch (b.Kind)
            {
                case BlockDefs.Wait:
                    Status = "Жду " + b.N.ToString("0.#") + " с";
                    yield return Seconds(b.N);
                    break;

                case BlockDefs.WaitRandom:
                {
                    float lo = Mathf.Min(b.N, b.N2), hi = Mathf.Max(b.N, b.N2);
                    float t = UnityEngine.Random.Range(lo, hi);
                    Status = "Жду " + t.ToString("0.#") + " с (случайно)";
                    yield return Seconds(t);
                    break;
                }

                case BlockDefs.SetFeature:
                {
                    string name = ResolveFeature(b.Text);
                    if (name == null) { Status = "Нет функции «" + b.Text + "» - пропускаю"; yield return null; break; }
                    bool now = CheatState.TryGetBool(name);
                    bool value = b.Mode == 0 || (b.Mode != 1 && !now);
                    Remember(name);
                    CheatState.TrySetBool(name, value);
                    Status = name + (value ? " включена" : " выключена");
                    yield return null;
                    break;
                }

                case BlockDefs.SetValue:
                {
                    string name = ResolveName(NumberNames, b.Text);
                    if (name != null) Remember(name);
                    bool ok = name != null && TrySetNumber(name, b.N);
                    _lastOk = ok;
                    Status = ok ? name + " = " + b.N.ToString("0.##") : "Нет числовой настройки «" + b.Text + "»";
                    yield return null;
                    break;
                }

                case BlockDefs.Restore:
                {
                    int changed = RestoreSnapshot();
                    Status = changed > 0 ? "Возвращено настроек: " + changed : "Скрипт пока ничего не менял";
                    yield return null;
                    break;
                }

                case BlockDefs.Notify:
                    Status = b.Text;
                    RockNotify.Push("Скрипт", b.Text ?? "", true, false);
                    yield return null;
                    break;

                case BlockDefs.Log:
                    Plugin.Log.LogInfo("[Blocks] " + b.Text);
                    Status = "Лог: " + b.Text;
                    yield return null;
                    break;

                case BlockDefs.SetVar:
                {
                    string key = (b.Text ?? "").Trim();
                    if (key.Length == 0) { Status = "У переменной нет имени"; yield return null; break; }
                    float cur = Var(key);
                    Vars[key] = b.Mode switch
                    {
                        0 => b.N,
                        1 => cur + b.N,
                        2 => cur - b.N,
                        _ => UnityEngine.Random.Range(0, Mathf.Max(0, Mathf.RoundToInt(b.N)) + 1),
                    };
                    Status = key + " = " + Vars[key].ToString("0.##");
                    yield return null;
                    break;
                }

                case BlockDefs.Leave:
                {
                    if (SceneIs(0)) { _lastOk = true; Status = "Уже в главном меню"; yield return null; break; }
                    if (!RequestLeave()) { _lastOk = false; Status = "Выйти не получилось (не в игре?)"; yield return null; break; }
                    Status = "Выхожу в главное меню";
                    float end = Time.unscaledTime + 60f;
                    yield return Seconds(1f);
                    yield return Until(() => SceneIs(0) || Time.unscaledTime > end);
                    _lastOk = SceneIs(0);
                    Status = _lastOk ? "В главном меню" : "Не вышел в меню за 60 с";
                    if (_lastOk) yield return Seconds(2f); // let the main menu settle before a next join
                    break;
                }

                case BlockDefs.ClearVisited:
                    ForgetVisited();
                    Status = "Список посещённых серверов очищен";
                    yield return null;
                    break;

                case BlockDefs.WaitInRoom:
                {
                    Status = "Жду, пока зайду в комнату";
                    float end = Time.unscaledTime + Mathf.Max(1f, b.N);
                    yield return Until(() => InRoom() || Time.unscaledTime > end);
                    _lastOk = InRoom();
                    Status = _lastOk ? "Я в комнате" : "Не зашёл в комнату за " + b.N.ToString("0") + " с";
                    break;
                }

                case BlockDefs.WaitLevel:
                {
                    Status = "Жду загрузки уровня";
                    float end = Time.unscaledTime + Mathf.Max(1f, b.N);
                    yield return Until(() => LevelReady() || Time.unscaledTime > end);
                    _lastOk = LevelReady();
                    Status = _lastOk ? "Уровень загружен" : "Уровень не загрузился за " + b.N.ToString("0") + " с";
                    break;
                }

                case BlockDefs.WaitPlayers:
                {
                    int need = Mathf.Max(1, Mathf.RoundToInt(b.N));
                    Status = "Жду игроков в комнате (нужно " + need + ")";
                    float end = Time.unscaledTime + Mathf.Max(1f, b.N2);
                    yield return Until(() => PlayersInRoom() >= need || Time.unscaledTime > end);
                    _lastOk = PlayersInRoom() >= need;
                    Status = _lastOk ? "Игроков: " + PlayersInRoom() : "Не дождался " + need + " игроков";
                    break;
                }

                case BlockDefs.WaitScene:
                {
                    int scene = Mathf.Clamp(b.Mode, 0, SceneNames.Length - 1);
                    Status = "Жду сцену: " + SceneNames[scene];
                    float end = Time.unscaledTime + Mathf.Max(1f, b.N);
                    yield return Until(() => SceneIs(scene) || Time.unscaledTime > end);
                    _lastOk = SceneIs(scene);
                    Status = _lastOk ? "Сцена: " + SceneNames[scene] : "Не дождался сцены «" + SceneNames[scene] + "»";
                    break;
                }

                case BlockDefs.WaitDead:
                {
                    Status = "Жду, пока умру";
                    float end = Time.unscaledTime + Mathf.Max(1f, b.N);
                    yield return Until(() => (PlayerAvatar.instance != null && MyHp() <= 0f) || Time.unscaledTime > end);
                    _lastOk = PlayerAvatar.instance != null && MyHp() <= 0f;
                    Status = _lastOk ? "Я умер" : "Не умер за " + b.N.ToString("0") + " с";
                    break;
                }

                case BlockDefs.PickPlayer:
                {
                    PlayerAvatar local = PlayerAvatar.instance;
                    List<PlayerAvatar> others = OtherLivingPlayers();
                    PlayerAvatar chosen = null;
                    if (others.Count > 0)
                    {
                        if (b.Mode == 0 || local == null) chosen = others[UnityEngine.Random.Range(0, others.Count)];
                        else
                        {
                            float bestD = b.Mode == 1 ? float.MaxValue : -1f;
                            foreach (PlayerAvatar p in others)
                            {
                                float d = Vector3.Distance(local.transform.position, p.transform.position);
                                if (b.Mode == 1 ? d < bestD : d > bestD) { bestD = d; chosen = p; }
                            }
                        }
                    }
                    _lastOk = chosen != null;
                    if (chosen != null)
                    {
                        Remember(nameof(CheatState.FakeSoundTargetName));
                        CheatState.FakeSoundTargetName.Value = PlayerTargets.NameOf(chosen);
                    }
                    Status = chosen != null ? "Выбран игрок: " + PlayerTargets.NameOf(chosen) : "Других живых игроков нет";
                    yield return null;
                    break;
                }

                case BlockDefs.Teleport:
                {
                    string label = TeleportDestinations[Mathf.Clamp(b.Mode, 0, TeleportDestinations.Length - 1)];
                    bool ok = false;
                    if (b.Mode == 5)
                    {
                        PlayerAvatar picked = PlayerTargets.Picked();
                        if (picked != null && picked != PlayerAvatar.instance && PlayerController.instance != null)
                        {
                            StopFollowing();
                            TeleportHack.TeleportToPlayer();
                            ok = true;
                        }
                    }
                    else
                    {
                        Vector3? dest = TeleportTarget(b);
                        if (dest.HasValue)
                        {
                            StopFollowing();
                            ok = TeleportHack.TeleportSelfTo(dest.Value, label);
                        }
                    }
                    _lastOk = ok;
                    Status = ok ? "Телепорт " + label : "Телепорт " + label + ": цели нет";
                    yield return null;
                    break;
                }

                case BlockDefs.SpawnItem:
                case BlockDefs.SpawnValuable:
                {
                    bool item = b.Kind == BlockDefs.SpawnItem;
                    List<string> names = null;
                    try { names = item ? ItemSpawnerHack.ScanItemNames() : EconomyHack.ScanValuableNames(); } catch (Exception) { }
                    string name = ResolveName(names, b.Text);
                    if (name == null)
                    {
                        _lastOk = false;
                        Status = "Нет такого " + (item ? "предмета" : "ценности") + " «" + b.Text + "» (список доступен только в игре)";
                        yield return null;
                        break;
                    }
                    int count = Mathf.Clamp(Mathf.RoundToInt(b.N), 1, 100);
                    float gap = Mathf.Max(0.05f, b.N2);
                    for (int k = 1; k <= count && Running; k++)
                    {
                        Status = "Спавн «" + name + "» " + k + "/" + count;
                        try { if (item) ItemSpawnerHack.SpawnSelected(name); else EconomyHack.SpawnValuable(name); }
                        catch (Exception e) { Plugin.Log.LogWarning("[Blocks] spawn: " + e.Message); }
                        yield return Seconds(gap);
                    }
                    _lastOk = true;
                    break;
                }

                case BlockDefs.JoinNew:
                {
                    IEnumerator join = JoinNewLobby(b);
                    while (join.MoveNext()) yield return join.Current;
                    break;
                }

                case BlockDefs.CallScript:
                {
                    string subName = (b.Text ?? "").Trim();
                    List<Block> sub = Load(subName);
                    if (sub == null) { _lastOk = false; Status = "Нет скрипта «" + subName + "»"; yield return null; break; }
                    if (_callDepth >= 6) { _lastOk = false; Status = "Слишком глубокая вложенность вызовов"; yield return null; break; }
                    _callDepth++;
                    Status = "Запускаю «" + subName + "»";
                    IEnumerator inner = Exec(Parse(sub));
                    Exception error = null;
                    while (true)
                    {
                        bool more;
                        try { more = inner.MoveNext(); }
                        catch (Exception e) { error = e; break; }
                        if (!more) break;
                        yield return inner.Current;
                    }
                    _callDepth--;
                    _break = false; // "Прервать цикл" outside any loop of the called script just returns from it
                    if (error != null) throw new Exception("в «" + subName + "»: " + error.Message, error);
                    MarkCurrent(n);
                    break;
                }

                case BlockDefs.Stop:
                    Stop("завершён блоком «Остановить»");
                    RockNotify.Push("Скрипт", "«" + RunningName + "» остановлен блоком", true, false);
                    yield break;

                case BlockDefs.Break:
                    _break = true;
                    Status = "Цикл прерван";
                    yield break;

                case BlockDefs.Repeat:
                {
                    int count = Mathf.Clamp(Mathf.RoundToInt(b.N), 0, 100000);
                    for (int i = 0; i < count; i++)
                    {
                        IEnumerator inner = Exec(n.Children);
                        while (inner.MoveNext()) yield return inner.Current;
                        if (!Running) yield break;
                        if (_break) { _break = false; break; }
                        MarkCurrent(n);
                        yield return null; // a loop body with no waits must not freeze the frame
                    }
                    break;
                }

                case BlockDefs.Forever:
                {
                    while (true)
                    {
                        IEnumerator inner = Exec(n.Children);
                        while (inner.MoveNext()) yield return inner.Current;
                        if (!Running) yield break;
                        if (_break) { _break = false; break; }
                        MarkCurrent(n);
                        yield return null;
                    }
                    break;
                }

                case BlockDefs.IfInRoom:
                case BlockDefs.IfOk:
                case BlockDefs.IfChance:
                case BlockDefs.IfScene:
                case BlockDefs.IfMoney:
                case BlockDefs.IfHp:
                case BlockDefs.IfVar:
                {
                    List<Node> branch = EvalCondition(b) ? n.Children : n.Else;
                    if (branch != null && branch.Count > 0)
                    {
                        IEnumerator inner = Exec(branch);
                        while (inner.MoveNext()) yield return inner.Current;
                    }
                    else yield return null;
                    break;
                }
            }
        }
    }

    private static IEnumerator JoinNewLobby(Block b)
    {
        _lastOk = false;

        // Joins are paced so a looping script can't hammer Steam/Photon.
        float wait = _lastJoinTime + 6f - Time.unscaledTime;
        if (wait > 0f) { Status = "Пауза между заходами " + wait.ToString("0") + " с"; yield return Seconds(wait); }

        if (SteamManager.instance == null || !SteamClient.IsValid) { Status = "Steam не готов"; yield return null; yield break; }

        Status = "Ищу открытые лобби…";
        Task<Lobby[]> task;
        try
        {
            LobbyQuery q = SteamMatchmaking.LobbyList.FilterDistanceWorldwide().WithMaxResults(200);
            string build = BuildManager.instance?.version?.title;
            if (!string.IsNullOrEmpty(build)) q = q.WithKeyValue("BuildName", build);
            task = q.RequestAsync();
        }
        catch (Exception e) { Status = "Ошибка поиска: " + e.Message; yield break; }

        float searchEnd = Time.unscaledTime + 20f;
        yield return Until(() => task.IsCompleted || Time.unscaledTime > searchEnd);
        if (!task.IsCompleted || task.IsFaulted || task.IsCanceled) { Status = "Steam не ответил со списком лобби"; yield break; }

        Lobby[] all = task.Result ?? Array.Empty<Lobby>();
        ulong me = (ulong)SteamClient.SteamId;
        ulong current = CurrentLobbyId();
        var fresh = new List<Lobby>();
        int seen = 0;
        foreach (Lobby l in all)
        {
            ulong id = (ulong)l.Id;
            if (id == 0 || id == current || (ulong)l.Owner.Id == me) continue;
            if (l.MemberCount >= l.MaxMembers) continue;
            if (l.GetData("HasPassword") == "1") continue;
            if (l.MemberCount < Mathf.RoundToInt(b.N2)) continue;
            if (Visited.Contains(id)) { seen++; continue; }
            fresh.Add(l);
        }
        if (fresh.Count == 0)
        {
            Status = "Новых лобби нет (открытых " + all.Length + ", уже бывал в " + seen + ")";
            yield break;
        }

        Lobby pick = fresh[UnityEngine.Random.Range(0, fresh.Count)];
        ulong pickId = (ulong)pick.Id;
        MarkVisited(pickId); // marked on the attempt: a dead lobby is not retried forever
        _lastJoinTime = Time.unscaledTime;
        Status = "Захожу в лобби «" + pick.GetData("RoomName") + "» (" + pick.MemberCount + "/" + pick.MaxMembers + ")";
        try { GameReflect.Invoke(SteamManager.instance, "JoinSteamLobby", pick); }
        catch (Exception e) { Status = "Игра не начала заход: " + e.Message; yield break; }

        // Success = the game is in a Photon room again AND it is the lobby we picked (not the one we came from).
        float end = Time.unscaledTime + Mathf.Max(5f, b.N);
        yield return Until(() => (InRoom() && CurrentLobbyId() == pickId) || Time.unscaledTime > end);
        _lastOk = InRoom() && CurrentLobbyId() == pickId;
        Status = _lastOk ? "Зашёл на новый сервер" : "Не удалось зайти за " + b.N.ToString("0") + " с";
    }

    private static void TryAutoRun()
    {
        if (_autoFired || Running || AutoRunName == null || string.IsNullOrEmpty(AutoRunName.Value)) return;
        if (Time.realtimeSinceStartup < 25f || !SceneIs(0)) return;
        _autoFired = true;
        List<Block> blocks = Load(AutoRunName.Value);
        if (blocks == null) return;
        Start(blocks, AutoRunName.Value);
        RockNotify.Push("Скрипт", "автозапуск «" + RunningName + "»", true, false);
    }

    private static void Finish()
    {
        Running = false;
        _root = null;
        _wait = null;
        CurrentBlock = null;
        Status = "Скрипт «" + RunningName + "» завершён";
        RockNotify.Push("Скрипт", "«" + RunningName + "» завершён", true, false);
        EventLog.Write("blocks", "finished «" + RunningName + "»");
    }

    // Called every frame from the UI tick.
    internal static void Tick()
    {
        try { ObserveCurrentLobby(); } catch (Exception) { }
        try { AutosaveDraft(); } catch (Exception) { }
        try { TryAutoRun(); } catch (Exception e) { Plugin.Log.LogWarning("[Blocks] autorun: " + e.Message); }

        if (!Running || _root == null) return;
        IEnumerator root = _root;
        try
        {
            for (int guard = 0; guard < 200; guard++)
            {
                if (_wait != null)
                {
                    bool done = _wait.Cond != null ? _wait.Cond() : Time.unscaledTime >= _wait.Until;
                    if (!done) return;
                    _wait = null;
                }
                bool more = root.MoveNext();
                if (!Running || !ReferenceEquals(root, _root)) return; // stopped (or restarted) from inside the script
                if (!more) { Finish(); return; }
                _wait = root.Current as WaitFor;
                if (_wait == null) return; // plain "yield return null" = continue next frame
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[Blocks] " + e);
            Running = false;
            _root = null;
            _wait = null;
            CurrentBlock = null;
            Status = "Ошибка: " + e.Message;
            RockNotify.Push("Скрипт", "ошибка: " + e.Message, false, false);
            EventLog.Write("blocks", "error «" + RunningName + "»: " + e.Message);
        }
    }
}
