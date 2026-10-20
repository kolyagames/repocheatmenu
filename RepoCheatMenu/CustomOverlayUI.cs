// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace RepoCheatMenu;

// Second, parallel menu built entirely on CustomGui (see that file's own comment for what it is
// and isn't) - RepoHax-style chrome: a left sidebar of icon+label tabs next to a scrollable
// content area, instead of the original OverlayUI's top tab bar. Runs alongside the original menu
// without touching it - bound to F6 instead of RightShift. Both menus are complete and kept in
// sync feature-for-feature (see BuildTabList for the full tab set); which one to open is up to the
// player - each menu's header has a button that closes itself and opens the other.
internal static partial class CustomOverlayUI
{
    // Second, parallel menu built entirely on CustomGui (see that file's own comment for what it is
    // and isn't). Bound to F6; the original OverlayUI stays on RightShift and each menu has a button
    // that closes itself and opens the other. Both are kept in sync feature-for-feature.
    //
    // Look and layout are Rockstar's "Modern" ClickGUI, in its GUI units (488 x 318, scaled by
    // CheatState.HudScale like the HUD cards): a 33-unit rail of group icons on the left, a 101-unit
    // list of the group's tabs, a 24-unit top band (search, buttons) and the settings area on the
    // right. Window panel, hairline separators, rounded border, open/close scale+fade, eased hover
    // states and the widgets themselves (RockGui) follow its numbers. Row helpers below keep their
    // old signatures, so every tab builder further down is unchanged.
    private static bool _visible;
    private static bool _closing;
    private static float _openT0, _closeT0;
    private static Vector2 _windowPos = new(80, 80);

    private const float WinUW = 488f, WinUH = 318f;
    private const float RailUW = 33f, ListUW = 101f, BandUH = 24f;
    private const float ContentUX = RailUW + ListUW + 11f;      // 145
    private const float ContentUW = WinUW - ContentUX - 11f;    // 332
    private const float RowsTopU = BandUH + 10.5f + 27.5f;      // 62: under the top band and the title block
    private const float CardPadU = 4f, CardGapU = 6f, RowUH = 18f;
    private const float CloseSeconds = 0.16f;

    private static float _u = 2f;   // pixels per Rockstar unit this frame
    private static Rect _win;       // window rect in CustomGui space (y up), unscaled by the open animation

    // Window-relative rectangle in units (y down from the window's top edge) -> CustomGui pixels.
    private static Rect UR(float ux, float uy, float uw, float uh) =>
        new(_win.x + ux * _u, _win.yMax - (uy + uh) * _u, uw * _u, uh * _u);

    // Card grouping: SectionHeader starts a new card, the previous one closes where it started. Heights
    // are not known until every row inside has been placed, and this renderer cannot draw "behind"
    // something already committed to the mesh - so DrawContent runs the tab's Build() callback three
    // times: once with _measuring (every row helper still advances the cursor but draws nothing) to
    // record each card's span, once for the card backgrounds, once for real, so rows always land on
    // top of their own card.
    private static bool _measuring;
    private static readonly List<(float Top, float Bottom)> _cardBounds = new();
    private static bool _cardOpen;
    private static float _cardTopY;
    private static float _contentStartY;

    private static readonly (string Label, Action Build)[] Tabs = BuildTabList();

    // The five categories of Rockstar's rail (same order and same icons), each listing the tabs it holds,
    // sorted by name like Rockstar sorts its modules.
    private static readonly (string Name, string Icon, string[] Tabs)[] Groups =
    {
        ("Бой", "combat", new[] { "Игроки", "Монстры", "Рейд", "Тролль" }),
        ("Движение", "movement", new[] { "Движение", "Камера" }),
        ("Визуал", "visuals", new[] { "HUD", "Визуал", "Косметика" }),
        ("Игрок", "player", new[] { "Голос", "Игрок", "Мир" }),
        ("Прочее", "other", new[] { "Микрофон", "Прочее", "Разработчик", "Скрипты", "Блоки" }),
    };
    private static readonly int[] _lastTabInGroup = { -1, -1, -1, -1, -1 };

    private static int TabIndex(string name)
    {
        for (int i = 0; i < Tabs.Length; i++)
            if (Tabs[i].Label == name) return i;
        return 0;
    }

    private static int _activeTab;
    private static float _scroll;
    private static float _contentHeight; // measured last frame, drives scroll clamp
    private static float _tabT0 = -10f;

    private static bool _draggingWindow;
    private static bool _scrollDrag;
    private static Vector2 _dragLast;
    private static bool _dragLastValid;
    private static string _search = "";
    private static string _searchTab = "";
    private static string _lastHeaderTab = "";
    private static int _searchHits;
    private static string _scriptDraft = "";
    private static string _scriptModuleName = "";
    private static string _scriptStatus = "";

    private static bool Searching => _search.Trim().Length > 0;

    // How many functions (rows) each tab holds, counted once per opening by running every builder with
    // _countOnly set: the row helpers then only count and lay nothing out. Shown next to the tab names and
    // in the title, so it is visible at a glance that nothing is missing.
    private static bool _countOnly;
    private static int _countCurrent;
    private static readonly int[] TabCounts = new int[16];

    private static void ComputeCounts()
    {
        _countOnly = true;
        try
        {
            for (int i = 0; i < Tabs.Length && i < TabCounts.Length; i++)
            {
                if (Tabs[i].Label == "Скрипты" || Tabs[i].Label == "Блоки") { TabCounts[i] = 0; continue; }
                _countCurrent = 0;
                try { Tabs[i].Build?.Invoke(); }
                catch (Exception e) { Plugin.Log.LogWarning("CustomOverlayUI: counting " + Tabs[i].Label + " failed: " + e.Message); }
                TabCounts[i] = _countCurrent;
            }
        }
        finally
        {
            _countOnly = false;
        }
    }

    public static bool IsOpen => _visible;

    // The HUD is drawn under the menu but reads the mouse on its own; it asks this before reacting.
    internal static bool PointerOverWindow => (_visible || _closing) && _win.Contains(CustomGui.MouseLocalPos());

    public static void Toggle()
    {
        if (_visible) Hide(); else Show();
    }

    // Cursor handling mirrors OverlayUI.Show()/Hide() exactly now that the cross-menu switch button
    // can land here as the FIRST menu opened in a session, with nothing else having touched the
    // cursor yet.
    private static bool _wasCursorVisible;
    private static CursorLockMode _wasLockState;

    // CustomGui.ScreenWidth/Height only exist AFTER HudRoot.EnsureBuilt() has created its canvas, so
    // the window centres itself the first time it is shown and never again, so a manual drag during
    // the session is not fought on the next open.
    private static bool _windowPositionInitialized;

    private static void ComputeScale()
    {
        float sh = CustomGui.ScreenHeight, sw = CustomGui.ScreenWidth;
        float u = Mathf.Max(0.5f, CheatState.HudScale.Value);
        if (sh > 40f) u = Mathf.Min(u, (sh - 16f) / WinUH);
        if (sw > 40f) u = Mathf.Min(u, (sw - 16f) / WinUW);
        _u = Mathf.Max(0.5f, u);
    }

    public static void Show()
    {
        HudRoot.EnsureBuilt();
        ComputeScale();

        if (!_windowPositionInitialized)
        {
            _windowPositionInitialized = true;
            _windowPos = new Vector2((CustomGui.ScreenWidth - WinUW * _u) * 0.5f, (CustomGui.ScreenHeight - WinUH * _u) * 0.5f);
        }

        if (!_visible)
        {
            _wasCursorVisible = Cursor.visible;
            _wasLockState = Cursor.lockState;
        }
        _visible = true;
        _closing = false;
        _openT0 = Time.unscaledTime;
        _search = "";
        ChooseLayout();
        ComputeCounts();
        Cursor.lockState = CursorLockMode.Confined;
        Cursor.visible = true;
        if (InputManager.instance != null) InputManager.instance.DisableAiming(0.5f);
    }

    public static void Hide()
    {
        if (!_visible) return;
        _closing = true;
        _closeT0 = Time.unscaledTime;
        _visible = false;
        CustomGui.ClearFocus(); // a field left focused would keep swallowing keybinds after the menu closed
        Cursor.lockState = _wasLockState;
        Cursor.visible = _wasCursorVisible;
    }

    // Called from HudRoot.Tick() inside its own shared BeginFrame/EndFrame - this menu does not own
    // its own draw cycle (see HudRoot's own comment for why two cycles in one frame do not work with
    // this mesh renderer).
    public static void DrawIfVisible()
    {
        if (!_visible && !_closing)
        {
            MenuIcons.HideAll();
            return;
        }
        if (_closing && Time.unscaledTime - _closeT0 > CloseSeconds)
        {
            _closing = false;
            MenuBlur.Hide();
            MenuIcons.HideAll();
            return;
        }
        if (_visible && InputManager.instance != null) InputManager.instance.DisableAiming(0.5f);
        DrawWindow();
    }

    private static void DrawWindow()
    {
        ComputeScale();
        ClampWindowPosition();
        if (_dropdown) ComputeDropdownWindow();
        else _win = new Rect(_windowPos.x, CustomGui.ScreenHeight - _windowPos.y - WinUH * _u, WinUW * _u, WinUH * _u);

        // Open: Rockstar scales 0.7 -> 1 over 300 ms; close: a short shrink and fade.
        float now = Time.unscaledTime;
        float scale, alpha;
        if (_closing)
        {
            float c = Mathf.Clamp01((now - _closeT0) / CloseSeconds);
            scale = 1f - 0.05f * c;
            alpha = 1f - c;
        }
        else
        {
            float t = Mathf.Clamp01((now - _openT0) / 0.3f);
            scale = 0.7f + 0.3f * HudKit.Pop(t);
            alpha = Mathf.Clamp01(t * 3f);
        }
        Vector2 pivot = _win.center;
        CustomGui.NoInput = _closing;
        CustomGui.SetTransform(pivot, scale, alpha);
        MenuIcons.Begin();

        if (_dropdown)
        {
            MenuBlur.Hide();
            DrawDropdown();
            MenuIcons.End();
            CustomGui.ClearTransform();
            CustomGui.NoInput = false;
            return;
        }

        // Backdrop: blurred game frame under a translucent panel (plain panel when there is no frame to blur).
        Rect scaledWin = new(pivot + (_win.position - pivot) * scale, _win.size * scale);
        bool blurred = CheatState.MenuBlur.Value
            && MenuBlur.Render(scaledWin, 12f * _u * scale, alpha, new Color(0.42f, 0.4f, 0.47f, 1f));
        if (!blurred) MenuBlur.Hide();
        CustomGui.Rect(_win, HudKit.Panel(blurred ? 0.9f : 0.95f), 12f * _u);
        CustomGui.RoundedRing(_win, 12f * _u, Mathf.Max(1f, 0.5f * _u), RockGui.Border);
        CustomGui.Rect(UR(RailUW - 1f, 1f, 1f, WinUH - 2f), RockGui.Border);
        CustomGui.Rect(UR(RailUW + ListUW - 1f, 1f, 1f, WinUH - 2f), RockGui.Border);
        CustomGui.Rect(UR(RailUW + ListUW, BandUH - 1f, WinUW - RailUW - ListUW - 1f, 1f), RockGui.Border);

        DrawRail();
        DrawGear();
        DrawTabList();
        Rect[] blockers = DrawBand();
        HandleDrag(blockers);

        // Settings area; fades in when the tab changes.
        float fade = HudKit.Soft(Mathf.Clamp01((now - _tabT0) / 0.22f));
        CustomGui.SetTransform(pivot, scale, alpha * Mathf.Clamp01(fade));
        DrawTitleAndContent();

        MenuIcons.End();
        CustomGui.ClearTransform();
        CustomGui.NoInput = false;
    }

    // The position is kept for the whole session, so a window dragged (or left by a resolution change)
    // past the screen edge could never be grabbed again. Keep the top band - the drag handle - on screen,
    // with at least 60 px of the window visible horizontally.
    private static void ClampWindowPosition()
    {
        float sw = CustomGui.ScreenWidth, sh = CustomGui.ScreenHeight;
        if (sw <= 0f || sh <= 0f) return;
        float w = WinUW * _u, band = BandUH * _u;
        _windowPos.x = Mathf.Clamp(_windowPos.x, 60f - w, sw - 60f);
        _windowPos.y = Mathf.Clamp(_windowPos.y, 0f, Mathf.Max(0f, sh - band));
    }

