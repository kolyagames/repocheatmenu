// ============================================================================
// RepoLauncher - лаунчер модпаков RepoCheatMenu
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// ============================================================================

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RepoLauncher;

internal enum RowStatus { Pending, Running, Done, Failed }
internal enum Integrity { Pending, Checking, Ok, Failed }

internal sealed class Row
{
    public string Title;
    public string Note;
    public double Percent;
    public RowStatus Status;
}

// Shared between the installer thread (writes) and the screen (reads) - every access takes Lock.
internal sealed class ApplyState
{
    public readonly object Lock = new();
    public string PackName = "";
    public string PackVersion = "";
    public Integrity Integrity = Integrity.Pending;
    public int BadFiles;
    public double Overall;
    public string Message = "";
    public bool Done;
    public string Error;
    public readonly List<Row> Rows = new();
}

// Builds and updates one pack's profile: <profiles>/<pack>/BepInEx/{core,plugins,config}.
//   * core is copied from the game's own BepInEx folder;
//   * every package of the pack is copied to plugins/<target>, files compared by SHA-256;
//   * files an earlier apply installed and this pack no longer wants are removed - ONLY those
//     (launcher-state.json lists them); anything else in the profile is never touched;
//   * config values from modpacks.json are written into the profile's .cfg files;
//   * finally every installed file is hashed against the store ("integrity check").
internal static class Installer
{
    private sealed class FileJob
    {
        public string Source;
        public string Dest;
        public string Rel;
        public long Size;
        public Row Row;
    }

    private sealed class StateFile
    {
        public List<string> Files { get; set; } = new();
    }

    public static string ProfileDir(string profilesRoot, Pack pack) => Path.Combine(profilesRoot, pack.Id);

    public static void Apply(Pack pack, ModpackFile all, string profilesRoot, string packagesDir,
        string gameDir, string seedConfigDir, ApplyState st)
    {
        try
        {
            Run(pack, all, profilesRoot, packagesDir, gameDir, seedConfigDir, st);
        }
        catch (Exception e)
        {
            lock (st.Lock) { st.Error = e.Message; st.Integrity = Integrity.Failed; }
        }
        finally
        {
            lock (st.Lock) st.Done = true;
        }
    }

    private static void Run(Pack pack, ModpackFile all, string profilesRoot, string packagesDir,
        string gameDir, string seedConfigDir, ApplyState st)
    {
        string profile = Path.GetFullPath(ProfileDir(profilesRoot, pack));
        string bepinex = Path.Combine(profile, "BepInEx");
        string statePath = Path.Combine(profile, "launcher-state.json");
        bool firstTime = !File.Exists(statePath);

        var packages = new List<Package>();
        foreach (string id in pack.Packages)
        {
            Package pkg = all.Packages.FirstOrDefault(p => p.Id == id)
                ?? throw new InvalidDataException($"modpacks.json: пакет '{id}' не описан");
            packages.Add(pkg);
        }

        lock (st.Lock)
        {
            st.PackName = pack.Name;
            st.PackVersion = pack.Version;
            st.Rows.Add(new Row { Title = "BepInEx core", Note = "Загрузчик модов из папки игры" });
            foreach (Package p in packages) st.Rows.Add(new Row { Title = p.Title, Note = p.Note });
            st.Message = "Подготовка профиля";
        }

        // Build the whole job list first so the total is known and a missing package fails before
        // anything is written.
        var jobs = new List<FileJob>();
        string coreSrc = Path.Combine(gameDir, "BepInEx", "core");
        if (!Directory.Exists(coreSrc)) throw new DirectoryNotFoundException("В папке игры нет BepInEx\\core");
        AddDirectory(jobs, coreSrc, Path.Combine(bepinex, "core"), profile, st.Rows[0]);

        for (int i = 0; i < packages.Count; i++)
        {
            string src = Path.Combine(packagesDir, packages[i].Dir);
            if (!Directory.Exists(src))
                throw new DirectoryNotFoundException($"Пакет '{packages[i].Title}' не найден в хранилище: {src}");
            AddDirectory(jobs, src, Path.Combine(bepinex, "plugins", packages[i].Target), profile, st.Rows[i + 1]);
        }

        Directory.CreateDirectory(Path.Combine(bepinex, "plugins"));
        Directory.CreateDirectory(Path.Combine(bepinex, "config"));
        Directory.CreateDirectory(Path.Combine(bepinex, "patchers"));

        if (firstTime) SeedFromExisting(seedConfigDir, bepinex, st);

        RemoveStale(statePath, jobs, profile);

        // --- copy (0 .. 85 %) ---
        long total = Math.Max(1, jobs.Sum(j => j.Size));
        long done = 0;
        lock (st.Lock) foreach (Row r in st.Rows) r.Status = RowStatus.Running;
        var rowDone = new Dictionary<Row, long>();
        var rowTotal = jobs.GroupBy(j => j.Row).ToDictionary(g => g.Key, g => Math.Max(1, g.Sum(j => j.Size)));

        lock (st.Lock) st.Message = "Копирование файлов";
        foreach (FileJob job in jobs)
        {
            if (!SameFile(job.Source, job.Dest)) CopyAtomic(job.Source, job.Dest);
            done += job.Size;
            rowDone[job.Row] = rowDone.GetValueOrDefault(job.Row) + job.Size;
            lock (st.Lock)
            {
                job.Row.Percent = 100.0 * rowDone[job.Row] / rowTotal[job.Row];
                st.Overall = 0.85 * done / total;
            }
        }

        // --- config values ---
        lock (st.Lock) st.Message = "Настройки модпака";
        foreach (ConfigEdit edit in pack.Config)
            IniFile.Set(Path.Combine(bepinex, "config", edit.File), edit.Section, edit.Key, edit.Value);

        // --- integrity (85 .. 100 %) ---
        lock (st.Lock) { st.Integrity = Integrity.Checking; st.Message = "Проверка целостности файлов"; }
        int bad = 0;
        long verified = 0;
        foreach (FileJob job in jobs)
        {
            if (!SameFile(job.Source, job.Dest))
            {
                bad++;
                lock (st.Lock) job.Row.Status = RowStatus.Failed;
            }
            verified += job.Size;
            lock (st.Lock) st.Overall = 0.85 + 0.15 * verified / total;
        }

        File.WriteAllText(statePath, JsonSerializer.Serialize(
            new StateFile { Files = jobs.Select(j => j.Rel).ToList() }, Modpacks.Json));

        lock (st.Lock)
        {
            st.BadFiles = bad;
            st.Integrity = bad == 0 ? Integrity.Ok : Integrity.Failed;
            foreach (Row r in st.Rows)
                if (r.Status != RowStatus.Failed) { r.Status = RowStatus.Done; r.Percent = 100; }
            st.Overall = 1;
            st.Message = bad == 0 ? "Готово" : $"Не совпало файлов: {bad}";
        }
    }

