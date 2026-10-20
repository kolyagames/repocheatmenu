// ============================================================================
// RepoLauncher - лаунчер модпаков RepoCheatMenu
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// ============================================================================

using System.Text;

namespace RepoLauncher;

internal readonly record struct Rgb(int R, int G, int B)
{
    public static Rgb Lerp(Rgb a, Rgb b, double t) => new(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
}

// Frame characters of a theme: ┌ ┐ └ ┘ - | ├ ┤
internal sealed record BoxStyle(char TL, char TR, char BL, char BR, char H, char V, char LT, char RT);

// A theme is a colour palette plus the drawing characters the screens use: frame style, progress
// bar glyphs, list bullet, spinner. Switching it (T key) restyles every screen.
internal sealed class Theme
{
    public string Name;
    public Rgb A, B;                  // banner gradient ends (also the accent colour = A)
    public Rgb Highlight, Text, Dim, Good, Bad, SelectBg, SelectFg;
    public BoxStyle Box;
    public char Rule;
    public char BarFull, BarEmpty;
    public string BarTail;            // soft end of an unfinished bar, e.g. "▓▒░"
    public string Bullet;
    public string Spinner;

    private static readonly BoxStyle Single = new('┌', '┐', '└', '┘', '─', '│', '├', '┤');
    private static readonly BoxStyle Rounded = new('╭', '╮', '╰', '╯', '─', '│', '├', '┤');
    private static readonly BoxStyle Double = new('╔', '╗', '╚', '╝', '═', '║', '╠', '╣');
    private static readonly BoxStyle Heavy = new('┏', '┓', '┗', '┛', '━', '┃', '┣', '┫');

    public static readonly Theme[] All =
    {
        new()
        {
            Name = "NEON", A = new(0, 229, 255), B = new(255, 60, 200),
            Highlight = new(255, 220, 90), Text = new(235, 240, 255), Dim = new(120, 125, 165),
            Good = new(80, 255, 170), Bad = new(255, 80, 110), SelectBg = new(45, 30, 85), SelectFg = new(255, 225, 120),
            Box = Rounded, Rule = '━', BarFull = '▰', BarEmpty = '▱', BarTail = "", Bullet = "❯", Spinner = "⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏",
        },
        new()
        {
            Name = "MATRIX", A = new(0, 255, 70), B = new(0, 110, 40),
            Highlight = new(200, 255, 200), Text = new(170, 255, 190), Dim = new(0, 120, 55),
            Good = new(0, 255, 100), Bad = new(255, 90, 60), SelectBg = new(0, 55, 22), SelectFg = new(210, 255, 210),
            Box = Single, Rule = '─', BarFull = '█', BarEmpty = '░', BarTail = "▓▒", Bullet = "▸", Spinner = "▖▘▝▗",
        },
        new()
        {
            Name = "SUNSET", A = new(255, 175, 0), B = new(255, 60, 120),
            Highlight = new(255, 232, 150), Text = new(255, 240, 230), Dim = new(175, 125, 115),
            Good = new(150, 235, 120), Bad = new(255, 70, 70), SelectBg = new(85, 32, 42), SelectFg = new(255, 228, 150),
            Box = Double, Rule = '═', BarFull = '█', BarEmpty = '─', BarTail = "▓▒░", Bullet = "◆", Spinner = "◐◓◑◒",
        },
        new()
        {
            Name = "ICE", A = new(255, 255, 255), B = new(70, 140, 255),
            Highlight = new(150, 220, 255), Text = new(230, 240, 255), Dim = new(110, 135, 170),
            Good = new(120, 255, 220), Bad = new(255, 120, 140), SelectBg = new(25, 48, 95), SelectFg = new(205, 238, 255),
            Box = Rounded, Rule = '─', BarFull = '▮', BarEmpty = '▯', BarTail = "", Bullet = "✦", Spinner = "◴◷◶◵",
        },
        new()
        {
            Name = "BLOOD", A = new(255, 45, 45), B = new(130, 0, 25),
            Highlight = new(255, 130, 90), Text = new(242, 222, 222), Dim = new(145, 85, 90),
            Good = new(130, 225, 130), Bad = new(255, 70, 70), SelectBg = new(75, 12, 18), SelectFg = new(255, 155, 115),
            Box = Heavy, Rule = '━', BarFull = '■', BarEmpty = '□', BarTail = "", Bullet = "▶", Spinner = "◢◣◤◥",
        },
        new()
        {
            Name = "MONO", A = new(255, 255, 255), B = new(125, 125, 125),
            Highlight = new(255, 255, 255), Text = new(210, 210, 210), Dim = new(115, 115, 115),
            Good = new(215, 215, 215), Bad = new(255, 255, 255), SelectBg = new(62, 62, 62), SelectFg = new(255, 255, 255),
            Box = Single, Rule = '-', BarFull = '#', BarEmpty = '-', BarTail = "", Bullet = ">", Spinner = "|/-\\",
        },
    };

    public static Theme Current { get; private set; } = All[0];

    public static void Set(string name)
    {
        Theme found = All.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (found != null) Current = found;
    }

    public static void Next() => Current = All[(Array.IndexOf(All, Current) + 1) % All.Length];

    // Colours `text` character by character along A -> B -> A. `phase` slides the gradient, which
    // is what makes the banner shimmer when it is redrawn a few times a second.
    public static string Gradient(string text, double phase)
    {
        if (Term.Plain) return text;
        var sb = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == ' ') { sb.Append(' '); continue; }
            double t = (double)i / Math.Max(1, text.Length - 1) + phase;
            t -= Math.Floor(t);
            double tri = t < 0.5 ? t * 2 : (1 - t) * 2;
            Rgb c = Rgb.Lerp(Current.A, Current.B, tri);
            sb.Append(Term.Fg(c)).Append(text[i]);
        }
        return sb.Append(Term.Reset).ToString();
    }
}