    private static void HandleDrag(Rect[] blockers)
    {
        bool overBand = CustomGui.IsHovered(UR(0f, 0f, WinUW, BandUH));
        foreach (Rect b in blockers)
            if (CustomGui.IsHovered(b)) overBand = false;

        if (overBand && UiInput.Down(0)) _draggingWindow = true;
        if (UiInput.Up(0)) _draggingWindow = false;

        // Position difference between ticks, not Input.GetAxis: the UI ticks ~33 times a second and
        // GetAxis only holds the movement of the last frame, so a drag would have lost most of it.
        Vector2 mouse = CustomGui.MouseLocalPos();
        if (_draggingWindow)
        {
            if (_dragLastValid)
            {
                _windowPos.x += mouse.x - _dragLast.x;
                _windowPos.y -= mouse.y - _dragLast.y;
            }
            _dragLast = mouse;
            _dragLastValid = true;
        }
        else
        {
            _dragLastValid = false;
        }
    }

    private static int TotalCount()
    {
        int sum = 0;
        foreach (int c in TabCounts) sum += c;
        return sum;
    }

    private static int GroupOf(int tab)
    {
        string label = Tabs[tab].Label;
        for (int g = 0; g < Groups.Length; g++)
            if (Array.IndexOf(Groups[g].Tabs, label) >= 0) return g;
        return 0;
    }

    private static void SelectTab(int index)
    {
        if (index == _activeTab && !Searching) return;
        _activeTab = index;
        _lastTabInGroup[GroupOf(index)] = index;
        _scroll = 0f;
        CustomGui.ClearFocus(); // a field on the old tab is no longer drawn, so it could never lose focus itself
        _tabT0 = Time.unscaledTime;
        _search = "";
    }

    // --- left rail: logo and one icon per group ---------------------------------------------------

    private static void DrawRail()
    {
        float u = _u;
        MenuIcons.Draw("logo", UR(RailUW * 0.5f - 5.5f, (BandUH - 11f) * 0.5f, 11f, 11f), RockGui.Accent);

        int active = GroupOf(_activeTab);
        for (int g = 0; g < Groups.Length; g++)
        {
            Rect ir = UR(8f, 27f + g * 20f, 17f, 17f);
            bool hov = CustomGui.IsHovered(ir);
            float h = RockGui.HoverAmount(("rail", g), hov);
            float sel = RockGui.OnAmount(("railsel", g), g == active && !Searching);

            if (h > 0.01f) CustomGui.Rect(ir, RockGui.White(0.05f * h), 4f * u);
            Color c = Color.Lerp(RockGui.White(0.62f), RockGui.Accent, sel);
            c.a = Mathf.Lerp(0.62f + 0.2f * h, 1f, sel);
            MenuIcons.Draw(Groups[g].Icon, new Rect(ir.center.x - 4.5f * u, ir.center.y - 4.5f * u, 9f * u, 9f * u), c);

            if (hov && UiInput.Down(0))
            {
                int target = _lastTabInGroup[g] >= 0 ? _lastTabInGroup[g] : TabIndex(Groups[g].Tabs[0]);
                SelectTab(target);
            }
        }
    }

    // --- gear -------------------------------------------------------------------------------------

    private static void DrawGear()
    {
        Rect gr = UR(8f, WinUH - 8f - 17f, 17f, 17f);
        bool hov = CustomGui.IsHovered(gr);
        float h = RockGui.HoverAmount("gear", hov);
        if (h > 0.01f) CustomGui.Rect(gr, RockGui.White(0.05f * h), 4f * _u);
        MenuIcons.Draw("setting", new Rect(gr.center.x - 4.5f * _u, gr.center.y - 4.5f * _u, 9f * _u, 9f * _u),
            RockGui.White(0.58f + 0.35f * h));
        if (hov && UiInput.Down(0)) SelectTab(TabIndex("Прочее"));
    }

    // --- second column: the active group's tabs ---------------------------------------------------

    private static void DrawTabList()
    {
        int g = GroupOf(_activeTab);
        CustomGui.Style = CustomGui.TextStyle.Medium;
        CustomGui.Text(UR(RailUW + 8f, 5f, ListUW - 10f, 14f), Groups[g].Name, RockGui.Font(9f, _u),
            RockGui.White(), TextAnchor.MiddleLeft);

        for (int k = 0; k < Groups[g].Tabs.Length; k++)
        {
            int i = TabIndex(Groups[g].Tabs[k]);
            Rect r = UR(RailUW + 4f, BandUH + 1f + k * 17f, ListUW - 9f, 15f);
            bool hov = CustomGui.IsHovered(r);
            float h = RockGui.HoverAmount(("tab", i), hov);
            float sel = RockGui.OnAmount(("tabsel", i), i == _activeTab && !Searching);

            Color baseC = new(0.102f, 0.09f, 0.122f, 0.4f);
            float mix = 0.08f * sel + 0.025f * h;
            Color bg = new(Mathf.Lerp(baseC.r, RockGui.Accent.r, mix), Mathf.Lerp(baseC.g, RockGui.Accent.g, mix),
                Mathf.Lerp(baseC.b, RockGui.Accent.b, mix), baseC.a + 0.12f * sel);
            CustomGui.Rect(r, bg, 3f * _u);

            Color tc = Color.Lerp(Color.white, RockGui.Accent, 0.5f * sel);
            tc.a = 0.6f + 0.3f * sel + 0.1f * h;
            string count = i < TabCounts.Length && TabCounts[i] > 0 ? TabCounts[i].ToString() : "";
            float countW = count.Length == 0 ? 0f : CustomGui.MeasureText(count, RockGui.Font(6f, _u)) + 6f * _u;
            CustomGui.TextClipped(new Rect(r.x + 5f * _u, r.y, r.width - 8f * _u - countW, r.height), Tabs[i].Label,
                RockGui.Font(7f, _u), tc, TextAnchor.MiddleLeft);
            if (count.Length > 0)
                CustomGui.Text(new Rect(r.xMax - countW - 2f * _u, r.y, countW, r.height), count, RockGui.Font(6f, _u),
                    RockGui.White(0.28f + 0.3f * sel), TextAnchor.MiddleRight);

            if (hov && UiInput.Down(0)) SelectTab(i);
        }

        CustomGui.Style = CustomGui.TextStyle.Regular;
        CustomGui.Text(UR(RailUW + 8f, WinUH - 14f, ListUW - 10f, 10f), "v" + Plugin.Version + " · всего функций: " + TotalCount(),
            RockGui.Font(6f, _u), RockGui.White(0.3f), TextAnchor.MiddleLeft);
    }

    // --- top band: search and the two header buttons ---------------------------------------------

    private static Rect[] DrawBand()
    {
        float u = _u;
        Rect search = UR(RailUW + ListUW + 7f, 6f, 81f, 12f);
        bool sh = CustomGui.IsHovered(search);
        float hv = RockGui.HoverAmount("search", sh || CustomGui.IsFocused("menu_search"));
        CustomGui.Rect(search, Color.Lerp(HudKit.Panel(0.68f), Color.white, 0.035f + 0.035f * hv), 3f * u);

        MenuIcons.Draw("search", new Rect(search.x + 3.5f * u, search.y + 3.5f * u, 5f * u, 5f * u), RockGui.White(0.48f));

        Rect field = new(search.x + 8f * u, search.y, search.width - 9f * u, search.height);
        bool focused = CustomGui.IsFocused("menu_search");
        string before = _search;
        _search = CustomGui.TextField(field, "menu_search", _search, Color.clear, RockGui.White(0.85f), Color.clear);
        if (_search != before) _scroll = 0f;
        if (_search.Length == 0 && !focused)
            CustomGui.Text(new Rect(field.x + 8f, field.y, field.width, field.height), "Поиск", RockGui.Font(7f, u),
                RockGui.White(0.48f), TextAnchor.MiddleLeft);

        // Header buttons, right-aligned: panic switch and the other menu.
        const string panicText = "ВЫКЛ ( ] )", switchText = "Старое меню";
        float pw = CustomGui.MeasureText(panicText, RockGui.Font(6.5f, u)) / u + 10f;
        float sw = CustomGui.MeasureText(switchText, RockGui.Font(6.5f, u)) / u + 10f;
        Rect switchBtn = UR(WinUW - 7f - sw, 6f, sw, 12f);
        Rect panicBtn = UR(WinUW - 7f - sw - 4f - pw, 6f, pw, 12f);

        // Kill switch - same PanicHack.Deactivate() the old menu's "ВЫКЛ ( ] )" button wires to, so a
        // player who only ever opens this menu can still switch everything off.
        if (RockGui.Button("panic", panicBtn, panicText, RockGui.Kind.Danger, u, 6.5f))
            PanicHack.Deactivate();
        if (RockGui.Button("switch", switchBtn, switchText, RockGui.Kind.Primary, u, 6.5f))
        {
            Hide();
            OverlayUI.Open();
        }

        return new[] { search, switchBtn, panicBtn };
    }

    // --- settings area ----------------------------------------------------------------------------

    private static float _rowCursorY;
    private static Rect _contentClip;

    private static void DrawTitleAndContent()
    {
        string title = Searching ? "Поиск" : Tabs[_activeTab].Label;
        string desc = Searching ? "«" + _search.Trim() + "»"
            : Groups[GroupOf(_activeTab)].Name + (_activeTab < TabCounts.Length && TabCounts[_activeTab] > 0 ? " · функций: " + TabCounts[_activeTab] : "");
        CustomGui.Style = CustomGui.TextStyle.Medium;
        CustomGui.Text(UR(ContentUX, BandUH + 10.5f, ContentUW, 13f), title, RockGui.Font(12f, _u), RockGui.White(), TextAnchor.MiddleLeft);
        CustomGui.Style = CustomGui.TextStyle.Regular;
        CustomGui.Text(UR(ContentUX, BandUH + 10.5f + 12.5f, ContentUW, 9f), desc, RockGui.Font(7f, _u), RockGui.White(0.58f), TextAnchor.MiddleLeft);

        DrawContent(UR(ContentUX, RowsTopU, ContentUW, WinUH - RowsTopU - 4f));
    }

    private static void BuildCurrent()
    {
        if (!Searching)
        {
            Tabs[_activeTab].Build?.Invoke();
            return;
        }

        _searchHits = 0;
        _lastHeaderTab = "";
        for (int i = 0; i < Tabs.Length; i++)
        {
            if (Tabs[i].Label == "Скрипты" || Tabs[i].Label == "Блоки") continue; // free-form editor, nothing row-shaped to search
            _searchTab = Tabs[i].Label;
            Tabs[i].Build?.Invoke();
        }
        if (_searchHits == 0)
            LiveInfoCore("Ничего не найдено.");
    }

    private static void DrawContent(Rect contentRect)
    {
        _contentClip = contentRect;
        float u = _u;

        float wheel = UiInput.Wheel();
        if (wheel != 0f && CustomGui.IsHovered(UR(RailUW + ListUW, BandUH, WinUW - RailUW - ListUW, WinUH - BandUH)))
            _scroll = Mathf.Clamp(_scroll - wheel * 12f * u, 0f, Mathf.Max(0f, _contentHeight - contentRect.height));

        float startY = contentRect.yMax + _scroll;
        _contentStartY = startY;

        // Pass 1: measure.
        _measuring = true;
        _cardBounds.Clear();
        _cardOpen = false;
        _rowCursorY = startY;
        BuildCurrent();
        CloseCard();
        _contentHeight = startY - _rowCursorY + 4f * u;
        _measuring = false;
        _scroll = Mathf.Clamp(_scroll, 0f, Mathf.Max(0f, _contentHeight - contentRect.height));

        // Pass 2: card backgrounds, clipped to the visible area.
        Color cardColor = new(26f / 255f, 23f / 255f, 31f / 255f, 0.4f);
        foreach ((float top, float bottom) in _cardBounds)
        {
            float yMax = Mathf.Min(top, contentRect.yMax), yMin = Mathf.Max(bottom, contentRect.yMin);
            if (yMax - yMin < 1f) continue;
            CustomGui.Rect(new Rect(contentRect.x, yMin, contentRect.width, yMax - yMin), cardColor, 7f * u);
        }

        // Pass 3: the real draw.
        _cardOpen = false;
        _rowCursorY = startY;
        BuildCurrent();
        CloseCard();

        // Thin scrollbar, only when there is something to scroll. The thumb can be dragged, so scrolling
        // still works if the mouse wheel does not reach the menu.
        float overflow = _contentHeight - contentRect.height;
        if (overflow > 1f)
        {
            Rect track = new(contentRect.xMax + 4f * u, contentRect.y, 2f * u, contentRect.height);
            float thumbH = Mathf.Max(14f * u, track.height * contentRect.height / _contentHeight);
            float t01 = Mathf.Clamp01(_scroll / overflow);
            Rect thumb = new(track.x, track.yMax - thumbH - (track.height - thumbH) * t01, track.width, thumbH);
            Rect hit = new(track.x - 4f * u, track.y, track.width + 8f * u, track.height);

            if (UiInput.Down(0) && CustomGui.IsHovered(hit)) _scrollDrag = true;
            if (!UiInput.Held(0)) _scrollDrag = false;
            if (_scrollDrag)
            {
                float travel = Mathf.Max(1f, track.height - thumbH);
                float frac = Mathf.Clamp01((track.yMax - thumbH * 0.5f - CustomGui.MouseLocalPos().y) / travel);
                _scroll = frac * overflow;
            }

            bool lit = _scrollDrag || CustomGui.IsHovered(hit);
            CustomGui.Rect(track, RockGui.White(0.05f), u);
            CustomGui.Rect(thumb, RockGui.White(lit ? 0.5f : 0.3f), u);
        }
    }

