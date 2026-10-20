// ============================================================================
// RepoLauncher - лаунчер модпаков RepoCheatMenu
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// ============================================================================

using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace RepoLauncher;

// update.json lives next to the exe, same spirit as modpacks.json: a plain editable file, created
// with safe defaults on first run. "Repo" is the private GitHub repo that holds built releases
// (not source - see tools\publish-release.ps1 in the dev repo) - it is the same for everyone and
// is not a secret. "Token" is a per-install, read-only, repo-scoped GitHub token: each friend
// pastes their own once (entered interactively via the "U" menu item on first use), never typed
// into chat or shipped inside the exe itself. Owner publishes with a separate, write-scoped token
// kept only in their own environment (tools\publish-release.ps1), never in this file.
internal sealed class UpdateConfig
{
    public string Repo { get; set; } = "owner/repo-placeholder";
    public string Token { get; set; } = "";
    public bool AutoCheck { get; set; } = true;

    public static UpdateConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            var fresh = new UpdateConfig();
            fresh.Save(path);
            return fresh;
        }
        try { return JsonSerializer.Deserialize<UpdateConfig>(File.ReadAllText(path), Modpacks.Json) ?? new UpdateConfig(); }
        catch { return new UpdateConfig(); }
    }

    public void Save(string path)
    {
        try { File.WriteAllText(path, JsonSerializer.Serialize(this, Modpacks.Json)); }
        catch { /* not worth failing over */ }
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Token) && Repo.Contains('/') && !Repo.Contains("placeholder");
}

internal sealed class UpdateInfo
{
    public string Version;
    public string ZipAssetUrl;   // GitHub API asset url (not browser_download_url - that one needs a browser session for a private repo)
    public string Notes;
}

// Checks a private GitHub repo's latest Release for a newer build, downloads it, and merges it
// into the current install. "Merge" is the operative word: this NEVER wipes the install folder
// first - it only overwrites the exe and the packages this project's own build controls, and only
// patches modpacks.json's version fields in place. An earlier version of the Desktop-side build
// script did a wipe-then-copy and destroyed mods a user had manually added to packages\ - see
// CLAUDE.md, 2026-10-04. The same safety rule applies here for exactly the same reason: a friend
// may have dropped an extra mod into their own packages\ folder too.
internal static class UpdateChecker
{
    private static readonly string[] ManagedPackages = { "RepoCheatMenu", "nickklmao-MenuLib", "Vippy-REPOFidelity" };

