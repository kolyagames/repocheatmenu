// ============================================================================
// RepoLauncher - лаунчер модпаков RepoCheatMenu
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// ============================================================================

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RepoLauncher;

// Вход в аккаунт и проверка лицензии на сервере сайта (repo-site: /api/client/login, /api/client/verify).
// Пароль нигде не хранится: после входа лаунчер держит только подписанный токен на сутки, и перед
// каждым запуском игры сервер заново проверяет лицензию, так что бан и отзыв ключа действуют сразу.

internal sealed class Session
{
    public string Server { get; set; }
    public string Login { get; set; }
    public string Token { get; set; }
    public long ExpiresAt { get; set; }   // unix ms, срок токена
    public bool Lifetime { get; set; }
    public long? Until { get; set; }      // unix ms, конец лицензии; null = без срока
    public string Plan { get; set; }

    public string LicenseText() => Lifetime
        ? "навсегда"
        : Until == null ? "—" : "до " + DateTimeOffset.FromUnixTimeMilliseconds(Until.Value).LocalDateTime.ToString("dd.MM.yyyy");

    // Меньше трёх дней до конца - повод предупредить.
    [JsonIgnore]
    public bool EndingSoon => !Lifetime && Until != null &&
        DateTimeOffset.FromUnixTimeMilliseconds(Until.Value) - DateTimeOffset.UtcNow < TimeSpan.FromDays(3);
}

internal sealed class AuthResult
{
    public bool Ok;
    public bool Network;      // сервер недоступен (а не отказал)
    public string Code;
    public string Message;
    public Session Session;
}

internal static class Auth
{
    // Адрес сервера лицензий. Перед выдачей сборки замените на свой домен (https!).
    // Для проверок: переменная REPO_SERVER или файл auth.json рядом с exe: { "server": "http://..." }.
    private const string DefaultServer = "http://127.0.0.1:3000";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    // Офлайн-аккаунт для разработки: логин dev, пароль 123. Не ходит на сервер и не проверяет лицензию -
    // сессия живёт локально, Verify её просто подтверждает. Это чёрный ход мимо лицензии, любой с такой
    // сборкой войдёт тем же паролем, поэтому в релизных сборках он выключен целиком: при обычном
    // `dotnet build` без явного `-c Debug` символ DEBUG не определён, весь блок ниже выпадает из IL,
    // DevAccountEnabled возвращает false и попытки войти через dev-аккаунт отвергаются как «отключён».
#if DEBUG
    private const bool DevAccountEnabled = true;
    private const string DevLogin = "dev", DevPassword = "123", DevToken = "offline-dev";

    private static Session DevSession(string server) => new()
    {
        Server = server,
        Login = DevLogin,
        Token = DevToken,
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(365).ToUnixTimeMilliseconds(),
        Lifetime = true,
        Plan = "dev (офлайн)",
    };
#else
    private const bool DevAccountEnabled = false;
    private const string DevLogin = "", DevPassword = "", DevToken = "";
    private static Session DevSession(string server) => null;
#endif

    public static string Server(string exeDir)
    {
        string s = Environment.GetEnvironmentVariable("REPO_SERVER");
        if (string.IsNullOrWhiteSpace(s))
        {
            try
            {
                string file = Path.Combine(exeDir, "auth.json");
                if (File.Exists(file))
                    s = JsonDocument.Parse(File.ReadAllText(file)).RootElement.GetProperty("server").GetString();
            }
            catch { /* битый auth.json - берём адрес по умолчанию */ }
        }
        return (string.IsNullOrWhiteSpace(s) ? DefaultServer : s).Trim().TrimEnd('/');
    }

    // --- запросы --------------------------------------------------------------------------------

    public static AuthResult Login(string server, string login, string password)
    {
        if (DevAccountEnabled && login == DevLogin && password == DevPassword)
            return new AuthResult { Ok = true, Session = DevSession(server) };

        string version = typeof(Auth).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        AuthResult r = Post(server, "/api/client/login",
            new { login, password, version = "launcher-" + version }, login);
        if (r.Ok) r.Session.Login = login;
        return r;
    }

    public static AuthResult Verify(string server, Session s)
    {
        if (s.Token == DevToken)
            return DevAccountEnabled
                ? new AuthResult { Ok = true, Session = DevSession(server) }
                : new AuthResult { Code = "revoked", Message = "Офлайн-аккаунт отключён в этой сборке" };

        AuthResult r = Post(server, "/api/client/verify", new { token = s.Token }, s.Login);
        if (r.Ok) { r.Session.Token = s.Token; r.Session.Login = s.Login; }
        return r;
    }

    private static AuthResult Post(string server, string path, object body, string login)
    {
        try
        {
            using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using HttpResponseMessage resp = Http.PostAsync(server + path, content).GetAwaiter().GetResult();
            string text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            JsonElement root = JsonDocument.Parse(text).RootElement;

            if (!resp.IsSuccessStatusCode)
            {
                return new AuthResult
                {
                    Code = Str(root, "error") ?? "error",
                    Message = Str(root, "message") ?? $"Сервер ответил {(int)resp.StatusCode}",
                };
            }

            var s = new Session { Server = server, Login = login, Token = Str(root, "token") };
            if (root.TryGetProperty("expires_at", out JsonElement exp) && exp.ValueKind == JsonValueKind.Number) s.ExpiresAt = exp.GetInt64();
            if (root.TryGetProperty("license", out JsonElement lic))
            {
                s.Lifetime = lic.TryGetProperty("lifetime", out JsonElement lf) && lf.ValueKind == JsonValueKind.True;
                if (lic.TryGetProperty("until", out JsonElement u) && u.ValueKind == JsonValueKind.Number) s.Until = u.GetInt64();
                s.Plan = Str(lic, "plan");
            }
            return new AuthResult { Ok = true, Session = s };
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new AuthResult { Network = true, Code = "network", Message = $"Нет связи с сервером лицензий ({server})" };
        }
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // --- сохранённый вход -----------------------------------------------------------------------

    public static Session LoadSession(string path, string server)
    {
        try
        {
            var s = JsonSerializer.Deserialize<Session>(File.ReadAllText(path), Modpacks.Json);
            return s != null && s.Server == server && !string.IsNullOrEmpty(s.Token) ? s : null;
        }
        catch { return null; }
    }

    public static void SaveSession(string path, Session s)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(s, Modpacks.Json));
        }
        catch { /* не сохранили - придётся войти снова, не критично */ }
    }

    public static void ClearSession(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
