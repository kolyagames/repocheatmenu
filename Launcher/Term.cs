// ============================================================================
// RepoLauncher - лаунчер модпаков RepoCheatMenu
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// ============================================================================

using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace RepoLauncher;

// Console plumbing: ANSI colours and an alternate screen buffer when the terminal supports them
// (Windows Terminal and Windows 10+ conhost do), plain text otherwise - also when output is
// redirected to a file, which is how the launcher is tested.
internal static class Term
{
    public static bool Plain;

    private const int StdOutputHandle = -11;
    private const uint EnableVirtualTerminalProcessing = 0x0004;

    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int handle);
    [DllImport("kernel32.dll")] private static extern bool GetConsoleMode(IntPtr handle, out uint mode);
    [DllImport("kernel32.dll")] private static extern bool SetConsoleMode(IntPtr handle, uint mode);

    private static readonly Regex Ansi = new("\u001b\\[[0-9;?]*[A-Za-z]", RegexOptions.Compiled);

    public static void Init(bool forcePlain)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        Plain = forcePlain || Console.IsOutputRedirected;
        if (!Plain)
        {
            IntPtr h = GetStdHandle(StdOutputHandle);
            bool vt = GetConsoleMode(h, out uint mode) && SetConsoleMode(h, mode | EnableVirtualTerminalProcessing);
            Plain = !vt;
        }
        if (!Plain) Console.Out.Write("\u001b[?1049h\u001b[?25l\u001b[2J");
    }

    public static void Shutdown()
    {
        if (!Plain) Console.Out.Write("\u001b[?25h\u001b[?1049l");
    }

    // --- colours -----------------------------------------------------------------------------

    public static string Fg(Rgb c) => Plain ? "" : $"\u001b[38;2;{c.R};{c.G};{c.B}m";
    public static string Bg(Rgb c) => Plain ? "" : $"\u001b[48;2;{c.R};{c.G};{c.B}m";
    public static string Reset => Plain ? "" : "\u001b[0m";
    public static string Bold => Plain ? "" : "\u001b[1m";

    // Semantic colours - what they look like depends on the current theme (Theme.cs).
    public static string Accent(string s) => Fg(Theme.Current.A) + s + Reset;
    public static string Hi(string s) => Fg(Theme.Current.Highlight) + Bold + s + Reset;
    public static string Good(string s) => Fg(Theme.Current.Good) + s + Reset;
    public static string Bad(string s) => Fg(Theme.Current.Bad) + s + Reset;
    public static string Dim(string s) => Fg(Theme.Current.Dim) + s + Reset;
    public static string Text(string s) => Fg(Theme.Current.Text) + s + Reset;

    // Old names/overloads the not-yet-themed screens (Screens.cs, Program.cs) still call. They map
    // onto the theme palette so the launcher already follows it; remove once Screens.cs is moved
    // to the semantic names above (that rewrite was paused).
    public static string Fg(int r, int g, int b) => Fg(new Rgb(r, g, b));
    public static string Bg(int r, int g, int b) => Bg(new Rgb(r, g, b));
    public static string Cyan(string s) => Accent(s);
    public static string Orange(string s) => Hi(s);
    public static string Green(string s) => Good(s);
    public static string Red(string s) => Bad(s);
    public static string White(string s) => Text(s);

    // A highlighted menu row: the text padded to `width` on the theme's selection background.
    public static string Select(string s, int width) =>
        Bg(Theme.Current.SelectBg) + Fg(Theme.Current.SelectFg) + Bold + Pad(s, width) + Reset;

    // --- drawing -----------------------------------------------------------------------------

    public static string Strip(string s) => Ansi.Replace(s, "");
    public static int Visible(string s) => Strip(s).Length;
    public static string Pad(string s, int width) => s + new string(' ', Math.Max(0, width - Visible(s)));

    // Shortens plain text to width with an ellipsis (no escape codes inside).
    public static string Fit(string s, int width) =>
        s.Length <= width ? s : width <= 1 ? s[..Math.Max(0, width)] : s[..(width - 1)] + "…";

    // Redraws the whole screen in place: cursor home, every line cleared to its end, then the rest
    // of the screen cleared. No flicker, no scrolling. Plain mode just prints the lines.
    public static void Present(IReadOnlyList<string> lines)
    {
        var sb = new StringBuilder();
        if (Plain)
        {
            foreach (string l in lines) sb.Append(Strip(l)).Append('\n');
        }
        else
        {
            sb.Append("\u001b[H");
            foreach (string l in lines) sb.Append(l).Append("\u001b[K\n");
            sb.Append("\u001b[J");
        }
        Console.Out.Write(sb.ToString());
        Console.Out.Flush();
    }
}

// Keyboard. RL_KEYS="Down,Enter,Escape" feeds scripted keys instead of reading the console, so the
// interactive screens can be driven (and tested) without a real terminal.
internal static class Input
{
    private static Queue<ConsoleKey> _script;

    public static bool Scripted => _script != null;

    public static void Init()
    {
        string s = Environment.GetEnvironmentVariable("RL_KEYS");
        if (string.IsNullOrWhiteSpace(s)) return;
        _script = new Queue<ConsoleKey>();
        foreach (string part in s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (Enum.TryParse(part, true, out ConsoleKey key)) _script.Enqueue(key);
    }

    // Like Read, but keeps the typed character (for text fields). Scripted keys carry no character.
    public static ConsoleKeyInfo? ReadInfo(int timeoutMs)
    {
        if (_script != null)
        {
            ConsoleKey? k = Read(timeoutMs);
            return k == null ? null : new ConsoleKeyInfo('\0', k.Value, false, false, false);
        }

        int waited = 0;
        while (waited < timeoutMs)
        {
            if (!Console.IsInputRedirected && Console.KeyAvailable) return Console.ReadKey(true);
            Thread.Sleep(15);
            waited += 15;
        }
        return null;
    }

    // Next key, or null if none arrives within the timeout. A scripted run that has used up its
    // keys keeps answering Escape, so every loop unwinds.
    public static ConsoleKey? Read(int timeoutMs)
    {
        if (_script != null)
        {
            Thread.Sleep(Math.Min(timeoutMs, 20));
            return _script.Count > 0 ? _script.Dequeue() : ConsoleKey.Escape;
        }

        int waited = 0;
        while (waited < timeoutMs)
        {
            if (!Console.IsInputRedirected && Console.KeyAvailable) return Console.ReadKey(true).Key;
            Thread.Sleep(15);
            waited += 15;
        }
        return null;
    }
}
