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
using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;

namespace RepoCheatMenu;

// Always-on gameplay HUD - separate from both menus (OverlayUI/CustomOverlayUI), drawn every real
// frame via HudRoot.Tick() regardless of whether either menu is open, through the shared CustomGui
// mesh renderer. The cards are a port of Rockstar's own HUD elements, sizes in its GUI units
// (CheatState.HudScale pixels each):
//   Keybinds -> "Бинды"  (header pill + two-pill rows, sorted by width, enter/exit slide)
//   TargetHud -> "Цель"  (91x27 squircle card, staggered content, hp bar, item pills below)
//   "Игрок" and "Плеер" use the same header-pill + row language (Rockstar has no card for them).
// Every card pops in from 50 % scale with Rockstar's overshoot curve (see HudKit). Item icons are the
// one thing CustomGui cannot draw (arbitrary Sprites), so they go through a small pool of ordinary
// Image components under HudRoot.IconLayer, positioned from the same mapped rects.
internal static class PlayerHud
{
    private enum Kind { Stats, Target, Media, Keybinds, Team, Enemies, Loot, Run }
    private enum Glyph { Person, Target, Note, Keys, Coin }

    private sealed class RowState
    {
        public string Id, Label, Value;
        public bool Buttons, Seen;
        public float NameW, ValueW, Sort;
        public readonly HudKit.Tween Appear = new(240f, HudKit.OutCubic);
        public readonly HudKit.Spring Y = new(0f);
        public bool Fresh = true;
    }

    private struct RowIn
    {
        public string Id, Label, Value;
        public bool Buttons;
    }

    private sealed class Widget
    {
        public string Id, Title;
        public Kind Kind;
        public CheatState.HudWidgetConfig Cfg;

        public Vector2 RenderPos, TargetPos;
        public bool Dragging, Initialized, Right, Dirty, Stacked;
        public float NextSave;

        public float WU = 40f, HU = 11f;      // current size in GUI units (what the last draw laid out)
        public float LastWU;

        public readonly HudKit.Tween Anim = new(300f, HudKit.Pop);
        public readonly HudKit.Tween Visible = new(300f, HudKit.Pop);
        public readonly HudKit.Tween Drag = new(300f, HudKit.InOut);
        public readonly HudKit.Spring Width = new(40f);

        public readonly Dictionary<string, RowState> Rows = new();
        public readonly List<RowState> Order = new();
        public readonly List<string> Dead = new();
    }

    private static readonly Widget[] Widgets =
    {
        new() { Id = "stats", Title = "Игрок", Kind = Kind.Stats, Cfg = CheatState.HudPlayerStats },
        new() { Id = "target", Title = "Цель", Kind = Kind.Target, Cfg = CheatState.HudTargetInfo },
        new() { Id = "media", Title = "Плеер", Kind = Kind.Media, Cfg = CheatState.HudMediaPlayer },
        new() { Id = "keybinds", Title = "Бинды", Kind = Kind.Keybinds, Cfg = CheatState.HudKeybinds },
        new() { Id = "team", Title = "Команда", Kind = Kind.Team, Cfg = CheatState.HudTeam },
        new() { Id = "enemies", Title = "Враги", Kind = Kind.Enemies, Cfg = CheatState.HudEnemies },
        new() { Id = "loot", Title = "Лут", Kind = Kind.Loot, Cfg = CheatState.HudLoot },
        new() { Id = "run", Title = "Забег", Kind = Kind.Run, Cfg = CheatState.HudRun },
    };

    private static readonly HudCtx Cx = new();
    private static Vector2 _dragLast;
    private static bool _dragLastValid;

    // Same 8-preset palette as CustomOverlayUI's own ColorCycle rows - not shared via a common
    // constant since it's a single small literal array, not worth a cross-file dependency for.
    private static readonly string[] Palette = { "#FF3B30", "#FF9500", "#FFCC00", "#34C759", "#0A84FF", "#AF52DE", "#FFFFFF", "#00FFFF" };

    public static void Draw()
    {
        if (!CheatState.HudEnabled.Value) return;

        ResetIcons();
        bool edit = CheatState.HudEditMode.Value;
        float u = Mathf.Max(0.5f, CheatState.HudScale.Value);

        if (CheatState.HudAutoStack.Value) RestackColumn(u, edit);
        else foreach (Widget w in Widgets) w.Stacked = false;

        foreach (Widget w in Widgets)
            DrawWidget(w, edit, u);

        if (edit) DrawPalette();

        FinishIcons();
    }

    // --- layout of the column -------------------------------------------------------------------

    // Cards are laid top-down from the first shown one's own position. Rockstar has no such stacking
    // (every element is free-standing), so this is off-by-choice-able; hidden cards leave no hole.
    private static bool Shown(Widget w, bool edit) =>
        w.Cfg.Enabled.Value && (w.Visible.Value > 0.01f || WantVisible(w, edit));

    private static void RestackColumn(float u, bool edit)
    {
        Widget anchor = null;
        float y = 0f;
        float gap = 3f * u;

        foreach (Widget w in Widgets)
        {
            w.Stacked = false;
            if (!Shown(w, edit)) continue;

            if (anchor == null)
            {
                anchor = w;
                y = w.Cfg.PosY.Value + w.HU * u + gap;
                continue;
            }

            w.Stacked = true;
            w.TargetPos = new Vector2(anchor.Cfg.PosX.Value, y);
            y += w.HU * u + gap;
        }
    }

    // Turns the auto column off without moving anything: every card's saved position becomes where it
    // is drawn right now.
    private static void ReleaseColumn()
    {
        foreach (Widget w in Widgets)
        {
            w.Stacked = false;
            w.Dirty = false;
            if (!w.Initialized) continue;
            w.TargetPos = w.RenderPos;
            w.Cfg.PosX.Value = w.RenderPos.x;
            w.Cfg.PosY.Value = w.RenderPos.y;
        }
        CheatState.HudAutoStack.Value = false;
        DynamicIsland.Notify("Авто-колонка", "Выключена: карточки теперь двигаются по одной", false, true);
    }

    private static bool MenuUnderPointer()
    {
        if (CustomOverlayUI.PointerOverWindow) return true;
        var es = UnityEngine.EventSystems.EventSystem.current;
        return OverlayUI.IsOpen && es != null && es.IsPointerOverGameObject();
    }

