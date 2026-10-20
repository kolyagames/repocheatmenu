// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using BepInEx;
using UnityEngine;

namespace RepoCheatMenu;

// Synced song text on the main menu (and optionally in a run), the same idea as Kimstar's
// WorldLyrics: the line being sung is big, the one before and after it are dim, and the current
// line fills word by word as it is sung.
//
// Where the data comes from - nothing is bundled, every song is looked up when it plays:
//   1. <BepInEx config>/RepoCheatMenu/lyrics/<artist> - <title>.lrc  (your own files + the cache)
//   2. the free LRCLIB service (lrclib.net) when HudLyricsOnline is on; whatever it returns is
//      saved to the folder above, so each song is fetched once.
//
// The playback clock: the only thing the mod can read is Spotify's window title
// (Win32.GetSpotifyWindowTitle - "Artist - Title" while playing, "Spotify" while paused), which
// has no position. So the clock starts at 0 when the title changes, freezes while paused, and
// HudLyricsOffset shifts it when you started the mod (or the song) mid-way. Same trade-off as
// Kimstar's .np clock; a seek inside Spotify is not noticed.
internal static class LyricsHud
{
    private enum State { None, Loading, Ready, NotFound }

    private sealed class Line
    {
        public double Time;
        public string Text;
    }

    private static readonly object Gate = new();

    private static State _state = State.None;
    private static string _trackKey = "";
    private static List<Line> _lines = new();
    private static double _duration;
    private static Result _pending;

    private static double _elapsed;
    private static bool _playing;
    private static float _nextPoll;

    private static int _shownIndex = -2;
    private static float _lineChangedAt = -10f;
    private static string _prevText = "";

    private sealed class Result
    {
        public string Key;
        public List<Line> Lines;
        public double Duration;
    }

    // What the Dynamic Island shows: the Spotify track ("Artist - Title"), whether it is playing, and
    // the lyric line being sung (null when there is none).
    public static string CurrentTrack => _trackKey;
    public static bool IsPlaying => _playing;
    public static string CurrentLine =>
        _state == State.Ready && _shownIndex >= 0 && _shownIndex < _lines.Count ? _lines[_shownIndex].Text : null;

    // Detects the track and moves the lyric clock - once per frame however many screens ask
    // (the menu text and the island both do), so the clock never runs double speed.
    private static int _advancedFrame = -1;
    private static double _now;

    public static void Advance()
    {
        if (_advancedFrame == Time.frameCount) return;
        _advancedFrame = Time.frameCount;

        Poll();

        // The clock runs from the moment the track changed, not from when its lyrics arrived - the
        // lookup takes a second or two, and lines would otherwise come in that much late.
        if (_playing) _elapsed += Time.unscaledDeltaTime;
        if (_state != State.Ready || _lines.Count == 0) return;
        if (_duration > 0 && _elapsed > _duration + 2.0) _elapsed = 0; // same track on repeat

        _now = _elapsed + CheatState.HudLyricsOffset.Value;
        int index = IndexAt(_now);
        if (index != _shownIndex)
        {
            _prevText = _shownIndex >= 0 && _shownIndex < _lines.Count ? _lines[_shownIndex].Text : "";
            _shownIndex = index;
            _lineChangedAt = Time.unscaledTime;
        }
    }

