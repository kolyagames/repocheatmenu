// ============================================================================
// RepoLauncher - лаунчер модпаков RepoCheatMenu
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// ============================================================================

using System.Text;

namespace RepoLauncher;

// Every screen is a function that returns its lines; Term.Present draws them. The layouts follow
// the three mock-ups (compact mode line, framed pack selector, full-screen configuration view).
internal static class Screens
{
    public const int Width = 74;

    private static readonly string[] BannerArt =
    {
        "   ___   _        _     ___    ___      ___   ___   ___    ___",
        "  / __| | |      /_\\   |   \\  | __|    | _ \\ | __| | _ \\  / _ \\",
        "  \\__ \\ | |__   / _ \\  | |) | | _|     |   / | _|  |  _/ | (_) |",
        "  |___/ |____| /_/ \\_\\ |___/  |___|    |_|_\\ |___| |_|    \\___/",
    };

    public static string Rule(char c = '=') => Term.Dim(new string(c, Width));

    public static List<string> Banner()
    {
        var lines = new List<string> { Rule() };
        foreach (string row in BannerArt) lines.Add(Term.Cyan(row));

        const string credit = "by Kolya";
        lines.Add(Term.Dim(new string(' ', Width - credit.Length) + credit));

        lines.Add(Rule());
        return lines;
    }

    // [MODE SELECT]: [1] DEFAULT | [2] OPTIMIZATION | >[3] REPO HD<
    public static string ModeLine(IReadOnlyList<Pack> packs, int active)
    {
        var sb = new StringBuilder(Term.Dim("[MODE SELECT]: "));
        for (int i = 0; i < packs.Count; i++)
        {
            if (i > 0) sb.Append(Term.Dim(" | "));
            string label = $"[{i + 1}] {packs[i].Name}";
            sb.Append(i == active ? Term.Orange(">" + label + "<") : Term.White(label));
        }
        return sb.ToString();
    }

    public static string StatusLine(string text) => Term.Dim("[STATUS]: ") + Term.White(text);

    public static string ProgressLine(double frac)
    {
        const int width = 30;
        int filled = (int)Math.Round(Math.Clamp(frac, 0, 1) * width);
        string bar = Term.Green(new string('█', filled)) + Term.Dim(new string('─', width - filled));
        return Term.Dim("[PROGRESS]: [") + bar + Term.Dim("] ") + Term.White($"{(int)(frac * 100)}%");
    }

    // [████████████████████████████▓▒░] 92%  - inner width 31, a soft ▓▒░ tail while unfinished.
    public static string BigBar(double frac)
    {
        const int inner = 31;
        frac = Math.Clamp(frac, 0, 1);
        int filled = (int)(frac * inner + 0.0001);
        string tail = "";
        if (frac < 1)
        {
            string soft = "▓▒░";
            tail = soft[..Math.Min(soft.Length, inner - filled)];
        }
        int rest = inner - filled - tail.Length;
        return Term.Dim("[") + Term.Green(new string('█', filled)) + Term.Green(tail)
            + Term.Dim(new string('─', Math.Max(0, rest))) + Term.Dim("] ") + Term.White($"{(int)(frac * 100)}%");
    }

    // --- framed box -----------------------------------------------------------------------------

    private static string BoxTop() => Term.Dim("┌" + new string('─', Width - 2) + "┐");
    private static string BoxMid() => Term.Dim("├" + new string('─', Width - 2) + "┤");
    private static string BoxBottom() => Term.Dim("└" + new string('─', Width - 2) + "┘");
    private static string BoxRow(string content) => Term.Dim("│") + Term.Pad(content, Width - 2) + Term.Dim("│");

