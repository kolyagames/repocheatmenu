// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// Часть дизайна, чисел и ресурсов здесь перенесена из клиента Rockstar;
// эти части принадлежат их авторам.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;

namespace RepoCheatMenu;

// Dynamic Island at the top of the screen, modelled on Rockstar's HUD island (its decompiled
// ScriptInternal112/124 were read for the numbers; this is our own implementation on CustomGui):
//   * pill 48x15 "GUI units" at rest, radius 7, 7 units from the top edge; size and radius follow
//     a spring, an expanded card adds 11 to the radius;
//   * fill black at 85 % over a blurred copy of the frame, 1-unit outline at 10 % white;
//   * a 7.5-radius bubble buds off to the right with a liquid neck (music: equalizer; extraction:
//     points done), pushing the pill 9.5 units left while it is out;
//   * in a run: clock on the left, ping bars on the right (plane icon in single player).
// One GUI unit = HudIslandScale canvas pixels (2 = Rockstar at GUI scale 2 on 1080p).
//
// Content, highest priority first: a short notification (feature toggled) for 2.5 s, then the
// nearest active extraction (direction arrow, distance, haul), then the Spotify track / current
// lyric line, otherwise the empty pill.
//
// Blur: REPO renders the game into RenderTextureMain.renderTexture (the pixelated look). That
// texture is shrunk a few times with Graphics.Blit (bilinear, all on the GPU, no read-back, so no
// stalls), shown in a full-screen RawImage, and a uGUI Mask with exactly the island's shape cuts it
// out under the pill. No texture (a scene without it) = no blur, the pill is just drawn opaque.
internal static class DynamicIsland
{
    private enum Kind { Idle, Music, Extraction, Notification, Target }

    // --- springs ----------------------------------------------------------------------------

    private sealed class Spring
    {
        public float Value, Velocity, Target;
        private readonly float _stiffness, _damping;
        public Spring(float value, float stiffness = 260f, float damping = 24f)
        {
            Value = Target = value;
            _stiffness = stiffness;
            _damping = damping;
        }
        public void Step(float dt)
        {
            // Semi-implicit Euler, slightly under-damped: the small overshoot is the "jelly" feel.
            Velocity += ((Target - Value) * _stiffness - Velocity * _damping) * dt;
            Value += Velocity * dt;
        }
    }

    private static readonly Spring W = new(48f), H = new(15f), R = new(7f);
    private static readonly Spring Bubble = new(0f, 180f, 22f);
    private static readonly Spring Expand = new(0f, 300f, 28f);
    private static readonly Spring Show = new(0f, 200f, 26f);
    private static readonly Spring Content = new(1f, 220f, 26f);   // new content slides up and fades in
    private static readonly Spring Knob = new(0f, 320f, 20f);      // the little switch in toggle notifications
    private static readonly Spring Health = new(1f, 200f, 28f);    // target card health bar
    private static string _contentKey = "";

    // --- notifications ------------------------------------------------------------------------

    private static string _noteTitle, _noteSub;
    private static bool _notePositive;
    private static bool _noteIsSwitch;
    private static float _noteUntil;
    private static bool _subscribed;

    // Set by bulk switches (panic) so flipping dozens of features at once shows nothing on screen.
    public static bool Muted;

    public static void Notify(string title, string subtitle, bool positive, bool isSwitch = false)
    {
        if (CheatState.NotifyStyle.Value == "Плашки")
        {
            RockNotify.Push(title, string.IsNullOrEmpty(subtitle) ? "" : " " + subtitle.ToLowerInvariant(), positive, isSwitch);
            return;
        }

        _noteTitle = title;
        _noteSub = subtitle;
        _notePositive = positive;
        _noteIsSwitch = isSwitch;
        _noteUntil = Time.unscaledTime + 2.5f;

        // The switch starts in its OLD position and the knob springs over to the new one.
        Knob.Value = positive ? 0f : 1f;
        Knob.Velocity = 0f;
        Knob.Target = positive ? 1f : 0f;
    }

    // Every on/off setting of the mod reports its changes here (menu clicks and keybinds alike).
    // HUD settings are left out - they are not "features" and would spam while editing the HUD.
    private static void SubscribeToggles()
    {
        if (_subscribed) return;
        _subscribed = true;
        foreach (FieldInfo f in typeof(CheatState).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (f.GetValue(null) is not ConfigEntry<bool> entry) continue;
            string section = entry.Definition.Section;
            if (section == "Hud" || section == "Keybinds" || section == "General") continue;
            string name = entry.Definition.Key;
            entry.SettingChanged += (_, _) =>
            {
                if (CheatState.HudIslandNotifications.Value && !Muted)
                    Notify(name, entry.Value ? "Включено" : "Выключено", entry.Value, true);
            };
        }
    }

