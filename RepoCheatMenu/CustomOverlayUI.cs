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
internal static class CustomOverlayUI
{
    private static bool _visible;
    private static Vector2 _windowPos = new(80, 80);
    // 2026-09-09: RepoHax-style pass - was 760x520/190px sidebar (cramped next to their 1200x600/
    // 215px reference). Scaled up proportionally, not all the way to their size - our row/font
    // sizes are smaller so a straight 1200x600 would leave far more empty air than theirs does.
    private static readonly Vector2 WindowSize = new(900, 580);
    private const float SidebarWidth = 210f;
    private const float HeaderHeight = 40f;
    private const float RowHeight = 30f;
    private const float RowSpacing = 4f;

    // RepoHax-style card grouping: SectionHeader marks a new card's start, the PREVIOUS card (if
    // any) closes at that same Y. Heights aren't known until every row inside has been placed, and
    // this renderer has no way to draw "behind" something already committed to the mesh (see
    // CustomGui.cs's own comment - later-drawn triangles paint over earlier ones, painter's
    // algorithm, no depth/z-order) - so DrawContent runs the tab's Build() callback twice: once
    // with _measuring true (every row helper's existing "if (!RowVisible(r)) return" after
    // NextRow() now doubles as a free "advance the cursor but draw nothing" measure pass, no
    // changes needed in any of the 13 row helpers themselves) to record each card's Y span, then
    // once for real after the card backgrounds are drawn from those spans - so rows always land on
    // top of their own card, never the other way round.
    private static bool _measuring;
    private static readonly List<(float Top, float Bottom)> _cardBounds = new();
    private static float _cardStartY = float.NaN;

    private static readonly (string Label, Action Build)[] Tabs = BuildTabList();

    private static int _activeTab;
    private static float _scroll;
    private static float _contentHeight; // measured last frame, drives scroll clamp

    private static bool _draggingWindow;
    private static string _scriptDraft = "";
    private static string _scriptModuleName = "";
    private static string _scriptStatus = "";

    public static bool IsOpen => _visible;

    public static void Toggle()
    {
        if (_visible) Hide(); else Show();
    }

    // Cursor handling was missing entirely before (this menu only ever got opened via F6 up to
    // now, usually with the RightShift menu already having unlocked the cursor earlier in the same
    // session) - mirrors OverlayUI.Show()/Hide() exactly now that the cross-menu switch button can
    // land here as the FIRST menu opened in a session, with nothing else having touched the cursor
    // yet.
    private static bool _wasCursorVisible;
    private static CursorLockMode _wasLockState;

    // _windowPos used to default to a hardcoded (80,80) top-left offset - fine on a 1920x1080-ish
    // screen, visibly off-center anywhere wider. CustomGui.ScreenWidth/Height only exist AFTER
    // HudRoot.EnsureBuilt() has actually created its canvas, so this can't just be a field
    // initializer - it centers once, the very first time the menu is ever shown in a session, and
    // never again after that so a manual drag during the session isn't fought on the next open.
    private static bool _windowPositionInitialized;

    public static void Show()
    {
        HudRoot.EnsureBuilt();

        if (!_windowPositionInitialized)
        {
            _windowPositionInitialized = true;
            _windowPos = new Vector2((CustomGui.ScreenWidth - WindowSize.x) * 0.5f, (CustomGui.ScreenHeight - WindowSize.y) * 0.5f);
        }

        _visible = true;
        _wasCursorVisible = Cursor.visible;
        _wasLockState = Cursor.lockState;
        Cursor.lockState = CursorLockMode.Confined;
        Cursor.visible = true;
        if (InputManager.instance != null) InputManager.instance.DisableAiming(0.5f);
    }

    public static void Hide()
    {
        _visible = false;
        Cursor.lockState = _wasLockState;
        Cursor.visible = _wasCursorVisible;
    }

    // Called from HudRoot.Tick() inside its own shared BeginFrame/EndFrame - this menu no longer
    // owns its own draw cycle (see HudRoot's own comment for why two independent cycles in one
    // frame doesn't work with this mesh renderer).
    public static void DrawIfVisible()
    {
        if (!_visible) return;
        if (InputManager.instance != null) InputManager.instance.DisableAiming(0.5f);
        DrawWindow();
    }