    public static List<string> PackSelect(IReadOnlyList<Pack> packs, int cursor, int active, string footerNote)
    {
        var lines = new List<string>
        {
            BoxTop(),
            BoxRow(" " + Term.Orange("[ ВЫБОР ПРОФИЛЯ МОДПАКА ]")),
            BoxMid(),
        };
        for (int i = 0; i < packs.Count; i++)
        {
            string mark = i == active ? "[●]" : "[ ]";
            string name = Term.Fit(packs[i].Name, 16).PadRight(16);
            int descWidth = Width - 2 - (2 + 3 + 1 + 3 + 1 + 16 + 4);
            string text = $"  {mark} {i + 1:00}. {name} ── {Term.Fit(packs[i].Description ?? "", descWidth)}";
            string row = i == cursor
                ? Term.Bg(40, 55, 80) + Term.Fg(255, 215, 120) + Term.Bold + Term.Pad(text, Width - 2) + Term.Reset
                : Term.White(text);
            lines.Add(BoxRow(row));
        }
        lines.Add(BoxBottom());
        lines.Add("   " + Term.Dim("[") + Term.White("ENTER") + Term.Dim("] Применить  |  [") + Term.White("↑/↓") +
            Term.Dim("] Выбор модпака  |  [") + Term.White("ESC") + Term.Dim("] Назад"));
        if (!string.IsNullOrEmpty(footerNote)) lines.Add("   " + footerNote);
        return lines;
    }

    // --- main menu ---------------------------------------------------------------------------

    public static readonly (string Title, string Note)[] MenuItems =
    {
        ("ЗАПУСТИТЬ ИГРУ", "Применить модпак, запуск через Steam"),
        ("ВЫБОР МОДПАКА", "DEFAULT / OPTIMIZATION / REPO HD"),
        ("ПРОВЕРКА ФАЙЛОВ", "Сверка профиля с хранилищем (SHA-256)"),
        ("ОТКРЫТЬ ПАПКУ ПРОФИЛЯ", "Конфиги, логи и плагины модпака"),
        ("СМЕНИТЬ АККАУНТ", "Выйти и войти под другим логином"),
        ("ВЫХОД", "Закрыть лоадер"),
    };

    // Экран входа: логин и пароль (пароль точками), активное поле подсвечено.
    public static List<string> Login(string server, string login, string passwordMask, int field, string message)
    {
        var lines = Banner();
        lines.Add(" " + Term.Orange("[ ВХОД В АККАУНТ ]"));
        lines.Add("");
        lines.Add("  " + Term.Orange("▸ ") + Term.Dim("Сервер:  ") + Term.White(Term.Fit(server, 56)));
        lines.Add("");
        lines.Add("  " + Term.Dim("Логин:   ") + (field == 0 ? Term.Orange(login + "▌") : Term.White(login)));
        lines.Add("  " + Term.Dim("Пароль:  ") + (field == 1 ? Term.Orange(passwordMask + "▌") : Term.White(passwordMask)));
        lines.Add("");
        lines.Add(string.IsNullOrEmpty(message) ? "" : "  " + Term.Red("✖ " + Term.Fit(message, Width - 6)));
        lines.Add("");
        lines.Add("  " + Term.Dim("Нет аккаунта или ключа? Регистрация и активация ключа - на сайте:"));
        lines.Add("  " + Term.White(Term.Fit(server, Width - 4)));
        lines.Add("");
        lines.Add("   " + Term.Dim("[") + Term.White("ENTER") + Term.Dim("] Далее / Войти  |  [") + Term.White("TAB") +
            Term.Dim("] Другое поле  |  [") + Term.White("ESC") + Term.Dim("] Выход"));
        return lines;
    }