    // --- per-frame ------------------------------------------------------------------------------

    private static float _lastTime = -1f;
    private static float _eqPhase;

    public static void Draw()
    {
        float now = Time.unscaledTime;
        float dt = _lastTime < 0f ? 0.016f : Mathf.Clamp(now - _lastTime, 0f, 0.05f);
        _lastTime = now;

        // Feature on/off notifications are collected whether or not the island itself is shown.
        SubscribeToggles();
        if (!CheatState.HudIsland.Value)
        {
            IslandBlur.Hide();
            return;
        }

        float u = Mathf.Max(0.5f, CheatState.HudIslandScale.Value);
        float sw = CustomGui.ScreenWidth, sh = CustomGui.ScreenHeight;
        if (sw <= 0f) return;

        // ---- choose content ----
        Kind kind = Kind.Idle;
        string main = "", sub = "";
        Color accent = Color.white;
        float arrowAngle = 0f;
        string bubbleText = null;
        bool equalizer = false;

        bool inGame = SafeInGame();
        TargetCard targetCard = default;
        if (inGame) Gps.Update();
        if (CheatState.HudIslandMusic.Value) LyricsHud.Advance();

        if (CheatState.HudIslandNotifications.Value && now < _noteUntil)
        {
            kind = Kind.Notification;
            main = _noteTitle;
            sub = _noteSub;
            accent = _notePositive ? new Color(0.35f, 0.95f, 0.55f) : new Color(1f, 0.38f, 0.38f);
        }
        else if (CheatState.HudIslandTarget.Value && inGame && TryTarget(out targetCard))
        {
            kind = Kind.Target;
            main = targetCard.Name;
            sub = targetCard.Dead ? "мёртв" : targetCard.Hp + " hp";
            accent = targetCard.Color;
            Health.Target = targetCard.Dead ? 0f : targetCard.Hp01;
        }
        else if (CheatState.HudIslandExtraction.Value && inGame && Gps.Active)
        {
            // GPS target (Gps.cs): the nearest active extraction by default, or the truck / a mark.
            kind = Kind.Extraction;
            main = $"{Mathf.RoundToInt(Gps.Distance)} м";
            sub = Gps.IsEvacuation ? HaulText() : Gps.Label;
            arrowAngle = Gps.Bearing();
            bubbleText = Gps.IsEvacuation ? PointsText() : null;
            accent = Gps.IsEvacuation ? new Color(1f, 0.82f, 0.3f) : new Color(0.4f, 0.8f, 1f);
        }
        else if (CheatState.HudIslandMusic.Value && LyricsHud.IsPlaying && !string.IsNullOrEmpty(LyricsHud.CurrentTrack))
        {
            kind = Kind.Music;
            string line = CheatState.HudIslandLyrics.Value ? LyricsHud.CurrentLine : null;
            main = string.IsNullOrEmpty(line) ? LyricsHud.CurrentTrack : line;
            equalizer = true;
        }
        else
        {
            // Your own phrases take turns (separated by "|"), 4 s each.
            string idle = CheatState.HudIslandText.Value;
            if (!string.IsNullOrWhiteSpace(idle))
            {
                string[] phrases = idle.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                if (phrases.Length > 0) main = phrases[(int)(now / 4f) % phrases.Length].Trim();
            }
        }

        // Anything that changes what is shown (a new kind, a new phrase, a new lyric line) makes the
        // content slide up and fade in instead of popping. Distances and the clock change every
        // frame, so the GPS card keys on its target only.
        string key = kind switch
        {
            Kind.Extraction => "gps|" + Gps.Label,
            Kind.Target => "target|" + main,
            Kind.Notification => "note|" + main + sub + _noteUntil,
            _ => kind + "|" + main,
        };
        if (key != _contentKey)
        {
            _contentKey = key;
            Content.Value = 0f;
            Content.Velocity = 0f;
        }
        Content.Target = 1f;

        // ---- target size (GUI units) ----
        float fontMain = 7f, fontSub = 6f;
        float targetW = 48f, targetH = 15f, targetR = 7f;
        bool expanded = kind == Kind.Notification || kind == Kind.Target;
        switch (kind)
        {
            case Kind.Notification:
            {
                float tw = Mathf.Max(Measure(main, fontMain, u), Measure(sub, fontSub, u));
                targetW = Mathf.Clamp(tw + 34f, 110f, 220f);
                targetH = 30f;
                targetR = 7f + 11f;
                break;
            }
            case Kind.Target:
                targetW = Mathf.Clamp(Measure(main, fontMain, u) + 74f, 120f, 190f);
                targetH = 30f;
                targetR = 7f + 9f;
                break;
            case Kind.Extraction:
                targetW = Mathf.Clamp(14f + Measure(main, fontMain, u) + 6f + Measure(sub, fontSub, u) + 10f, 70f, 200f);
                break;
            case Kind.Idle:
                if (main.Length > 0) targetW = Mathf.Clamp(Measure(main, fontMain, u) + 20f, 48f, 200f);
                break;
            case Kind.Music:
                targetW = Mathf.Clamp(Measure(main, fontMain, u) + 20f, 70f, 200f);
                break;
        }

        W.Target = targetW; H.Target = targetH; R.Target = targetR;
        Expand.Target = expanded ? 1f : 0f;
        Bubble.Target = (kind == Kind.Music || (kind == Kind.Extraction && bubbleText != null)) ? 1f : 0f;
        Show.Target = 1f;
        W.Step(dt); H.Step(dt); R.Step(dt); Expand.Step(dt); Bubble.Step(dt); Show.Step(dt);
        Content.Step(dt); Knob.Step(dt); Health.Step(dt);

        float alpha = Mathf.Clamp01(Show.Value);
        float bubble = Mathf.Clamp01(Bubble.Value);
        float expand = Mathf.Clamp01(Expand.Value);

        // ---- geometry in canvas pixels (origin bottom-left, y up) ----
        float w = W.Value * u, h = H.Value * u;
        float radius = Mathf.Min(R.Value * u, Mathf.Min(w, h) * 0.5f);
        float x = sw * 0.5f - w * 0.5f - 9.5f * u * bubble;
        float top = sh - 7f * u;
        var pill = new Rect(x, top - h, w, h);

        float br = 7.5f * u;
        float cy = pill.center.y;
        float startCx = pill.xMax - br;
        float endCx = pill.xMax + 4f * u + br;
        float bubbleCx = startCx + (endCx - startCx) * bubble;
        float gapUnits = (bubbleCx - br - pill.xMax) / u;
        float neck = Mathf.Max(0f, 8f - 2f * Mathf.Abs(gapUnits)) * u;

        IslandShapes.Begin();
        IslandShapes.AddSquircle(pill, radius);
        if (bubble > 0.01f)
        {
            IslandShapes.AddCircle(new Vector2(bubbleCx, cy), br);
            if (neck > 0.5f)
                IslandShapes.AddSquircle(new Rect(pill.xMax - br, cy - neck * 0.5f, bubbleCx - (pill.xMax - br), neck), neck * 0.5f);
        }

        // ---- blur, outline, fill ----
        // With blur on, the darkening is the blur image's own tint (85 % black over the blur), applied
        // once through the mask - overlapping shapes (pill + bubble + neck) cannot stack it.
        bool blurred = CheatState.HudIslandBlur.Value && IslandBlur.Render(alpha, new Color(0.15f, 0.15f, 0.15f, 1f));
        if (!blurred) IslandBlur.Hide();

        Color ring = new(1f, 1f, 1f, 0.10f * alpha);
        Color fill = new(0f, 0f, 0f, alpha); // opaque: only used when there is nothing to blur
        foreach (IslandShapes.Shape s in IslandShapes.All)
            DrawRing(s, 1f * u, ring, IslandShapes.All);
        if (!blurred)
            foreach (IslandShapes.Shape s in IslandShapes.All)
                DrawFill(s, fill);

        // ---- content ----
        float contentAlpha = alpha * Mathf.Clamp01((w / u - 40f) / 20f) * Mathf.Clamp01(Content.Value); // hide text while the pill is still tiny
        float slide = (1f - Mathf.Clamp01(Content.Value)) * 4f * u;
        var cp = new Rect(pill.x, pill.y - slide, pill.width, pill.height);
        switch (kind)
        {
            case Kind.Notification:
                DrawNotification(cp, u, main, sub, accent, contentAlpha * expand, fontMain, fontSub);
                break;
            case Kind.Target:
                DrawTarget(cp, u, main, sub, accent, Mathf.Clamp01(Health.Value), contentAlpha * expand, fontMain, fontSub);
                break;
            case Kind.Extraction:
                DrawExtraction(cp, u, main, sub, arrowAngle, accent, contentAlpha, fontMain, fontSub);
                break;
            case Kind.Music:
            case Kind.Idle:
                if (main.Length > 0)
                    CustomGui.TextClipped(new Rect(cp.x + 10f * u, cp.y, cp.width - 20f * u, cp.height), main,
                        Mathf.RoundToInt(fontMain * u), new Color(1f, 1f, 1f, contentAlpha), TextAnchor.MiddleCenter);
                break;
        }

        if (bubble > 0.3f)
        {
            float ba = alpha * Mathf.Clamp01((bubble - 0.3f) / 0.5f);
            if (equalizer) DrawEqualizer(new Vector2(bubbleCx, cy), u, ba, dt);
            else if (bubbleText != null)
                CustomGui.Text(new Rect(bubbleCx - br, cy - br, br * 2f, br * 2f), bubbleText,
                    Mathf.RoundToInt(5.5f * u), new Color(accent.r, accent.g, accent.b, ba), TextAnchor.MiddleCenter);
        }

        // ---- side items (only in a run, fade out while a card is expanded) ----
        if (CheatState.HudIslandSides.Value && inGame)
        {
            float sideAlpha = alpha * (1f - expand);
            DrawClock(pill, u, sideAlpha);
            float rightEdge = bubble > 0.01f ? Mathf.Max(pill.xMax, bubbleCx + br * bubble) : pill.xMax;
            DrawPing(rightEdge, top, u, sideAlpha);
        }
    }

