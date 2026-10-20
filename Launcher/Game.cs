// ============================================================================
// RepoLauncher - лаунчер модпаков RepoCheatMenu
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// ============================================================================

using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace RepoLauncher;

// Finds the Steam copy of R.E.P.O. and starts it with a chosen BepInEx profile.
internal static class Game
{
    public const string SteamAppId = "3241660";

    public static string FindGameDir(string overrideDir, string remembered)
    {
        foreach (string candidate in new[] { overrideDir, remembered })
            if (IsGameDir(candidate)) return candidate;

        foreach (string library in SteamLibraries())
        {
            string dir = Path.Combine(library, "steamapps", "common", "REPO");
            if (IsGameDir(dir)) return dir;
        }
        return null;
    }

    private static bool IsGameDir(string dir) =>
        !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "REPO.exe"));

    private static IEnumerable<string> SteamLibraries()
    {
        string steam = SteamPath();
        if (steam == null) yield break;
        yield return steam;

        string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;
        foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
            yield return m.Groups[1].Value.Replace("\\\\", "\\");
    }

    public static string SteamPath()
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            string path = key?.GetValue("SteamPath") as string;
            return string.IsNullOrEmpty(path) ? null : path.Replace('/', '\\');
        }
        catch { return null; }
    }

    public static string SteamExe()
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            string exe = (key?.GetValue("SteamExe") as string)?.Replace('/', '\\');
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe)) return exe;
        }
        catch { /* fall through */ }
        string steam = SteamPath();
        string fallback = steam == null ? null : Path.Combine(steam, "steam.exe");
        return fallback != null && File.Exists(fallback) ? fallback : null;
    }

    public static bool IsRunning() => Process.GetProcessesByName("REPO").Length > 0;

    // The same thing r2modman does: tell the running (or starting) Steam to launch the game, and
    // hand Doorstop - the thing that injects BepInEx - the preloader of THIS profile instead of
    // the one in the game folder. Nothing in the game folder or in other profiles is touched.
    public static string[] LaunchArguments(string profileDir) => new[]
    {
        "-applaunch", SteamAppId,
        "--doorstop-enabled", "true",
        "--doorstop-target-assembly", Path.Combine(profileDir, "BepInEx", "core", "BepInEx.Preloader.dll"),
    };

    public static string Describe(string steamExe, string[] args) =>
        $"\"{steamExe}\" " + string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));

    public static void Start(string steamExe, string[] args)
    {
        var psi = new ProcessStartInfo(steamExe) { UseShellExecute = false };
        foreach (string a in args) psi.ArgumentList.Add(a);
        Process.Start(psi);
    }
}