    private static bool WantVisible(Widget w, bool edit)
    {
        switch (w.Kind)
        {
            case Kind.Target: return edit || TargetSource(false) != null;
            case Kind.Keybinds: return edit || HasBinds();
            case Kind.Team: return edit || TeamRows().Count > 0;
            case Kind.Enemies: return edit || EnemyRows().Count > 0;
            case Kind.Loot: return edit || LootRows().Count > 0;
            case Kind.Run: return edit || RunRows().Count > 0;
            default: return true;
        }
    }

    // --- one card -------------------------------------------------------------------------------

    private static void DrawWidget(Widget w, bool edit, float u)
    {
        bool enabled = w.Cfg.Enabled.Value;
        bool want = enabled && WantVisible(w, edit);
        w.Anim.Easing = enabled ? HudKit.Pop : HudKit.Retreat;
        w.Visible.Easing = want ? HudKit.Pop : HudKit.Retreat;
        w.Anim.Set(enabled);
        w.Visible.Set(want);
        float a = w.Anim.Value * w.Visible.Value;

        if (!w.Initialized)
        {
            w.RenderPos = new Vector2(w.Cfg.PosX.Value, w.Cfg.PosY.Value);
            w.TargetPos = w.RenderPos;
            w.Initialized = true;
        }

        if (a <= 0.002f && !w.Dragging) return;

        float sw = CustomGui.ScreenWidth;
        float pxW = w.WU * u, pxH = w.HU * u;
        Rect hit = new(w.RenderPos.x, CustomGui.ScreenHeight - w.RenderPos.y - pxH, pxW, pxH);
        // Not through an open menu: a click on a menu row lying over a card used to grab (or, right click, remove) the card too.
        bool over = edit && a > 0.5f && CustomGui.IsHovered(hit) && !MenuUnderPointer();

        if (edit)
        {
            if (over && UiInput.Down(0) && !w.Dragging)
            {
                // RestackColumn rewrites a stacked card's position every frame, so it can only ever
                // be nudged for one frame before snapping back - grabbing one frees the whole column
                // first (each card keeps exactly where it is now) and turns the auto column off.
                if (w.Stacked) ReleaseColumn();
                w.Dragging = true;
                w.TargetPos = w.RenderPos;
            }
            if (UiInput.Up(0) && w.Dragging)
            {
                w.Dragging = false;
                w.Cfg.PosX.Value = w.TargetPos.x;
                w.Cfg.PosY.Value = w.TargetPos.y;
                w.Dirty = false;
            }
            if (w.Dragging)
            {
                // Position difference between ticks (GetAxis only has the last frame's movement).
                Vector2 m = CustomGui.MouseLocalPos();
                if (_dragLastValid)
                {
                    w.TargetPos.x += m.x - _dragLast.x;
                    w.TargetPos.y -= m.y - _dragLast.y;
                }
                _dragLast = m;
                _dragLastValid = true;
            }
            // Right click removes the card, middle click cycles its accent - the card has no room
            // for the old close/colour buttons any more.
            if (over && UiInput.Down(1)) w.Cfg.Enabled.Value = false;
            if (over && UiInput.Down(2))
            {
                int idx = Array.IndexOf(Palette, w.Cfg.Color.Value);
                w.Cfg.Color.Value = Palette[(idx + 1 + Palette.Length) % Palette.Length];
            }
        }
        else
        {
            w.Dragging = false;
        }

        if (!w.Dragging) _dragLastValid = false;
        w.Drag.Set(w.Dragging);

        if (!w.Dragging && !w.Stacked && !w.Dirty)
            w.TargetPos = new Vector2(w.Cfg.PosX.Value, w.Cfg.PosY.Value);

        // Frame-rate independent version of the old 0.25-per-frame trailing follow.
        float follow = 1f - Mathf.Pow(0.75f, Time.unscaledDeltaTime * 60f);
        w.RenderPos = Vector2.Lerp(w.RenderPos, w.TargetPos, follow);

        // Throttled so a card riding the stack does not rewrite the config file every frame.
        if (w.Stacked && Time.unscaledTime >= w.NextSave
            && (Mathf.Abs(w.Cfg.PosY.Value - w.TargetPos.y) > 0.5f || Mathf.Abs(w.Cfg.PosX.Value - w.TargetPos.x) > 0.5f))
        {
            w.Cfg.PosX.Value = w.TargetPos.x;
            w.Cfg.PosY.Value = w.TargetPos.y;
            w.NextSave = Time.unscaledTime + 1f;
        }

        w.Right = w.RenderPos.x + w.WU * u * 0.5f >= sw * 0.5f;

        Color accent = ParseColor(w.Cfg.Color.Value);
        float scale = 0.5f + Mathf.Max(a, 0f) * 0.5f;
        Cx.Begin(w.RenderPos.x, w.RenderPos.y, w.WU, w.HU, u, scale, Mathf.Clamp01(a));

        if (edit) DrawEditFrame(Cx, w, accent);
        if (w.Drag.Value > 0.01f) DrawDragShadow(Cx, w);

        switch (w.Kind)
        {
            case Kind.Target: DrawTarget(w, Cx, accent); break;
            case Kind.Keybinds: DrawKeybinds(w, Cx, accent); break;
            case Kind.Stats: DrawStats(w, Cx, accent); break;
            case Kind.Media: DrawMedia(w, Cx, accent); break;
            case Kind.Team: DrawPillList(w, Cx, accent, Glyph.Person, TeamRows(), false); break;
            case Kind.Enemies: DrawPillList(w, Cx, accent, Glyph.Target, EnemyRows(), false); break;
            case Kind.Loot: DrawPillList(w, Cx, accent, Glyph.Coin, LootRows(), false); break;
            case Kind.Run: DrawPillList(w, Cx, accent, Glyph.Target, RunRows(), false); break;
        }

        // A card on the right half keeps its right edge where it was when its content changes width
        // (Rockstar does the same), so it grows leftwards instead of marching off the screen.
        if (w.LastWU > 0f && w.Right && !w.Dragging && !w.Stacked && Mathf.Abs(w.WU - w.LastWU) > 0.0005f)
        {
            float dx = (w.WU - w.LastWU) * u;
            w.RenderPos.x -= dx;
            w.TargetPos.x -= dx;
            w.Dirty = true;
        }
        w.LastWU = w.WU;

        if (w.Dirty && Mathf.Abs(w.Width.Velocity) < 0.02f && Time.unscaledTime >= w.NextSave)
        {
            w.Cfg.PosX.Value = w.TargetPos.x;
            w.Cfg.PosY.Value = w.TargetPos.y;
            w.Dirty = false;
            w.NextSave = Time.unscaledTime + 1f;
        }
    }