    // --- content painters ---------------------------------------------------------------------

    private static void DrawNotification(Rect pill, float u, string title, string sub, Color accent, float a, float fMain, float fSub)
    {
        if (a <= 0.01f) return;
        float left = 14f;
        if (_noteIsSwitch)
        {
            // iOS-style switch: track tints from grey to the accent as the knob springs across.
            float k = Mathf.Clamp01(Knob.Value);
            float tw = 16f * u, th = 9f * u;
            var track = new Rect(pill.x + 7f * u, pill.center.y - th * 0.5f, tw, th);
            Color off = new(0.35f, 0.35f, 0.38f, a);
            Color on = new(accent.r, accent.g, accent.b, a);
            CustomGui.Rect(track, Color.Lerp(off, on, k), th * 0.5f);
            float knobR = th * 0.5f - 1f * u;
            float cx = Mathf.Lerp(track.x + th * 0.5f, track.xMax - th * 0.5f, Knob.Value);
            CustomGui.Circle(new Vector2(cx, track.center.y), knobR, new Color(1f, 1f, 1f, a));
            left = 7f + 16f + 4f;
        }
        else
        {
            var dot = new Vector2(pill.x + 14f * u, pill.center.y);
            CustomGui.Circle(dot, 5f * u, new Color(accent.r, accent.g, accent.b, a));
            CustomGui.Circle(dot, 2.2f * u, new Color(0f, 0f, 0f, a * 0.85f));
            left = 25f;
        }
        float tx = pill.x + left * u, txw = pill.xMax - tx - 8f * u;
        CustomGui.TextClipped(new Rect(tx, pill.center.y, txw, 9f * u), title, Mathf.RoundToInt(fMain * u),
            new Color(1f, 1f, 1f, a), TextAnchor.LowerLeft);
        CustomGui.TextClipped(new Rect(tx, pill.center.y - 9f * u, txw, 9f * u), sub, Mathf.RoundToInt(fSub * u),
            new Color(accent.r, accent.g, accent.b, a), TextAnchor.UpperLeft);
    }