    private static HttpClient NewClient(string token)
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RepoLauncher", "1.0"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.Timeout = TimeSpan.FromSeconds(15);
        return http;
    }

    // Never throws - a missing/misconfigured token, no internet, or the repo being unreachable
    // should just mean "no update available right now", not an error dialog on every launch.
    public static async Task<UpdateInfo> CheckLatestAsync(UpdateConfig cfg)
    {
        if (!cfg.IsConfigured) return null;
        try
        {
            using HttpClient http = NewClient(cfg.Token);
            using HttpResponseMessage resp = await http.GetAsync($"https://api.github.com/repos/{cfg.Repo}/releases/latest");
            if (!resp.IsSuccessStatusCode) return null;

            using JsonDocument doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            string tag = doc.RootElement.GetProperty("tag_name").GetString();
            string version = tag.TrimStart('v', 'V');
            string notes = doc.RootElement.TryGetProperty("body", out JsonElement b) ? b.GetString() : "";

            string assetUrl = null;
            if (doc.RootElement.TryGetProperty("assets", out JsonElement assets))
            {
                foreach (JsonElement asset in assets.EnumerateArray())
                {
                    string name = asset.GetProperty("name").GetString();
                    if (name != null && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        assetUrl = asset.GetProperty("url").GetString();
                        break;
                    }
                }
            }
            if (assetUrl == null) return null;

            return new UpdateInfo { Version = version, ZipAssetUrl = assetUrl, Notes = notes ?? "" };
        }
        catch { return null; }
    }

    // "1.0.41" vs "1.0.42", length-agnostic, numeric per segment.
    public static bool IsNewer(string local, string remote)
    {
        int[] a = ParseVersion(local), b = ParseVersion(remote);
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            int av = i < a.Length ? a[i] : 0, bv = i < b.Length ? b[i] : 0;
            if (bv != av) return bv > av;
        }
        return false;
    }

    private static int[] ParseVersion(string v) =>
        (v ?? "").Split('.').Select(p => int.TryParse(p, out int n) ? n : 0).ToArray();

    // Downloads the release zip, extracts it, merges it into exeDir, logs any failure to
    // update-log.txt next to the exe instead of crashing the launcher. Returns null on success,
    // or a short message to show the user on failure.
    public static async Task<string> DownloadAndApply(UpdateConfig cfg, UpdateInfo info, string exeDir)
    {
        string tempZip = Path.Combine(Path.GetTempPath(), $"RepoLauncher-update-{Guid.NewGuid():N}.zip");
        string tempDir = Path.Combine(Path.GetTempPath(), $"RepoLauncher-update-{Guid.NewGuid():N}");
        try
        {
            using HttpClient http = NewClient(cfg.Token);
            http.DefaultRequestHeaders.Accept.Clear();
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

            byte[] data = await http.GetByteArrayAsync(info.ZipAssetUrl);
            await File.WriteAllBytesAsync(tempZip, data);

            Directory.CreateDirectory(tempDir);
            ZipFile.ExtractToDirectory(tempZip, tempDir, overwriteFiles: true);

            MergeInto(tempDir, exeDir, info.Version);
            return null;
        }
        catch (Exception e)
        {
            Log(exeDir, $"ОБНОВЛЕНИЕ НЕ УДАЛОСЬ ({info.Version}): {e}");
            return "Не удалось установить обновление - подробности в update-log.txt";
        }
        finally
        {
            try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    // See the type's own comment: additive only, never a wholesale folder replace.
    private static void MergeInto(string src, string dest, string newVersion)
    {
        // The running exe can't be overwritten in place while it's executing, but Windows DOES
        // allow renaming it - the current process keeps running fine from the renamed file, and
        // the next launch picks up the new one. Same trick most self-updating console tools use.
        string exePath = Path.Combine(dest, "RepoLauncher.exe");
        string newExe = Path.Combine(src, "RepoLauncher.exe");
        if (File.Exists(newExe) && File.Exists(exePath))
        {
            string oldAside = exePath + ".old";
            try { if (File.Exists(oldAside)) File.Delete(oldAside); } catch { }
            File.Move(exePath, oldAside);
            File.Copy(newExe, exePath, overwrite: true);
            try { File.Delete(oldAside); } catch { /* fine if Windows still has it open */ }
        }

        string licenseSrc = Path.Combine(src, "LICENSE.txt");
        if (File.Exists(licenseSrc)) File.Copy(licenseSrc, Path.Combine(dest, "LICENSE.txt"), overwrite: true);

        string pkgDestRoot = Path.Combine(dest, "packages");
        Directory.CreateDirectory(pkgDestRoot);
        foreach (string pkg in ManagedPackages)
        {
            string pkgSrc = Path.Combine(src, "packages", pkg);
            if (!Directory.Exists(pkgSrc)) continue;
            string pkgDest = Path.Combine(pkgDestRoot, pkg);
            if (Directory.Exists(pkgDest)) Directory.Delete(pkgDest, recursive: true);   // this one folder is fully build-controlled
            CopyDirectory(pkgSrc, pkgDest);
        }

        // modpacks.json: patch only the version fields, same as the Desktop build script - a
        // friend's hand-added pack/package entries survive.
        string destJson = Path.Combine(dest, "modpacks.json");
        if (File.Exists(destJson))
        {
            string text = File.ReadAllText(destJson);
            text = System.Text.RegularExpressions.Regex.Replace(text, "\"version\":\\s*\"[^\"]*\"", $"\"version\": \"{newVersion}\"");
            File.WriteAllText(destJson, text);
        }
        else
        {
            string srcJson = Path.Combine(src, "modpacks.json");
            if (File.Exists(srcJson)) File.Copy(srcJson, destJson);
        }

        Log(dest, $"Обновлено до версии {newVersion}.");
    }

    private static void CopyDirectory(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (string file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
        foreach (string dir in Directory.GetDirectories(src))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }

    private static void Log(string exeDir, string text)
    {
        try
        {
            File.AppendAllText(Path.Combine(exeDir, "update-log.txt"),
                $"\n[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}\n");
        }
        catch { /* logging must never itself throw */ }
    }
}