    public static List<string> MainMenu(IReadOnlyList<Pack> packs, int activePack, int cursor, string status,
        double progress, string gameDir, bool gameRunning, string profileDir, string account)
    {
        var lines = Banner();
        lines.Add(" " + ModeLine(packs, activePack));
        lines.Add(" " + StatusLine(status));
        lines.Add(" " + ProgressLine(progress));
        lines.Add("");
        lines.Add("  " + Term.Orange("▸ ") + Term.Dim("Игра:        ") +
            (gameDir == null ? Term.Red("не найдена (укажите --game <папка>)") : Term.White(Term.Fit(gameDir, 56))));
        lines.Add("  " + Term.Orange("▸ ") + Term.Dim("Состояние:   ") +
            (gameRunning ? Term.Orange("игра запущена - смена модпака недоступна") : Term.Green("игра закрыта")));
        lines.Add("  " + Term.Orange("▸ ") + Term.Dim("Профиль:     ") + Term.White(Term.Fit(profileDir, 56)));
        lines.Add("  " + Term.Orange("▸ ") + Term.Dim("Аккаунт:     ") + account);
        lines.Add("");

        lines.Add(BoxTop());
        lines.Add(BoxRow(" " + Term.Orange("[ ГЛАВНОЕ МЕНЮ ]")));
        lines.Add(BoxMid());
        for (int i = 0; i < MenuItems.Length; i++)
        {
            string title = MenuItems[i].Title.PadRight(22);
            string text = $"  {(i == cursor ? "▶" : " ")} {i + 1:00}. {title}── {Term.Fit(MenuItems[i].Note, 39)}";
            string row = i == cursor
                ? Term.Bg(40, 55, 80) + Term.Fg(255, 215, 120) + Term.Bold + Term.Pad(text, Width - 2) + Term.Reset
                : Term.White(text);
            lines.Add(BoxRow(row));
        }
        lines.Add(BoxBottom());
        lines.Add("   " + Term.Dim("[") + Term.White("ENTER") + Term.Dim("] Выбрать  |  [") + Term.White("↑/↓") +
            Term.Dim("] Навигация  |  [") + Term.White("1-" + packs.Count) + Term.Dim("] Быстрый выбор модпака  |  [") +
            Term.White("ESC") + Term.Dim("] Выход"));
        return lines;
    }

    // --- full-screen apply / configure ---------------------------------------------------------

    private static readonly string[] Spinner = { "◐", "◓", "◑", "◒" };

    public static List<string> Apply(ApplyState st, int frame)
    {
        var lines = Banner();
        lock (st.Lock)
        {
            lines.Add(" " + Term.Orange("[CONFIGURING MODPACKS]"));
            lines.Add("");
            lines.Add("  " + Term.Orange("▸ ") + Term.Dim("Активный пресет:   ") +
                Term.White($"[ {st.PackName} MODPACK v{st.PackVersion} ]"));

            string integrity = st.Integrity switch
            {
                Integrity.Ok => Term.Green("✔ Проверка целостности файлов пройдена"),
                Integrity.Failed => Term.Red(st.Error != null ? "✖ Ошибка" : $"✖ Не совпало файлов: {st.BadFiles}"),
                Integrity.Checking => Term.Orange("… Проверка целостности файлов"),
                _ => Term.Dim("… Ожидание проверки"),
            };
            lines.Add("  " + Term.Orange("▸ ") + Term.Dim("Статус защиты:    ") + integrity);
            lines.Add("");
            lines.Add("  " + BigBar(st.Overall));
            lines.Add("");

            foreach (Row r in st.Rows)
            {
                string icon = r.Status switch
                {
                    RowStatus.Done => Term.Green("[✔]"),
                    RowStatus.Failed => Term.Red("[✖]"),
                    RowStatus.Running => Term.Orange($"[{Spinner[frame % Spinner.Length]}]"),
                    _ => Term.Dim("[ ]"),
                };
                string title = Term.Fit(r.Title, 22).PadRight(22);
                lines.Add($"  {Term.Orange("▸")} {icon} {Term.White(title)} {Term.Dim($"({(int)r.Percent,3}%)")} {Term.Dim("— " + Term.Fit(r.Note ?? "", 30))}");
            }

            lines.Add("");
            if (st.Error != null) lines.Add("  " + Term.Red("✖ " + Term.Fit(st.Error, Width - 6)));
            else lines.Add("  " + Term.Dim(st.Message));
        }
        lines.Add(Rule());
        return lines;
    }

    // --- launch -----------------------------------------------------------------------------

    public static List<string> Launch(IReadOnlyList<Pack> packs, int activePack, string status, double progress,
        IEnumerable<string> details)
    {
        var lines = Banner();
        lines.Add(" " + ModeLine(packs, activePack));
        lines.Add(" " + StatusLine(status));
        lines.Add(" " + ProgressLine(progress));
        lines.Add("");
        foreach (string d in details) lines.Add("  " + d);
        lines.Add("");
        lines.Add("   " + Term.Dim("[") + Term.White("ESC") + Term.Dim("] Закрыть лоадер (игра продолжит запуск)"));
        return lines;
    }
}