    // The picked player as a live card: colour chip (their eye colour), name, hp and a spring-driven
    // health bar - the island's version of Kimstar's fight card.
    private static void DrawTarget(Rect pill, float u, string name, string hpText, Color accent, float hp01, float a, float fMain, float fSub)
    {
        if (a <= 0.01f) return;
        float chip = 16f * u;
        var chipRect = new Rect(pill.x + 8f * u, pill.center.y - chip * 0.5f, chip, chip);
        CustomGui.Rect(chipRect, new Color(accent.r, accent.g, accent.b, a), 5f * u);

        float tx = chipRect.xMax + 6f * u;
        float right = pill.xMax - 9f * u;
        CustomGui.TextClipped(new Rect(tx, pill.center.y + 1f * u, right - tx - 30f * u, 9f * u), name,
            Mathf.RoundToInt(fMain * u), new Color(1f, 1f, 1f, a), TextAnchor.LowerLeft);
        CustomGui.Text(new Rect(right - 32f * u, pill.center.y + 1f * u, 32f * u, 9f * u), hpText,
            Mathf.RoundToInt(fSub * u), new Color(accent.r, accent.g, accent.b, a), TextAnchor.LowerRight);

        var track = new Rect(tx, pill.center.y - 7f * u, right - tx, 3f * u);
        CustomGui.Rect(track, new Color(1f, 1f, 1f, 0.14f * a), 1.5f * u);
        if (hp01 > 0.01f)
        {
            Color bar = Color.Lerp(new Color(1f, 0.3f, 0.3f), new Color(0.35f, 0.95f, 0.55f), hp01);
            CustomGui.Rect(new Rect(track.x, track.y, track.width * hp01, track.height), new Color(bar.r, bar.g, bar.b, a), 1.5f * u);
        }
    }