    // --- helpers ---------------------------------------------------------------------------

    private static void AddDirectory(List<FileJob> jobs, string srcDir, string destDir, string profile, Row row)
    {
        foreach (string file in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories))
        {
            string relInside = Path.GetRelativePath(srcDir, file);
            string dest = Path.Combine(destDir, relInside);
            jobs.Add(new FileJob
            {
                Source = file,
                Dest = dest,
                Rel = Path.GetRelativePath(profile, dest),
                Size = new FileInfo(file).Length,
                Row = row,
            });
        }
    }

    // First time a profile is created it inherits the settings you already have, so switching to
    // the launcher does not reset keybinds and toggles: the mod's .cfg and your script folder.
    private static void SeedFromExisting(string seedConfigDir, string bepinex, ApplyState st)
    {
        if (string.IsNullOrEmpty(seedConfigDir) || !Directory.Exists(seedConfigDir)) return;
        lock (st.Lock) st.Message = "Перенос ваших настроек";

        string cfg = Path.Combine(seedConfigDir, "local.repocheatmenu.cfg");
        if (File.Exists(cfg)) File.Copy(cfg, Path.Combine(bepinex, "config", "local.repocheatmenu.cfg"), true);

        string scripts = Path.GetFullPath(Path.Combine(seedConfigDir, "..", "plugins", "RepoCheatMenu_Scripts"));
        if (Directory.Exists(scripts))
        {
            string dest = Path.Combine(bepinex, "plugins", "RepoCheatMenu_Scripts");
            Directory.CreateDirectory(dest);
            foreach (string f in Directory.EnumerateFiles(scripts, "*.csx"))
                File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), true);
        }
    }

    // Deletes only files an earlier apply wrote that the new pack does not include.
    private static void RemoveStale(string statePath, List<FileJob> jobs, string profile)
    {
        if (!File.Exists(statePath)) return;
        StateFile old;
        try { old = JsonSerializer.Deserialize<StateFile>(File.ReadAllText(statePath), Modpacks.Json); }
        catch { return; }
        if (old?.Files == null) return;

        var keep = new HashSet<string>(jobs.Select(j => j.Rel), StringComparer.OrdinalIgnoreCase);
        foreach (string rel in old.Files.Where(r => !keep.Contains(r)))
        {
            string full = Path.GetFullPath(Path.Combine(profile, rel));
            if (!full.StartsWith(profile, StringComparison.OrdinalIgnoreCase)) continue; // never leave the profile
            try
            {
                if (File.Exists(full)) File.Delete(full);
                string dir = Path.GetDirectoryName(full);
                while (dir != null && dir.Length > profile.Length && !Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch { /* locked or already gone - the next apply retries */ }
        }
    }

    // Written under a temp name and renamed, so a file is never half-copied under its real name.
    private static void CopyAtomic(string src, string dest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        string tmp = dest + ".part";
        File.Copy(src, tmp, true);
        File.Move(tmp, dest, true);
    }

    private static bool SameFile(string a, string b)
    {
        if (!File.Exists(b)) return false;
        if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
        return Hash(a).SequenceEqual(Hash(b));
    }

    private static byte[] Hash(string path)
    {
        using FileStream fs = File.OpenRead(path);
        return SHA256.HashData(fs);
    }
}

// Minimal editor for BepInEx .cfg files: sets "Key = Value" inside [Section], creating the file,
// section or key if missing. Comments and every other line are left exactly as they are.
internal static class IniFile
{
    public static void Set(string path, string section, string key, string value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var lines = File.Exists(path) ? File.ReadAllLines(path, Encoding.UTF8).ToList() : new List<string>();

        int start = lines.FindIndex(l => l.Trim().Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase));
        if (start < 0)
        {
            if (lines.Count > 0 && lines[^1].Trim().Length > 0) lines.Add("");
            lines.Add("[" + section + "]");
            lines.Add("");
            lines.Add($"{key} = {value}");
        }
        else
        {
            int end = lines.FindIndex(start + 1, l => l.TrimStart().StartsWith('['));
            if (end < 0) end = lines.Count;

            int at = -1;
            for (int i = start + 1; i < end; i++)
            {
                string t = lines[i].TrimStart();
                if (t.StartsWith('#')) continue;
                int eq = t.IndexOf('=');
                if (eq > 0 && t[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) { at = i; break; }
            }
            if (at >= 0) lines[at] = $"{key} = {value}";
            else
            {
                int insert = end;
                while (insert > start + 1 && lines[insert - 1].Trim().Length == 0) insert--;
                lines.Insert(insert, $"{key} = {value}");
            }
        }
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }
}
