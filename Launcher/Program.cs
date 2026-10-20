// ============================================================================
// RepoLauncher - лаунчер модпаков RepoCheatMenu
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// ============================================================================

using System.Diagnostics;

namespace RepoLauncher;

// RepoLauncher: run once, pick a modpack, it builds that pack's BepInEx profile and starts the game
// through Steam with that profile. Interactive by default; the flags below exist for scripting and
// for testing without a real terminal:
//   --pack <id>        choose the pack (default: the one chosen last time)
//   --apply            build/verify the profile and exit (no game start)
//   --launch           build the profile and start the game, no menus
//   --dry-run          with --launch: print the Steam command instead of running it
//   --demo             print each screen once and exit
//   --plain            no colours / alternate screen
//   --game <dir> --root <dir> --packages <dir> --seed <dir>   override detected paths
internal static class Program
{
    private sealed class Ctx
    {
        public ModpackFile Packs;
        public int Active;
        public string GameDir;
        public string ExeDir;
        public string PackagesDir;
        public string ProfilesRoot;
        public string SettingsPath;
        public string SeedConfigDir;
        public Settings Settings;
        public string Status = "Готов к работе";
        public double Progress;
        public bool DryRun;
        public string Server;
        public string SessionPath;
        public Session Session;

        public string Account => Session == null
            ? Term.Red("не выполнен вход")
            : Term.White(Session.Login) + Term.Dim(" · лицензия ") +
              (Session.EndingSoon ? Term.Orange(Session.LicenseText()) : Term.Green(Session.LicenseText()));

        public Pack ActivePack => Packs.Packs[Active];
        public string ProfileDir => Installer.ProfileDir(ProfilesRoot, ActivePack);
    }

    private static int Main(string[] argv)
    {
        var args = new Args(argv);
        Input.Init();
        Term.Init(args.Has("--plain"));
        try
        {
            return Run(args);
        }
        catch (Exception e)
        {
            Term.Shutdown();
            Console.Error.WriteLine("Ошибка: " + e.Message);
            return 1;
        }
        finally
        {
            Term.Shutdown();
        }
    }

    private static int Run(Args args)
    {
        var c = new Ctx { ExeDir = AppContext.BaseDirectory, DryRun = args.Has("--dry-run") };
        c.Packs = Modpacks.Load(Path.Combine(c.ExeDir, "modpacks.json"));
        c.PackagesDir = args.Get("--packages") ?? Path.Combine(c.ExeDir, "packages");

        string dataRoot = args.Get("--root")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RepoLauncher");
        c.ProfilesRoot = Path.Combine(dataRoot, "profiles");
        c.SettingsPath = Path.Combine(dataRoot, "settings.json");
        c.Settings = Settings.Load(c.SettingsPath);

        c.GameDir = Game.FindGameDir(args.Get("--game"), c.Settings.GameDir);
        if (c.GameDir != null && c.Settings.GameDir != c.GameDir) { c.Settings.GameDir = c.GameDir; c.Settings.Save(c.SettingsPath); }
        c.SeedConfigDir = args.Get("--seed") ?? FindSeedConfig(c.GameDir);

        string wanted = args.Get("--pack") ?? c.Settings.ActivePack;
        int index = c.Packs.Packs.FindIndex(p => p.Id.Equals(wanted, StringComparison.OrdinalIgnoreCase));
        c.Active = index >= 0 ? index : 0;

        if (args.Has("--demo")) return Demo(c);
        if (args.Has("--apply")) return ApplyHeadless(c); // собирает профиль, игру не запускает - входа не требует

        c.Server = Auth.Server(c.ExeDir);
        c.SessionPath = Path.Combine(dataRoot, "session.json");
        if (!EnsureLogin(c)) return 1;

        if (args.Has("--launch")) return LaunchHeadless(c);
        return Interactive(c);
    }

    // --- вход в аккаунт -------------------------------------------------------------------------