    private static void DrawExtraction(Rect pill, float u, string dist, string haul, float angle, Color accent, float a, float fMain, float fSub)
    {
        if (a <= 0.01f) return;
        var c = new Vector2(pill.x + 9f * u, pill.center.y);
        DrawArrow(c, 3.4f * u, angle, new Color(accent.r, accent.g, accent.b, a));

        float x = pill.x + 16f * u;
        float dw = Measure(dist, fMain, u) * u;
        CustomGui.Text(new Rect(x, pill.y, dw + 4f * u, pill.height), dist, Mathf.RoundToInt(fMain * u),
            new Color(1f, 1f, 1f, a), TextAnchor.MiddleLeft);
        if (!string.IsNullOrEmpty(haul))
            CustomGui.TextClipped(new Rect(x + dw + 5f * u, pill.y, pill.xMax - (x + dw + 5f * u) - 6f * u, pill.height),
                haul, Mathf.RoundToInt(fSub * u), new Color(1f, 1f, 1f, a * 0.6f), TextAnchor.MiddleLeft);
    }

    // Up = straight ahead, rotating clockwise towards the target.
    private static void DrawArrow(Vector2 c, float size, float angleDeg, Color color)
    {
        float rad = -angleDeg * Mathf.Deg2Rad;
        Vector2 Rot(float px, float py) => c + new Vector2(px * Mathf.Cos(rad) - py * Mathf.Sin(rad), px * Mathf.Sin(rad) + py * Mathf.Cos(rad));
        CustomGui.Triangle(Rot(0f, size), Rot(-size * 0.85f, -size * 0.8f), Rot(0f, -size * 0.35f), color);
        CustomGui.Triangle(Rot(0f, size), Rot(0f, -size * 0.35f), Rot(size * 0.85f, -size * 0.8f), color);
    }

    // Rockstar's bubble visualiser: 4 bars, 1 unit wide, 0.9 apart, 2.5-9.5 units tall. There is no
    // audio level to read from Spotify's window, so the bars move on their own - decoration only.
    private static void DrawEqualizer(Vector2 c, float u, float a, float dt)
    {
        _eqPhase += dt;
        const float barW = 1f, gap = 0.9f;
        float total = 4f * barW + 3f * gap;
        float x0 = c.x - total * 0.5f * u;
        for (int i = 0; i < 4; i++)
        {
            float v = 0.5f + 0.5f * Mathf.Sin(_eqPhase * (5.3f + i * 1.7f) + i * 1.3f);
            float bh = Mathf.Clamp(1.5f + v * 8f, 2.5f, 9.5f) * u;
            var r = new Rect(x0 + i * (barW + gap) * u, c.y - bh * 0.5f, barW * u, bh);
            CustomGui.Rect(r, new Color(1f, 1f, 1f, a), barW * u * 0.5f);
        }
    }

    private static void DrawClock(Rect pill, float u, float a)
    {
        if (a <= 0.01f) return;
        string time = DateTime.Now.ToString("HH:mm");
        int size = Mathf.RoundToInt(7f * u);
        float tw = Measure(time, 7f, u) * u;
        float top = pill.yMax;
        CustomGui.Text(new Rect(pill.x - tw - 6f * u, top - 15f * u, tw + 2f * u, 15f * u), time, size,
            new Color(1f, 1f, 1f, a), TextAnchor.MiddleRight);
    }

    private static readonly int[] PingSteps = { 450, 300, 150, 75 };