    // Four hairlines around the card while editing, so you can see what you are about to grab.
    private static void DrawEditFrame(HudCtx cx, Widget w, Color accent)
    {
        Color c = new(accent.r, accent.g, accent.b, 0.55f);
        const float t = 0.35f, m = 1f;
        float x = -m, y = -m, ww = w.WU + m * 2f, hh = w.HU + m * 2f;
        cx.Rect(x, y, ww, t, c);
        cx.Rect(x, y + hh - t, ww, t, c);
        cx.Rect(x, y, t, hh, c);
        cx.Rect(x + ww - t, y, t, hh, c);
    }

    // Rockstar's drag shadow, as a few widening dark layers.
    private static void DrawDragShadow(HudCtx cx, Widget w)
    {
        float k = w.Drag.Value;
        for (int i = 3; i >= 1; i--)
        {
            float g = i * 1.6f;
            cx.Rect(-g, -g, w.WU + g * 2f, w.HU + g * 2f, new Color(0f, 0f, 0f, 0.07f * k), 6f + g);
        }
    }

    // --- header + pill rows (Keybinds, and the Player / Media cards in the same language) --------

    private const float HeaderH = 11f, RowH = 10f, Gap = 1f, Pad = 3f, FontU = 6f;
    private static readonly Color White = Color.white;

    private static void DrawPillList(Widget w, HudCtx cx, Color accent, Glyph glyph, List<RowIn> rows, bool sortByWidth)
    {
        float u = cx.U;
        float dt = Time.unscaledDeltaTime;
        bool right = w.Right;

        float titleW = cx.Measure(w.Title, FontU);
        float headerW = Pad + 7f + 3f + titleW + Pad;

        foreach (RowState s in w.Rows.Values) s.Seen = false;
        for (int i = 0; i < rows.Count; i++)
        {
            RowIn r = rows[i];
            if (!w.Rows.TryGetValue(r.Id, out RowState st))
                w.Rows[r.Id] = st = new RowState { Id = r.Id };
            st.Seen = true;
            st.Buttons = r.Buttons;
            st.Label = r.Label;
            st.Value = r.Value;
            if (r.Buttons)
            {
                st.NameW = 0f;
                st.ValueW = ButtonsWidth(w);
            }
            else
            {
                st.NameW = Pad * 2f + cx.Measure(r.Label, FontU);
                st.ValueW = string.IsNullOrEmpty(r.Value) ? 0f : Pad * 2f + cx.Measure(r.Value, FontU);
            }
            st.Sort = sortByWidth ? -(st.NameW + st.ValueW) : i;
        }

        w.Order.Clear();
        w.Dead.Clear();
        foreach (RowState s in w.Rows.Values)
        {
            s.Appear.Set(s.Seen);
            if (!s.Seen && s.Appear.Value < 0.002f) { w.Dead.Add(s.Id); continue; }
            w.Order.Add(s);
        }
        foreach (string id in w.Dead) w.Rows.Remove(id);
        w.Order.Sort((x, y) => x.Sort.CompareTo(y.Sort));

        float colW = headerW, cursor = HeaderH;
        foreach (RowState s in w.Order)
        {
            float c = Mathf.Clamp01(s.Appear.Value);
            float yT = cursor + Gap * c;
            if (s.Fresh) { s.Y.Snap(yT); s.Fresh = false; }
            s.Y.Target = yT;
            s.Y.Step(dt);
            cursor = yT + RowH * c;
            if (s.Seen) colW = Mathf.Max(colW, RowWidth(s));
        }

        if (w.Width.Value <= 0.001f || w.LastWU <= 0f) w.Width.Snap(colW);
        w.Width.Target = colW;
        w.Width.Step(dt);
        float W = Mathf.Max(w.Width.Value, headerW);
        w.WU = W;
        w.HU = cursor;

        // Header: glass card with the accent glow strip on the left.
        float baseA = cx.Alpha;
        float hx = right ? W - headerW : 0f;
        cx.Squircle(hx, 0f, headerW, HeaderH, 3f, 3f, HudKit.Panel(HudKit.CardAlpha));
        cx.Squircle(hx, 0f, Mathf.Min(23f, headerW), HeaderH, 3f, 3f,
            new Color(HudKit.Glow.r, HudKit.Glow.g, HudKit.Glow.b, 0.10f),
            new Color(HudKit.Glow.r, HudKit.Glow.g, HudKit.Glow.b, 0f));
        DrawGlyph(cx, glyph, hx + Pad, (HeaderH - 7f) * 0.5f, accent);
        cx.Text(hx + Pad + 7f + 3f, 0f, titleW + 4f, HeaderH, w.Title, FontU, White, TextAnchor.MiddleLeft);

        foreach (RowState s in w.Order)
        {
            float c = Mathf.Clamp01(s.Appear.Value);
            if (c <= 0.003f) continue;
            cx.Alpha = baseA * c;
            float off = (right ? 6f : -6f) * (1f - c);
            float rw = RowWidth(s);
            float rx = (right ? W - rw : 0f) + off;
            float ry = s.Y.Value;

            if (s.Buttons)
            {
                DrawButtons(w, cx, rx, ry);
                continue;
            }

            cx.Rect(rx, ry, s.NameW, RowH, HudKit.Panel(HudKit.NameAlpha), 2f);
            cx.Text(rx + Pad, ry, s.NameW, RowH, s.Label, FontU, White, TextAnchor.MiddleLeft);
            if (s.ValueW > 0f)
            {
                float vx = rx + s.NameW + Gap;
                cx.Rect(vx, ry, s.ValueW, RowH, HudKit.Panel(HudKit.ValueAlpha), 2f);
                cx.Text(vx, ry, s.ValueW, RowH, s.Value, FontU, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleCenter);
            }
        }
        cx.Alpha = baseA;
    }