    private static void DrawWindow()
    {
        // CustomGui's own coordinate origin is bottom-left (see CustomGui.cs's pivot comment) -
        // _windowPos here is top-left in the usual "screen" sense, so the window rect's Y is
        // flipped into that space right here, once, rather than threading the flip through every
        // row below.
        float top = CustomGui.ScreenHeight - _windowPos.y;
        Rect windowRect = new(_windowPos.x, top - WindowSize.y, WindowSize.x, WindowSize.y);

        CustomGui.Rect(windowRect, new Color(0.078f, 0.078f, 0.082f, 0.97f), 10f);

        Rect headerRect = new(windowRect.x, windowRect.yMax - HeaderHeight, windowRect.width, HeaderHeight);
        CustomGui.Rect(headerRect, new Color(0.114f, 0.118f, 0.125f, 1f), 10f);
        CustomGui.Text(new Rect(headerRect.x + 12f, headerRect.y, 300f, headerRect.height),
            "REPO ЧИТ-МЕНЮ (CustomGui)", 16, Color.white, TextAnchor.MiddleLeft);

        // Kill switch - same PanicHack.Deactivate() the old menu's own "ВЫКЛ ( ] )" header button
        // wires to. Was only reachable through that OverlayUI button until now - if someone only
        // ever opened this menu (F6), there was literally no way to panic everything off short of
        // toggling each row by hand.
        Rect panicBtnRect = new(headerRect.xMax - 218f, headerRect.y + headerRect.height * 0.5f - 13f, 90f, 26f);
        if (CustomGui.Button(panicBtnRect, "ВЫКЛ ( ] )", new Color(0.5f, 0.09f, 0.09f, 0.7f), new Color(0.7f, 0.13f, 0.13f, 0.9f), Color.white, 12, 6f))
            PanicHack.Deactivate();

        // Cross-menu switch - reverse direction of OverlayUI's own "Новое меню" header button.
        Rect switchBtnRect = new(headerRect.xMax - 122f, headerRect.y + headerRect.height * 0.5f - 13f, 110f, 26f);
        if (CustomGui.Button(switchBtnRect, "Старое меню", new Color(0.169f, 0.412f, 1f, 0.55f), new Color(0.169f, 0.412f, 1f, 0.85f), Color.white, 13, 6f))
        {
            Hide();
            OverlayUI.Open();
        }

        HandleDrag(headerRect);

        Rect sidebarRect = new(windowRect.x, windowRect.yMin, SidebarWidth, windowRect.height - HeaderHeight);
        DrawSidebar(sidebarRect);

        Rect contentRect = new(sidebarRect.xMax + 6f, windowRect.yMin + 6f,
            windowRect.width - SidebarWidth - 12f, windowRect.height - HeaderHeight - 12f);
        DrawContent(contentRect);
    }