    private static void OpenCard()
    {
        if (_cardOpen) return;
        _cardOpen = true;
        _cardTopY = _rowCursorY;
        _rowCursorY -= CardPadU * _u;
    }

    private static void CloseCard()
    {
        if (!_cardOpen) return;
        _rowCursorY -= CardPadU * _u;
        if (_measuring) _cardBounds.Add((_cardTopY, _rowCursorY));
        _cardOpen = false;
    }

    // Row helpers - mirror the old OverlayUI's AddToggleRow/AddButtonRow/etc. shape so migrating a
    // tab is a mechanical pass. Each one advances _rowCursorY itself and skips drawing (but still
    // advances the cursor) while measuring or when the row is not fully inside the settings area -
    // our own substitute for clipping, since CustomGui has no scissor rect.
    private static Rect NextRow(float heightUnits = RowUH)
    {
        OpenCard();
        float h = heightUnits * _u;
        Rect r = new(_contentClip.x + CardPadU * _u, _rowCursorY - h, _contentClip.width - CardPadU * 2f * _u, h);
        _rowCursorY -= h;
        return r;
    }

    private static bool RowVisible(Rect r) =>
        !_measuring && r.yMin >= _contentClip.yMin - 0.5f && r.yMax <= _contentClip.yMax + 0.5f;