    private static void DrawPing(float rightEdge, float top, float u, float a)
    {
        if (a <= 0.01f) return;
        float x = rightEdge + 6f * u;
        float baseY = top - 15f * u + 4f * u;

        // GameManager.Multiplayer() is the game's own flag - checked BEFORE touching PhotonNetwork
        // (touching it too early hangs the connect flow, see CLAUDE.md).
        if (!GameManager.Multiplayer() || !PhotonNetwork.InRoom)
        {
            DrawPlane(new Vector2(x + 5f * u, baseY + 3.5f * u), u, a);
            return;
        }

        int ping = PhotonNetwork.GetPing();
        for (int i = 0; i < PingSteps.Length; i++)
        {
            bool lit = ping < PingSteps[i];
            float bh = (3f + i) * u;
            var r = new Rect(x + i * 2.7f * u, baseY, 2f * u, bh);
            CustomGui.Rect(r, new Color(1f, 1f, 1f, a * (lit ? 1f : 0.25f)), 0.6f * u);
        }
        CustomGui.Text(new Rect(x + 12f * u, top - 15f * u, 40f * u, 15f * u), ping + " ms",
            Mathf.RoundToInt(6f * u), new Color(1f, 1f, 1f, a * 0.7f), TextAnchor.MiddleLeft);
    }

    // Offline: a small paper plane, like Rockstar's single-player icon.
    private static void DrawPlane(Vector2 c, float u, float a)
    {
        var col = new Color(1f, 1f, 1f, a);
        CustomGui.Triangle(c + new Vector2(-4f * u, 1f * u), c + new Vector2(4f * u, 0f), c + new Vector2(-4f * u, -1f * u), col);
        CustomGui.Triangle(c + new Vector2(-1f * u, 0f), c + new Vector2(1.5f * u, 0f), c + new Vector2(-2.5f * u, 3.5f * u), col);
        CustomGui.Triangle(c + new Vector2(-1f * u, 0f), c + new Vector2(-2.5f * u, -3.5f * u), c + new Vector2(1.5f * u, 0f), col);
    }

    // --- game data ----------------------------------------------------------------------------

    private static bool SafeInGame()
    {
        try { return GameState.IsInGame() && PlayerAvatar.instance != null; }
        catch (Exception) { return false; }
    }

    private struct TargetCard
    {
        public string Name;
        public int Hp;
        public float Hp01;
        public bool Dead;
        public Color Color;
    }

    // The player picked in "Игроки", while they are within 30 m of you.
    private static bool TryTarget(out TargetCard card)
    {
        card = default;
        try
        {
            PlayerAvatar target = PlayerTargets.Picked();
            PlayerAvatar local = PlayerTargets.Local;
            if (target == null || local == null) return false;
            if (Vector3.Distance(local.transform.position, target.transform.position) > 30f) return false;

            bool dead = GameReflect.Get<bool>(target, "deadSet");
            int hp = 0, max = 100;
            if (!dead && target.playerHealth != null)
            {
                hp = GameReflect.Get<int>(target.playerHealth, "health");
                max = Mathf.Max(1, GameReflect.Get<int>(target.playerHealth, "maxHealth"));
            }
            card = new TargetCard
            {
                Name = PlayerTargets.DisplayName(target),
                Hp = hp,
                Hp01 = Mathf.Clamp01(hp / (float)max),
                Dead = dead,
                Color = CosmeticHack.GetSyncedEyeColor(target) ?? new Color(0.45f, 0.75f, 1f),
            };
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string HaulText()
    {
        try
        {
            RoundDirector rd = RoundDirector.instance;
            if (rd == null) return "";
            int haul = GameReflect.Get<int>(rd, "currentHaul");
            int goal = GameReflect.Get<int>(rd, "haulGoal");
            return goal > 0 ? $"${Short(haul)} / ${Short(goal)}" : "";
        }
        catch (Exception) { return ""; }
    }

    private static string PointsText()
    {
        try
        {
            RoundDirector rd = RoundDirector.instance;
            if (rd == null) return null;
            int done = GameReflect.Get<int>(rd, "extractionPointsCompleted");
            int total = GameReflect.Get<int>(rd, "extractionPoints");
            return total > 0 ? $"{done}/{total}" : null;
        }
        catch (Exception) { return null; }
    }

    private static string Short(int v) => v >= 1000 ? (v / 1000f).ToString(v >= 10000 ? "0" : "0.#") + "k" : v.ToString();

    // Width of `text` in GUI units at `fontUnits`.
    private static float Measure(string text, float fontUnits, float u) =>
        string.IsNullOrEmpty(text) ? 0f : CustomGui.MeasureText(text, Mathf.RoundToInt(fontUnits * u)) / u;

    // --- shape drawing ------------------------------------------------------------------------

    internal static void DrawFill(IslandShapes.Shape s, Color c)
    {
        List<Vector2> p = s.Points;
        for (int i = 0; i < p.Count; i++)
            CustomGui.Triangle(s.Center, p[i], p[(i + 1) % p.Count], c);
    }

    // A band `width` wide just outside the shape. The outward direction at each point is the normal of
    // the outline there (taken from its neighbours) - pushing points away from the shape's centre, as
    // before, skews the band on a wide pill. Segments that lie inside another shape of the island
    // (the bubble over the pill, the neck over both) are skipped, so the outline is the union's.
    internal static void DrawRing(IslandShapes.Shape s, float width, Color c, List<IslandShapes.Shape> others = null)
    {
        List<Vector2> p = s.Points;
        int n = p.Count;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = p[i], b = p[(i + 1) % n];

            if (others != null)
            {
                Vector2 mid = (a + b) * 0.5f;
                bool covered = false;
                foreach (IslandShapes.Shape o in others)
                {
                    if (o == s) continue;
                    if (IslandShapes.Contains(o, mid)) { covered = true; break; }
                }
                if (covered) continue;
            }

            Vector2 ao = a + Outward(p, i) * width;
            Vector2 bo = b + Outward(p, (i + 1) % n) * width;
            CustomGui.Triangle(a, ao, bo, c);
            CustomGui.Triangle(a, bo, b, c);
        }
    }

    // Points are counter-clockwise, so the outward normal is the tangent rotated a quarter turn clockwise.
    private static Vector2 Outward(List<Vector2> p, int i)
    {
        Vector2 prev = p[(i + p.Count - 1) % p.Count], next = p[(i + 1) % p.Count];
        Vector2 t = next - prev;
        Vector2 normal = new Vector2(t.y, -t.x);
        return normal.sqrMagnitude < 1e-6f ? Vector2.zero : normal.normalized;
    }
}

// The island's outline for this frame, shared by the visible fill and the blur mask so the blur
// is cut out exactly under the pill, the bubble and the neck.
internal static class IslandShapes
{
    public sealed class Shape
    {
        public Vector2 Center;
        public List<Vector2> Points;
    }