    private static float RowWidth(RowState s) => s.NameW + (s.ValueW > 0f && s.NameW > 0f ? Gap : 0f) + s.ValueW;

    private static void DrawGlyph(HudCtx cx, Glyph glyph, float x, float y, Color c)
    {
        switch (glyph)
        {
            case Glyph.Keys:
                for (int r = 0; r < 2; r++)
                    for (int k = 0; k < 3; k++)
                        cx.Rect(x + 0.3f + k * 2.3f, y + 0.8f + r * 2.3f, 1.8f, 1.8f, c, 0.4f);
                cx.Rect(x + 0.3f, y + 5.4f, 6.4f, 1.2f, c, 0.4f);
                break;
            case Glyph.Person:
                cx.Circle(x + 3.5f, y + 2.1f, 1.7f, c);
                cx.Rect(x + 0.9f, y + 4.2f, 5.2f, 2.8f, c, 1.3f);
                break;
            case Glyph.Coin:
                cx.Circle(x + 3.5f, y + 3.5f, 3.2f, c);
                cx.Circle(x + 3.5f, y + 3.5f, 2.0f, new Color(0f, 0f, 0f, 0.35f * c.a));
                cx.Circle(x + 3.5f, y + 3.5f, 1.1f, c);
                break;
            case Glyph.Note:
                cx.Circle(x + 2.4f, y + 5.4f, 1.6f, c);
                cx.Rect(x + 3.4f, y + 0.8f, 0.9f, 4.9f, c, 0.3f);
                cx.Rect(x + 3.4f, y + 0.8f, 2.6f, 1.0f, c, 0.4f);
                break;
            default:
                cx.Circle(x + 3.5f, y + 3.5f, 1.0f, c);
                cx.Rect(x + 3.1f, y, 0.8f, 1.8f, c, 0.3f);
                cx.Rect(x + 3.1f, y + 5.2f, 0.8f, 1.8f, c, 0.3f);
                cx.Rect(x, y + 3.1f, 1.8f, 0.8f, c, 0.3f);
                cx.Rect(x + 5.2f, y + 3.1f, 1.8f, 0.8f, c, 0.3f);
                break;
        }
    }

    // --- Keybinds -------------------------------------------------------------------------------

    private static bool HasBinds()
    {
        foreach (Keybind kb in KeybindManager.All)
            if (BoundAndOn(kb)) return true;
        return false;
    }

    // Rockstar lists enabled modules that have a key - here that is toggles that are on and bound.
    private static bool BoundAndOn(Keybind kb) => kb.Vk.Value != 0 && kb.Toggle != null && kb.Toggle.Value;

    private static readonly List<RowIn> RowBuf = new();

    private static void DrawKeybinds(Widget w, HudCtx cx, Color accent)
    {
        RowBuf.Clear();
        foreach (Keybind kb in KeybindManager.All)
            if (BoundAndOn(kb))
                RowBuf.Add(new RowIn { Id = kb.Label, Label = Truncate(kb.Label, 22), Value = Win32.VkName(kb.Vk.Value) });
        DrawPillList(w, cx, accent, Glyph.Keys, RowBuf, true);
    }

    // --- Team / Enemies ---------------------------------------------------------------------------

    // Both lists are rebuilt at most once per frame - WantVisible, the column restack and the draw all
    // ask for them in the same frame.
    private static readonly List<RowIn> TeamBuf = new();
    private static readonly List<RowIn> EnemyBuf = new();
    private static int _teamFrame = -1, _enemyFrame = -1;
    private const int MaxEnemyRows = 6;

    // One pill per teammate: name + "<hp> HP  <distance> м", or "мёртв". Local-only reads of the
    // already replicated PlayerList / PlayerHealth.health (the same data the ESP label shows).
    private static List<RowIn> TeamRows()
    {
        if (_teamFrame == Time.frameCount) return TeamBuf;
        _teamFrame = Time.frameCount;
        TeamBuf.Clear();

        PlayerAvatar local = PlayerTargets.Local;
        GameDirector director = GameDirector.instance;
        List<PlayerAvatar> players = director != null ? GameReflect.Get<List<PlayerAvatar>>(director, "PlayerList") : null;
        if (local == null || players == null) return TeamBuf;

        foreach (PlayerAvatar p in players)
        {
            if (p == null || p == local) continue;
            string value;
            if (GameReflect.Get<bool>(p, "deadSet"))
            {
                value = "мёртв";
            }
            else
            {
                PlayerHealth ph = GameReflect.Get<PlayerHealth>(p, "playerHealth");
                int hp = ph != null ? GameReflect.Get<int>(ph, "health") : 0;
                float d = Vector3.Distance(local.transform.position, p.transform.position);
                value = $"{hp} HP  {d:0} м";
            }
            // isTalking is what the game itself syncs (IsTalkingRPC), so it works even when the audio is
            // muted or out of range for us.
            PlayerVoiceChat vc = GameReflect.Get<PlayerVoiceChat>(p, "voiceChat");
            if (vc != null && GameReflect.Get<bool>(vc, "isTalking")) value = "● говорит  " + value;
            TeamBuf.Add(new RowIn { Id = p.GetInstanceID().ToString(), Label = Truncate(PlayerTargets.DisplayName(p), 14), Value = value });
        }
        return TeamBuf;
    }

    // The nearest MaxEnemyRows spawned enemies, closest first: name + distance.
    private static readonly List<(float Dist, RowIn Row)> EnemySort = new();

    private static List<RowIn> EnemyRows()
    {
        if (_enemyFrame == Time.frameCount) return EnemyBuf;
        _enemyFrame = Time.frameCount;
        EnemyBuf.Clear();
        EnemySort.Clear();

        PlayerAvatar local = PlayerTargets.Local;
        EnemyDirector director = EnemyDirector.instance;
        List<EnemyParent> enemies = director != null ? GameReflect.Get<List<EnemyParent>>(director, "enemiesSpawned") : null;
        if (local == null || enemies == null) return EnemyBuf;

        foreach (EnemyParent parent in enemies)
        {
            if (parent == null || !GameReflect.Get<bool>(parent, "Spawned")) continue; // despawned: not on the map
            Enemy enemy = GameReflect.Get<Enemy>(parent, "Enemy");
            if (enemy == null) continue;
            Transform center = GameReflect.Get<Transform>(enemy, "CenterTransform");
            if (center == null) continue;

            float d = Vector3.Distance(local.transform.position, center.position);
            string name = GameReflect.Get<string>(parent, "enemyName");
            EnemySort.Add((d, new RowIn
            {
                Id = parent.GetInstanceID().ToString(),
                Label = Truncate(string.IsNullOrEmpty(name) ? "Враг" : name, 16),
                Value = $"{d:0} м",
            }));
        }

        EnemySort.Sort((a, b) => a.Dist.CompareTo(b.Dist));
        for (int i = 0; i < EnemySort.Count && i < MaxEnemyRows; i++)
            EnemyBuf.Add(EnemySort[i].Row);
        return EnemyBuf;
    }