    // Search filter: while a query is typed every tab's builder runs and only rows whose label
    // contains it are laid out (preceded by the tab's name the first time one matches).
    private static bool Pass(string label)
    {
        if (_countOnly)
        {
            _countCurrent++;
            return false;
        }
        if (!Searching) return true;
        if (string.IsNullOrEmpty(label) || label.IndexOf(_search.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        _searchHits++;
        if (_lastHeaderTab != _searchTab)
        {
            _lastHeaderTab = _searchTab;
            SectionHeaderCore(_searchTab);
        }
        return true;
    }

    internal static void Toggle(string label, ConfigEntry<bool> entry)
    {
        if (_capture != null) { Capture(RowKind.Toggle, label, () => Toggle(label, entry), entry, null); return; }
        if (!Pass(label)) return;
        Rect r = NextRow();
        if (!RowVisible(r)) return;
        entry.Value = RockGui.Toggle(entry, r, label, entry.Value, _u, KeybindManager.Find(entry));
        RockGui.Hairline(r, _u);
    }

    // Same as Toggle, but fires a callback on change - for the handful of toggles that need to do
    // something at the moment of flipping (PacketTestHack.OnToggled).
    internal static void Toggle(string label, ConfigEntry<bool> entry, Action<bool> onChanged)
    {
        if (_capture != null)
        {
            // The dropdown flips toggles itself; give it the callback too, or flipping there skipped it.
            Capture(RowKind.Toggle, label, () => Toggle(label, entry, onChanged), entry, null,
                () => { entry.Value = !entry.Value; onChanged?.Invoke(entry.Value); });
            return;
        }
        bool before = entry.Value;
        Toggle(label, entry);
        if (entry.Value != before) onChanged?.Invoke(entry.Value);
    }

    // Target name field - the shared CheatState.FakeSoundTargetName every target-based action reads
    // (PlayerTargets.Picked()), typed directly instead of cycled through a player list.
    internal static void PlayerPicker(string label)
    {
        if (_capture != null) { Capture(RowKind.Setting, label, () => PlayerPicker(label), null, null); return; }
        if (!Pass(label)) return;
        Rect r = NextRow();
        if (!RowVisible(r)) return;

        // Same job as the old menu's cycle button: step through whoever is in the lobby. The name is also
        // editable by hand, which still works for someone who has left or is not loaded.
        float u = _u;
        var entry = CheatState.FakeSoundTargetName;
        float pillW = 26f * u, boxW = Mathf.Min(96f * u, r.width * 0.4f);
        Rect pill = new(r.xMax - pillW - 6f * u, r.y + (r.height - 12f * u) * 0.5f, pillW, 12f * u);
        Rect box = new(pill.x - 4f * u - boxW, pill.y, boxW, 12f * u);

        RockGui.Label(r, label, u, 0.85f, boxW + pillW + 14f * u);
        entry.Value = RockGui.TextBox("target_name", box, entry.Value, u);
        if (RockGui.Button("target_next", pill, "След.", RockGui.Kind.Primary, u))
        {
            List<string> names = FakeSoundHack.ScanPlayerNames();
            entry.Value = names.Count == 0 ? "None" : names[(names.IndexOf(entry.Value) + 1) % names.Count];
        }
        RockGui.Hairline(r, u);
    }

    internal static void TextFieldRow(string label, string fieldId, ConfigEntry<string> entry)
    {
        if (_capture != null) { Capture(RowKind.Setting, label, () => TextFieldRow(label, fieldId, entry), null, null); return; }
        if (!Pass(label)) return;
        Rect r = NextRow();
        if (!RowVisible(r)) return;
        entry.Value = RockGui.TextRow(fieldId, r, label, entry.Value, _u);
        RockGui.Hairline(r, _u);
    }

    internal static void Button(string label, string btnText, Action action)
    {
        if (_capture != null) { Capture(RowKind.Button, label, () => Button(label, btnText, action), null, action); return; }
        if (!Pass(label)) return;
        Rect r = NextRow();
        if (!RowVisible(r)) return;
        if (RockGui.ButtonRow(label + "|" + btnText, r, label, btnText, _u, KeybindManager.Find(action)))
            action?.Invoke();
        RockGui.Hairline(r, _u);
    }

    internal static void FloatStepper(string label, ConfigEntry<float> entry, float min, float max, float step)
    {
        if (_capture != null) { Capture(RowKind.Setting, label, () => FloatStepper(label, entry, min, max, step), null, null); return; }
        if (!Pass(label)) return;
        Rect r = NextRow(24f);
        if (!RowVisible(r)) return;
        entry.Value = RockGui.Slider(entry, r, label, entry.Value, min, max, step, entry.Value.ToString("0.##"), _u);
        RockGui.Hairline(r, _u);
    }

    internal static void IntStepper(string label, ConfigEntry<int> entry, int min, int max, int step)
    {
        if (_capture != null) { Capture(RowKind.Setting, label, () => IntStepper(label, entry, min, max, step), null, null); return; }
        if (!Pass(label)) return;
        Rect r = NextRow(24f);
        if (!RowVisible(r)) return;
        entry.Value = Mathf.RoundToInt(RockGui.Slider(entry, r, label, entry.Value, min, max, step, entry.Value.ToString(), _u));
        RockGui.Hairline(r, _u);
    }

    // Live info - computed fresh from the callback every draw, word-wrapped to the card.
    internal static void LiveInfo(Func<string> getText)
    {
        if (_capture != null) return;
        if (Searching || _countOnly) return;
        LiveInfoCore(getText?.Invoke() ?? "");
    }

    private static void LiveInfoCore(string text)
    {
        int font = RockGui.Font(7f, _u);
        float width = _contentClip.width - (CardPadU * 2f + 12f) * _u;
        List<string> lines = WrapLines(text, font, width);
        float lineU = 9f;

        Rect r = NextRow(lines.Count * lineU + 4f);
        if (!RowVisible(r)) return;
        for (int i = 0; i < lines.Count; i++)
        {
            Rect line = new(r.x + 6f * _u, r.yMax - (2f + (i + 1) * lineU) * _u, r.width - 12f * _u, lineU * _u);
            CustomGui.Text(line, lines[i], font, RockGui.White(0.55f), TextAnchor.MiddleLeft);
        }
    }

    private static List<string> WrapLines(string text, int font, float width)
    {
        var result = new List<string>();
        foreach (string paragraph in text.Split('\n'))
        {
            string current = "";
            foreach (string word in paragraph.Split(' '))
            {
                string candidate = current.Length == 0 ? word : current + " " + word;
                if (current.Length > 0 && CustomGui.MeasureText(candidate, font) > width)
                {
                    result.Add(current);
                    current = word;
                }
                else
                {
                    current = candidate;
                }
            }
            result.Add(current);
        }
        return result;
    }

    internal static void Cycle(string label, ConfigEntry<string> entry, string[] options)
    {
        if (_capture != null) { Capture(RowKind.Setting, label, () => Cycle(label, entry, options), null, null); return; }
        if (!Pass(label) || options == null || options.Length == 0) return;
        Rect r = NextRow();
        if (!RowVisible(r)) return;
        // Only a click writes. This used to write options[0] back every frame whenever the saved value was
        // not in the list (a list that is still empty or changed, a value typed into the .cfg), so merely
        // looking at the tab destroyed the setting.
        int idx = Array.IndexOf(options, entry.Value);
        string shown = idx >= 0 ? options[idx] : string.IsNullOrEmpty(entry.Value) ? options[0] : entry.Value;
        if (RockGui.CycleRow(entry, r, label, shown, _u))
            entry.Value = options[(idx + 1) % options.Length];
        RockGui.Hairline(r, _u);
    }

    internal static void SectionHeader(string label)
    {
        if (_capture != null) return;
        if (Searching || _countOnly) return;
        SectionHeaderCore(label);
    }

    private static void SectionHeaderCore(string label)
    {
        CloseCard();
        if (_rowCursorY < _contentStartY - 0.5f) _rowCursorY -= CardGapU * _u;

        float h = 11f * _u;
        Rect r = new(_contentClip.x + 2f * _u, _rowCursorY - h, _contentClip.width - 4f * _u, h);
        _rowCursorY -= h + 1.5f * _u;
        if (RowVisible(r))
            CustomGui.TextClipped(r, label, RockGui.Font(7f, _u), RockGui.White(0.5f), TextAnchor.MiddleLeft);
        OpenCard();
    }

    // A fixed palette of named presets on a string ConfigEntry, cycled on click - same fixed-palette
    // approach CosmeticHack's eye colors settled on.
    private static readonly string[] ColorPresetNames = { "#FF3B30", "#FF9500", "#FFCC00", "#34C759", "#0A84FF", "#AF52DE", "#FFFFFF", "#00FFFF" };

    // Per-row scan caches for CycleAndAction - see that method's own comment for why these are not
    // rescanned every frame.
    private static List<string> _itemSpawnerNamesCache;
    private static List<string> _levelLoaderNamesCache;
    private static List<string> _moduleLoaderNamesCache;
    private static List<string> _stealItemNamesCache;
    private static List<string> _valuableNamesCache;

    internal static void ColorCycle(string label, ConfigEntry<string> entry)
    {
        if (_capture != null) { Capture(RowKind.Setting, label, () => ColorCycle(label, entry), null, null); return; }
        if (!Pass(label)) return;
        Rect r = NextRow();
        if (!RowVisible(r)) return;
        // Same as Cycle: a colour typed into the .cfg (not one of the presets) is shown and kept until clicked.
        int idx = Array.IndexOf(ColorPresetNames, entry.Value);
        string shown = idx >= 0 ? ColorPresetNames[idx] : entry.Value;
        Color swatch = ColorUtility.TryParseHtmlString(shown, out Color parsed) ? parsed : Color.white;
        if (RockGui.CycleRow(entry, r, label, shown, _u, swatch))
            entry.Value = ColorPresetNames[(idx + 1) % ColorPresetNames.Length];
        RockGui.Hairline(r, _u);
    }

    // "Cycle a scanned name, then act on it" rows (item spawner / level loader / module loader)
    // rescan on every click, not every frame - the source lists (StatsManager.itemDictionary,
    // RunManager.instance.levels, ...) are cheap but not free, and this is an immediate-mode UI
    // redrawing the whole tab every tick. `cache` is the caller's own static field (one per row) so
    // the scanned list survives across frames until the next click.
    internal static void CycleAndAction(string label, ConfigEntry<string> nameEntry, ref List<string> cache,
        Func<List<string>> scan, string actionLabel, Action action)
    {
        if (_capture != null) { Capture(RowKind.Setting, label, () => { var c = CacheFor(label); CycleAndAction(label, nameEntry, ref c, scan, actionLabel, action); StoreCache(label, c); }, null, null); return; }
        cache ??= scan();
        if (cache.Count > 0 && !cache.Contains(nameEntry.Value))
            nameEntry.Value = cache[0];
        else if (cache.Count == 0 && string.IsNullOrEmpty(nameEntry.Value))
            nameEntry.Value = "None";

        if (!Pass(label)) return;
        Rect r = NextRow();
        if (!RowVisible(r)) return;

        string display = cache.Count > 0 ? nameEntry.Value : "None";
        float u = _u;
        float actionW = Mathf.Clamp(CustomGui.MeasureText(actionLabel, RockGui.Font(7f, u)) + 14f * u, 36f * u, 70f * u);
        float cycleW = Mathf.Clamp(CustomGui.MeasureText(display, RockGui.Font(7f, u)) + 14f * u, 50f * u, 110f * u);
        float y = r.y + (r.height - 11f * u) * 0.5f;
        Rect actionBtn = new(r.xMax - actionW - 6f * u, y, actionW, 11f * u);
        Rect cycleBtn = new(actionBtn.x - 4f * u - cycleW, y, cycleW, 11f * u);

        RockGui.Label(r, label, u, 0.85f, actionW + cycleW + 14f * u);
        if (RockGui.Button(("cyc", label), cycleBtn, display, RockGui.Kind.Normal, u))
        {
            cache = scan();
            nameEntry.Value = cache.Count == 0 ? "None" : cache[(cache.IndexOf(nameEntry.Value) + 1) % cache.Count];
        }
        if (RockGui.Button(("act", label), actionBtn, actionLabel, RockGui.Kind.Primary, u))
            action?.Invoke();
        RockGui.Hairline(r, u);
    }

    // Same rescan-on-click cache behaviour as CycleAndAction, minus the action button - for a scanned
    // name that just feeds a ticking hack's own Apply() (ItemStealHack.StealHeldItemName) rather than
    // needing a separate "do it now" click.
    internal static void CycleWithScan(string label, ConfigEntry<string> nameEntry, ref List<string> cache, Func<List<string>> scan)
    {
        if (_capture != null) { Capture(RowKind.Setting, label, () => { var c = CacheFor(label); CycleWithScan(label, nameEntry, ref c, scan); StoreCache(label, c); }, null, null); return; }
        cache ??= scan();
        if (cache.Count > 0 && !cache.Contains(nameEntry.Value))
            nameEntry.Value = cache[0];
        else if (cache.Count == 0 && string.IsNullOrEmpty(nameEntry.Value))
            nameEntry.Value = "None";

        if (!Pass(label)) return;
        Rect r = NextRow();
        if (!RowVisible(r)) return;

        string display = cache.Count > 0 ? nameEntry.Value : "None";
        if (RockGui.CycleRow(("scan", label), r, label, display, _u))
        {
            cache = scan();
            nameEntry.Value = cache.Count == 0 ? "None" : cache[(cache.IndexOf(nameEntry.Value) + 1) % cache.Count];
        }
        RockGui.Hairline(r, _u);
    }

    // Tab list - each entry is (sidebar label, "build this tab's rows" callback), same shape as
    // the old menu's AddTab(name, () => {...}). All 13 tabs are migrated (matches the old menu's
    // 12 plus this one's own HUD tab) plus this session's own new Camera tab (14).
    private static (string, Action)[] BuildTabList() => new (string, Action)[]
    {
        ("Игрок", BuildPlayerTab),
        ("Движение", BuildMovementTab),
        ("Камера", BuildCameraTab),
        ("Визуал", BuildVisionTab),
        ("Мир", BuildWorldTab),
        ("Игроки", BuildPlayersTab),
        ("Рейд", BuildRaidTab),
        ("Монстры", BuildMonstersTab),
        ("Косметика", BuildCosmeticTab),
        ("Голос", BuildVoiceTab),
        ("Прочее", BuildMiscTab),
        ("Микрофон", BuildMicTab),
        ("Разработчик", BuildDevTab),
        ("Скрипты", BuildScriptsTab),
        ("Блоки", BuildBlocksTab),
        ("Тролль", BuildTrollTab),
        ("HUD", BuildHudTab),
    };

    // New tab, split out of "Визуал" - groups everything that moves the render camera itself
    // (as opposed to general vision cheats like FOV/flashlight/draw distance next door) so the
    // smoothing control sits right next to what it's smoothing instead of scattered across a
    // long tab.
    private static void BuildCameraTab()
    {
        SectionHeader("Вид от третьего лица");
        Toggle("Включено", CheatState.ThirdPerson);
        FloatStepper("Дистанция", CheatState.ThirdPersonDistance, 0.5f, 8f, 0.5f);
        FloatStepper("Высота", CheatState.ThirdPersonHeight, -2f, 2f, 0.25f);
        FloatStepper("Смещение вбок", CheatState.ThirdPersonHorizontal, -2f, 2f, 0.25f);
        FloatStepper("Плавность (меньше = плавнее/с задержкой)", CheatState.ThirdPersonSmoothness, 1f, 30f, 1f);
        FloatStepper("Масштаб своей модели (косметика)", CheatState.ThirdPersonModelScale, 0.1f, 3f, 0.1f);

        SectionHeader("Скриншоты и фото-режим");
        Button("Скриншот без интерфейса (папка Изображения\\REPO)", "Снять", PhotoModeHack.TakeScreenshot);
        Toggle("Фото-режим: скрыть весь интерфейс (кроме открытого меню)", CheatState.PhotoMode);
        Toggle("Режим стримера: скрыть ники других игроков в HUD, ESP, пингах, оповещениях", CheatState.StreamerMode);
        LiveInfo(() => string.IsNullOrEmpty(PhotoModeHack.LastShot) ? "Повесьте «Снять» и фото-режим на клавиши средним кликом." : "Последний: " + PhotoModeHack.LastShot);
        LiveInfo(() => "Камера плавно тянется к цели вместо мгновенного прыжка - тот же " +
            "\"смуз-камера\" приём, что в визуальных клиентах Minecraft. Заодно убирает дёрганье " +
            "на большой дистанции у стен: SphereCast-проверка стен пересчитывается каждый кадр и " +
            "может дёргаться между \"чисто\"/\"упёрлось\" от долей градуса поворота - плавность " +
            "гасит именно этот скачок.");
    }

    private static void BuildHudTab()
    {
        Cycle("Режим меню RightShift: старый, новый (Rockstar) или выпадающий", CheatState.MenuStyle, MenuRouter.Modes);
        Toggle("HUD включён (виджеты поверх игры, отдельно от меню)", CheatState.HudEnabled);
        Toggle("Режим редактирования (перетаскивание/цвет/вкл-выкл)", CheatState.HudEditMode);
        Toggle("Авто-колонка (виджеты стакаются вплотную)", CheatState.HudAutoStack);
        FloatStepper("Размер карточек (px на единицу Rockstar)", CheatState.HudScale, 1f, 4f, 0.25f);
        Toggle("Размытие фона за окном меню (как в Rockstar)", CheatState.MenuBlur);
        LiveInfo(() => "В режиме редактирования: тащите виджет мышью, правый клик убирает его, " +
            "средний клик меняет акцентный цвет. Убранные виджеты возвращаются кнопками сверху экрана.");

        SectionHeader("Виджет «Игрок» (ник, fps, пинг, координаты)");
        Toggle("Показывать", CheatState.HudPlayerStats.Enabled);
        ColorCycle("  -> Цвет", CheatState.HudPlayerStats.Color);

        SectionHeader("Виджет «Цель» (ник, HP, дистанция, иконки предметов)");
        Toggle("Показывать", CheatState.HudTargetInfo.Enabled);
        ColorCycle("  -> Цвет", CheatState.HudTargetInfo.Color);
        LiveInfo(() => "Цель берётся из общего поля имени на вкладке «Игроки».");

        SectionHeader("Виджет «Плеер»");
        Toggle("Показывать", CheatState.HudMediaPlayer.Enabled);
        ColorCycle("  -> Цвет", CheatState.HudMediaPlayer.Color);
        Toggle("  -> Реальный трек из Spotify (по заголовку окна)", CheatState.HudMediaPlayerUseSpotify);
        TextFieldRow("  -> Название трека (если Spotify выключен)", "hud_media_title", CheatState.HudMediaPlayerTitle);
        Toggle("Главный экран Rockstar (часы и круглые кнопки поверх меню игры)", CheatState.MainScreen);
        Toggle("  -> Сразу показывать кнопки (без часов)", CheatState.MainScreenStartOpen);
        Toggle("Dynamic Island (остров сверху)", CheatState.HudIsland);
        Toggle("  -> Уведомления о включении функций", CheatState.HudIslandNotifications);
        Toggle("  -> GPS: расстояние и стрелка", CheatState.HudIslandExtraction);
        Toggle("  -> Музыка в острове", CheatState.HudIslandMusic);
        Toggle("  -> Текст песни в острове", CheatState.HudIslandLyrics);
        Toggle("  -> Карточка выбранного игрока", CheatState.HudIslandTarget);
        TextFieldRow("  -> Свой текст в острове (фразы через |)", "hud_island_text", CheatState.HudIslandText);
        Toggle("  -> Часы и пинг по бокам", CheatState.HudIslandSides);
        Toggle("  -> Размытие фона под островом", CheatState.HudIslandBlur);
        FloatStepper("  -> Размер острова", CheatState.HudIslandScale, 1f, 4f, 0.25f);
        Button("GPS: следующая цель (эвакуация / грузовик / метка / выкл)", "Дальше", Gps.NextMode);
        Button("GPS: поставить метку здесь", "Метка", Gps.SetMarkHere);
        Toggle("Текст песни в главном меню", CheatState.HudLyrics);
        Toggle("  -> Показывать и в игре", CheatState.HudLyricsInGame);
        Toggle("  -> Искать текст в интернете (lrclib.net)", CheatState.HudLyricsOnline);
        FloatStepper("  -> Сдвиг текста, сек", CheatState.HudLyricsOffset, -30f, 30f, 0.5f);

        SectionHeader("Виджет «Бинды» (реальные привязанные клавиши)");
        Toggle("Показывать", CheatState.HudKeybinds.Enabled);
        ColorCycle("  -> Цвет", CheatState.HudKeybinds.Color);
        LiveInfo(() => "Выключен по умолчанию - список тех же биндов, что заданы средним кликом по строкам меню.");

        SectionHeader("Виджет «Команда» (ник, HP и дистанция каждого тиммейта)");
        Toggle("Показывать", CheatState.HudTeam.Enabled);
        ColorCycle("  -> Цвет", CheatState.HudTeam.Color);

        SectionHeader("Виджет «Враги» (6 ближайших монстров и расстояние до них)");
        Toggle("Показывать", CheatState.HudEnemies.Enabled);
        ColorCycle("  -> Цвет", CheatState.HudEnemies.Color);

        SectionHeader("Виджет «Лут» (сколько ценностей осталось на карте и 5 самых дорогих)");
        Toggle("Показывать", CheatState.HudLoot.Enabled);
        ColorCycle("  -> Цвет", CheatState.HudLoot.Color);

        SectionHeader("Виджет «Забег» (уровень, добыча, точки эвакуации, время, кто жив)");
        Toggle("Показывать", CheatState.HudRun.Enabled);
        ColorCycle("  -> Цвет", CheatState.HudRun.Color);
    }

    private static void BuildVisionTab()
    {
        Cycle("Стиль подписей ESP: Rockstar (плашки) или классика", CheatState.EspStyle, new[] { "Rockstar", "Классика" });
        Toggle("ESP ловушек (плашка на каждой ловушке уровня; только в стиле Rockstar)", CheatState.EspTraps);
        Toggle("Маркеры над тиммейтами (зелёный кристалл над головой, как у Rockstar)", CheatState.FriendMarkers);
        Toggle("Боксы ESP (3D-рамка вокруг игроков, врагов и ценностей)", CheatState.EspBoxes);
        Cycle("  -> Режим боксов", CheatState.EspBoxMode, new[] { "Контур + заливка", "Контур", "Заливка" });
        Toggle("Стрелки ESP (стрелка по краю прицела к игрокам и врагам вне экрана)", CheatState.EspArrows);
        Toggle("Стрелки и для тех, кто на экране", CheatState.EspArrowsOnScreen);
        Toggle("ESP игроков (видно сквозь стены)", CheatState.EspPlayers);
        Toggle("  -> Экипировка тиммейтов", CheatState.EspLoadout);
        Toggle("ESP врагов (видно сквозь стены)", CheatState.EspEnemies);
        Toggle("ESP ценностей (цена и позиция сквозь стены)", CheatState.EspValuables);
        Toggle("ESP точек эвакуации", CheatState.EspExtractionPoints);
        IntStepper("Макс. дистанция ESP (м)", CheatState.EspMaxDistance, 5, 200, 5);
        Toggle("Режим силуэта (заливка цветом сквозь стены)", CheatState.EspShaderMode);
        ColorCycle("Цвет ESP", CheatState.EspColor);
        Toggle("Радужный режим ESP", CheatState.EspRainbow);
        Toggle("Улучшенное зрение", CheatState.BetterVision);
        IntStepper("Угол обзора (FOV)", CheatState.Fov, 60, 140, 5);
        SectionHeader("Эффекты Rockstar");
        Toggle("Круги от прыжков (кольцо на полу там, где игрок оттолкнулся)", CheatState.JumpCircles);
        IntStepper("  -> Радиус кругов (см)", CheatState.JumpCircleRadius, 50, 250, 25);
        Toggle("Эффекты убийства (частицы или молния при смерти врага)", CheatState.KillFx);
        Cycle("  -> Режим", CheatState.KillFxMode, new[] { "Частицы", "Молния" });
        Cycle("  -> Физика частиц", CheatState.KillFxPhysics, new[] { "Разлёт", "Гравитация" });
        Toggle("  -> Ещё и при смерти игроков", CheatState.KillFxPlayers);
        Toggle("Метка цели (светящееся кольцо вокруг выбранного игрока)", CheatState.TargetFx);
        Cycle("  -> Режим", CheatState.TargetFxMode, new[] { "Души", "Кристаллы", "Круг", "Желе" });
        Toggle("  -> Если игрок не выбран - ближайший враг", CheatState.TargetFxEnemies);
        Toggle("Цвет эффектов = цвет темы", CheatState.FxUseTheme);
        ColorCycle("  -> Свой цвет", CheatState.FxColor);
        Toggle("Свой туман", CheatState.CustomFog);
        FloatStepper("  -> Начало, м", CheatState.CustomFogStart, 0f, 100f, 1f);
        FloatStepper("  -> Конец, м", CheatState.CustomFogEnd, 2f, 200f, 1f);
        FloatStepper("  -> Сила оттенка, %", CheatState.CustomFogTint, 0f, 100f, 2f);
        Cycle("Уведомления: плашки Rockstar или остров", CheatState.NotifyStyle, new[] { "Плашки", "Остров" });
        SectionHeader("Оповещения");
        Toggle("Оповещение: лобби (зашёл / вышел / погиб / возродился)", CheatState.LobbyAlerts);
        Toggle("Оповещение: монстр рядом", CheatState.EnemyAlert);
        FloatStepper("  -> Радиус, м", CheatState.EnemyAlertRange, 3f, 60f, 1f);
        Toggle("Оповещение: квота набрана", CheatState.HaulAlert);
        Toggle("Оповещение: мало здоровья (у вас и у тиммейтов)", CheatState.LowHealthAlert);
        IntStepper("  -> Порог, % здоровья", CheatState.LowHealthPercent, 5, 90, 5);
        Toggle("Зум (сужает обзор)", CheatState.Zoom);
        FloatStepper("  -> Угол зума (FOV)", CheatState.ZoomFov, 5f, 90f, 5f);
        Toggle("Спокойная камера (без тряски, покачивания, рывка при беге)", CheatState.StableCamera);
        IntStepper("Яркость фонарика", CheatState.FlashlightIntensity, 1, 30, 1);
        IntStepper("Угол луча фонарика", CheatState.FlashAngle, 10, 170, 5);
        Toggle("Фонарик не гаснет в приседе", CheatState.FlashInCrouch);
        Toggle("Цветной фонарик", CheatState.FlashlightColorEnabled);
        ColorCycle("Цвет фонарика", CheatState.FlashlightColorHex);
        Toggle("Радужный фонарик", CheatState.FlashlightColorRainbow);
        Toggle("Макс. заряд головы", CheatState.MaxHeadBattery);
        Button("Экран загрузки", "Пропустить", LoadingHack.SkipNow);
        Toggle("Принудительный пропуск (рискованно)", CheatState.LoadingForceSkip);
    }

    private static void BuildWorldTab()
    {
        Toggle("Автофарм (сам ходит и сдаёт лут)", CheatState.AutoFarm);
        Toggle("Магнит лута", CheatState.LootMagnet);
        Toggle("Бог-режим для ценностей (хост/сингл)", CheatState.ValuableGodMode);
        Toggle("Тягач тележек", CheatState.CartPuller);
        Toggle("Автоподбор", CheatState.AutoLoot);
        Toggle("Радар эвакуации", CheatState.ExtractionRadar);
        Toggle("Автозапуск эвакуации", CheatState.AutoActivateExtraction);
        CycleAndAction("Спавнер предметов", CheatState.ItemSpawnerName, ref _itemSpawnerNamesCache,
            ItemSpawnerHack.ScanItemNames, "Заспавнить", ItemSpawnerHack.SpawnSelected);
        Toggle("Без лимита времени хвата врагами", CheatState.NoGrabMaxTime);
        Toggle("Небьющиеся ценности", CheatState.Unbreakable);

        SectionHeader("Небьющиеся предметы по типу");
        Toggle("Оружие ближнего боя", CheatState.UnbreakableMelee);
        Toggle("Огнестрел", CheatState.UnbreakableGun);
        Toggle("Гранаты", CheatState.UnbreakableGrenade);
        Toggle("Мины", CheatState.UnbreakableMine);
        Toggle("Гранатомёты/пусковые", CheatState.UnbreakableLauncher);
        Toggle("Аптечки", CheatState.UnbreakableHealthPack);
        Toggle("Силовые кристаллы", CheatState.UnbreakablePowerCrystal);
        Toggle("Инструменты", CheatState.UnbreakableTool);
        Toggle("Трекеры", CheatState.UnbreakableTracker);
        Toggle("Дроны", CheatState.UnbreakableDrone);
        Toggle("Шары (orb)", CheatState.UnbreakableOrb);
        Toggle("Тележки", CheatState.UnbreakableCart);
        Toggle("Карманные тележки", CheatState.UnbreakablePocketCart);
        Toggle("Транспорт", CheatState.UnbreakableVehicle);
        Toggle("Станции апгрейда предметов", CheatState.UnbreakableItemUpgrade);
        Toggle("Станции апгрейда игрока", CheatState.UnbreakablePlayerUpgrade);

        SectionHeader("Квота и прочее");
        Toggle("Бесплатная квота", CheatState.FreeQuota);
        Toggle("Квота в 0 при загрузке уровня", CheatState.QuotaZeroRace);
        Toggle("Авто-скип в магазин при загрузке", CheatState.AutoSkipToShop);
        Toggle("Безлимит перероллов апгрейдов (хост/сингл, цена настоящая)", CheatState.UnlimitedRerolls);
        Toggle("Лазерный прицел", CheatState.UseLaser);
        Toggle("Вибро-оружие (только хост/соло)", CheatState.MeleeVibrate);
        Toggle("Вечная зарядная станция", CheatState.InfiniteChargingStation);
        Toggle("Лечение у зарядной станции", CheatState.ChargingStationHeal);
        Toggle("После смерти - голова к грузовику", CheatState.DeathAutoToTruck);
        Toggle("После смерти - голова к эвакуации", CheatState.DeathAutoToExtraction);
        Toggle("Осколок с любой смерти", CheatState.TeammateDeathDropBead);
    }

    private static void BuildPlayerTab()
    {
        SectionHeader("Основное");
        Toggle("Бог-режим (не получаете урон)", CheatState.GodMode);
        Button("Лечение (работает у гостя)", "Лечить", HealHack.HealFull);
        Button("Возрождение себя (гость: голова в точку выхода/грузовик)", "Возродиться", ReviveHack.ReviveSelf);
        Button("Мгновенная смерть себе", "Убить себя", InstantKillHack.KillSelf);

        SectionHeader("Сила и хват");
        Toggle("Высокая сила захвата", CheatState.HighStrength);
        IntStepper("Уровень силы", CheatState.StrengthLevel, 1, 50, 1);
        Toggle("Живой оверрайд силы (без потолка апгрейдов)", CheatState.StrengthOverrideMode);
        FloatStepper("Значение оверрайда силы", CheatState.StrengthOverrideValue, 1f, 50f, 1f);
        Toggle("Безлимитная дистанция захвата", CheatState.UnlimitedGrabRange);
        IntStepper("Уровень дальности хвата", CheatState.GrabRangeLevel, 1, 50, 1);
        Toggle("Захват сквозь стены", CheatState.GrabThroughWalls);
        Toggle("Без перегрева инструментов", CheatState.NoOvercharge);
        Toggle("Безлимитные батарейки", CheatState.InfiniteBatteries);
        Toggle("Мощный хват (снап к прицелу)", CheatState.PowerGrab);
        Toggle("Мягкое падение предметов (без броска)", CheatState.GentleDrop);
        Toggle("Обход MasterOnlyRPC для себя (нужен гостю для возрождения себя выше)", CheatState.MasterRpcSelfBypass);

        SectionHeader("Выживаемость");
        Toggle("Безлимитная стамина", CheatState.InfiniteStamina);
        Toggle("Не сбивают с ног", CheatState.NoKnockdown);
        Toggle("Без урона от падения", CheatState.NoFallDamage);
    }

    private static void BuildMovementTab()
    {
        SectionHeader("Скорость");
        FloatStepper("Скорость ходьбы x", CheatState.SpeedMultiplier, 0.5f, 6f, 0.1f);
        FloatStepper("Скорость бега x", CheatState.SprintSpeedMultiplier, 0.5f, 6f, 0.1f);
        FloatStepper("Скорость в приседе x", CheatState.CrouchSpeedMultiplier, 0.5f, 6f, 0.1f);
        Toggle("Безлимитные прыжки (двойной/тройной прыжок)", CheatState.InfiniteJumps);
        FloatStepper("Сила прыжка x", CheatState.JumpMultiplier, 1f, 5f, 0.25f);
        Toggle("Режим Колобка (авто-прыжки после смерти)", CheatState.KolobokMode);

        SectionHeader("Полёт и защита от толчков");
        Toggle("Анти-нокбэк", CheatState.AntiKnockback);
        Toggle("Страфы (доп. скорость)", CheatState.Strafe);
        FloatStepper("Множитель страфа", CheatState.StrafeMultiplier, 1f, 3f, 0.1f);
        Toggle("Полёт", CheatState.Fly);
        FloatStepper("Скорость полёта", CheatState.FlySpeed, 2f, 60f, 2f);
        Toggle("Спринт удваивает скорость полёта", CheatState.FlySprintBoost);
        Toggle("Прятать от других во время полёта/ноуклипа", CheatState.FlyAutoHide);
        Toggle("Ноуклип (провалиться сквозь пол)", CheatState.Noclip);
        Toggle("Движение мёртвым", CheatState.DeadFly);
        FloatStepper("Скорость движения мёртвым", CheatState.DeadFlySpeed, 2f, 50f, 1f);
    }

    private static void BuildPlayersTab()
    {
        SectionHeader("Найти лобби по ссылке Steam-профиля");
        TextFieldRow("Ссылка (…/profiles/… , …/id/… или SteamID64)", "steam_hunter_url", SteamProfileHunter.UrlInput);
        Button("Найти и зайти (сканирует открытые REPO-лобби)", "Найти", SteamProfileHunter.StartSearch);
        LiveInfo(SteamProfileHunter.StatusLine);
        LiveInfo(() => "  -> Работает по хосту открытых лобби. Если цель — гость, найдёт только когда она у вас в друзьях Steam.");

        SectionHeader("Цель (любая дистанция)");
        PlayerPicker("Цель (одна на все действия ниже)");
        Button("Телепорт к цели", "К цели", TeleportHack.TeleportToPlayer);
        Button("Притянуть цель к себе", "Тащить сюда", TeleportHack.PullPlayerToMe);
        Toggle("Клетка (держит цель под картой)", CheatState.PlayerCage);
        Button("Бросок (закинуть цель в воздух)", "Бросить", RaidHack.Yeet);
        Button("Пугающий звук у цели", "Играть", FakeSoundHack.PlaySelected);
        Button("Подстава продавцу (только в магазине)", "Подставить", RaidHack.FrameForShopKeeper);
        Button("Посадить в машину", "Посадить", RaidHack.ForceSeat);
        Toggle("Кресельная чехарда", CheatState.SeatShuffle);
        Toggle("Приманка для монстра", CheatState.MonsterBait);
        Toggle("Спам-лечение (безвредно)", CheatState.HealSpam);
        Toggle("Ловец игроков (сам хватает упавших в кувырок Q, через стены)", CheatState.PlayerCatcher);
        FloatStepper("  -> Радиус, м", CheatState.PlayerCatcherRange, 2f, 100f, 2f);
        FloatStepper("  -> Держать, сек (0 = пока не отпустите)", CheatState.PlayerCatcherHoldSeconds, 0f, 60f, 1f);
        Toggle("  -> Только выбранную цель", CheatState.PlayerCatcherTargetOnly);
        LiveInfo(PlayerCatcherHack.Status);
        Button("Возродить цель (только хост реально)", "Возродить", ReviveHack.ReviveOther);
        Button("Мгновенная смерть цели (только хост)", "Убить", InstantKillHack.KillOther);
        Toggle("Авто-возрождение всех мёртвых", CheatState.AutoReviveTeammates);

        SectionHeader("Всё лобби разом");
        Toggle("Заморозить всех", CheatState.FreezeAll);
        Toggle("Клетка ВСЕМ", CheatState.PlayerCageAll);
        Button("Бросок ВСЕХ", "Бросить всех", RaidHack.YeetAll);
        Button("Групповой нокдаун", "Нокдаун всех", MassRaidHack.InstantGroupKnockdown);
        Button("NUKE (нокдаун + выбить из рук)", "NUKE", MassRaidHack.Nuke);
        Toggle("Шторм бросков", CheatState.YeetStorm);
        Toggle("Наказание кэмперов эвакуации", CheatState.ExtractionCamperPunish);
        Button("Похищение в машину", "Похитить", MassRaidHack.MassSeatKidnap);
        Button("Подстава ВСЕМ продавцу", "Подставить всех", MassRaidHack.FrameAllForShopKeeper);
        Toggle("Казнь в магазине (по очереди)", CheatState.ShopExecution);

        SectionHeader("Отобрать из рук");
        Toggle("Украсть предмет из рук (по имени, ниже)", CheatState.StealHeldItem);
        CycleWithScan("  -> Предмет для кражи", CheatState.StealHeldItemName, ref _stealItemNamesCache, ItemStealHack.ScanItemNames);
        Toggle("Тотальный грабёж (без фильтра по имени - тащит всё подряд)", CheatState.StealAllHeldItems);
        Button("Выбить всё из рук у всех", "Выбить", RaidHack.ForceDropAll);
        Toggle("Тотальное разоружение", CheatState.MassDisarm);

        SectionHeader("Арена (auto-win)");
        Toggle("Auto-Win Арены (Race)", CheatState.ArenaSabotage);
        Toggle("Auto-Win Арены с оружием", CheatState.ArenaWeaponsSabotage);
        Toggle("Auto-Win Арены, легит-версия", CheatState.ArenaLegitSabotage);

        SectionHeader("Продавец-палач (магазин, урон настоящий)");
        Button("Мгновенная казнь цели", "Казнить", RuckusFrameHack.InstantExecute);
        Button("Мгновенная казнь ВСЕХ", "Казнить всех", RuckusFrameHack.InstantExecuteAll);
        Toggle("Авто-казнь", CheatState.AutoShopExecute);
        Button("Подставить за избиение", "Подставить", RuckusFrameHack.FrameForAttack);
        Button("Подставить как убийцу", "Убийца", RuckusFrameHack.FrameAsKiller);
        Button("Перекрёстная драка", "Драка", RuckusFrameHack.FrameEveryoneCrossfire);
        Button("Бомба репутации", "Бомба", RuckusFrameHack.ScoreBomb);
        Button("Подстава за тычки", "Тычки", RuckusFrameHack.CarryThrustFrame);
        Toggle("Шторм тычков", CheatState.CarryThrustStorm);

        SectionHeader("Лишение вещей");
        Button("Обезоружить цель", "Обезоружить", GrabDenialHack.DisarmTarget);
        Button("Обезоружить ВСЕХ", "Обезоружить всех", GrabDenialHack.DisarmAll);
        Toggle("Блок хвата цели", CheatState.GrabLockTarget);
        Toggle("Блок хвата ВСЕМ", CheatState.GrabLockAll);
        Toggle("Отбирать при поднятии", CheatState.DisarmOnGrab);
        Button("Снять экипировку с цели", "Снять", GrabDenialHack.ForceUnequipTarget);
        Button("Снять экипировку со ВСЕХ", "Снять всем", GrabDenialHack.ForceUnequipAll);
        Toggle("Шторм разэкипировки", CheatState.UnequipStorm);
        Button("Навязать предметы цели", "Навязать", GrabDenialHack.ForceEquipOnTarget);
        Toggle("Проклятые руки", CheatState.CursedHands);
        Button("Раздать хлам всем", "Раздать", GrabDenialHack.ForceEquipJunkAll);
        Button("Сжечь оружие цели", "Сжечь", GrabDenialHack.DrainTargetWeapon);
        Button("Сжечь всё оружие на уровне", "Сжечь всё", GrabDenialHack.DrainAllWeaponsNow);
        Toggle("Постоянный выжиг оружия", CheatState.WeaponDrainAll);
        Button("ОБОБРАТЬ ЦЕЛЬ (всё разом)", "Обобрать", GrabDenialHack.StripTarget);

        SectionHeader("Нокдаун вблизи (нужно подходить)");
        Toggle("Рывок (нокдаун всех по очереди)", CheatState.KnockdownAll);
        Toggle("Нокдаун + Изгнание", CheatState.KnockdownBanish);
        Toggle("  -> Ещё и убить (только хост/соло)", CheatState.KnockdownBanishKill);

        SectionHeader("Таран тележкой (эксперимент)");
        Toggle("Хватает тележку и таранит ею цель", CheatState.CartRam);
    }

    private static void BuildRaidTab()
    {
        SectionHeader("Лут (осторожно - бьёт и по вашей выручке)");
        Button("Чистка предметов (разбрасывает шмот на уровне)", "Чистка", MassRaidHack.ItemPurge);
        Button("Разброс тележек", "Раскидать", MassRaidHack.CartYeetAll);
        Button("Чёрная дыра ценностей (прячет весь лут)", "Спрятать", MassRaidHack.ValuableBlackhole);
        Toggle("Уничтожить все ценности (кроме дешёвых)", CheatState.DestroyAllValuables);
        Toggle("Разрушитель магазина 3000", CheatState.ShopDestroyer);

        SectionHeader("Эвакуация");
        Button("Активировать ближайшую точку", "Ближняя", WorldHolesHack2.ActivateNearestExtraction);
        Button("Активировать САМУЮ ДАЛЬНЮЮ", "Дальняя", WorldHolesHack2.ActivateFarthestExtraction);
        Toggle("Перехват эвакуации", CheatState.ExtractionSnipe);

        SectionHeader("Карта и взрывы");
        Button("Раскрыть все ценности на карте", "Раскрыть", WorldHolesHack2.RevealAllValuables);
        Toggle("Авто-раскрытие ценностей", CheatState.AutoRevealValuables);
        Button("Взрыв на цели", "Взрыв", WorldHolesHack2.StrikeTarget);
        Button("Взрыв по всем разом", "Взрыв всем", WorldHolesHack2.StrikeEveryone);
        Toggle("Шторм взрывов", CheatState.ProjectileStrikeStorm);

        SectionHeader("Транспорт и застревания");
        Button("Тряска всех машин", "Трясти", WorldHolesHack2.WobbleAllVehicles);
        Button("Тряска машины цели", "Трясти цель", WorldHolesHack2.WobbleTargetVehicle);
        Toggle("Шторм тряски", CheatState.VehicleWobbleStorm);
        Button("Гудок всех машин (хост: монстры идут к машинам)", "Гудеть", HonkHack.HonkAll);
        Button("Гудок ближайшей машины", "Гудок", HonkHack.HonkNearest);
        Toggle("Шторм гудков", CheatState.HonkStorm);
        FloatStepper("  -> Интервал, сек", CheatState.HonkStormInterval, 0.2f, 10f, 0.2f);
        Button("Сбросить толкатель всем", "Сбросить", WorldHolesHack2.ResetAllPhysPushers);
        Button("Сбросить толкатель цели", "Сбросить цель", WorldHolesHack2.ResetTargetPhysPusher);

        SectionHeader("Уровень");
        Button("Снести все ловушки целиком", "Снести", WorldHolesHack2.DestroyAllTrapsFully);
        Button("Открыть все музыкальные шкатулки", "Открыть", WorldHolesHack2.TriggerAllMusicBoxes);
        Button("Остановить все книги-кусаки", "Остановить", WorldHolesHack2.StopAllChompBooks);
        Button("ЗАЧИСТКА МИРА", "Зачистка", WorldHolesHack2.WorldSweep);
        Button("Обезвредить все ловушки", "Обезвредить", UtilityHolesHack.DisarmAllTraps);
        Button("Переключить все рычаги/switches", "Переключить", UtilityHolesHack.ToggleAllSwitches);
        Toggle("Запереть все двери", CheatState.LockAllDoors);
        Toggle("Чёрная дыра: лут и двери орбитой над картой", CheatState.BlackHole);
        LiveInfo(BlackHoleHack.Status);
    }

    private static void BuildMonstersTab()
    {
        Button("Телепорт к ближайшему монстру", "К монстру", TeleportHack.TeleportToEnemy);
        Button("Притянуть ближайшего монстра к себе", "Тащить монстра", TeleportHack.PullEnemyToMe);
        Button("Рой на группу", "Наслать", TeleportHack.SwarmTheGroup);
        Toggle("Шторм роя", CheatState.SwarmStorm);
        Toggle("Ловушка комнаты", CheatState.TrapRoom);
        Toggle("Убийство монстров падением", CheatState.EnemyDropKill);
        Toggle("Заморозка врагов (реальная, не визуал)", CheatState.FreezeEnemies);
        FloatStepper("  -> Радиус (0 = все на уровне)", CheatState.FreezeEnemiesRange, 0f, 100f, 5f);

        SectionHeader("Количество врагов на уровне (только хост/соло)");
        FloatStepper("Множитель спавна (1 = как обычно)", CheatState.SpawnRateMultiplier, 1f, 10f, 0.5f);

        SectionHeader("Дроп ценностей с врагов (только хост/соло)");
        Toggle("Убрать лимит (игра держит не больше 10)", CheatState.RemovePillarLimit);
        IntStepper("Множитель дропа", CheatState.PillarDropMultiplier, 1, 10, 1);
        Toggle("  -> Снять лимит дропа с одного врага (иначе капается на 3)", CheatState.PillarDropRemoveLimit);
    }

    private static void BuildCosmeticTab()
    {
        SectionHeader("Ценность для спавна (по имени, из всех уровней)");
        // Rescan on click, not every frame: the scan walks every level's presets with a regex per prefab.
        CycleWithScan("Имя", CheatState.SpawnValuableName, ref _valuableNamesCache, EconomyHack.ScanValuableNames);
        Button("Заспавнить выбранную ценность в зоне эвакуации", "Заспавнить", EconomyHack.SpawnValuable);

        SectionHeader("Крылья и глаза");
        Toggle("Принудительные крылья (только визуал)", CheatState.ForceWings);
        Toggle("Розовые крылья (второй реальный цвет игры)", CheatState.WingsPink);
        Toggle("Цвет глаз (фикс. палитра, видно другим)", CheatState.EyeColor);
        Cycle("Цвет глаз", CheatState.EyeColorName, CosmeticHack.EyeColorNames);
        Toggle("Большие зрачки (видно другим, если у них тоже наш мод)", CheatState.BigPupils);
        Toggle("Эффект глаз (игровой, видят ВСЕ - даже без мода)", CheatState.EyeOverride);
        Cycle("  -> Какой", CheatState.EyeOverrideState, EyeOverrideHack.StateNames);
        Toggle("Зафиксировать выражение лица (настоящая фича игры, видно вообще всем)", CheatState.ExpressionLock);
        IntStepper("  -> Какое (1-6)", CheatState.ExpressionIndex, 1, 6, 1);

        SectionHeader("Смена имени (реально для всех, хост/гость)");
        TextFieldRow("Новое имя", "name_changer_text", CheatState.NameChangerText);
        Button("Применить имя", "Сменить", () => NameChangerHack.SetName(CheatState.NameChangerText.Value));
        Button("Разблокировать ВСЕ косметики (правит сейв, будет бэкап; нажать дважды)", "Открыть всё", CosmeticUnlockHack.UnlockAll);
        Toggle("Маскировка под хоста (авто, копирует ник хоста себе)", CheatState.MimicHostName);
        Toggle("Маскировка скина под хоста (авто, копирует косметику и цвета)", CheatState.MimicHostSkin);
        Toggle("  -> Копировать выбранного игрока вместо хоста", CheatState.MimicFollowPicked);
        Button("Скопировать ник и скин выбранного игрока (разово)", "Копировать", CopyIdentityHack.CopyPicked);
    }

    // Split out into its own tab (2026-09-11) - was scattered inside "Прочее". Two different
    // directions live here: what OTHERS hear from you (GlitchyVoice - real, transmitted,
    // Recorder.TransmitEnabled) vs what YOU hear from THEM (VoiceFilter/ChipmunkVoices - local
    // playback only, AudioSource-level, nothing sent over the network).
    private static void BuildVoiceTab()
    {
        SectionHeader("Кого слышно вам (только локально, ни на что в сети не влияет)");
        Cycle("Слышать (все/живые/мёртвые)", CheatState.VoiceFilter, new[] { "All", "Living", "Dead" });
        Toggle("Чужие голоса (бурундуки/демоны - меняет высоту тона для вас)", CheatState.ChipmunkVoices);
        FloatStepper("  -> Высота тона (>1 выше, <1 ниже)", CheatState.ChipmunkPitch, 0.5f, 3f, 0.1f);
        Toggle("Слышать всех без затухания (мёртвы или призрак-наблюдатель)", CheatState.GhostHearAll);

        SectionHeader("Что слышат от вас другие (реально передаётся всем)");
        Toggle("Демонический голос (другие реально слышат замедленным, нужна транк-пушка)", CheatState.DemonVoice);
        LiveInfo(() => !CheatState.DemonVoice.Value ? "  -> Выключено"
            : DemonVoiceHack.HasGun ? "  -> Транк-пушка найдена, эффект идёт"
            : "  -> В уровне нет транк-пушки: заспавните её во вкладке «Мир»");
        Toggle("Лаганый голос (другие слышат заикания, вид обычный)", CheatState.GlitchyVoice);
        FloatStepper("  -> Длина рывка, сек", CheatState.GlitchyVoiceInterval, 0.05f, 1f, 0.05f);
    }

    // New tab in the "Прочее" group: the microphone fix (picks a physical mic and rewrites the game's
    // saved device name) plus the invisibility voice toggle that broke transmission for others.
    private static void BuildMicTab()
    {
        SectionHeader("Микрофон");
        Button("Выбрать физический микрофон (вместо виртуального Sonar/VB/Broadcast)", "Исправить", MicFixHack.PickButton);
        LiveInfo(() => MicFixHack.StatusLine());
        LiveInfo(() => "  -> Unity видит: " + string.Join(" | ", UnityEngine.Microphone.devices));

        SectionHeader("Голос в невидимке");
        Toggle("Говорить голосом в невидимке (анонимно; у других может не звучать)", CheatState.InvisibleSpeak);
    }

    private static void BuildMiscTab()
    {
        Button("Публичная игра (родная кнопка, только из главного меню)", "Найти", PublicGameHack.JoinRandomPublic);
        Button("Сбросить регион на авто", "Сбросить", PublicGameHack.ResetToBestRegion);
        Button("Форсировать WebSocket-протокол", "Форсировать", PublicGameHack.ForceWebSocketProtocol);
        Toggle("Всегда взаимодействие", CheatState.AlwaysInteract);
        Toggle("Пропустить ожидание луны", CheatState.SkipMoonWait);
        Toggle("Пропустить экран результатов", CheatState.SkipResultsScreen);
        Button("Пересканировать свет/частицы/звук", "Скан", LateJoinFixHack.RescanNow);
        Button("Телепорт к ближайшему игроку", "Идти", LateJoinFixHack.TeleportToNearestPlayer);
        CycleAndAction("Загрузить уровень (хост/соло, минуя консоль)", CheatState.LevelLoaderName, ref _levelLoaderNamesCache,
            LevelLoaderHack.ScanLevelNames, "Загрузить", LevelLoaderHack.LoadSelected);
        CycleAndAction("Принудить модуль уровня (перезапускает сцену, хост/соло)", CheatState.ModuleLoaderName, ref _moduleLoaderNamesCache,
            LevelLoaderHack.ScanCurrentLevelModuleNames, "Принудить", LevelLoaderHack.ForceCurrentLevelModule);

        SectionHeader("Пресеты настроек (все настройки мода, включая HUD)");
        Button("Пресет 1: сохранить текущие настройки", "Сохранить", PresetsHack.Save1);
        Button("Пресет 1: загрузить", "Загрузить", PresetsHack.Load1);
        Button("Пресет 2: сохранить текущие настройки", "Сохранить", PresetsHack.Save2);
        Button("Пресет 2: загрузить", "Загрузить", PresetsHack.Load2);
        Button("Пресет 3: сохранить текущие настройки", "Сохранить", PresetsHack.Save3);
        Button("Пресет 3: загрузить", "Загрузить", PresetsHack.Load3);
        Button("Отменить последнюю загрузку / сброс", "Отменить", PresetsHack.Undo);
        Button("Скопировать текущие настройки (поделиться с другом)", "В буфер", PresetsHack.CopyToClipboard);
        Button("Вставить настройки из буфера обмена", "Вставить", PresetsHack.PasteFromClipboard);
        Button("Сбросить все настройки мода (нажать дважды)", "Сбросить", PresetsHack.ResetAll);
        LiveInfo(() => PresetsHack.SlotInfo(1) + "\n" + PresetsHack.SlotInfo(2) + "\n" + PresetsHack.SlotInfo(3) +
            (PresetsHack.LastMessage.Length > 0 ? "\nПоследнее: " + PresetsHack.LastMessage : ""));

        SectionHeader("Резервные копии файла настроек (вместе с биндами)");
        Button("Сделать копию сейчас", "Копия", ConfigBackup.BackupNow);
        Button("Вернуть последнюю копию (нажать дважды)", "Вернуть", ConfigBackup.RestoreLatest);
        Button("Отменить возврат копии", "Отменить", ConfigBackup.UndoRestore);
        LiveInfo(ConfigBackup.Info);

        SectionHeader("Журнал забега (итог каждого уровня, хранится между сессиями)");
        Toggle("Сообщать, кто от чего погиб", CheatState.DeathRecap);
        Toggle("Сообщать, сколько стоимости потерял предмет в руках", CheatState.DamageAlert);
        LiveInfo(RunJournal.Info);
        Button("Открыть всю историю (run_history.txt)", "Открыть", RunJournal.OpenHistory);

        SectionHeader("Назначенные клавиши");
        LiveInfo(BindsOverview.Info);
        Button("Снять все клавиши (нажать дважды; перед этим делается копия)", "Снять все", BindsOverview.ClearAll);

        SectionHeader("Чат (в игре он только озвучивается)");
        Toggle("Показывать чужие сообщения на острове", CheatState.ChatToIsland);
        Toggle("Не озвучивать чужие сообщения (только у вас)", CheatState.MuteOthersTts);
        LiveInfo(ChatLogHack.Info);

        SectionHeader("Пинги (видят только игроки с этим модом)");
        Toggle("Пинги включены", CheatState.Pings);
        Button("Пинг туда, куда смотрите (повесьте на клавишу средним кликом)", "Пинг", PingSystem.PingHere);
        FloatStepper("  -> Сколько держится, сек", CheatState.PingSeconds, 2f, 30f, 1f);
        Button("Убрать все метки", "Убрать", PingSystem.ClearAll);

        SectionHeader("Быстрые фразы в чат (клавиша - средний клик по строке)");
        TextFieldRow("Фраза 1", "quick_phrase_1", CheatState.QuickPhrase1);
        Button("  -> Отправить фразу 1", "Отправить", QuickChatHack.Send1);
        TextFieldRow("Фраза 2", "quick_phrase_2", CheatState.QuickPhrase2);
        Button("  -> Отправить фразу 2", "Отправить", QuickChatHack.Send2);
        TextFieldRow("Фраза 3", "quick_phrase_3", CheatState.QuickPhrase3);
        Button("  -> Отправить фразу 3", "Отправить", QuickChatHack.Send3);
        TextFieldRow("Фраза 4", "quick_phrase_4", CheatState.QuickPhrase4);
        Button("  -> Отправить фразу 4", "Отправить", QuickChatHack.Send4);
        LiveInfo(() => QuickChatHack.PlaceholderHelp);

        SectionHeader("Защита от краша");
        Toggle("Анти-краш", CheatState.AntiCrash);
        Toggle("Карантин нарушителей", CheatState.AntiCrashQuarantine);
        IntStepper("Лимит RPC/сек на игрока", CheatState.AntiCrashMaxRpcsPerSecond, 20, 1000, 20);
        IntStepper("Лимит спавнов/сек на игрока", CheatState.AntiCrashMaxInstantiatesPerSecond, 10, 500, 10);
        IntStepper("Макс. длина строки в RPC", CheatState.AntiCrashMaxArgStringLength, 200, 20000, 200);
        IntStepper("Карантин, сек", CheatState.AntiCrashQuarantineSeconds, 1, 60, 1);
        LiveInfo(() =>
            $"Погашено исключений: {AntiCrashHack.ExceptionsSwallowed}   Отброшено RPC: {AntiCrashHack.RpcsDropped}\n" +
            $"Отброшено спавнов: {AntiCrashHack.InstantiatesDropped}   В карантине: {AntiCrashHack.QuarantinedCount(System.Environment.TickCount)}");

        SectionHeader("Игнор (чужие пакеты на меня не выполняются)");
        Toggle("Игнорировать убийство от других (хост с модом)", CheatState.IgnoreDeath);
        Toggle("Игнорировать урон от других", CheatState.IgnoreHurt);
        Toggle("Игнорировать толчки/импульсы", CheatState.IgnoreImpulse);
        Toggle("Игнорировать принудительное падение/вращение", CheatState.IgnoreTumble);
        Toggle("Игнорировать принудительную установку здоровья", CheatState.IgnoreHealthSet);
        LiveInfo(() =>
            $"Отброшено: убийств {IgnoreHack.DeathDropped}, урона {IgnoreHack.HurtDropped}, толчков {IgnoreHack.ImpulseDropped}, падений {IgnoreHack.TumbleDropped}, здоровья {IgnoreHack.HealthDropped}");

        SectionHeader("Соседние комнаты");
        IntStepper("Сколько ещё комнат создавать", CheatState.MultiRoomExtraCount, 1, 10, 1);
        IntStepper("Лимит игроков в каждой", CheatState.MultiRoomMaxPlayers, 6, 100, 2);
        Button("Создать N независимых комнат с текущим именем", "Создать", MultiRoomHack.CreateSiblingRooms);

        SectionHeader("Оптимизация (FPS)");
        Toggle("Оптимизация графики", CheatState.Optimization);
        Toggle("  -> Выключить тени", CheatState.OptShadows);
        Toggle("  -> Выключить сглаживание", CheatState.OptAntiAliasing);
        Toggle("  -> Без попиксельных источников света", CheatState.OptPixelLights);
        Toggle("  -> Упростить модели вдали (LOD)", CheatState.OptLodBias);
        Toggle("  -> Без мягких частиц и травы", CheatState.OptSoftParticles);
        Toggle("  -> Без динамических отражений", CheatState.OptReflectionProbes);
        Toggle("  -> Выключить постобработку (блум, зерно, размытие)", CheatState.OptPostProcessing);
        Toggle("  -> Выключить туман", CheatState.OptFog);
        Toggle("Ограничить FPS, когда игра в фоне", CheatState.BackgroundFpsLimit);
        IntStepper("  -> FPS в фоне", CheatState.BackgroundFps, 5, 60, 5);
        Toggle("Выключать звук, когда игра в фоне", CheatState.MuteInBackground);
    }

    private static void BuildDevTab()
    {
        SectionHeader("Об авторе");
        LiveInfo(() => $"{Plugin.Name} {Plugin.Version} - автор: Kolya. Личный мод, распространение под чужим именем не разрешено.");

        Toggle("Тестовый пакет (для отладки сети)", CheatState.SendTestPacket, PacketTestHack.OnToggled);
        IntStepper("  -> Интервал отправки, мс", CheatState.PacketTestIntervalMs, 50, 5000, 50);

        SectionHeader("Сетевая диагностика");
        Toggle("Показывать состояние сети (только чтение)", CheatState.NetworkDiagnostics);
        LiveInfo(NetworkDiagnostics.GetStatsString);

        SectionHeader("Логи событий");
        Toggle("Писать логи в RepoCheatMenu_Logs (по категориям)", CheatState.EventLogging);
        LiveInfo(() => "Папка сессии: " + (EventLog.SessionDir == null ? "ещё не создана" : System.IO.Path.GetFileName(EventLog.SessionDir)));

        SectionHeader("Мост для автономного тестирования");
        Toggle("Включить мост (выполняет C# из RepoCheatMenu_Bridge/command.txt)", CheatState.DebugBridgeEnabled);
        LiveInfo(() => "Только для доверенной сессии разработки - произвольный код с полным доступом к моду и игре.");
    }

    private static void SetScriptStatus(ScriptEngine.Result result)
    {
        _scriptStatusOk = result.Success;
        _scriptStatus = result.Success ? "OK" : result.Message;
    }

    private static bool _scriptStatusOk = true;

    private static void BuildScriptsTab()
    {
        SectionHeader("Свой скрипт (C#) - Local/Controller/Director/Steam/Game/Run, Players(), " +
            "OtherPlayers(), Log(...), SetCheat/GetCheat, RenameRoom(...). await Task.Delay(...) " +
            "вместо пустого while.");

        float u = _u;
        Rect editorRow = NextRow(88f);
        if (RowVisible(editorRow))
        {
            Rect editorArea = new(editorRow.x + 2f * u, editorRow.y + 2f * u, editorRow.width - 4f * u, editorRow.height - 4f * u);
            CustomGui.Rect(editorArea, new Color(0f, 0f, 0f, 0.3f), 3f * u);
            _scriptDraft = CustomGui.TextArea(editorArea, "script_editor", _scriptDraft,
                Color.clear, Color.white, new Color(RockGui.Accent.r, RockGui.Accent.g, RockGui.Accent.b, 0.1f), RockGui.Font(6.5f, u));
        }

        Rect actionRow = NextRow();
        if (RowVisible(actionRow))
        {
            float h = 12f * u;
            float y = actionRow.y + (actionRow.height - h) * 0.5f;
            Rect nameField = new(actionRow.x + 6f * u, y, 90f * u, h);
            Rect runBtn = new(nameField.xMax + 5f * u, y, 48f * u, h);
            Rect saveBtn = new(runBtn.xMax + 4f * u, y, 54f * u, h);
            Rect deleteBtn = new(saveBtn.xMax + 4f * u, y, 48f * u, h);

            _scriptModuleName = RockGui.TextBox("script_name", nameField, _scriptModuleName, u);

            if (RockGui.Button("script_run", runBtn, "Запустить", RockGui.Kind.Primary, u))
            {
                _scriptStatus = "Выполняется...";
                _scriptStatusOk = true;
                ScriptEngine.StartRun(_scriptDraft, SetScriptStatus);
            }
            if (RockGui.Button("script_save", saveBtn, "Сохранить", RockGui.Kind.Normal, u))
                SetScriptStatus(ScriptModuleManager.Add(_scriptModuleName.Trim(), _scriptDraft));
            if (RockGui.Button("script_delete", deleteBtn, "Удалить", RockGui.Kind.Danger, u))
                ScriptModuleManager.Remove(_scriptModuleName.Trim());
        }

        if (!string.IsNullOrEmpty(_scriptStatus))
            LiveInfo(() => _scriptStatus);

        SectionHeader("Сохранённые модули");
        foreach (var module in ScriptModuleManager.Modules)
        {
            Rect row = NextRow();
            if (!RowVisible(row)) continue;

            float h = 11f * u;
            float y = row.y + (row.height - h) * 0.5f;
            Rect runBtn = new(row.xMax - 6f * u - 48f * u, y, 48f * u, h);
            Rect loadBtn = new(runBtn.x - 4f * u - 56f * u, y, 56f * u, h);

            RockGui.Label(row, module.Name, u, 0.85f, 120f * u);
            if (RockGui.Button(("script_load", module.Name), loadBtn, "В редактор", RockGui.Kind.Normal, u))
            {
                _scriptDraft = module.Source;
                _scriptModuleName = module.Name;
            }
            if (RockGui.Button(("script_run_m", module.Name), runBtn, "Запустить", RockGui.Kind.Primary, u))
            {
                _scriptStatus = "Выполняется...";
                _scriptStatusOk = true;
                ScriptModuleManager.Run(module.Name, SetScriptStatus);
            }
            RockGui.Hairline(row, u);
        }
    }

    private static void BuildTrollTab()
    {
        SectionHeader("Внешний вид у других");
        Toggle("Анти-аим (крутит голову)", CheatState.AntiAimSpin);
        Toggle("DinnerBone (переворот)", CheatState.DinnerBone);
        FloatStepper("Угол переворота", CheatState.DinnerBoneAngle, 0f, 180f, 10f);
        Toggle("  -> Показать себе", CheatState.DinnerBoneShowSelf);
        Toggle("Флип (непрерывное вращение)", CheatState.FlipEnabled);
        Cycle("  -> Ось вращения", CheatState.FlipAxis, new[] { "Roll", "Pitch", "Yaw" });
        FloatStepper("  -> Скорость, град/сек", CheatState.FlipSpeed, 30f, 1080f, 30f);
        FloatStepper("  -> Радиус орбиты, м (0 = выкл)", CheatState.FlipOrbitRadius, 0f, 5f, 0.5f);
        Toggle("  -> Показать себе", CheatState.FlipShowSelf);
        Toggle("T-поза", CheatState.TPose);
        Toggle("Вечный краул (не совмещать с T-позой)", CheatState.GhostCrawl);
        Toggle("Фантомный спринт (бежит изо всех сил, стоя на месте)", CheatState.PhantomSprint);
        Toggle("Все смотрят на меня (аватары рядом провожают глазами, звука нет)", CheatState.AllEyesOnMe);
        Toggle("Фантомные прыжки (другие видят и слышат ваши прыжки)", CheatState.PhantomJump);
        Toggle("Фейк-урон (у других краснеете и стонете, настоящее HP цело)", CheatState.FakeHurt);
        FloatStepper("  -> Интервал фантомных действий, сек", CheatState.FakeMotionInterval, 0.3f, 10f, 0.1f);
        Toggle("Скорость анимации у других (реальное движение не меняется)", CheatState.AnimSpeedSpoof);
        FloatStepper("  -> Множитель (0.2 = слоу-мо, 3+ = дёрганый)", CheatState.AnimSpeedValue, 0.1f, 5f, 0.1f);
        Toggle("Невидимка (модель улетает в небо у других)", CheatState.Invisible);
        FloatStepper("  -> Высота", CheatState.InvisibleHeight, 500f, 20000f, 500f);
        Toggle("  -> Блокировать хват (чтобы луч и улетающие ценности не выдали позицию)", CheatState.InvisibleBlockGrab);
        Toggle("  -> Телекинез (общий с «Призраком», бинд «Схватить/отпустить»)", CheatState.GhostTelekinesis);
        FloatStepper("  -> Дистанция удержания, м", CheatState.GhostTelekinesisDistance, 1f, 15f, 0.5f);
        LiveInfo(GhostTelekinesisHack.Status);
        LiveInfo(GhostTelekinesisHack.KeyHint);
        LiveInfo(InvisibleStatus.Line);
        Toggle("  -> Говорить анонимно, не показываясь (голос без 3D-позиции, слышно всем)", CheatState.InvisibleSpeak);
        Toggle("  -> Звучать нормально во время разговора (тогда видно, пока говорите)", CheatState.InvisibleRevealWhileTalking);
        Toggle("  -> Над потолком вместо неба (эксперимент: голос слышен приглушённо)", CheatState.InvisibleOverCeiling);
        FloatStepper("  -> Высота над потолком, м", CheatState.InvisibleOverCeilingHeight, 4f, 25f, 1f);
        Toggle("Дёрганый фонарик (у других луч мечется, у вас нормально)", CheatState.GlitchyFlashlight);
        FloatStepper("  -> Радиус болтания, м", CheatState.GlitchyFlashlightRadius, 1f, 10f, 0.5f);
        Toggle("Фейковый пинг в лобби (другие видят выдуманное число)", CheatState.FakePing);
        IntStepper("  -> Значение, мс (шаг 500)", CheatState.FakePingValue, 0, 1000000, 500);
        TextFieldRow("  -> Своё число, мс (до 2147483647)", "fake_ping_text", CheatState.FakePingText);
        Toggle("Фальшивый перегрев луча (рука светится/искрит/гудит у других)", CheatState.FakeOvercharge);
        IntStepper("  -> Интенсивность", CheatState.FakeOverchargeValue, 0, 200, 20);
        Toggle("Диско-луч (у других луч захвата и рука меняют цвет, у вас оранжевый)", CheatState.DiscoBeam);
        IntStepper("  -> Режим (0 цикл, 1 зелёный, 2 фиолетовый, 3 синий)", CheatState.DiscoBeamMode, 0, 3, 1);
        FloatStepper("  -> Смен цвета в секунду (цикл)", CheatState.DiscoBeamSpeed, 0.5f, 12f, 0.5f);
        Toggle("Призрак-наблюдатель (вас нет в списках игроков; не хватать, не говорить; только гость)", CheatState.GhostObserver);
        Toggle("  -> Слышать других (только приём, микрофон не передаётся)", CheatState.GhostObserverHear);
        Toggle("  -> Говорить голосом (анонимно, звук без 3D-позиции; кто вас слышит - в статусе призрака)", CheatState.GhostSpeak);
        Toggle("  -> Слышать всех без затухания", CheatState.GhostHearAll);
        Toggle("  -> Полёт сквозь стены (у призрака никто не видит)", CheatState.Noclip);
        Toggle("  -> Телекинез (бинд: Схватить/отпустить)", CheatState.GhostTelekinesis);
        FloatStepper("  -> Дистанция удержания, м", CheatState.GhostTelekinesisDistance, 1f, 15f, 0.5f);
        LiveInfo(GhostTelekinesisHack.Status);
        LiveInfo(GhostTelekinesisHack.KeyHint);
        LiveInfo(GhostObserverHack.Status);
        Toggle("  -> Следовать за выбранным игроком", CheatState.GhostFollow);
        FloatStepper("  -> Дистанция следования, м", CheatState.GhostFollowDistance, 1f, 10f, 0.5f);
        Toggle("  -> Смотреть его глазами", CheatState.GhostFollowEyes);
        Button("  -> Следить за следующим игроком", "Следующий", GhostFollowHack.NextTarget);
        Button("  -> Выбрать игрока в прицеле", "В прицеле", GhostFollowHack.PickHoveredPlayer);
        Button("  -> Прыгнуть к выбранному игроку", "Прыгнуть", GhostFollowHack.JumpToPicked);
        Button("  -> Прыгнуть к ценности", "Ценность", GhostFollowHack.JumpToTopValuable);
        Button("  -> Прыгнуть к грузовику", "Грузовик", GhostFollowHack.JumpToTruck);
        Button("  -> Прыгнуть к точке выхода", "Выход", GhostFollowHack.JumpToExtraction);
        LiveInfo(GhostFollowHack.PlayersPanel);
        Toggle("Игроки на карте (Tab)", CheatState.TeammatesOnMap);
        Toggle("Предупреждать о невидимых игроках", CheatState.HiddenPlayerAlert);
        LiveInfo(HiddenPlayersHack.Status);
        LiveInfo(GhostFollowHack.Status);
        LiveInfo(GhostFollowHack.KeysHint);
        Toggle("Призрачный наездник (у других вы едете на ближайшем предмете)", CheatState.GhostRider);
        LiveInfo(() => !CheatState.GhostRider.Value ? "  -> Выключено"
            : GhostRiderHack.HasTarget ? "  -> Цель (видно только другим): " + GhostRiderHack.TargetName
            : "  -> Ищу цель рядом...");

        SectionHeader("Спам в чат");
        Toggle("Спам фразами в чат", CheatState.ChatSpam);
        TextFieldRow("  -> Фразы (через |)", "chat_spam_text", CheatState.ChatSpamText);
        FloatStepper("Интервал, сек", CheatState.ChatSpamInterval, 1f, 30f, 0.5f);
        Toggle("  -> Раскрашивать (rich text)", CheatState.ChatSpamRichText);
        ColorCycle("  -> Цвет", CheatState.ChatSpamColor);
        IntStepper("  -> Размер шрифта", CheatState.ChatSpamFontSize, 10, 100, 5);

        SectionHeader("Ловушки (удалённый запуск)");
        Button("Ближайшая ловушка", "Запустить", TrapTriggerHack.TriggerNearest);
        Button("Все ловушки на уровне", "Запустить все", TrapTriggerHack.TriggerAllInLevel);
    }
}