    public static readonly List<Shape> All = new();

    public static void Begin() => All.Clear();

    public static bool Contains(Shape s, Vector2 point)
    {
        List<Vector2> p = s.Points;
        for (int i = 0; i < p.Count; i++)
        {
            Vector2 a = p[i], b = p[(i + 1) % p.Count];
            Vector2 edge = b - a, to = point - a;
            if (edge.x * to.y - edge.y * to.x < 0f) return false;
        }
        return true;
    }

    // Rounded rect with "continuous" corners (superellipse quadrants, like iOS) - Rockstar draws
    // a squircle, a plain circular-arc corner reads noticeably rounder next to it.
    public static void AddSquircle(Rect r, float radius, int perCorner = 10)
    {
        radius = Mathf.Clamp(radius, 0f, Mathf.Min(r.width, r.height) * 0.5f);
        var pts = new List<Vector2>(perCorner * 4);
        Corner(pts, new Vector2(r.xMax - radius, r.yMax - radius), radius, 0f, perCorner);
        Corner(pts, new Vector2(r.xMin + radius, r.yMax - radius), radius, 90f, perCorner);
        Corner(pts, new Vector2(r.xMin + radius, r.yMin + radius), radius, 180f, perCorner);
        Corner(pts, new Vector2(r.xMax - radius, r.yMin + radius), radius, 270f, perCorner);
        All.Add(new Shape { Center = r.center, Points = pts });
    }

    private static void Corner(List<Vector2> pts, Vector2 c, float radius, float startDeg, int n)
    {
        const float exponent = 4f; // 2 = circle, higher = squarer; 4 is close to Apple's corner
        for (int i = 0; i <= n; i++)
        {
            float t = (startDeg + 90f * i / n) * Mathf.Deg2Rad;
            float cos = Mathf.Cos(t), sin = Mathf.Sin(t);
            float x = Mathf.Sign(cos) * Mathf.Pow(Mathf.Abs(cos), 2f / exponent);
            float y = Mathf.Sign(sin) * Mathf.Pow(Mathf.Abs(sin), 2f / exponent);
            pts.Add(c + new Vector2(x, y) * radius);
        }
    }