    // --- Loot -------------------------------------------------------------------------------------

    // What is still lying on the map: a summary row (count and total value) and the MaxLootRows most
    // valuable items with their distance. Reads the same ValuableDirector.valuableList the ESP uses;
    // items that were extracted or destroyed leave the list / become null. Rebuilt once per frame.
    private static readonly List<RowIn> LootBuf = new();
    private static readonly List<(float Value, float Dist, string Name, int Id)> LootSort = new();
    private static int _lootFrame = -1;
    private static float _nextLootScan;
    private const int MaxLootRows = 5;

    private static string Money(float v) =>
        v >= 1000000f ? $"${v / 1000000f:0.0}M" : v >= 1000f ? $"${v / 1000f:0.#}K" : $"${v:0}";

    private static string LootName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "Ценность";
        string n = raw.Replace("(Clone)", "").Replace("Valuable ", "").Replace("Valuable_", "").Replace('_', ' ').Trim();
        return string.IsNullOrEmpty(n) ? "Ценность" : n;
    }

    private static List<RowIn> LootRows()
    {
        if (_lootFrame == Time.frameCount) return LootBuf;
        _lootFrame = Time.frameCount;

        // The list is scanned a few times a second: sorting hundreds of items every frame buys nothing.
        if (Time.unscaledTime < _nextLootScan && LootBuf.Count > 0) return LootBuf;
        _nextLootScan = Time.unscaledTime + 0.25f;

        LootBuf.Clear();
        LootSort.Clear();

        PlayerAvatar local = PlayerTargets.Local;
        ValuableDirector director = ValuableDirector.instance;
        List<ValuableObject> valuables = director != null ? GameReflect.Get<List<ValuableObject>>(director, "valuableList") : null;
        if (local == null || valuables == null) return LootBuf;

        float total = 0f;
        int count = 0;
        Vector3 here = local.transform.position;
        foreach (ValuableObject v in valuables)
        {
            if (v == null) continue;
            PhysGrabObject phys = GameReflect.Get<PhysGrabObject>(v, "physGrabObject");
            if (phys == null) continue;
            float value = GameReflect.Get<float>(v, "dollarValueCurrent");
            if (value <= 0f) continue;
            total += value;
            count++;
            LootSort.Add((value, Vector3.Distance(here, GameReflect.Get<Vector3>(phys, "midPoint")), LootName(v.name), v.GetInstanceID()));
        }
        if (count == 0) return LootBuf;

        LootBuf.Add(new RowIn { Id = "sum", Label = "Осталось", Value = $"{count} · {Money(total)}" });
        LootSort.Sort((a, b) => b.Value.CompareTo(a.Value));
        for (int i = 0; i < LootSort.Count && i < MaxLootRows; i++)
        {
            var it = LootSort[i];
            LootBuf.Add(new RowIn { Id = it.Id.ToString(), Label = Truncate(it.Name, 16), Value = $"{Money(it.Value)}  {it.Dist:0} м" });
        }
        return LootBuf;
    }

    // --- Run ---------------------------------------------------------------------------------------

    // The current level at a glance: level number, haul against the quota, extraction points done, time in
    // the level and how many players are alive. Everything is read-only state the game already replicates.
    // Only exists while a round is running (RoundDirector present).
    private static readonly List<RowIn> RunBuf = new();
    private static int _runFrame = -1;
    private static int _runKey;
    private static float _runStart;

    private static List<RowIn> RunRows()
    {
        if (_runFrame == Time.frameCount) return RunBuf;
        _runFrame = Time.frameCount;
        RunBuf.Clear();

        RoundDirector rd = RoundDirector.instance;
        if (rd == null) { _runKey = 0; return RunBuf; }

        int key = rd.GetInstanceID();
        if (key != _runKey) { _runKey = key; _runStart = Time.unscaledTime; }

        RunManager rm = RunManager.instance;
        if (rm != null)
            RunBuf.Add(new RowIn { Id = "level", Label = "Уровень", Value = (GameReflect.Get<int>(rm, "levelsCompleted") + 1).ToString() });

        int goal = GameReflect.Get<int>(rd, "haulGoal");
        if (goal > 0)
            RunBuf.Add(new RowIn { Id = "haul", Label = "Добыча", Value = $"{Money(GameReflect.Get<int>(rd, "currentHaul"))} / {Money(goal)}" });

        int points = GameReflect.Get<int>(rd, "extractionPoints");
        if (points > 0)
            RunBuf.Add(new RowIn { Id = "points", Label = "Точки", Value = $"{GameReflect.Get<int>(rd, "extractionPointsCompleted")} / {points}" });

        // Distance to the nearest extraction point (the same list the ESP and GPS use).
        PlayerAvatar me = PlayerTargets.Local;
        List<GameObject> pointObjects = GameReflect.Get<List<GameObject>>(rd, "extractionPointList");
        if (me != null && pointObjects != null)
        {
            float best = float.MaxValue;
            foreach (GameObject go in pointObjects)
            {
                if (go == null) continue;
                best = Mathf.Min(best, Vector3.Distance(me.transform.position, go.transform.position));
            }
            if (best < float.MaxValue)
                RunBuf.Add(new RowIn { Id = "near", Label = "До точки", Value = $"{best:0} м" });
        }

        int secs = Mathf.Max(0, (int)(Time.unscaledTime - _runStart));
        RunBuf.Add(new RowIn { Id = "time", Label = "Время", Value = $"{secs / 60}:{secs % 60:00}" });

        GameDirector director = GameDirector.instance;
        List<PlayerAvatar> players = director != null ? GameReflect.Get<List<PlayerAvatar>>(director, "PlayerList") : null;
        if (players != null && players.Count > 0)
        {
            int alive = 0, total = 0;
            foreach (PlayerAvatar p in players)
            {
                if (p == null) continue;
                total++;
                if (!GameReflect.Get<bool>(p, "deadSet")) alive++;
            }
            RunBuf.Add(new RowIn { Id = "alive", Label = "Живы", Value = $"{alive} / {total}" });
        }
        return RunBuf;
    }

    // --- Player ---------------------------------------------------------------------------------

    private static float _fps, _fpsAcc;
    private static int _fpsFrames;

    private static void DrawStats(Widget w, HudCtx cx, Color accent)
    {
        // fps is sampled four times a second: a number that changes every frame is unreadable.
        _fpsFrames++;
        _fpsAcc += Time.unscaledDeltaTime;
        if (_fpsAcc >= 0.25f) { _fps = _fpsFrames / _fpsAcc; _fpsFrames = 0; _fpsAcc = 0f; }

        RowBuf.Clear();
        PlayerAvatar local = PlayerTargets.Local;
        if (local == null)
        {
            RowBuf.Add(new RowIn { Id = "state", Label = "Статус", Value = "Не в игре" });
        }
        else
        {
            // Everything that reads PhotonNetwork is behind "local player exists" - the documented
            // premature-PhotonNetwork-touch rule (see CLAUDE.md).
            int ping = PhotonNetwork.InRoom ? PhotonNetwork.GetPing() : 0;
            Vector3 pos = local.transform.position;
            RowBuf.Add(new RowIn { Id = "name", Label = "Ник", Value = CheatState.StreamerMode.Value ? "скрыт" : Truncate(PlayerTargets.NameOf(local), 16) });
            PlayerHealth health = GameReflect.Get<PlayerHealth>(local, "playerHealth");
            if (health != null)
                RowBuf.Add(new RowIn { Id = "hp", Label = "HP", Value = $"{GameReflect.Get<int>(health, "health")} / {GameReflect.Get<int>(health, "maxHealth")}" });
            PlayerController pc = PlayerController.instance;
            if (pc != null && pc.EnergyStart > 0f)
                RowBuf.Add(new RowIn { Id = "stamina", Label = "Стамина", Value = $"{Mathf.Clamp(pc.EnergyCurrent / pc.EnergyStart * 100f, 0f, 100f):0}%" });
            RowBuf.Add(new RowIn { Id = "fps", Label = "FPS", Value = _fps.ToString("0") });
            RowBuf.Add(new RowIn { Id = "ping", Label = "Пинг", Value = ping + " мс" });
            RowBuf.Add(new RowIn { Id = "pos", Label = "XYZ", Value = $"{pos.x:0} {pos.y:0} {pos.z:0}" });
            AddHiddenRow();
        }
        DrawPillList(w, cx, accent, Glyph.Person, RowBuf, false);
    }

    // "Hidden" status for Invisible and fly auto-hide, so you can tell at a glance whether other players
    // currently see you. Two things the position spoof cannot hide are called out: talking (when the reveal
    // option is on, the real position is sent) and holding something (the grab beam and the item itself
    // stay where they really are).
    private static void AddHiddenRow()
    {
        if (GhostObserverHack.SessionGhost)
        {
            RowBuf.Add(new RowIn { Id = "hidden", Label = "Скрыт", Value = "призрак: вас нет у других" });
            return;
        }
        bool flyHide = CheatState.FlyAutoHide.Value && (CheatState.Fly.Value || CheatState.Noclip.Value);
        if (!CheatState.Invisible.Value && !flyHide) return;

        string value;
        if (!InvisiblePatch.BroadcastIsHidden) value = "НЕТ: виден";
        else if (PhysGrabber.instance != null && PhysGrabber.instance.grabbed) value = "да, но видна ноша";
        else value = "да";
        RowBuf.Add(new RowIn { Id = "hidden", Label = "Скрыт", Value = value });
    }

    // --- Media ----------------------------------------------------------------------------------

    // Real Spotify track when CheatState.HudMediaPlayerUseSpotify is on (Win32.GetSpotifyWindowTitle -
    // see that method's own comment for why WinRT's now-playing API was deliberately avoided).
    // Spotify's window title is "Spotify" while paused/idle and "Artist - Track" while something is
    // playing - no timestamp in either, so there is no real progress to show, only a state. Polled once
    // a second. With Spotify off it falls back to the manual title and a fake clock, same as before.
    private static string _spotifyStatus = "…";
    private static bool _spotifyPlaying;
    private static float _nextSpotifyPoll;
    private static float _mediaElapsed;
    private static bool _mediaPaused;
    private const float MediaFakeDuration = 208f; // 3:28

    private static void DrawMedia(Widget w, HudCtx cx, Color accent)
    {
        RowBuf.Clear();
        if (CheatState.HudMediaPlayerUseSpotify.Value)
        {
            PollSpotify();
            RowBuf.Add(new RowIn { Id = "track", Label = Truncate(_spotifyStatus, 26), Value = "" });
            RowBuf.Add(new RowIn { Id = "state", Label = "Статус", Value = _spotifyPlaying ? "Играет" : "Пауза" });
        }
        else
        {
            if (!_mediaPaused) _mediaElapsed = (_mediaElapsed + Time.unscaledDeltaTime) % MediaFakeDuration;
            string title = CheatState.HudMediaPlayerTitle.Value;
            int e = Mathf.RoundToInt(_mediaElapsed);
            RowBuf.Add(new RowIn { Id = "track", Label = Truncate(string.IsNullOrEmpty(title) ? "—" : title, 26), Value = "" });
            RowBuf.Add(new RowIn { Id = "time", Label = "Время", Value = $"{e / 60}:{e % 60:00} / {(int)MediaFakeDuration / 60}:{(int)MediaFakeDuration % 60:00}" });
        }
        RowBuf.Add(new RowIn { Id = "btn", Buttons = true });
        DrawPillList(w, cx, accent, Glyph.Note, RowBuf, false);
    }

    private const float ButtonW = 14f;

    private static float ButtonsWidth(Widget w) =>
        CheatState.HudMediaPlayerUseSpotify.Value ? ButtonW * 3f + Gap * 2f : ButtonW;

    // Real transport: OS media keys (Win32.MediaPlayPause/Next/Prev) - Spotify answers them like a
    // hardware media row. Each button is one of Rockstar's pills with a vector glyph on it.
    private static void DrawButtons(Widget w, HudCtx cx, float x, float y)
    {
        bool spotify = CheatState.HudMediaPlayerUseSpotify.Value;
        int count = spotify ? 3 : 1;
        for (int i = 0; i < count; i++)
        {
            float bx = x + i * (ButtonW + Gap);
            bool hover = cx.Hovered(bx, y, ButtonW, RowH);
            cx.Rect(bx, y, ButtonW, RowH, HudKit.Panel(hover ? 0.95f : HudKit.ValueAlpha), 2f);

            Vector2 c = new(bx + ButtonW * 0.5f, y + RowH * 0.5f);
            Action onClick;
            if (!spotify)
            {
                DrawPlayPause(cx, c, _mediaPaused);
                onClick = () => _mediaPaused = !_mediaPaused;
            }
            else if (i == 0) { DrawSkip(cx, c, false); onClick = Win32.MediaPrevTrack; }
            else if (i == 1) { DrawPlayPause(cx, c, !_spotifyPlaying); onClick = Win32.MediaPlayPause; }
            else { DrawSkip(cx, c, true); onClick = Win32.MediaNextTrack; }

            // Only with a visible cursor (menu open / edit mode). In normal play the cursor is locked at the
            // screen centre and the left button grabs things - a card placed there used to skip tracks.
            if (hover && Cursor.visible && UiInput.Down(0)) onClick();
        }
    }

    // Vector glyphs, never font characters: no dependency on which symbols the resolved OS font has.
    private static void DrawPlayPause(HudCtx cx, Vector2 c, bool showPlay)
    {
        if (showPlay)
        {
            cx.Triangle(new Vector2(c.x - 1.1f, c.y - 2.4f), new Vector2(c.x - 1.1f, c.y + 2.4f), new Vector2(c.x + 2.0f, c.y), White);
            return;
        }
        cx.Rect(c.x - 1.9f, c.y - 2.3f, 1.4f, 4.6f, White, 0.3f);
        cx.Rect(c.x + 0.5f, c.y - 2.3f, 1.4f, 4.6f, White, 0.3f);
    }

    private static void DrawSkip(HudCtx cx, Vector2 c, bool forward)
    {
        float d = forward ? 1f : -1f;
        for (int i = -1; i <= 0; i++)
        {
            float x = c.x + d * 2.1f * i + d * 1.0f;
            cx.Triangle(new Vector2(x - d * 1.3f, c.y - 2.2f), new Vector2(x - d * 1.3f, c.y + 2.2f), new Vector2(x + d * 1.5f, c.y), White);
        }
    }

    private static void PollSpotify()
    {
        if (Time.unscaledTime < _nextSpotifyPoll) return;
        _nextSpotifyPoll = Time.unscaledTime + 1f;

        string title = Win32.GetSpotifyWindowTitle();
        if (title == null) { _spotifyStatus = "Spotify не запущен"; _spotifyPlaying = false; }
        else if (title == "Spotify" || title.Length == 0) { _spotifyStatus = "Пауза"; _spotifyPlaying = false; }
        else { _spotifyStatus = title; _spotifyPlaying = true; }
    }

    // --- Target ---------------------------------------------------------------------------------

    private const float TW = 91f, TH = 27f;

    // Rockstar's TargetHud animation chain: each stage starts once the previous one is 70 % in.
    private static readonly HudKit.Tween THead = new(300f, HudKit.Soft);
    private static readonly HudKit.Tween TName = new(300f, HudKit.Soft);
    private static readonly HudKit.Tween TBar = new(300f, HudKit.Soft);
    private static readonly HudKit.Tween THp = new(300f, HudKit.Soft);
    private static readonly HudKit.Tween TItems = new(300f, HudKit.Soft);
    private static readonly HudKit.Tween TFrac = new(300f, HudKit.Pop);
    private static readonly HudKit.Tween TNum = new(300f, HudKit.InOut);

    // Kept after the target goes away so the card can fade out showing what it last showed.
    private static string _tName = "";
    private static float _tFrac;
    private static int _tHp;
    private static Color _tChip = Color.white;
    private static EspOverlay.InventorySlot[] _tSlots = Array.Empty<EspOverlay.InventorySlot>();

    // Target = whoever is picked in the shared name field (Игроки tab); while editing the HUD the local
    // player stands in so there is always something on the card to place.
    private static PlayerAvatar TargetSource(bool edit) => PlayerTargets.Picked() ?? (edit ? PlayerTargets.Local : null);

    private static void DrawTarget(Widget w, HudCtx cx, Color accent)
    {
        PlayerAvatar target = TargetSource(CheatState.HudEditMode.Value);
        if (target != null)
        {
            _tName = PlayerTargets.DisplayName(target);
            bool dead = GameReflect.Get<bool>(target, "deadSet");
            // Talking (the game's own synced isTalking) - handy when watching someone as a ghost.
            PlayerVoiceChat vc = GameReflect.Get<PlayerVoiceChat>(target, "voiceChat");
            if (vc != null && GameReflect.Get<bool>(vc, "isTalking")) _tName = "● " + _tName;
            if (dead) _tName += " (мёртв)";
            int hp = !dead && target.playerHealth != null ? GameReflect.Get<int>(target.playerHealth, "health") : 0;
            int max = target.playerHealth != null ? GameReflect.Get<int>(target.playerHealth, "maxHealth") : 100;
            _tHp = hp;
            _tFrac = Mathf.Clamp01(hp / (float)Mathf.Max(1, max));
            _tChip = CosmeticHack.GetSyncedEyeColor(target) ?? accent;
            _tSlots = EspOverlay.ResolveInventory(target);
        }

        if (target == null && _tName.Length == 0) { _tName = "Игрок"; _tHp = 100; _tFrac = 1f; _tChip = accent; }

        float baseA = cx.Alpha;
        bool full = w.Anim.Value * w.Visible.Value >= 1f;
        float head = THead.Set(full);
        float name = TName.Set(head >= 0.7f);
        float bar = TBar.Set(name >= 0.7f);
        float hpT = THp.Set(bar >= 0.7f);
        float items = TItems.Set(head >= 0.7f);
        float frac = TFrac.Set(_tFrac);
        float num = TNum.Set(_tHp);

        cx.Squircle(0f, 0f, TW, TH, 7f, 7f, HudKit.Panel(HudKit.CardAlpha));

        // Rockstar draws the player's head here; there is no portrait in this game, so it is the target's
        // synced eye colour with a robot's two eyes.
        cx.Alpha = baseA * Mathf.Clamp01(head);
        float hx = 5f * head;
        cx.Rect(hx, 5f, 17f, 17f, _tChip, 4f);
        cx.Rect(hx + 4.2f, 6.8f, 3f, 5f, new Color(1f, 1f, 1f, 0.92f), 1f);
        cx.Rect(hx + 9.8f, 6.8f, 3f, 5f, new Color(1f, 1f, 1f, 0.92f), 1f);

        string hpText = Mathf.RoundToInt(num).ToString();
        float hpW = cx.Measure(hpText, FontU);

        cx.Alpha = baseA * Mathf.Clamp01(name);
        cx.TextClipped(26f, 2f + 6f * name, TW - 37f - hpW, 7f, _tName, 7f, White, TextAnchor.MiddleLeft);

        cx.Alpha = baseA * Mathf.Clamp01(hpT);
        cx.Text(TW - 7f - 24f, 2f + 6f * hpT, 24f, 6.5f, hpText, FontU, accent, TextAnchor.MiddleRight);

        cx.Alpha = baseA * Mathf.Clamp01(bar);
        float by = TH - 5f - 6f * bar;
        cx.Rect(26f, by, 57f, 3f, new Color(1f, 1f, 1f, 0.10f), 1.5f);
        float fw = 57f * Mathf.Clamp01(frac);
        if (fw > 0.2f) cx.Rect(26f, by, fw, 3f, accent, 1.5f);

        // Item pills sit just under the card and slide out from behind it.
        cx.Alpha = baseA * Mathf.Clamp01(items);
        int shown = 0;
        foreach (EspOverlay.InventorySlot slot in _tSlots)
            if (slot.Icon != null) shown++;

        if (shown > 0)
        {
            const float pill = 11f;
            float total = shown * pill + (shown - 1) * 2f;
            float x = (TW - total) * 0.5f, y = TH - 4f + 6f * items;
            foreach (EspOverlay.InventorySlot slot in _tSlots)
            {
                if (slot.Icon == null) continue;
                cx.Rect(x, y, pill, pill, HudKit.Panel(0.55f), 1.5f);
                DrawIcon(cx.Map(x + 1f, y + 1f, pill - 2f, pill - 2f), slot.Icon, cx.Alpha);
                x += pill + 2f;
            }
        }
        cx.Alpha = baseA;

        w.WU = TW;
        w.HU = TH + (shown > 0 ? 13f * Mathf.Clamp01(items) : 0f);
    }

    // --- chips for removed widgets (edit mode only) ----------------------------------------------

    // Edit-mode-only strip listing any removed widgets so they can be added back - lives at a
    // fixed top-of-screen spot rather than wherever the widget used to be, since that position is
    // exactly what got removed.
    private static void DrawPalette()
    {
        List<Widget> disabled = null;
        foreach (Widget w in Widgets)
            if (!w.Cfg.Enabled.Value) (disabled ??= new List<Widget>()).Add(w);
        if (disabled == null) return;

        const float chipWidth = 150f, rowHeight = 30f;
        float x = 20f;
        float y = CustomGui.ScreenHeight - 20f;

        // Wraps onto more rows when they do not fit the screen width (8 widgets x 150 px ran off a narrow screen).
        int perRow = Mathf.Max(1, Mathf.FloorToInt((CustomGui.ScreenWidth - 2f * x + 8f) / chipWidth));
        int rows = (disabled.Count + perRow - 1) / perRow;
        int cols = Mathf.Min(perRow, disabled.Count);
        CustomGui.Rect(new Rect(x - 8f, y - 6f - rows * rowHeight - 4f, chipWidth * cols + 8f, rows * rowHeight + 10f),
            new Color(0f, 0f, 0f, 0.55f), 8f);

        for (int i = 0; i < disabled.Count; i++)
        {
            int col = i % perRow, row = i / perRow;
            Rect chip = new(x + col * chipWidth, y - 28f - row * rowHeight, chipWidth - 8f, 22f);
            if (CustomGui.Button(chip, "+ " + disabled[i].Title, new Color(1f, 1f, 1f, 0.08f), new Color(1f, 1f, 1f, 0.18f), Color.white, 12, 5f))
                disabled[i].Cfg.Enabled.Value = true;
        }
    }

    private static Color ParseColor(string hex)
    {
        if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out Color c)) return c;
        return Color.white;
    }

    private static string Truncate(string s, int maxChars) =>
        !string.IsNullOrEmpty(s) && s.Length > maxChars ? s[..maxChars] + "…" : s;

    // --- Item icon pool (ordinary Image components - see this file's top comment) -------------

    private static readonly List<Image> _iconPool = new();
    private static int _iconUsed;

    private static void ResetIcons() => _iconUsed = 0;

    private static void FinishIcons()
    {
        for (int i = _iconUsed; i < _iconPool.Count; i++)
            _iconPool[i].gameObject.SetActive(false);
    }

    private static void DrawIcon(Rect area, Sprite sprite, float alpha)
    {
        Image img = _iconUsed < _iconPool.Count ? _iconPool[_iconUsed] : CreateIcon();
        _iconUsed++;

        img.sprite = sprite;
        img.color = new Color(1f, 1f, 1f, alpha);
        img.gameObject.SetActive(true);
        RectTransform rt = img.rectTransform;
        rt.anchoredPosition = new Vector2(area.x, area.y);
        rt.sizeDelta = new Vector2(area.width, area.height);
    }

    private static Image CreateIcon()
    {
        var go = new GameObject("HudIcon", typeof(RectTransform));
        go.transform.SetParent(HudRoot.IconLayer, false);

        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        img.rectTransform.pivot = Vector2.zero;
        img.rectTransform.anchorMin = Vector2.zero;
        img.rectTransform.anchorMax = Vector2.zero;

        _iconPool.Add(img);
        return img;
    }
}
