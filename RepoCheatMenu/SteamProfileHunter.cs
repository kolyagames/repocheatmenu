// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Steamworks;
using Steamworks.Data;

namespace RepoCheatMenu;

// Paste a Steam profile URL, press "Найти" - the mod resolves it to a SteamId64, scans the public
// REPO lobby list through Facepunch.Steamworks (SteamMatchmaking.LobbyList) for one whose owner
// matches, and joins it automatically via SteamMatchmaking.JoinLobbyAsync. REPO itself hooks the
// subsequent OnLobbyEntered -> JoinRoom (SteamManager.OnLobbyEntered, confirmed in the decompiled
// source), so once we hand control back the game walks the normal "join a lobby" path end-to-end.
//
// Supported URL forms (vanity names cost one extra HTTP GET to the public steamcommunity.com
// ?xml=1 endpoint - no Web API key required, works for private profiles since the endpoint
// returns the id, not profile content):
//   https://steamcommunity.com/profiles/76561198012345678[/]
//   https://steamcommunity.com/id/myvanityname[/]
//   steamcommunity.com/profiles/76561198012345678 (bare, no scheme)
//   76561198012345678 (raw SteamId64)
//
// Scope of the lookup, stated up front in Status so the user knows what it can and cannot do:
//   * Public lobbies only (SetFriendsOnly / SetPrivate hide a lobby from LobbyList entirely).
//   * Owner-based match against the host of each lobby.
//   * Friends fast-path reads SteamFriends.GameInfo.Lobby, which covers friends who are guests too.
//   * Finding a non-friend GUEST would require entering each public lobby and reading its member
//     list - deliberately not implemented here.
internal static class SteamProfileHunter
{
    internal static BepInEx.Configuration.ConfigEntry<string> UrlInput;
    internal static string Status { get; private set; } = "Готово. Вставьте ссылку и нажмите «Найти».";
    internal static bool Busy { get; private set; }

    internal static string StatusLine() => Busy ? "  -> " + Status + " ⏳" : "  -> " + Status;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly Regex ProfilesRe = new(@"/profiles/(\d{17})\b", RegexOptions.IgnoreCase);
    private static readonly Regex VanityRe = new(@"/id/([^/\?\s#]+)", RegexOptions.IgnoreCase);
    private static readonly Regex BareIdRe = new(@"^\s*(\d{17})\s*$", RegexOptions.IgnoreCase);
    private static readonly Regex XmlIdRe = new(@"<steamID64>(\d{17})</steamID64>", RegexOptions.IgnoreCase);

    public static void StartSearch()
    {
        if (Busy) return;
        string url = UrlInput?.Value?.Trim() ?? "";
        if (url.Length == 0) { Status = "Пустая ссылка - вставьте URL из Steam."; return; }
        Busy = true;
        _ = SearchAsync(url);
    }

    private static async Task SearchAsync(string url)
    {
        try
        {
            Status = "Ищем SteamID…";
            ulong steamId = await ResolveSteamIdAsync(url);
            if (steamId == 0)
            {
                Status = "Не узнал SteamID из ссылки. Поддерживается /profiles/… и /id/… .";
                return;
            }
            Status = "SteamID: " + steamId + ". Ищем Steam-друга в игре…";

            try
            {
                Friend f = new(steamId);
                var info = f.GameInfo;
                if (info.HasValue && info.Value.Lobby.HasValue && (ulong)info.Value.Lobby.Value.Id != 0)
                {
                    Lobby friendLobby = info.Value.Lobby.Value;
                    Status = "Друг в лобби " + friendLobby.Id + " — заходим…";
                    bool ok = await TryJoinAsync(friendLobby);
                    Status = ok
                        ? "В лобби. Игра продолжит обычный переход."
                        : "Не удалось зайти в лобби друга — попробуем общий список.";
                    if (ok) return;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogInfo("[SteamHunter] friend check skipped: " + ex.Message);
            }

            Status = "Запрашиваем список лобби…";
            var query = SteamMatchmaking.LobbyList;
            if (BuildManager.instance != null && BuildManager.instance.version != null)
            {
                string buildName = BuildManager.instance.version.title;
                if (!string.IsNullOrEmpty(buildName)) query = query.WithKeyValue("BuildName", buildName);
            }
            Lobby[] lobbies = await query.RequestAsync() ?? Array.Empty<Lobby>();
            Status = "Лобби: " + lobbies.Length + ". Проверяем хостов…";

            Lobby? match = null;
            foreach (Lobby l in lobbies)
            {
                if ((ulong)l.Owner.Id == steamId) { match = l; break; }
            }
            if (match == null)
            {
                Status = "Не нашли этого игрока как хоста ни в одном открытом лобби. "
                       + "Если он гость, найдёт только когда он у вас в друзьях Steam.";
                return;
            }
            Status = "Нашли хоста. Входим в лобби " + match.Value.Id + "…";
            bool joined = await TryJoinAsync(match.Value);
            Status = joined
                ? "Заход в лобби выполнен — остальное делает игра."
                : "Steam отклонил вход в лобби (закрыто/заполнено/битая версия).";
        }
        catch (Exception ex)
        {
            Status = "Ошибка: " + ex.GetType().Name + " — " + ex.Message;
            Plugin.Log.LogWarning("[SteamHunter] " + ex);
        }
        finally
        {
            Busy = false;
        }
    }

    private static async Task<bool> TryJoinAsync(Lobby lobby)
    {
        Lobby? result = await SteamMatchmaking.JoinLobbyAsync(lobby.Id);
        if (!result.HasValue)
        {
            Plugin.Log.LogInfo("[SteamHunter] JoinLobbyAsync returned null (closed/full/version mismatch)");
            return false;
        }
        return true;
    }

    private static async Task<ulong> ResolveSteamIdAsync(string url)
    {
        Match m = BareIdRe.Match(url);
        if (m.Success) return ulong.Parse(m.Groups[1].Value);

        m = ProfilesRe.Match(url);
        if (m.Success) return ulong.Parse(m.Groups[1].Value);

        m = VanityRe.Match(url);
        if (m.Success)
        {
            string vanity = m.Groups[1].Value;
            string xmlUrl = "https://steamcommunity.com/id/" + Uri.EscapeDataString(vanity) + "/?xml=1";
            string body;
            try { body = await Http.GetStringAsync(xmlUrl); }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[SteamHunter] vanity fetch failed: " + ex.Message);
                return 0;
            }
            Match x = XmlIdRe.Match(body);
            if (x.Success) return ulong.Parse(x.Groups[1].Value);
        }
        return 0;
    }
}