    // A one-off squircle card: outline band outside, fill inside. Does not touch the island's own
    // shape list (and so not its blur mask) - used by the HUD cards so they share the island's corners.
    public static void Panel(Rect r, float radius, Color fill, Color ring, float ringWidth)
    {
        int before = All.Count;
        AddSquircle(r, radius);
        Shape s = All[before];
        All.RemoveAt(before);
        DynamicIsland.DrawRing(s, ringWidth, ring);
        DynamicIsland.DrawFill(s, fill);
    }

    public static void AddCircle(Vector2 c, float radius, int segments = 28)
    {
        var pts = new List<Vector2>(segments);
        for (int i = 0; i < segments; i++)
        {
            float t = i * Mathf.PI * 2f / segments;
            pts.Add(c + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * radius);
        }
        All.Add(new Shape { Center = c, Points = pts });
    }
}

// uGUI graphic that draws IslandShapes as solid geometry - used only as the stencil of a Mask.
internal sealed class IslandMaskGraphic : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        UIVertex v = UIVertex.simpleVert;
        v.color = Color.white;
        foreach (IslandShapes.Shape s in IslandShapes.All)
        {
            int start = vh.currentVertCount;
            v.position = s.Center;
            vh.AddVert(v);
            foreach (Vector2 p in s.Points)
            {
                v.position = p;
                vh.AddVert(v);
            }
            int n = s.Points.Count;
            for (int i = 0; i < n; i++)
                vh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % n);
        }
    }
}

// GPU blur of the game frame, shown only inside the island's shape.
internal static class IslandBlur
{
    private static GameObject _root;
    private static IslandMaskGraphic _mask;
    private static RawImage _image;
    private static RenderTexture _half, _quarter, _eighth, _back;
    private static int _srcW, _srcH;

    public static void Hide()
    {
        if (_root != null && _root.activeSelf) _root.SetActive(false);
    }

    // Returns false when there is nothing to blur (no game render texture in this scene).
    public static bool Render(float alpha, Color tint)
    {
        RenderTexture src = null;
        try
        {
            RenderTextureMain main = RenderTextureMain.instance;
            if (main != null) src = main.renderTexture;
        }
        catch (Exception) { }
        if (src == null || !src.IsCreated() || HudRoot.IconLayer == null) return false;

        Ensure(HudRoot.IconLayer.parent);
        EnsureTargets(src.width, src.height);

        // Progressive bilinear down-sampling then one step back up: a cheap wide blur.
        Graphics.Blit(src, _half);
        Graphics.Blit(_half, _quarter);
        Graphics.Blit(_quarter, _eighth);
        Graphics.Blit(_eighth, _back);

        _image.texture = _back;
        _image.color = new Color(tint.r, tint.g, tint.b, alpha);
        if (!_root.activeSelf) _root.SetActive(true);
        _mask.SetVerticesDirty();
        return true;
    }

    private static void Ensure(Transform canvas)
    {
        if (_root != null) return;

        _root = new GameObject("RepoCheatMenu_IslandBlur", typeof(RectTransform));
        _root.transform.SetParent(canvas, false);
        _root.transform.SetSiblingIndex(0); // under CustomGui's shapes and text
        Stretch(_root.GetComponent<RectTransform>());
        _mask = _root.AddComponent<IslandMaskGraphic>();
        _mask.raycastTarget = false;
        _root.AddComponent<Mask>().showMaskGraphic = false;

        var img = new GameObject("Blur", typeof(RectTransform));
        img.transform.SetParent(_root.transform, false);
        Stretch(img.GetComponent<RectTransform>());
        _image = img.AddComponent<RawImage>();
        _image.raycastTarget = false;
    }

    // Same layout as CustomGui's surfaces (full stretch, pivot bottom-left), so IslandShapes'
    // coordinates mean the same thing here.
    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = Vector2.zero;
    }

    private static void EnsureTargets(int w, int h)
    {
        if (_half != null && _srcW == w && _srcH == h) return;
        Release();
        _srcW = w;
        _srcH = h;
        _half = Make(w / 2, h / 2);
        _quarter = Make(w / 4, h / 4);
        _eighth = Make(w / 8, h / 8);
        _back = Make(w / 4, h / 4);
    }

    private static RenderTexture Make(int w, int h)
    {
        var rt = new RenderTexture(Mathf.Max(8, w), Mathf.Max(8, h), 0) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        rt.Create();
        return rt;
    }

    private static void Release()
    {
        foreach (RenderTexture rt in new[] { _half, _quarter, _eighth, _back })
            if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); }
        _half = _quarter = _eighth = _back = null;
    }
}