    private static void HandleDrag(Rect headerRect)
    {
        bool overHeader = CustomGui.IsHovered(headerRect);
        if (overHeader && Input.GetMouseButtonDown(0))
            _draggingWindow = true;
        if (Input.GetMouseButtonUp(0)) _draggingWindow = false;

        if (_draggingWindow)
        {
            // Screen.width/height deltas map fine here since we only care about the FRAME-TO-FRAME
            // delta, not an absolute position - CanvasScaler's uniform scale cancels out of a delta.
            Vector2 mouseDelta = new(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
            _windowPos.x += mouseDelta.x * (CustomGui.ScreenWidth / Mathf.Max(Screen.width, 1));
            _windowPos.y -= mouseDelta.y * (CustomGui.ScreenHeight / Mathf.Max(Screen.height, 1));
        }
    }

    // RepoHax groups its sidebar into logical clusters (Stats/Entities/Vision | Valuables/Items/
    // Level/Cosmetic/Misc | Settings) with a thin separator between each, instead of one long flat
    // list - separates "yourself" from "the world" from "extras/tools" here the same way. A
    // separator is drawn right BEFORE the tab index listed.
    private static readonly HashSet<int> SidebarBreaks = new() { 3, 8, 12 };

    private static void DrawSidebar(Rect sidebarRect)
    {
        CustomGui.Rect(sidebarRect, new Color(1f, 1f, 1f, 0.03f), 8f);

        float y = sidebarRect.yMax - 8f;
        for (int i = 0; i < Tabs.Length; i++)
        {
            if (SidebarBreaks.Contains(i))
            {
                y -= 3f;
                CustomGui.Rect(new Rect(sidebarRect.x + 10f, y, sidebarRect.width - 20f, 1f), new Color(1f, 1f, 1f, 0.08f));
                y -= 3f + 4f;
            }

            Rect tabRect = new(sidebarRect.x + 4f, y - 30f, sidebarRect.width - 8f, 28f);
            bool active = i == _activeTab;
            Color bg = active ? new Color(0.169f, 0.412f, 1f, 1f) : new Color(1f, 1f, 1f, 0.04f);
            Color hover = active ? bg : new Color(1f, 1f, 1f, 0.09f);

            if (CustomGui.Button(tabRect, Tabs[i].Label, bg, hover, Color.white, 14, 6f))
            {
                _activeTab = i;
                _scroll = 0f;
            }

            // Marker bar on the active tab. Replaces the emoji prefixes these labels used to
            // carry: Unity's LegacyRuntime font has no emoji glyphs (each surrogate half resolves
            // to a notdef), so those drew as nothing while still costing an indent - a drawn shape
            // can't have that problem.
            if (active)
                CustomGui.Rect(new Rect(tabRect.x + 3f, tabRect.y + 6f, 3f, tabRect.height - 12f), Color.white, 1.5f);

            y -= 28f + 4f;
        }
    }

    private static float _rowCursorY;
    private static Rect _contentClip;

    // RepoHax panel background - 0x18181A, same near-black distinct from the window's own 0x141415
    // (window bg color is set in DrawWindow), 5px rounding to match their WindowR-scale corners on
    // small elements.
    private static readonly Color CardColor = new(0.094f, 0.094f, 0.102f, 1f);
    private const float CardVPad = 6f;

    private static void DrawContent(Rect contentRect)
    {
        _contentClip = contentRect;

        float wheel = Input.mouseScrollDelta.y;
        if (wheel != 0f && CustomGui.IsHovered(contentRect))
            _scroll = Mathf.Clamp(_scroll + wheel * 24f, 0f, Mathf.Max(0f, _contentHeight - contentRect.height));

        float startY = contentRect.yMax + _scroll;

        // Pass 1: measure. _measuring makes RowVisible() false everywhere, so every row helper
        // still advances _rowCursorY (and SectionHeader still records card spans below) but draws
        // nothing - the whole tab renders "for free" query-only.
        _measuring = true;
        _cardBounds.Clear();
        _cardStartY = float.NaN;
        _rowCursorY = startY;
        Tabs[_activeTab].Build?.Invoke();
        if (!float.IsNaN(_cardStartY))
            _cardBounds.Add((_cardStartY, _rowCursorY));
        _contentHeight = startY - _rowCursorY + 8f;
        _measuring = false;

        // Pass 2: card backgrounds, drawn before any row content so rows always paint on top.
        foreach ((float top, float bottom) in _cardBounds)
        {
            Rect cardRect = new(contentRect.x, bottom - CardVPad, contentRect.width, top - bottom + CardVPad * 2f);
            if (cardRect.yMax > contentRect.yMin && cardRect.y < contentRect.yMax)
                CustomGui.Rect(cardRect, CardColor, 8f);
        }

        // Pass 3: the real draw, same starting cursor as pass 1.
        _rowCursorY = startY;
        Tabs[_activeTab].Build?.Invoke();
    }

    // Row helpers - mirror the old OverlayUI's AddToggleRow/AddButtonRow/etc. shape so migrating a
    // tab is a fairly mechanical "read the old row, write the new one" pass. Each one advances
    // _rowCursorY itself and skips drawing (but still advances the cursor, so scroll math stays
    // correct) if the row has scrolled outside contentRect - our own simple substitute for
    // RectMask2D clipping, since CustomGui doesn't have a scissor rect.
    private static Rect NextRow()
    {
        Rect r = new(_contentClip.x, _rowCursorY - RowHeight, _contentClip.width, RowHeight);
        _rowCursorY -= RowHeight + RowSpacing;
        return r;
    }

    private static bool RowVisible(Rect r) => !_measuring && r.yMax > _contentClip.yMin && r.y < _contentClip.yMax;

    internal static void Toggle(string label, ConfigEntry<bool> entry)
    {
        Rect r = NextRow();
        if (!RowVisible(r)) return;
        entry.Value = CustomGui.Toggle(r, label, entry.Value,
            new Color(0.169f, 0.412f, 1f, 1f), new Color(1f, 1f, 1f, 0.15f), Color.white);
    }

    // Same as Toggle, but fires a callback on change - for the handful of toggles that need to do
    // something at the moment of flipping (PacketTestHack.OnToggled), not just get polled by
    // Apply() every tick like the rest of the mod's toggles.
    internal static void Toggle(string label, ConfigEntry<bool> entry, Action<bool> onChanged)
    {
        bool before = entry.Value;
        Toggle(label, entry);
        if (entry.Value != before) onChanged?.Invoke(entry.Value);
    }

    // Target name field - same shared CheatState.FakeSoundTargetName every target-based action
    // reads (PlayerTargets.Picked(), confirmed already used across the whole mod), just typed
    // directly instead of cycled through a player list - simpler than reimplementing a dropdown,
    // and works even for players who've left/aren't currently loaded.
    internal static void PlayerPicker(string label)
    {
        Rect r = NextRow();
        if (!RowVisible(r)) return;
        Rect labelArea = new(r.x, r.y, 170f, r.height);
        Rect fieldArea = new(labelArea.xMax + 4f, r.y + r.height * 0.5f - 13f, r.width - 174f, 26f);
        CustomGui.TextClipped(labelArea, label, 14, Color.white, TextAnchor.MiddleLeft);
        CheatState.FakeSoundTargetName.Value = CustomGui.TextField(fieldArea, "target_name",
            CheatState.FakeSoundTargetName.Value, new Color(1f, 1f, 1f, 0.08f), Color.white,
            new Color(0.169f, 0.412f, 1f, 0.35f));
    }

    // Generic text field row for any string ConfigEntry - PlayerPicker above predates this and
    // stays hardcoded to its one field since changing it isn't needed, but new string rows
    // (HudMediaPlayerTitle) go through this instead of copy-pasting another hardcoded variant.
    internal static void TextFieldRow(string label, string fieldId, ConfigEntry<string> entry)
    {
        Rect r = NextRow();
        if (!RowVisible(r)) return;
        Rect labelArea = new(r.x, r.y, 170f, r.height);
        Rect fieldArea = new(labelArea.xMax + 4f, r.y + r.height * 0.5f - 13f, r.width - 174f, 26f);
        CustomGui.TextClipped(labelArea, label, 14, Color.white, TextAnchor.MiddleLeft);
        entry.Value = CustomGui.TextField(fieldArea, fieldId, entry.Value,
            new Color(1f, 1f, 1f, 0.08f), Color.white, new Color(0.169f, 0.412f, 1f, 0.35f));
    }

    internal static void Button(string label, string btnText, Action action)
    {
        Rect r = NextRow();
        if (!RowVisible(r)) return;
        Rect labelArea = new(r.x, r.y, r.width - 110f, r.height);
        Rect btnArea = new(labelArea.xMax + 4f, r.y + r.height * 0.5f - 13f, 106f, 26f);
        CustomGui.TextClipped(labelArea, label, 14, Color.white, TextAnchor.MiddleLeft);
        if (CustomGui.Button(btnArea, btnText, new Color(0.09f, 0.22f, 0.5f, 1f), new Color(0.169f, 0.412f, 1f, 1f), Color.white, 14, 6f))
            action?.Invoke();
    }

    internal static void FloatStepper(string label, ConfigEntry<float> entry, float min, float max, float step)
    {
        Rect r = NextRow();
        if (!RowVisible(r)) return;
        entry.Value = CustomGui.FloatStepper(r, label, entry.Value, min, max, step,
            new Color(1f, 1f, 1f, 0.08f), Color.white);
    }

    // Live info - computed fresh from the callback every draw, same "just show current state" job
    // as the old menu's AddLiveInfoRow. Two lines tall since a couple of the old rows use "\n".
    internal static void LiveInfo(Func<string> getText)
    {
        Rect r = NextRow();
        _rowCursorY -= RowHeight * 0.6f; // second line's worth of extra room
        if (!RowVisible(r)) return;
        Rect twoLine = new(r.x, r.y - RowHeight * 0.6f, r.width, r.height + RowHeight * 0.6f);

        // Per line, so a single long one (a file path, a stats readout) gets trimmed to the panel
        // instead of drawing straight over the game behind the menu.
        string text = getText?.Invoke() ?? "";
        string[] lines = text.Split('\n');
        float lineHeight = twoLine.height / Mathf.Max(lines.Length, 1);
        for (int i = 0; i < lines.Length; i++)
        {
            Rect lineRect = new(twoLine.x, twoLine.yMax - lineHeight * (i + 1), twoLine.width, lineHeight);
            CustomGui.TextClipped(lineRect, lines[i], 13, new Color(0.75f, 0.75f, 0.78f, 1f), TextAnchor.MiddleLeft);
        }
    }

    internal static void IntStepper(string label, ConfigEntry<int> entry, int min, int max, int step)
    {
        Rect r = NextRow();
        if (!RowVisible(r)) return;
        entry.Value = CustomGui.IntStepper(r, label, entry.Value, min, max, step,
            new Color(1f, 1f, 1f, 0.08f), Color.white);
    }

    internal static void Cycle(string label, ConfigEntry<string> entry, string[] options)
    {
        Rect r = NextRow();
        if (!RowVisible(r)) return;
        entry.Value = CustomGui.Cycle(r, label, entry.Value, options,
            new Color(1f, 1f, 1f, 0.08f), new Color(1f, 1f, 1f, 0.15f), Color.white);
    }

    internal static void SectionHeader(string label)
    {
        // _rowCursorY right here, before NextRow() touches it, is exactly the bottom edge of
        // whatever card came before (or the content top, for the very first section) - see
        // DrawContent's own comment for the two-pass reasoning this depends on.
        if (_measuring)
        {
            if (!float.IsNaN(_cardStartY))
                _cardBounds.Add((_cardStartY, _rowCursorY));
            _cardStartY = _rowCursorY;
        }

        Rect r = NextRow();
        if (RowVisible(r))
            CustomGui.SectionHeader(r, label, new Color(0.573f, 0.6f, 0.659f, 1f));
    }

    // Old menu's AddColorCycleRow let you cycle a small fixed set of hex presets on a string
    // ConfigEntry - same idea here, just naming the presets instead of showing raw hex, matching
    // the fixed-palette approach CosmeticHack's eye colors already settled on this session (see
    // that file's own comment for why arbitrary hex/rainbow got dropped in favor of this).
    private static readonly string[] ColorPresetNames = { "#FF3B30", "#FF9500", "#FFCC00", "#34C759", "#0A84FF", "#AF52DE", "#FFFFFF", "#00FFFF" };

    // Per-row scan caches for CycleAndAction - see that method's own comment for why these aren't
    // rescanned every frame.
    private static List<string> _itemSpawnerNamesCache;
    private static List<string> _levelLoaderNamesCache;
    private static List<string> _moduleLoaderNamesCache;
    private static List<string> _stealItemNamesCache;

    internal static void ColorCycle(string label, ConfigEntry<string> entry)
    {
        Rect r = NextRow();
        if (!RowVisible(r)) return;
        entry.Value = CustomGui.Cycle(r, label, entry.Value, ColorPresetNames,
            new Color(1f, 1f, 1f, 0.08f), new Color(1f, 1f, 1f, 0.15f), Color.white);
    }

    // Old menu's three "cycle a scanned name, then act on it" rows (item spawner/level loader/
    // module loader) rescan on every click, not every frame - the source lists (StatsManager.
    // itemDictionary, RunManager.instance.levels, ...) are cheap but not free, and this is an
    // immediate-mode UI redrawing the whole tab every Tick(). `cache` is the caller's own static
    // field (one per row) so the scanned list survives across frames until the next click.
    internal static void CycleAndAction(string label, ConfigEntry<string> nameEntry, ref List<string> cache,
        Func<List<string>> scan, string actionLabel, Action action)
    {
        cache ??= scan();
        if (cache.Count > 0 && !cache.Contains(nameEntry.Value))
            nameEntry.Value = cache[0];
        else if (cache.Count == 0 && string.IsNullOrEmpty(nameEntry.Value))
            nameEntry.Value = "None";

        Rect r = NextRow();
        if (!RowVisible(r)) return;

        Rect labelArea = new(r.x, r.y, r.width - 264f, r.height);
        Rect cycleArea = new(labelArea.xMax + 4f, r.y + r.height * 0.5f - 13f, 170f, 26f);
        Rect btnArea = new(cycleArea.xMax + 4f, r.y + r.height * 0.5f - 13f, 90f, 26f);

        CustomGui.TextClipped(labelArea, label, 13, Color.white, TextAnchor.MiddleLeft);

        string display = cache.Count > 0 ? nameEntry.Value : "None";
        if (CustomGui.Button(cycleArea, display, new Color(1f, 1f, 1f, 0.08f), new Color(1f, 1f, 1f, 0.15f), Color.white, 13, 6f))
        {
            cache = scan();
            nameEntry.Value = cache.Count == 0 ? "None" : cache[(cache.IndexOf(nameEntry.Value) + 1) % cache.Count];
        }

        if (CustomGui.Button(btnArea, actionLabel, new Color(0.09f, 0.22f, 0.5f, 1f), new Color(0.169f, 0.412f, 1f, 1f), Color.white, 13, 6f))
            action?.Invoke();
    }

    // Same rescan-on-click cache behaviour as CycleAndAction, minus the action button - for a
    // scanned name that just feeds a ticking hack's own Apply() (ItemStealHack.StealHeldItemName)
    // rather than needing a separate "do it now" click.
    internal static void CycleWithScan(string label, ConfigEntry<string> nameEntry, ref List<string> cache, Func<List<string>> scan)
    {
        cache ??= scan();
        if (cache.Count > 0 && !cache.Contains(nameEntry.Value))
            nameEntry.Value = cache[0];
        else if (cache.Count == 0 && string.IsNullOrEmpty(nameEntry.Value))
            nameEntry.Value = "None";

        Rect r = NextRow();
        if (!RowVisible(r)) return;

        Rect labelArea = new(r.x, r.y, r.width - 174f, r.height);
        Rect cycleArea = new(labelArea.xMax + 4f, r.y + r.height * 0.5f - 13f, 170f, 26f);

        CustomGui.TextClipped(labelArea, label, 13, Color.white, TextAnchor.MiddleLeft);

        string display = cache.Count > 0 ? nameEntry.Value : "None";
        if (CustomGui.Button(cycleArea, display, new Color(1f, 1f, 1f, 0.08f), new Color(1f, 1f, 1f, 0.15f), Color.white, 13, 6f))
        {
            cache = scan();
            nameEntry.Value = cache.Count == 0 ? "None" : cache[(cache.IndexOf(nameEntry.Value) + 1) % cache.Count];
        }
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
        ("Разработчик", BuildDevTab),
        ("Скрипты", BuildScriptsTab),
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
        LiveInfo(() => "Камера плавно тянется к цели вместо мгновенного прыжка - тот же " +
            "\"смуз-камера\" приём, что в визуальных клиентах Minecraft. Заодно убирает дёрганье " +
            "на большой дистанции у стен: SphereCast-проверка стен пересчитывается каждый кадр и " +
            "может дёргаться между \"чисто\"/\"упёрлось\" от долей градуса поворота - плавность " +
            "гасит именно этот скачок.");
    }

    private static void BuildHudTab()
    {
        Toggle("HUD включён (виджеты поверх игры, отдельно от меню)", CheatState.HudEnabled);
        Toggle("Режим редактирования (перетаскивание/цвет/вкл-выкл)", CheatState.HudEditMode);
        Toggle("Авто-колонка (виджеты стакаются вплотную)", CheatState.HudAutoStack);
        LiveInfo(() => "В режиме редактирования: тащите виджет за шапку, \"x\" убирает его, " +
            "цветной квадрат меняет акцентный цвет. Убранные виджеты возвращаются кнопками сверху экрана.");

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

        SectionHeader("Виджет «Бинды» (реальные привязанные клавиши)");
        Toggle("Показывать", CheatState.HudKeybinds.Enabled);
        ColorCycle("  -> Цвет", CheatState.HudKeybinds.Color);
        LiveInfo(() => "Выключен по умолчанию - список тех же биндов, что заданы средним кликом по строкам меню.");
    }

    private static void BuildVisionTab()
    {
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
        Button("Возрождение себя (гостю нужен обход RPC ниже, хосту не нужен)", "Возродиться", ReviveHack.ReviveSelf);
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
        Toggle("Ноуклип (провалиться сквозь пол)", CheatState.Noclip);
        Toggle("Движение мёртвым", CheatState.DeadFly);
        FloatStepper("Скорость движения мёртвым", CheatState.DeadFlySpeed, 2f, 50f, 1f);
    }

    private static void BuildPlayersTab()
    {
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
        Cycle("Имя", CheatState.SpawnValuableName, EconomyHack.ScanValuableNames().ToArray());
        Button("Заспавнить выбранную ценность в зоне эвакуации", "Заспавнить", EconomyHack.SpawnValuable);

        SectionHeader("Крылья и глаза");
        Toggle("Принудительные крылья (только визуал)", CheatState.ForceWings);
        Toggle("Розовые крылья (второй реальный цвет игры)", CheatState.WingsPink);
        Toggle("Цвет глаз (фикс. палитра, видно другим)", CheatState.EyeColor);
        Cycle("Цвет глаз", CheatState.EyeColorName, CosmeticHack.EyeColorNames);
        Toggle("Большие зрачки (видно другим, если у них тоже наш мод)", CheatState.BigPupils);

        SectionHeader("Смена имени (реально для всех, хост/гость)");
        TextFieldRow("Новое имя", "name_changer_text", CheatState.NameChangerText);
        Button("Применить имя", "Сменить", () => NameChangerHack.SetName(CheatState.NameChangerText.Value));
        Toggle("Маскировка под хоста (авто, копирует ник хоста себе)", CheatState.MimicHostName);
        Toggle("Маскировка скина под хоста (авто, копирует косметику и цвета)", CheatState.MimicHostSkin);
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

        SectionHeader("Что слышат от вас другие (реально передаётся всем)");
        Toggle("Лаганый голос (другие слышат заикания, вид обычный)", CheatState.GlitchyVoice);
        FloatStepper("  -> Длина рывка, сек", CheatState.GlitchyVoiceInterval, 0.05f, 1f, 0.05f);
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

        SectionHeader("Соседние комнаты");
        IntStepper("Сколько ещё комнат создавать", CheatState.MultiRoomExtraCount, 1, 10, 1);
        IntStepper("Лимит игроков в каждой", CheatState.MultiRoomMaxPlayers, 6, 100, 2);
        Button("Создать N независимых комнат с текущим именем", "Создать", MultiRoomHack.CreateSiblingRooms);
    }

    private static void BuildDevTab()
    {
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

        Rect editorRow = NextRow();
        const float editorExtra = 140f;
        _rowCursorY -= editorExtra;
        if (RowVisible(editorRow))
        {
            Rect editorArea = new(editorRow.x, editorRow.y - editorExtra, editorRow.width, editorRow.height + editorExtra);
            _scriptDraft = CustomGui.TextArea(editorArea, "script_editor", _scriptDraft,
                new Color(0f, 0f, 0f, 0.35f), Color.white, new Color(0.169f, 0.412f, 1f, 0.12f), 13);
        }

        Rect actionRow = NextRow();
        if (RowVisible(actionRow))
        {
            Rect nameField = new(actionRow.x, actionRow.y, 160f, actionRow.height);
            Rect runBtn = new(nameField.xMax + 6f, actionRow.y + 2f, 100f, actionRow.height - 4f);
            Rect saveBtn = new(runBtn.xMax + 6f, actionRow.y + 2f, 100f, actionRow.height - 4f);
            Rect deleteBtn = new(saveBtn.xMax + 6f, actionRow.y + 2f, 100f, actionRow.height - 4f);

            _scriptModuleName = CustomGui.TextField(nameField, "script_name", _scriptModuleName,
                new Color(1f, 1f, 1f, 0.08f), Color.white, new Color(0.169f, 0.412f, 1f, 0.35f));

            if (CustomGui.Button(runBtn, "Запустить", new Color(0.09f, 0.22f, 0.5f, 1f), new Color(0.169f, 0.412f, 1f, 1f), Color.white, 13, 6f))
            {
                _scriptStatus = "Выполняется...";
                _scriptStatusOk = true;
                ScriptEngine.StartRun(_scriptDraft, SetScriptStatus);
            }
            if (CustomGui.Button(saveBtn, "Сохранить", new Color(1f, 1f, 1f, 0.08f), new Color(1f, 1f, 1f, 0.15f), Color.white, 13, 6f))
                SetScriptStatus(ScriptModuleManager.Add(_scriptModuleName.Trim(), _scriptDraft));
            if (CustomGui.Button(deleteBtn, "Удалить", new Color(0.4f, 0.1f, 0.1f, 1f), new Color(0.6f, 0.15f, 0.15f, 1f), Color.white, 13, 6f))
                ScriptModuleManager.Remove(_scriptModuleName.Trim());
        }

        if (!string.IsNullOrEmpty(_scriptStatus))
            LiveInfo(() => _scriptStatus);

        SectionHeader("Сохранённые модули");
        foreach (var module in ScriptModuleManager.Modules)
        {
            Rect row = NextRow();
            if (!RowVisible(row)) continue;

            Rect nameArea = new(row.x, row.y, row.width - 220f, row.height);
            Rect loadBtn = new(nameArea.xMax + 4f, row.y + 2f, 100f, row.height - 4f);
            Rect runBtn = new(loadBtn.xMax + 4f, row.y + 2f, 100f, row.height - 4f);

            CustomGui.TextClipped(nameArea, module.Name, 14, Color.white, TextAnchor.MiddleLeft);

            if (CustomGui.Button(loadBtn, "В редактор", new Color(1f, 1f, 1f, 0.08f), new Color(1f, 1f, 1f, 0.15f), Color.white, 12, 6f))
            {
                _scriptDraft = module.Source;
                _scriptModuleName = module.Name;
            }
            if (CustomGui.Button(runBtn, "Запустить", new Color(0.09f, 0.22f, 0.5f, 1f), new Color(0.169f, 0.412f, 1f, 1f), Color.white, 12, 6f))
            {
                _scriptStatus = "Выполняется...";
                _scriptStatusOk = true;
                ScriptModuleManager.Run(module.Name, SetScriptStatus);
            }
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
        Toggle("Невидимка (модель улетает в небо у других)", CheatState.Invisible);
        FloatStepper("  -> Высота", CheatState.InvisibleHeight, 500f, 20000f, 500f);
        Toggle("  -> Звучать нормально во время разговора (тогда видно, пока говорите)", CheatState.InvisibleRevealWhileTalking);
        Toggle("Дёрганый фонарик (у других луч мечется, у вас нормально)", CheatState.GlitchyFlashlight);
        FloatStepper("  -> Радиус болтания, м", CheatState.GlitchyFlashlightRadius, 1f, 10f, 0.5f);
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