    // True = можно продолжать. Сначала пробуем сохранённый вход (сервер заново проверяет лицензию),
    // потом спрашиваем логин и пароль. Пароль нигде не сохраняется.
    private static bool EnsureLogin(Ctx c)
    {
        Session saved = Auth.LoadSession(c.SessionPath, c.Server);
        string message = "";
        if (saved != null && saved.ExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        {
            AuthResult r = Auth.Verify(c.Server, saved);
            if (r.Ok) { c.Session = r.Session; Auth.SaveSession(c.SessionPath, c.Session); return true; }
            message = r.Message;
        }
        return LoginScreen(c, saved?.Login ?? "", message);
    }

    // RL_LOGIN / RL_PASSWORD задают данные без терминала (как RL_KEYS для клавиш) - для проверок.
    private static bool LoginScreen(Ctx c, string login, string message)
    {
        string envLogin = Environment.GetEnvironmentVariable("RL_LOGIN");
        string envPassword = Environment.GetEnvironmentVariable("RL_PASSWORD");
        if (!string.IsNullOrEmpty(envLogin) && envPassword != null)
        {
            AuthResult r = Auth.Login(c.Server, envLogin, envPassword);
            if (!r.Ok) { Console.Error.WriteLine("Вход не выполнен: " + r.Message); return false; }
            StoreSession(c, r.Session);
            return true;
        }

        if (Term.Plain) return LoginPlain(c);

        string password = "";
        int field = login.Length == 0 ? 0 : 1;
        while (true)
        {
            Term.Present(Screens.Login(c.Server, login, new string('•', password.Length), field, message));
            ConsoleKeyInfo? info = Input.ReadInfo(250);
            if (info == null) continue;
            ConsoleKeyInfo k = info.Value;

            switch (k.Key)
            {
                case ConsoleKey.Escape: return false;
                case ConsoleKey.Tab:
                case ConsoleKey.UpArrow:
                case ConsoleKey.DownArrow:
                    field ^= 1;
                    continue;
                case ConsoleKey.Backspace:
                    if (field == 0 && login.Length > 0) login = login[..^1];
                    else if (field == 1 && password.Length > 0) password = password[..^1];
                    continue;
                case ConsoleKey.Enter:
                    if (field == 0) { if (login.Length > 0) field = 1; continue; }
                    if (login.Length == 0) { field = 0; continue; }
                    message = "";
                    Term.Present(Screens.Login(c.Server, login, new string('•', password.Length), field, ""));
                    Console.Out.Write("\n  " + Term.Dim("Проверяю лицензию на сервере..."));
                    AuthResult r = Auth.Login(c.Server, login, password);
                    if (r.Ok) { StoreSession(c, r.Session); return true; }
                    message = r.Message;
                    password = "";
                    continue;
            }

            if (char.IsControl(k.KeyChar)) continue;
            if (field == 0 && login.Length < 24) login += k.KeyChar;
            else if (field == 1 && password.Length < 128) password += k.KeyChar;
        }
    }

    // Без терминала (вывод в файл): обычные строки, пароль виден - только для отладки.
    private static bool LoginPlain(Ctx c)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Console.Write("Логин: ");
            string login = Console.ReadLine();
            Console.Write("Пароль: ");
            string password = Console.ReadLine();
            if (login == null || password == null) return false;
            AuthResult r = Auth.Login(c.Server, login.Trim(), password);
            if (r.Ok) { StoreSession(c, r.Session); return true; }
            Console.WriteLine("Вход не выполнен: " + r.Message);
        }
        return false;
    }

    private static void StoreSession(Ctx c, Session s)
    {
        c.Session = s;
        Auth.SaveSession(c.SessionPath, s);
        c.Status = "Вход выполнен: " + s.Login;
    }

    // Перед запуском игры сервер заново смотрит лицензию: бан и отзыв ключа действуют сразу.
    private static bool CheckLicense(Ctx c)
    {
        if (c.Session == null) { c.Status = "Нужен вход - выберите «Сменить аккаунт»"; return false; }
        AuthResult r = Auth.Verify(c.Server, c.Session);
        if (r.Ok) { c.Session = r.Session; Auth.SaveSession(c.SessionPath, c.Session); return true; }

        c.Status = "Лицензия: " + r.Message;
        if (!r.Network) { c.Session = null; Auth.ClearSession(c.SessionPath); }
        return false;
    }

    // The settings you already have live in the r2modman profile you used until now (or in the game
    // folder); a new pack profile starts from a copy of them.
    private static string FindSeedConfig(string gameDir)
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string[] candidates =
        {
            Path.Combine(appData, "r2modmanPlus-local", "REPO", "profiles", "for cheat", "BepInEx", "config"),
            gameDir == null ? null : Path.Combine(gameDir, "BepInEx", "config"),
        };
        return candidates.FirstOrDefault(d => d != null && File.Exists(Path.Combine(d, "local.repocheatmenu.cfg")));
    }

    // --- interactive ----------------------------------------------------------------------------

    private static int Interactive(Ctx c)
    {
        int cursor = 0;
        while (true)
        {
            bool running = Game.IsRunning();
            Term.Present(Screens.MainMenu(c.Packs.Packs, c.Active, cursor, c.Status, c.Progress,
                c.GameDir, running, c.ProfileDir, c.Account));

            ConsoleKey? key = Input.Read(250);
            if (key == null) continue;

            int digit = DigitIndex(key.Value);
            if (digit >= 0 && digit < c.Packs.Packs.Count)
            {
                SetActive(c, digit);
                c.Status = $"Выбран модпак {c.ActivePack.Name} (применится при запуске)";
                continue;
            }

            switch (key.Value)
            {
                case ConsoleKey.UpArrow: cursor = (cursor + Screens.MenuItems.Length - 1) % Screens.MenuItems.Length; break;
                case ConsoleKey.DownArrow: cursor = (cursor + 1) % Screens.MenuItems.Length; break;
                case ConsoleKey.Escape: return 0;
                case ConsoleKey.Enter:
                    if (RunMenuItem(c, cursor)) return 0;
                    break;
            }
        }
    }

    // True = leave the launcher.
    private static bool RunMenuItem(Ctx c, int item)
    {
        switch (item)
        {
            case 0: // launch
                if (!CheckLicense(c)) return false;
                if (!CanApply(c)) return false;
                if (!ApplyScreen(c)) return false;
                return LaunchScreen(c);

            case 1:
                PackSelectScreen(c);
                return false;

            case 2: // verify
                if (!CanApply(c)) return false;
                ApplyScreen(c);
                return false;

            case 3:
                if (Directory.Exists(c.ProfileDir)) Process.Start(new ProcessStartInfo("explorer.exe", c.ProfileDir) { UseShellExecute = true });
                else c.Status = "Профиль ещё не создан - сначала примените модпак";
                return false;

            case 4: // сменить аккаунт
                Auth.ClearSession(c.SessionPath);
                c.Session = null;
                return !LoginScreen(c, "", ""); // отказ от входа = выход из лаунчера

            default:
                return true;
        }
    }

    private static bool CanApply(Ctx c)
    {
        if (c.GameDir == null) { c.Status = "Игра не найдена - запустите с --game <папка с REPO.exe>"; return false; }
        if (Game.IsRunning()) { c.Status = "Игра запущена - закройте её, потом применяйте модпак"; return false; }
        return true;
    }

    private static void PackSelectScreen(Ctx c)
    {
        int cursor = c.Active;
        string note = "";
        while (true)
        {
            Term.Present(Screens.PackSelect(c.Packs.Packs, cursor, c.Active, note));
            ConsoleKey? key = Input.Read(250);
            if (key == null) continue;

            int digit = DigitIndex(key.Value);
            if (digit >= 0 && digit < c.Packs.Packs.Count) { cursor = digit; continue; }

            switch (key.Value)
            {
                case ConsoleKey.UpArrow: cursor = (cursor + c.Packs.Packs.Count - 1) % c.Packs.Packs.Count; break;
                case ConsoleKey.DownArrow: cursor = (cursor + 1) % c.Packs.Packs.Count; break;
                case ConsoleKey.Escape: return;
                case ConsoleKey.Enter:
                    SetActive(c, cursor);
                    if (!CanApply(c)) { note = Term.Orange(c.Status); break; }
                    ApplyScreen(c);
                    return;
            }
        }
    }

    // Full-screen configuration view. The installer runs on its own thread; this redraws from the
    // shared state. Returns true when the profile is ready.
    private static bool ApplyScreen(Ctx c)
    {
        var st = new ApplyState();
        Task worker = Task.Run(() => Installer.Apply(c.ActivePack, c.Packs, c.ProfilesRoot, c.PackagesDir,
            c.GameDir, c.SeedConfigDir, st));

        int frame = 0;
        if (Term.Plain) worker.Wait();
        else
        {
            while (!worker.IsCompleted)
            {
                Term.Present(Screens.Apply(st, frame++));
                Thread.Sleep(60);
            }
        }
        Term.Present(Screens.Apply(st, frame));

        bool ok;
        lock (st.Lock) ok = st.Error == null && st.BadFiles == 0;
        if (ok)
        {
            c.Status = $"Модпак {c.ActivePack.Name} применён";
            c.Progress = 1;
            if (!Term.Plain) Thread.Sleep(Input.Scripted ? 0 : 900);
        }
        else
        {
            string error;
            lock (st.Lock) error = st.Error ?? $"не совпало файлов: {st.BadFiles}";
            c.Status = "Ошибка применения: " + error;
            c.Progress = 0;
            if (!Term.Plain)
            {
                Console.Out.Write("\n  Нажмите любую клавишу...");
                Input.Read(int.MaxValue / 2);
            }
        }
        return ok;
    }

    // Starts the game and waits for it (and the mod) to come up. True = leave the launcher.
    private static bool LaunchScreen(Ctx c)
    {
        string steam = Game.SteamExe();
        string[] launchArgs = Game.LaunchArguments(Path.GetFullPath(c.ProfileDir));
        if (steam == null) { c.Status = "Steam не найден"; return false; }

        var details = new List<string>
        {
            Term.Dim("Команда:  ") + Term.White(Term.Fit(Game.Describe(steam, launchArgs), Screens.Width - 14)),
        };
        void Show(string status, double progress)
        {
            c.Status = status;
            c.Progress = progress;
            Term.Present(Screens.Launch(c.Packs.Packs, c.Active, status, progress, details));
        }

        if (c.DryRun)
        {
            Show("Проверка запуска: игра НЕ запускается (--dry-run)", 1);
            Console.WriteLine(Game.Describe(steam, launchArgs)); // the screen shortens it; print it whole
            return true;
        }

        DateTime launchedAt = DateTime.Now;
        Show("Отправляю команду в Steam...", 0.05);
        Game.Start(steam, launchArgs);

        // Phase 1: the process appears. The bar is time-based (Steam gives no real progress) and
        // reaches 100 % only once the game really exists.
        var clock = Stopwatch.StartNew();
        while (!Game.IsRunning())
        {
            if (clock.Elapsed.TotalSeconds > 180) { Show("Игра не запустилась за 3 минуты - проверьте Steam", 0); return false; }
            Show("Steam запускает игру...", Math.Min(0.8, 0.1 + clock.Elapsed.TotalSeconds / 60.0 * 0.7));
            if (Input.Read(250) == ConsoleKey.Escape) return true;
        }

        // Phase 2: the mod reports in (hot-status.json is written by the mod's own loader).
        string status = Path.Combine(c.ProfileDir, "BepInEx", "plugins", "local-RepoCheatMenu", "hot-status.json");
        clock.Restart();
        while (clock.Elapsed.TotalSeconds < 120)
        {
            if (File.Exists(status) && File.GetLastWriteTime(status) >= launchedAt)
            {
                string text = File.ReadAllText(status);
                if (text.Contains("\"loaded\"") || text.Contains("\"reloaded\""))
                {
                    Show($"Игра запущена, мод загружен ({c.ActivePack.Name})", 1);
                    Thread.Sleep(Input.Scripted ? 0 : 2000);
                    return true;
                }
            }
            Show("Игра запущена, жду загрузки мода...", 0.85 + Math.Min(0.14, clock.Elapsed.TotalSeconds / 120.0 * 0.14));
            if (Input.Read(500) == ConsoleKey.Escape) return true;
        }
        Show("Игра запущена (мод не подтвердил загрузку - см. BepInEx\\LogOutput.log профиля)", 1);
        Thread.Sleep(Input.Scripted ? 0 : 3000);
        return true;
    }

    // --- headless / demo ------------------------------------------------------------------------

    private static int ApplyHeadless(Ctx c)
    {
        if (c.GameDir == null) { Console.Error.WriteLine("Игра не найдена, укажите --game"); return 1; }
        var st = new ApplyState();
        Installer.Apply(c.ActivePack, c.Packs, c.ProfilesRoot, c.PackagesDir, c.GameDir, c.SeedConfigDir, st);
        Term.Present(Screens.Apply(st, 0));
        SaveActive(c);
        lock (st.Lock) return st.Error == null && st.BadFiles == 0 ? 0 : 1;
    }

    private static int LaunchHeadless(Ctx c)
    {
        if (c.GameDir == null) { Console.Error.WriteLine("Игра не найдена, укажите --game"); return 1; }
        if (!c.DryRun && Game.IsRunning()) { Console.Error.WriteLine("Игра уже запущена"); return 1; }
        if (!CheckLicense(c)) { Console.Error.WriteLine(c.Status); return 1; }
        if (!ApplyScreen(c)) return 1;
        SaveActive(c);
        LaunchScreen(c);
        return 0;
    }

    private static int Demo(Ctx c)
    {
        Term.Present(Screens.MainMenu(c.Packs.Packs, c.Active, 0, c.Status, 0.67, c.GameDir, false, c.ProfileDir, Term.White("demo")));
        Console.WriteLine();
        Term.Present(Screens.PackSelect(c.Packs.Packs, c.Active, c.Active, ""));
        return 0;
    }

    // --- small helpers ------------------------------------------------------------------------

    private static void SetActive(Ctx c, int index)
    {
        c.Active = index;
        SaveActive(c);
    }

    private static void SaveActive(Ctx c)
    {
        c.Settings.ActivePack = c.ActivePack.Id;
        c.Settings.Save(c.SettingsPath);
    }

    private static int DigitIndex(ConsoleKey key) => key switch
    {
        >= ConsoleKey.D1 and <= ConsoleKey.D9 => key - ConsoleKey.D1,
        >= ConsoleKey.NumPad1 and <= ConsoleKey.NumPad9 => key - ConsoleKey.NumPad1,
        _ => -1,
    };

    private sealed class Args
    {
        private readonly string[] _a;
        public Args(string[] a) => _a = a;
        public bool Has(string name) => _a.Any(x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
        public string Get(string name)
        {
            for (int i = 0; i < _a.Length - 1; i++)
                if (_a[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return _a[i + 1];
            return null;
        }
    }
}