    public static void Draw()
    {
        if (!CheatState.HudLyrics.Value) return;

        bool inMenu = SemiFunc.IsMainMenu() || SemiFunc.RunIsLobbyMenu();
        if (!inMenu && !CheatState.HudLyricsInGame.Value) return;

        Advance();
        if (_state != State.Ready || _lines.Count == 0) return;

        double now = _now;
        int index = _shownIndex;
        if (index < 0) return;

        float sw = CustomGui.ScreenWidth;
        float sh = CustomGui.ScreenHeight;
        if (sw <= 0f) return;

        // The menu list takes the left ~480 px; the text sits in the free area to its right and
        // slightly above the middle, so it stays clear of the logo and of the road at the bottom.
        bool overScreen = inMenu && RockMainScreen.Visible;
        float areaLeft = inMenu && !overScreen ? 520f : 0f;
        float cx = areaLeft + (sw - areaLeft) * 0.5f;
        float cy = overScreen ? sh * 0.21f : inMenu ? sh * 0.40f : sh * 0.72f;

        float t = Mathf.Clamp01((Time.unscaledTime - _lineChangedAt) / 0.35f);
        float ease = 1f - (1f - t) * (1f - t) * (1f - t);

        string current = _lines[index].Text;
        string next = index + 1 < _lines.Count ? _lines[index + 1].Text : "";

        // The line that just ended slides up and fades; the new one rises into place.
        if (_prevText.Length > 0 && t < 1f)
            DrawCentered(_prevText, 22, cx, cy - 46f - 18f * ease, 0.55f * (1f - ease), -1f);
        else if (index > 0)
            DrawCentered(_lines[index - 1].Text, 22, cx, cy - 46f, 0.38f, -1f);

        if (current.Length > 0)
        {
            float progress = LineProgress(index, now);
            DrawCentered(current, 38, cx, cy + 14f * (1f - ease), ease, progress);
        }
        DrawCentered(next, 22, cx, cy + 50f, 0.38f * ease, -1f);
    }

    // progress >= 0 lights the words sung so far (by character count - the file only has one time
    // per line); progress < 0 draws the whole line in one dim colour.
    private static void DrawCentered(string text, int size, float cx, float cy, float alpha, float progress)
    {
        if (string.IsNullOrEmpty(text) || alpha <= 0.01f) return;

        string[] words = text.Split(' ');
        float space = size * 0.3f;
        float[] widths = new float[words.Length];
        float total = 0f;
        for (int i = 0; i < words.Length; i++)
        {
            widths[i] = CustomGui.MeasureText(words[i], size);
            total += widths[i] + (i > 0 ? space : 0f);
        }

        Color lit = Color.Lerp(HudAccent(), Color.white, 0.35f);
        Color dim = new(1f, 1f, 1f, 1f);
        int chars = Math.Max(1, text.Length);
        int seen = 0;
        float x = cx - total * 0.5f;

        for (int i = 0; i < words.Length; i++)
        {
            bool isLit = progress >= 0f && (seen + words[i].Length * 0.5f) / chars <= progress;
            Color c = progress < 0f ? dim : isLit ? lit : dim;
            c.a = alpha * (progress < 0f || isLit ? 1f : 0.55f);

            Rect r = new(x, cy - size, widths[i] + 4f, size * 2f);
            // A dark edge keeps the text readable on the bright sky behind the menu.
            CustomGui.Text(new Rect(r.x + 1.5f, r.y + 2f, r.width, r.height), words[i], size, new Color(0f, 0f, 0f, alpha * 0.7f), TextAnchor.MiddleLeft);
            CustomGui.Text(r, words[i], size, c, TextAnchor.MiddleLeft);

            x += widths[i] + space;
            seen += words[i].Length + 1;
        }
    }

    private static Color HudAccent()
    {
        if (ColorUtility.TryParseHtmlString(CheatState.HudMediaPlayer.Color.Value, out Color c)) return c;
        return new Color(0.3f, 0.85f, 0.4f, 1f);
    }

    // --- Track detection + clock ---------------------------------------------------------------

    private static void Poll()
    {
        ApplyPending();

        if (Time.unscaledTime < _nextPoll) return;
        _nextPoll = Time.unscaledTime + 0.5f;

        string title = Win32.GetSpotifyWindowTitle();
        if (title == null || title.Length == 0 || title == "Spotify")
        {
            _playing = false; // not running or paused: freeze the clock, keep the loaded lines
            return;
        }

        _playing = true;
        if (title == _trackKey) return;

        // New track: the clock restarts and the lookup starts.
        _trackKey = title;
        _elapsed = 0;
        _shownIndex = -2;
        _prevText = "";
        _lines = new List<Line>();
        _duration = 0;

        SplitTitle(title, out string artist, out string song);
        _state = State.Loading;
        string key = title;
        ThreadPool.QueueUserWorkItem(_ => Lookup(key, artist, song));
    }

    private static void ApplyPending()
    {
        Result r;
        lock (Gate)
        {
            r = _pending;
            _pending = null;
        }
        if (r == null || r.Key != _trackKey) return; // the track changed while it was loading

        _lines = r.Lines;
        _duration = r.Duration;
        _state = _lines.Count > 0 ? State.Ready : State.NotFound;
    }

    // Spotify's window title is "Artist - Title". Song titles can contain " - " themselves, the
    // artist cannot be told apart from that, so the first separator wins.
    private static void SplitTitle(string title, out string artist, out string song)
    {
        int sep = title.IndexOf(" - ", StringComparison.Ordinal);
        if (sep <= 0) { artist = ""; song = title.Trim(); return; }
        artist = title.Substring(0, sep).Trim();
        song = title.Substring(sep + 3).Trim();
    }

    // --- Lookup (thread pool) ------------------------------------------------------------------

    private static string Folder() => Path.Combine(Paths.ConfigPath, "RepoCheatMenu", "lyrics");

    private static string FileFor(string artist, string song)
    {
        string name = artist.Length == 0 ? song : artist + " - " + song;
        foreach (char bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
        return Path.Combine(Folder(), name.Trim() + ".lrc");
    }

    private static void Lookup(string key, string artist, string song)
    {
        var result = new Result { Key = key, Lines = new List<Line>() };
        try
        {
            string file = FileFor(artist, song);
            string lrc = null;

            if (File.Exists(file))
            {
                lrc = File.ReadAllText(file, Encoding.UTF8);
            }
            else if (CheatState.HudLyricsOnline.Value)
            {
                string body = Get("https://lrclib.net/api/get?artist_name=" + Uri.EscapeDataString(artist)
                    + "&track_name=" + Uri.EscapeDataString(song));
                lrc = ExtractString(body, "syncedLyrics", out int lrcAt);
                if (lrc == null)
                {
                    body = Get("https://lrclib.net/api/search?q=" + Uri.EscapeDataString((artist + " " + song).Trim()));
                    lrc = ExtractString(body, "syncedLyrics", out lrcAt); // first entry that has synced text
                }
                if (lrc != null)
                {
                    // The duration must come from the entry the lyrics came from - in a search result the
                    // first "duration" in the body can belong to another version of the song, and a wrong
                    // one restarts the lyric clock in the middle of the track.
                    result.Duration = ExtractNumberBefore(body, "duration", lrcAt);
                    Directory.CreateDirectory(Folder());
                    File.WriteAllText(file, lrc, Encoding.UTF8);
                }
            }

            if (lrc != null) result.Lines = ParseLrc(lrc);
        }
        catch (Exception e)
        {
            Plugin.Log.LogInfo("Lyrics: lookup failed for \"" + key + "\": " + e.Message);
        }

        lock (Gate) _pending = result;
    }

    // Body of a 200 response, null on 404 or any failure.
    private static string Get(string url)
    {
        try
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "RepoCheatMenu lyrics";
            req.Timeout = 8000;
            using var res = (HttpWebResponse)req.GetResponse();
            using var reader = new StreamReader(res.GetResponseStream(), Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (WebException)
        {
            return null;
        }
    }

    // Pulls the first non-null string value of "key" out of a JSON body without a JSON library
    // (the response is one object or an array of objects; entries with null are skipped).
    private static string ExtractString(string json, string key, out int at)
    {
        at = -1;
        if (json == null) return null;
        string needle = "\"" + key + "\"";
        int from = 0;
        while (true)
        {
            int k = json.IndexOf(needle, from, StringComparison.Ordinal);
            if (k < 0) return null;
            int i = k + needle.Length;
            while (i < json.Length && (json[i] == ' ' || json[i] == ':')) i++;
            if (i < json.Length && json[i] == '"') { at = k; return ReadJsonString(json, i + 1); }
            from = i;
        }
    }

    // The last "key" number before `before` (LRCLIB writes duration ahead of the lyrics of the same object).
    private static double ExtractNumberBefore(string json, string key, int before)
    {
        if (json == null || before < 0) return 0;
        int k = json.LastIndexOf("\"" + key + "\"", before, StringComparison.Ordinal);
        if (k < 0) return 0;
        int i = k + key.Length + 2;
        while (i < json.Length && (json[i] == ' ' || json[i] == ':')) i++;
        int start = i;
        while (i < json.Length && (char.IsDigit(json[i]) || json[i] == '.')) i++;
        return double.TryParse(json.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0;
    }

    private static string ReadJsonString(string s, int i)
    {
        var sb = new StringBuilder();
        for (; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"') break;
            if (c != '\\') { sb.Append(c); continue; }
            if (++i >= s.Length) break;
            switch (s[i])
            {
                case 'n': sb.Append('\n'); break;
                case 'r': break;
                case 't': sb.Append('\t'); break;
                case 'u':
                    if (i + 4 < s.Length && int.TryParse(s.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                    {
                        sb.Append((char)code);
                        i += 4;
                    }
                    break;
                default: sb.Append(s[i]); break;
            }
        }
        return sb.ToString();
    }

    // --- LRC -----------------------------------------------------------------------------------

    // "[mm:ss.xx] words", several tags per line allowed, [offset:+/-ms] honoured, other tags skipped.
    private static List<Line> ParseLrc(string text)
    {
        var lines = new List<Line>();
        double offset = 0;
        foreach (string raw in text.Split('\n'))
        {
            string row = raw.Trim();
            if (row.StartsWith("[offset:", StringComparison.OrdinalIgnoreCase))
            {
                int close = row.IndexOf(']');
                if (close > 8 && int.TryParse(row.Substring(8, close - 8).Trim(), out int ms)) offset = ms / 1000.0;
                continue;
            }

            var times = new List<double>();
            int p = 0;
            while (p < row.Length && row[p] == '[')
            {
                int close = row.IndexOf(']', p);
                if (close < 0) break;
                if (!TryParseStamp(row.Substring(p + 1, close - p - 1), out double seconds)) break;
                times.Add(seconds);
                p = close + 1;
            }
            if (times.Count == 0) continue;

            string words = row.Substring(p).Trim();
            foreach (double t in times) lines.Add(new Line { Time = Math.Max(0, t - offset), Text = words });
        }
        lines.Sort((a, b) => a.Time.CompareTo(b.Time));
        return lines;
    }

    private static bool TryParseStamp(string s, out double seconds)
    {
        seconds = 0;
        int colon = s.IndexOf(':');
        if (colon <= 0) return false;
        if (!int.TryParse(s.Substring(0, colon), out int minutes)) return false;
        if (!double.TryParse(s.Substring(colon + 1).Replace(':', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double sec)) return false;
        seconds = minutes * 60 + sec;
        return true;
    }

    // Index of the line being sung at `seconds`, -1 before the first one.
    private static int IndexAt(double seconds)
    {
        int lo = 0, hi = _lines.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (_lines[mid].Time <= seconds) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return found;
    }

    // 0..1 through line `index` (until the next one starts, or 4 s for the last).
    private static float LineProgress(int index, double seconds)
    {
        double start = _lines[index].Time;
        double next = index + 1 < _lines.Count ? _lines[index + 1].Time : start + 4;
        return (float)Math.Max(0, Math.Min(1, (seconds - start) / Math.Max(0.05, next - start)));
    }
}
