using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RepoCheatMenu;

// RepoHax draws its menu as an always-available overlay rendered directly over gameplay
// (a DirectX Present hook drawing ImGui), independent of the game's own menu/page stack.
// We can't hook Present from managed C#, but a Canvas in ScreenSpaceOverlay mode is rendered
// by Unity's own pipeline every frame regardless of whether our scripts' Update/OnGUI ever
// fire (which - unlike Time.deltaTime's getter - they don't, in this game/setup). So the menu
// itself is built once and just shown/hidden; only the open/close hotkey check and live value
// refresh ride on the confirmed-working Time.deltaTime tick. Pointer events (drag, click,
// hold) come straight from EventSystem's own input handling, not our Update, so those work
// normally - confirmed by clicks/toggles already working before this pass.
internal static class OverlayUI
{
    private static Canvas _canvas;
    private static RectTransform _window;
    private static RectTransform _tabBar;
    private static RectTransform _content;
    private static readonly List<GameObject> _contentRows = new();
    private static readonly Dictionary<string, Action> _tabs = new();
    private static readonly Dictionary<string, Button> _tabButtons = new();
    private static string _activeTab;
    private static bool _wasCursorVisible;
    private static CursorLockMode _wasLockState;

    private static Action _repeatAction;
    private static float _repeatNextTime;
    private static bool _repeatStarted;
    private static Action _activeKeybindRefresh;
    private static readonly List<(Text Label, Func<string> Source)> _liveTexts = new();

    // Scripts tab state - kept outside the rebuilt row GameObjects (SelectTab destroys and
    // rebuilds every row in the tab on each visit/refresh) so the draft text and last
    // run/save result survive a rebuild instead of resetting every time a module list changes.
    private static string _scriptDraft = "";
    private static string _scriptModuleName = "";
    private static string _scriptStatus = "";
    private static bool _scriptStatusOk = true;

    // Apple system-dark palette: neutral translucent glass (not tinted black-red), a single
    // blue accent for anything active/on (macOS/iOS's default systemwide accent, not tied to any
    // one app), and system red reserved specifically for the one genuinely destructive/critical
    // control (the panic switch, the close button) - per the "material weight encodes hierarchy"
    // and restraint principles, color carries meaning instead of just decorating everything.
    // Palette values below match RepoHax's overlay theme numerically (WindowBg/SidePanelBg/BtnBg/
    // BtnBgActive/ActiveColor/DescColor - plain hex color constants, not copyrightable expression,
    // read from its own widgets.cpp) - our rendering code is independently written Unity UGUI,
    // RepoHax's is a native ImGui-style renderer, nothing about the drawing code is shared.
    private static readonly Color WindowBg = new(0.078f, 0.078f, 0.082f, 0.97f);
    private static readonly Color HeaderBg = new(0.114f, 0.118f, 0.125f, 1f);
    private static readonly Color CardBg = new(1f, 1f, 1f, 0.06f);
    private static readonly Color AccentBlue = new(0.169f, 0.412f, 1f, 1f);
    private static readonly Color AccentBlueDim = new(0.09f, 0.22f, 0.5f, 1f);
    private static readonly Color DangerRed = new(1f, 0.27f, 0.23f, 1f);
    private static readonly Color DangerRedDim = new(0.55f, 0.16f, 0.14f, 1f);
    private static readonly Color ButtonIdleColor = new(1f, 1f, 1f, 0.11f);
    private static readonly Color TabIdleColor = new(1f, 1f, 1f, 0.045f);
    private static readonly Color TabActiveColor = AccentBlue;
    private static readonly Color SectionHeaderColor = new(0.573f, 0.6f, 0.659f, 1f);

    private static Sprite _roundedSprite;
    private static Sprite _circleSprite;

    // Current "card" (grouped section) rows are being added into - iOS/macOS Settings-style
    // grouped list: a small gray all-caps label, then a rounded card holding the related rows,
    // instead of everything sitting flat in one undifferentiated column. Reset to null on every
    // tab switch so the first row lazily opens an unlabeled card via NewRow, and
    // AddSectionHeader starts a fresh labeled one whenever a tab wants to split further.
    private static RectTransform _currentCard;

    // Toggle-switch knob positions currently easing toward their target x (on/off) - driven from
    // Tick(), the one thing in this whole menu confirmed to fire reliably every frame in this
    // game/setup (see the class comment). A real interruptible spring (per Apple's own fluid-
    // interface guidance) needs live velocity tracking across true per-frame Update calls, which
    // this game doesn't reliably give us; this is the practical equivalent available here - a
    // simple critically-damped-style ease (no overshoot) toward the target each tick, close
    // enough to read as "settling" rather than "snapping".
    private static readonly Dictionary<RectTransform, float> _knobTargets = new();

    // Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd") looked like the easy way to
    // get rounded corners, but that's an editor-only default asset - this game's player build
    // never references it, so Unity strips it from the build entirely and the lookup fails every
    // single time (confirmed in LogOutput.log: "could not be loaded from the resource file",
    // spammed thousands of times because every tab switch rebuilds all its rows from scratch and
    // each panel/button re-tried the same failing lookup - "??=" never sticks since the result
    // stayed null). That log spam alone was enough to stall the game hard enough to cause a
    // Photon server-timeout kick. Generating our own tiny rounded-rect texture at runtime instead
    // doesn't depend on any builtin/editor-only asset being present in the build, so it can't fail.
    private static Sprite RoundedSprite => _roundedSprite ??= BuildRoundedSprite();

    private static Sprite BuildRoundedSprite()
    {
        const int size = 32;
        const float radius = 14f; // softer, more continuous corner than a typical UI rect - closer to SF/macOS control shapes

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                float dist = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                byte alpha = (byte)(Mathf.Clamp01(radius - dist + 0.5f) * 255);
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size, 0,
            SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
    }

    // Plain circle (Simple, not Sliced) - used only for the toggle-switch knob, so it stays a
    // crisp circle at any size instead of inheriting the capsule's 9-slice corner math.
    private static Sprite CircleSprite => _circleSprite ??= BuildCircleSprite();

    private static Sprite BuildCircleSprite()
    {
        const int size = 32;
        const float radius = size / 2f;

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        var center = new Vector2(size / 2f, size / 2f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                byte alpha = (byte)(Mathf.Clamp01(radius - dist + 0.5f) * 255);
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    public static bool IsOpen => _canvas != null && _canvas.gameObject.activeSelf;

    public static void Toggle()
    {
        EnsureBuilt();
        if (IsOpen) Hide();
        else Show();
    }

    public static void Close()
    {
        if (IsOpen) Hide();
    }

    // Cross-menu switch button's target - opens this menu without toggling (unlike Toggle(), never
    // closes it if already open), matching CustomOverlayUI.Show()'s equivalent on the other side.
    public static void Open()
    {
        if (!IsOpen) Show();
    }

    // Called every Time.deltaTime tick so held +/- buttons repeat without needing a
    // coroutine (coroutines on a disabled-Update component would be just as unreliable).
    public static void Tick()
    {
        // Cursor.lockState alone doesn't stop the game's own camera look - InputManager.GetMouseX/Y
        // (confirmed in the decompiled source) read raw mouse delta straight from the new Input
        // System regardless of cursor lock state, so with the menu open you'd still spin the
        // camera while trying to click a button. DisableAiming(float) is the game's own public,
        // official way to zero that out - it's a decaying timer (confirmed: disableAimingTimer
        // ticks down every frame), same mechanism the game's own menus/pause screen use, so this
        // just needs refreshing every tick the menu is open instead of a one-shot call.
        if (IsOpen && InputManager.instance != null)
            InputManager.instance.DisableAiming(0.5f);

        if (_activeKeybindRefresh != null && KeybindManager.Capturing == null)
        {
            _activeKeybindRefresh();
            _activeKeybindRefresh = null;
        }

        UpdateKnobTweens();

        if (IsOpen)
        {
            foreach (var live in _liveTexts)
                if (live.Label != null) live.Label.text = live.Source();
        }

        if (_repeatAction == null) return;
        if (Time.unscaledTime < _repeatNextTime) return;

        _repeatAction();
        _repeatNextTime = Time.unscaledTime + (_repeatStarted ? 0.06f : 0.4f);
        _repeatStarted = true;
    }

    private static void StartRepeat(Action action)
    {
        _repeatAction = action;
        _repeatStarted = false;
        _repeatNextTime = Time.unscaledTime;
    }

    private static void StopRepeat()
    {
        _repeatAction = null;
    }

    private static void UpdateKnobTweens()
    {
        if (_knobTargets.Count == 0) return;

        List<RectTransform> done = null;
        foreach (var kv in _knobTargets)
        {
            RectTransform knob = kv.Key;
            if (knob == null) { (done ??= new()).Add(knob); continue; }

            float target = kv.Value;
            float current = knob.anchoredPosition.x;
            float next = Mathf.Lerp(current, target, 0.35f);
            if (Mathf.Abs(next - target) < 0.5f)
            {
                next = target;
                (done ??= new()).Add(knob);
            }
            knob.anchoredPosition = new Vector2(next, knob.anchoredPosition.y);
        }

        if (done != null)
            foreach (RectTransform k in done) _knobTargets.Remove(k);
    }

    private static void Show()
    {
        EnsureBuilt();
        _canvas.gameObject.SetActive(true);
        _wasCursorVisible = Cursor.visible;
        _wasLockState = Cursor.lockState;
        // CursorLockMode.None leaves the cursor completely free of the game window - on a
        // multi-monitor setup that means it can wander straight onto the second screen while
        // clicking around the menu, even in fullscreen. Confined keeps it free to move (still
        // clicks buttons normally) but clamped to the game window's own bounds - None never
        // needed to be that permissive, we just needed "not locked/hidden".
        Cursor.lockState = CursorLockMode.Confined;
        Cursor.visible = true;
        if (_activeTab == null && _tabs.Count > 0)
        {
            foreach (var kv in _tabs) { SelectTab(kv.Key); break; }
        }
    }

    private static void Hide()
    {
        if (_canvas == null) return;
        _canvas.gameObject.SetActive(false);
        StopRepeat();
        KeybindManager.Capturing = null;
        _activeKeybindRefresh = null;
        Cursor.lockState = _wasLockState;
        Cursor.visible = _wasCursorVisible;
    }

    private static void EnsureBuilt()
    {
        if (_canvas != null) return;

        if (UnityEngine.Object.FindObjectOfType<EventSystem>() == null)
        {
            var esGo = new GameObject("RepoCheatMenu_EventSystem");
            UnityEngine.Object.DontDestroyOnLoad(esGo);
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<StandaloneInputModule>();
        }

        var canvasGo = new GameObject("RepoCheatMenu_Overlay");
        UnityEngine.Object.DontDestroyOnLoad(canvasGo);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 10000;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        var window = CreatePanel(canvasGo.transform, WindowBg);
        _window = window;
        // 2026-09-11: matched to CustomOverlayUI's window (900x580) at the user's request - was
        // 800x740 (offsetMin/Max below are edges relative to the anchor point, not a direct
        // width/height pair - anchorMin==anchorMax==(0, 0.5) pins the anchor to the canvas' left
        // edge at vertical center, so xMin/xMax=offsetMin.x/offsetMax.x give width directly, but
        // yMin/yMax=offsetMin.y/offsetMax.y are symmetric around that center line, not from 0).
        // Same rendering (uGUI, not CustomGui) - only the two numbers below changed.
        SetRect(window, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(30, -290), new Vector2(930, 290));
        var windowLayout = window.gameObject.AddComponent<VerticalLayoutGroup>();
        windowLayout.padding = new RectOffset(8, 8, 8, 8);
        windowLayout.spacing = 6;
        windowLayout.childControlWidth = true;
        windowLayout.childControlHeight = true;
        windowLayout.childForceExpandWidth = true;
        windowLayout.childForceExpandHeight = false;

        var headerGo = CreatePanel(window.transform, HeaderBg);
        var headerLe = headerGo.gameObject.AddComponent<LayoutElement>();
        headerLe.preferredHeight = 34;
        headerLe.minHeight = 34;
        var headerLayout = headerGo.gameObject.AddComponent<HorizontalLayoutGroup>();
        headerLayout.padding = new RectOffset(8, 4, 0, 0);
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childForceExpandHeight = false;
        headerLayout.childAlignment = TextAnchor.MiddleLeft;
        headerGo.gameObject.AddComponent<WindowDragHandler>().Target = window;

        var accentStrip = CreatePanel(headerGo.transform, AccentBlue, rounded: false);
        var accentLe = accentStrip.gameObject.AddComponent<LayoutElement>();
        accentLe.preferredWidth = 4;
        accentLe.preferredHeight = 24;

        var title = CreateText(headerGo.transform, "REPO ЧИТ-МЕНЮ   <size=12><color=#9A9AA6>тащить за шапку  •  средняя кнопка мыши по строке = бинд, Esc = снять</color></size>", 18, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.92f));
        title.fontStyle = FontStyle.Bold;
        var titleLe = title.gameObject.AddComponent<LayoutElement>();
        titleLe.flexibleWidth = 1;

        // Kill switch: closes the menu and forces every toggle off (PanicHack.Deactivate), so
        // nothing about the mod is active or reachable via RightShift until ']' is pressed -
        // meant for actually playing a real match, not just tucking the window away. Red like
        // the close button, deliberately - both are the only two "critical/irreversible-feeling"
        // controls in the header, everything else in the menu is the neutral accent blue.
        var panicBtn = CreateButton(headerGo.transform, "ВЫКЛ ( ] )", PanicHack.Deactivate);
        var panicLe = panicBtn.gameObject.AddComponent<LayoutElement>();
        panicLe.preferredWidth = 92;
        panicLe.preferredHeight = 28;
        SetPanelColor(panicBtn, DangerRedDim);

        // Cross-menu switch - closes this one, opens the CustomGui-based menu (F6) in its place.
        // Symmetric button lives in CustomOverlayUI's own header for the reverse direction.
        var switchBtn = CreateButton(headerGo.transform, "Новое меню", () => { Hide(); CustomOverlayUI.Show(); });
        var switchLe = switchBtn.gameObject.AddComponent<LayoutElement>();
        switchLe.preferredWidth = 92;
        switchLe.preferredHeight = 28;
        SetPanelColor(switchBtn, AccentBlue);

        var closeBtn = CreateButton(headerGo.transform, "X", Hide);
        var closeLe = closeBtn.gameObject.AddComponent<LayoutElement>();
        closeLe.preferredWidth = 28;
        closeLe.preferredHeight = 28;
        SetPanelColor(closeBtn, DangerRedDim);

        var bodyGo = CreatePanel(window.transform, new Color(0, 0, 0, 0));
        var bodyLe = bodyGo.gameObject.AddComponent<LayoutElement>();
        bodyLe.flexibleHeight = 1;
        bodyLe.minHeight = 310;
        var bodyLayout = bodyGo.gameObject.AddComponent<HorizontalLayoutGroup>();
        bodyLayout.spacing = 8;
        bodyLayout.childControlWidth = true;
        bodyLayout.childControlHeight = true;
        bodyLayout.childForceExpandWidth = false;
        bodyLayout.childForceExpandHeight = true;

        var tabBarGo = CreatePanel(bodyGo.transform, new Color(1f, 1f, 1f, 0.02f));
        _tabBar = tabBarGo;
        var tabBarLe = tabBarGo.gameObject.AddComponent<LayoutElement>();
        tabBarLe.preferredWidth = 210; // matched to CustomOverlayUI.SidebarWidth
        tabBarLe.minWidth = 210;
        var tabLayout = tabBarGo.gameObject.AddComponent<VerticalLayoutGroup>();
        tabLayout.padding = new RectOffset(4, 4, 6, 6);
        tabLayout.spacing = 4;
        tabLayout.childControlWidth = true;
        tabLayout.childControlHeight = true;
        tabLayout.childForceExpandWidth = true;
        tabLayout.childForceExpandHeight = false;

        // Scrollable content area: a tab can grow past the window's fixed height without
        // overlapping anything below it - mouse wheel / drag to scroll via ScrollRect.
        var scrollGo = CreatePanel(bodyGo.transform, new Color(1, 1, 1, 0.03f));
        var scrollLe = scrollGo.gameObject.AddComponent<LayoutElement>();
        scrollLe.flexibleWidth = 1;
        var scrollRect = scrollGo.gameObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 24f;

        var viewportGo = CreatePanel(scrollGo.transform, new Color(0, 0, 0, 0));
        SetRect(viewportGo, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        viewportGo.gameObject.AddComponent<RectMask2D>();
        scrollRect.viewport = viewportGo;

        var contentGo = CreatePanel(viewportGo.transform, new Color(0, 0, 0, 0));
        _content = contentGo;
        SetRect(contentGo, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
        contentGo.pivot = new Vector2(0.5f, 1f);
        var contentLayout = contentGo.gameObject.AddComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 4;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        contentLayout.padding = new RectOffset(8, 8, 8, 8);
        var contentFitter = contentGo.gameObject.AddComponent<ContentSizeFitter>();
        contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scrollRect.content = contentGo;

        BuildTabs();
        _canvas.gameObject.SetActive(false);
    }

    // Reorganized from the original 11 single-topic tabs into fewer, broader groups - the old
    // layout had several tabs holding just one or two rows (Entities, Valuables, Items, Voice)
    // which meant constant tab-hopping to reach related settings. "Player" holds
    // stat/economy/protection toggles, "Movement" holds everything about how you physically
    // move (speed/jump/fly/noclip/kolobok), "World" holds anything affecting the level/items
    // around you, "Visuals" merges ESP+Vision (and is where the ambient effects tab content
    // will land), "Misc" absorbs the old Voice/Repo Debug single-purpose tabs.
    private static void BuildTabs()
    {
        AddTab("Игрок", () =>
        {
            AddToggleRow("Режим бога (полный игнор входящего урона)", CheatState.GodMode);
            AddButtonRow("Лечение (работает у гостя)", "Лечить", HealHack.HealFull);
            AddToggleRow("Высокая сила (сила хвата/броска - реально работает только у хоста/соло)", CheatState.HighStrength);
            AddIntStepperRow("Уровень силы", CheatState.StrengthLevel, 1, 50, 1);
            AddToggleRow("Живой оверрайд силы (без потолка апгрейдов, через OverrideGrabStrength)", CheatState.StrengthOverrideMode);
            AddFloatStepperRow("Значение оверрайда силы", CheatState.StrengthOverrideValue, 1f, 50f, 1f);
            AddToggleRow("Безлимитная дальность хвата (работает и у гостя)", CheatState.UnlimitedGrabRange);
            AddIntStepperRow("Уровень дальности хвата", CheatState.GrabRangeLevel, 1, 50, 1);
            AddToggleRow("Хват сквозь стены (луч хвата бьёт через препятствия)", CheatState.GrabThroughWalls);
            AddToggleRow("Без перегрузки хвата (блокирует штраф за перегруз)", CheatState.NoOvercharge);
            AddToggleRow("Бесконечные батарейки (фонарик и голова не садятся)", CheatState.InfiniteBatteries);
            AddToggleRow("Мощный хват (работает у гостя)", CheatState.PowerGrab);
            AddToggleRow("Мягкий сброс (без броска при отпускании - только хост/соло)", CheatState.GentleDrop);
            AddToggleRow("Обход MasterOnlyRPC для себя (нужен гостю для возрождения себя ниже)", CheatState.MasterRpcSelfBypass);
            AddButtonRow("Возрождение себя (гостю нужен тоггл выше, хосту не нужен)", "Возродиться", ReviveHack.ReviveSelf);
            AddButtonRow("Мгновенная смерть себе", "Убить себя", InstantKillHack.KillSelf);
        });

        AddTab("Движение", () =>
        {
            AddToggleRow("Бесконечная выносливость (стамина не тратится)", CheatState.InfiniteStamina);
            AddFloatStepperRow("Скорость ходьбы x", CheatState.SpeedMultiplier, 0.5f, 6f, 0.1f);
            AddFloatStepperRow("Скорость бега x", CheatState.SprintSpeedMultiplier, 0.5f, 6f, 0.1f);
            AddFloatStepperRow("Скорость в приседе x", CheatState.CrouchSpeedMultiplier, 0.5f, 6f, 0.1f);
            AddToggleRow("Бесконечные прыжки (двойной/тройной прыжок без апгрейда)", CheatState.InfiniteJumps);
            AddFloatStepperRow("Сила прыжка x", CheatState.JumpMultiplier, 1f, 5f, 0.25f);
            AddToggleRow("Без нокдауна (не улетаете в тумбл от ударов)", CheatState.NoKnockdown);
            AddToggleRow("Без урона от падения (высота не считается)", CheatState.NoFallDamage);
            AddToggleRow("Анти-отбрасывание (блок толчков от ударов/взрывов)", CheatState.AntiKnockback);
            AddToggleRow("Страфы (одинаковая скорость + буст в любую сторону)", CheatState.Strafe);
            AddFloatStepperRow("Множитель страфа", CheatState.StrafeMultiplier, 1f, 3f, 0.1f);
            AddToggleRow("Полёт (отключает гравитацию, летите камерой, сквозь стены тоже)", CheatState.Fly);
            AddFloatStepperRow("Скорость полёта", CheatState.FlySpeed, 2f, 50f, 1f);
            AddToggleRow("Спринт удваивает скорость полёта", CheatState.FlySprintBoost);
            AddToggleRow("Ноуклип (провалиться сквозь пол, без полёта)", CheatState.Noclip);
            AddToggleRow("Движение мёртвым (до возрождения; двигает вашу голову, камера сама следует)", CheatState.DeadFly);
            AddFloatStepperRow("Скорость движения мёртвым", CheatState.DeadFlySpeed, 2f, 50f, 1f);
            AddToggleRow("Режим Колобка (авто-прыжки после смерти)", CheatState.KolobokMode);
        });

        // New tab, split out of "Визуал" - groups everything that moves the render camera itself,
        // so the smoothing control sits right next to what it's smoothing instead of buried in a
        // long tab. Same content as CustomOverlayUI's own Camera tab.
        AddTab("Камера", () =>
        {
            AddSectionHeader("Вид от третьего лица");
            AddToggleRow("Включено", CheatState.ThirdPerson);
            AddFloatStepperRow("Дистанция", CheatState.ThirdPersonDistance, 0.5f, 8f, 0.5f);
            AddFloatStepperRow("Высота", CheatState.ThirdPersonHeight, -2f, 2f, 0.25f);
            AddFloatStepperRow("Смещение вбок", CheatState.ThirdPersonHorizontal, -2f, 2f, 0.25f);
            AddFloatStepperRow("Плавность (меньше = плавнее/с задержкой)", CheatState.ThirdPersonSmoothness, 1f, 30f, 1f);
            AddFloatStepperRow("Масштаб своей модели (косметика)", CheatState.ThirdPersonModelScale, 0.1f, 3f, 0.1f);
            AddLiveInfoRow(() => "Камера плавно тянется к цели вместо мгновенного прыжка - тот же " +
                "\"смуз-камера\" приём, что в визуальных клиентах Minecraft. Заодно убирает " +
                "дёрганье на большой дистанции у стен: SphereCast-проверка стен пересчитывается " +
                "каждый кадр и может дёргаться между \"чисто\"/\"упёрлось\" от долей градуса " +
                "поворота - плавность гасит именно этот скачок.");
        });

        // RepoHax groups its sidebar the same way - a couple of thin separators splitting
        // "about yourself" from "world/other players" from "settings", instead of one long
        // undifferentiated list of tabs. Grouping/order matched to CustomOverlayUI's own
        // SidebarBreaks (2026-09-11) - both menus' sidebars now split at the exact same tabs.
        AddTabBarSeparator();

        AddTab("Визуал", () =>
        {
            AddToggleRow("ESP игроков (видно сквозь стены)", CheatState.EspPlayers);
            AddToggleRow("  -> Экипировка тиммейтов (что держат в руках)", CheatState.EspLoadout);
            AddToggleRow("ESP врагов (видно сквозь стены)", CheatState.EspEnemies);
            AddToggleRow("ESP ценностей (цена и позиция сквозь стены)", CheatState.EspValuables);
            AddToggleRow("ESP точек эвакуации (статус: ожидание/активна)", CheatState.EspExtractionPoints);
            AddIntStepperRow("Макс. дистанция ESP (м)", CheatState.EspMaxDistance, 5, 200, 5);
            AddToggleRow("Режим силуэта (заливка цветом сквозь стены)", CheatState.EspShaderMode);
            AddColorCycleRow("Цвет ESP", CheatState.EspColor);
            AddToggleRow("Радужный режим ESP (перебирает цвета по кругу)", CheatState.EspRainbow);
            AddToggleRow("Улучшенное зрение (дальше видно, без темноты)", CheatState.BetterVision);
            AddIntStepperRow("Угол обзора (FOV)", CheatState.Fov, 60, 140, 5);
            AddIntStepperRow("Яркость фонарика", CheatState.FlashlightIntensity, 1, 30, 1);
            AddIntStepperRow("Угол луча фонарика", CheatState.FlashAngle, 10, 170, 5);
            AddToggleRow("Фонарик не гаснет в приседе", CheatState.FlashInCrouch);
            AddToggleRow("Цветной фонарик", CheatState.FlashlightColorEnabled);
            AddColorCycleRow("Цвет фонарика", CheatState.FlashlightColorHex);
            AddToggleRow("Радужный фонарик", CheatState.FlashlightColorRainbow);
            AddToggleRow("Макс. заряд головы (при полёте призраком)", CheatState.MaxHeadBattery);
            AddButtonRow("Экран загрузки", "Пропустить", LoadingHack.SkipNow);
            AddToggleRow("Принудительный пропуск (мгновенно, рискованно - может рассинхронить других)", CheatState.LoadingForceSkip);
        });

        AddTab("Мир", () =>
        {
            AddToggleRow("Автофарм (сам ходит, собирает и сдаёт лут - забирает управление и камеру)", CheatState.AutoFarm);
            AddToggleRow("Магнит лута (тянет ценности к эвакуации, ничего не ломается)", CheatState.LootMagnet);
            AddToggleRow("Бог-режим для ценностей (не бьются, только хост/сингл)", CheatState.ValuableGodMode);
            AddToggleRow("Тягач тележек (тянет тележки к эвакуации)", CheatState.CartPuller);
            AddToggleRow("Автоподбор (сам подбирает предметы рядом)", CheatState.AutoLoot);
            AddToggleRow("Радар эвакуации (расстояние и направление)", CheatState.ExtractionRadar);
            AddToggleRow("Автозапуск эвакуации (не нужно жать кнопку)", CheatState.AutoActivateExtraction);
            AddItemSpawnerRow();
            AddToggleRow("Без лимита времени хвата врагами", CheatState.NoGrabMaxTime);
            AddToggleRow("Небьющиеся ценности (не ломаются от ударов)", CheatState.Unbreakable);

            AddSectionHeader("Небьющиеся предметы по типу (настоящие категории игры)");
            AddToggleRow("Оружие ближнего боя", CheatState.UnbreakableMelee);
            AddToggleRow("Огнестрел", CheatState.UnbreakableGun);
            AddToggleRow("Гранаты", CheatState.UnbreakableGrenade);
            AddToggleRow("Мины", CheatState.UnbreakableMine);
            AddToggleRow("Гранатомёты/пусковые", CheatState.UnbreakableLauncher);
            AddToggleRow("Аптечки", CheatState.UnbreakableHealthPack);
            AddToggleRow("Силовые кристаллы (топливо зарядной станции)", CheatState.UnbreakablePowerCrystal);
            AddToggleRow("Инструменты", CheatState.UnbreakableTool);
            AddToggleRow("Трекеры", CheatState.UnbreakableTracker);
            AddToggleRow("Дроны", CheatState.UnbreakableDrone);
            AddToggleRow("Шары (orb)", CheatState.UnbreakableOrb);
            AddToggleRow("Тележки", CheatState.UnbreakableCart);
            AddToggleRow("Карманные тележки", CheatState.UnbreakablePocketCart);
            AddToggleRow("Транспорт", CheatState.UnbreakableVehicle);
            AddToggleRow("Станции апгрейда предметов", CheatState.UnbreakableItemUpgrade);
            AddToggleRow("Станции апгрейда игрока", CheatState.UnbreakablePlayerUpgrade);
            AddToggleRow("Бесплатная квота (у хоста/соло - мгновенно, у гостя - через спавн ценностей, не мгновенно)", CheatState.FreeQuota);
            AddToggleRow("Квота в 0 при загрузке уровня (ловит момент до синхронизации цели рана - работает и у гостя, и у хоста)", CheatState.QuotaZeroRace);
            AddToggleRow("Авто-скип в магазин при загрузке (только хост/соло - закрывает 1 квоту в 0 и сразу едет в магазин, вся комната)", CheatState.AutoSkipToShop);
            AddToggleRow("Безлимит перероллов апгрейдов (только хост/соло, цена настоящая)", CheatState.UnlimitedRerolls);
            AddToggleRow("Лазерный прицел (на оружии в руках)", CheatState.UseLaser);
            AddToggleRow("Вибро-оружие (только хост/соло)", CheatState.MeleeVibrate);
            AddToggleRow("Вечная зарядная станция (не садится и не ломается)", CheatState.InfiniteChargingStation);
            AddToggleRow("Лечение у зарядной станции (5 HP/сек в радиусе)", CheatState.ChargingStationHeal);
            AddToggleRow("После смерти - голова летит к грузовику", CheatState.DeathAutoToTruck);
            AddToggleRow("После смерти - голова летит к ближайшей эвакуации", CheatState.DeathAutoToExtraction);
            AddToggleRow("Осколок с любой смерти (вашей или тиммейта)", CheatState.TeammateDeathDropBead);
        });

        // Everything that acts on other PEOPLE lives here now, in one place - it used to be split
        // across three tabs (target-picker actions in "Рейд", teleport-to-player in "Телепорт",
        // close-range knockdown in "Тролль") purely by which mechanic each one happened to use,
        // which is a distinction only the code cares about. "Рейд" keeps the level/loot sabotage,
        // "Тролль" keeps the appearance spoofs, "Монстры" keeps the enemy tools.
        AddTab("Игроки", () =>
        {
            AddSectionHeader("Цель (работает с любой дистанции, не нужно подходить)");
            AddPlayerPickerRow("Цель (одна на все действия ниже)", CheatState.FakeSoundTargetName);
            AddButtonRow("Телепорт к цели (переместить себя)", "К цели", TeleportHack.TeleportToPlayer);
            AddButtonRow("Притянуть цель к себе (любая дистанция)", "Тащить сюда", TeleportHack.PullPlayerToMe);
            AddToggleRow("Клетка (держит цель под картой, пока не выключите)", CheatState.PlayerCage);
            AddButtonRow("Бросок (закинуть цель высоко в воздух, разово)", "Бросить", RaidHack.Yeet);
            AddButtonRow("Пугающий звук (джампскейр у цели)", "Играть", FakeSoundHack.PlaySelected);
            AddButtonRow("Подстава продавцу (только в магазине - NPC-охранник атакует цель, урон настоящий)", "Подставить", RaidHack.FrameForShopKeeper);
            AddButtonRow("Посадить в машину (только карты с транспортом - разово)", "Посадить", RaidHack.ForceSeat);
            AddToggleRow("Кресельная чехарда (постоянно пересаживает цель по случайным местам)", CheatState.SeatShuffle);
            AddToggleRow("Приманка для монстра (натравливает реального врага на цель, выглядит естественно)", CheatState.MonsterBait);
            AddToggleRow("Спам-лечение (раздражающий экранный глитч на цели, безвредно)", CheatState.HealSpam);
            AddButtonRow("Возродить цель (реально только у хоста, у гостя иллюзия только на своём экране)", "Возродить", ReviveHack.ReviveOther);
            AddButtonRow("Мгновенная смерть цели (только хост, у гостя не сработает)", "Убить", InstantKillHack.KillOther);
            AddToggleRow("Авто-возрождение всех мёртвых (только хост/соло реально, у гостя иллюзия)", CheatState.AutoReviveTeammates);

            AddSectionHeader("Всё лобби разом");
            AddToggleRow("Заморозить всех (держит каждого ровно там, где он есть)", CheatState.FreezeAll);
            AddToggleRow("Клетка ВСЕМ (утаскивает всех под карту)", CheatState.PlayerCageAll);
            AddButtonRow("Бросок ВСЕХ (закидывает всех в воздух разом)", "Бросить всех", RaidHack.YeetAll);
            AddButtonRow("Групповой нокдаун (валит всех разом, любая дистанция)", "Нокдаун всех", MassRaidHack.InstantGroupKnockdown);
            AddButtonRow("NUKE (нокдаун + выбить всё из рук; лут не трогает)", "NUKE", MassRaidHack.Nuke);
            AddToggleRow("Шторм бросков (кидает случайного игрока каждые ~6 сек)", CheatState.YeetStorm);
            AddToggleRow("Наказание кэмперов эвакуации (клетка тем, кто торчит у точки)", CheatState.ExtractionCamperPunish);
            AddButtonRow("Похищение в машину (рассаживает всех по свободным местам)", "Похитить", MassRaidHack.MassSeatKidnap);
            AddButtonRow("Подстава ВСЕМ продавцу (только в магазине)", "Подставить всех", MassRaidHack.FrameAllForShopKeeper);
            AddToggleRow("Казнь в магазине (по очереди; умер - выбыл, выжил - в конец очереди)", CheatState.ShopExecution);

            AddSectionHeader("Отобрать из рук");
            AddItemStealRow();
            AddToggleRow("Тотальный грабёж (без фильтра по имени - тащит всё подряд)", CheatState.StealAllHeldItems);
            AddButtonRow("Выбить всё из рук (у всех разом)", "Выбить", RaidHack.ForceDropAll);
            AddToggleRow("Тотальное разоружение (постоянно выбивает из рук у всех)", CheatState.MassDisarm);

            AddSectionHeader("Арена (auto-win)");
            AddToggleRow("Auto-Win Арены (сбрасывает чужие карты с трассы, только Arena Race)", CheatState.ArenaSabotage);
            AddToggleRow("Auto-Win Арены с оружием (скидывает всех в яму, только режим с падающим полом)", CheatState.ArenaWeaponsSabotage);
            AddToggleRow("Auto-Win Арены, легит-версия (подкидывает препятствия на пути соперников)", CheatState.ArenaLegitSabotage);

            AddSectionHeader("Продавец-палач (магазин, урон настоящий)");
            AddButtonRow("Мгновенная казнь цели (один RPC, без очереди и ожидания)", "Казнить", RuckusFrameHack.InstantExecute);
            AddButtonRow("Мгновенная казнь ВСЕХ (продавец пойдёт по списку сам)", "Казнить всех", RuckusFrameHack.InstantExecuteAll);
            AddToggleRow("Авто-казнь (держит счёт всех выше порога, выживших добивает)", CheatState.AutoShopExecute);
            AddButtonRow("Подставить за избиение (цель бьёт другого - продавец злится, но не убивает)", "Подставить", RuckusFrameHack.FrameForAttack);
            AddButtonRow("Подставить как убийцу (флаг killed - минуя все пороги)", "Убийца", RuckusFrameHack.FrameAsKiller);
            AddButtonRow("Перекрёстная драка (все подставлены друг против друга)", "Драка", RuckusFrameHack.FrameEveryoneCrossfire);
            AddButtonRow("Бомба репутации (проводит цель через все стадии злости продавца)", "Бомба", RuckusFrameHack.ScoreBomb);
            AddButtonRow("Подстава за тычки (7 фейковых carry-thrust за раз)", "Тычки", RuckusFrameHack.CarryThrustFrame);
            AddToggleRow("Шторм тычков (продавец постоянно зол на всё лобби)", CheatState.CarryThrustStorm);

            AddSectionHeader("Лишение вещей (новые дыры)");
            AddButtonRow("Обезоружить цель (роняет из рук + блок хвата на 4 сек)", "Обезоружить", GrabDenialHack.DisarmTarget);
            AddButtonRow("Обезоружить ВСЕХ", "Обезоружить всех", GrabDenialHack.DisarmAll);
            AddToggleRow("Блок хвата цели (цель вообще ничего не может поднять)", CheatState.GrabLockTarget);
            AddToggleRow("Блок хвата ВСЕМ (никто ничего не держит, пока включено)", CheatState.GrabLockAll);
            AddToggleRow("Отбирать при поднятии (тише блокировки: поднял - тут же выбило)", CheatState.DisarmOnGrab);
            AddButtonRow("Снять экипировку с цели (вычищает слоты инвентаря)", "Снять", GrabDenialHack.ForceUnequipTarget);
            AddButtonRow("Снять экипировку со ВСЕХ", "Снять всем", GrabDenialHack.ForceUnequipAll);
            AddToggleRow("Шторм разэкипировки (ни у кого ничего не держится в слотах)", CheatState.UnequipStorm);
            AddButtonRow("Навязать предметы цели (запихивает чужой шмот в её слоты)", "Навязать", GrabDenialHack.ForceEquipOnTarget);
            AddToggleRow("Проклятые руки (постоянно суёт цели случайный предмет)", CheatState.CursedHands);
            AddButtonRow("Раздать хлам всем (каждому по предмету в слот)", "Раздать", GrabDenialHack.ForceEquipJunkAll);
            AddButtonRow("Сжечь оружие цели (выжигает заряд того, что она держит)", "Сжечь", GrabDenialHack.DrainTargetWeapon);
            AddButtonRow("Сжечь всё оружие на уровне (разово)", "Сжечь всё", GrabDenialHack.DrainAllWeaponsNow);
            AddToggleRow("Постоянный выжиг оружия (ни у кого нет заряженного оружия)", CheatState.WeaponDrainAll);
            AddButtonRow("ОБОБРАТЬ ЦЕЛЬ (слоты + руки + блок хвата + оружие разом)", "Обобрать", GrabDenialHack.StripTarget);

            AddSectionHeader("Нокдаун вблизи (нужно подходить самому)");
            AddToggleRow("Рывок (нокдаун всех игроков по очереди)", CheatState.KnockdownAll);
            AddToggleRow("Нокдаун + Изгнание (режим 2 - утаскивает под карту)", CheatState.KnockdownBanish);
            AddToggleRow("  -> Ещё и убить (только хост/соло)", CheatState.KnockdownBanishKill);

            AddSectionHeader("Таран тележкой (эксперимент, не гарантирован)");
            AddToggleRow("Хватает ближайшую тележку и таранит ею цель (см. Цель выше)", CheatState.CartRam);
        });

        AddTab("Рейд", () =>
        {
            AddSectionHeader("Лут (осторожно - бьёт и по вашей же выручке)");
            AddButtonRow("Чистка предметов (разбрасывает весь шмот на уровне)", "Чистка", MassRaidHack.ItemPurge);
            AddButtonRow("Разброс тележек (раскидывает все тележки)", "Раскидать", MassRaidHack.CartYeetAll);
            AddButtonRow("Чёрная дыра ценностей (прячет весь собранный лут)", "Спрятать", MassRaidHack.ValuableBlackhole);
            AddToggleRow("Уничтожить все ценности (кроме самых дешёвых, непрерывно)", CheatState.DestroyAllValuables);
            AddToggleRow("Разрушитель магазина 3000 (оружие/аптечки/всё - только в магазине)", CheatState.ShopDestroyer);

            AddSectionHeader("Эвакуация (новая дыра - выбор точки за всю команду)");
            AddButtonRow("Активировать ближайшую точку эвакуации", "Ближняя", WorldHolesHack2.ActivateNearestExtraction);
            AddButtonRow("Активировать САМУЮ ДАЛЬНЮЮ (остальные точки блокируются на раунд)", "Дальняя", WorldHolesHack2.ActivateFarthestExtraction);
            AddToggleRow("Перехват эвакуации (постоянно занимает худшую точку раньше всех)", CheatState.ExtractionSnipe);

            AddSectionHeader("Карта и взрывы");
            AddButtonRow("Раскрыть все ценности на карте (всем игрокам)", "Раскрыть", WorldHolesHack2.RevealAllValuables);
            AddToggleRow("Авто-раскрытие ценностей (по мере подгрузки уровня)", CheatState.AutoRevealValuables);
            AddButtonRow("Взрыв на цели (детонирует снаряды в её координатах)", "Взрыв", WorldHolesHack2.StrikeTarget);
            AddButtonRow("Взрыв по всем разом", "Взрыв всем", WorldHolesHack2.StrikeEveryone);
            AddToggleRow("Шторм взрывов (повторяет каждые ~4 сек, нужен живой снаряд)", CheatState.ProjectileStrikeStorm);

            AddSectionHeader("Транспорт и застревания");
            AddButtonRow("Тряска всех машин (только визуал кузова и звук, езду не портит)", "Трясти", WorldHolesHack2.WobbleAllVehicles);
            AddButtonRow("Тряска машины цели (кузов дёргается и бренчит, физика не трогается)", "Трясти цель", WorldHolesHack2.WobbleTargetVehicle);
            AddToggleRow("Шторм тряски (непрерывный дребезг корпуса, чисто нервировать)", CheatState.VehicleWobbleStorm);
            AddButtonRow("Сбросить толкатель всем (вытаскивает застрявших из геометрии)", "Сбросить", WorldHolesHack2.ResetAllPhysPushers);
            AddButtonRow("Сбросить толкатель цели", "Сбросить цель", WorldHolesHack2.ResetTargetPhysPusher);

            AddSectionHeader("Уровень");
            AddButtonRow("Снести все ловушки целиком (не только триггер)", "Снести", WorldHolesHack2.DestroyAllTrapsFully);
            AddButtonRow("Открыть все музыкальные шкатулки", "Открыть", WorldHolesHack2.TriggerAllMusicBoxes);
            AddButtonRow("Остановить все книги-кусаки", "Остановить", WorldHolesHack2.StopAllChompBooks);
            AddButtonRow("ЗАЧИСТКА МИРА (карта + ловушки + машины + толкатели)", "Зачистка", WorldHolesHack2.WorldSweep);
            AddButtonRow("Обезвредить все ловушки на уровне", "Обезвредить", UtilityHolesHack.DisarmAllTraps);
            AddButtonRow("Переключить все рычаги/switches на уровне", "Переключить", UtilityHolesHack.ToggleAllSwitches);
            AddToggleRow("Запереть все двери (замыкает в текущем положении)", CheatState.LockAllDoors);
        });

        AddTab("Монстры", () =>
        {
            AddButtonRow("Телепорт к ближайшему монстру (переместить себя)", "К монстру", TeleportHack.TeleportToEnemy);
            AddButtonRow("Притянуть ближайшего монстра к себе (работает как ловлю ценностей)", "Тащить монстра", TeleportHack.PullEnemyToMe);
            AddButtonRow("Рой на группу (шлёт монстра к самому большому скоплению игроков)", "Наслать", TeleportHack.SwarmTheGroup);
            AddToggleRow("Шторм роя (повторяет само, каждые ~8 сек, игнорит одиночек)", CheatState.SwarmStorm);
            AddToggleRow("Ловушка комнаты (рой + срывает двери и строит из них стену вокруг группы)", CheatState.TrapRoom);
            AddToggleRow("Убийство монстров падением (кидает их с высоты каждые ~4 сек)", CheatState.EnemyDropKill);
            AddToggleRow("Заморозка врагов (реальная, не визуал - не двигаются и не атакуют)", CheatState.FreezeEnemies);
            AddFloatStepperRow("  -> Радиус заморозки (0 = все враги на уровне)", CheatState.FreezeEnemiesRange, 0f, 100f, 5f);

            AddSectionHeader("Количество врагов на уровне (только хост/соло)");
            AddFloatStepperRow("Множитель спавна (1 = как обычно)", CheatState.SpawnRateMultiplier, 1f, 10f, 0.5f);

            AddSectionHeader("Дроп ценностей с врагов (только хост/соло реально)");
            AddToggleRow("Убрать лимит (игра держит не больше 10 таких ценностей разом)", CheatState.RemovePillarLimit);
            AddIntStepperRow("Множитель дропа (сколько ценностей за одну смерть врага)", CheatState.PillarDropMultiplier, 1, 10, 1);
            AddToggleRow("  -> Снять лимит дропа с одного врага (иначе капается на 3 за всю жизнь)", CheatState.PillarDropRemoveLimit);
        });

        AddTabBarSeparator();

        AddTab("Косметика", () =>
        {
            AddValuableSpawnRow();
            AddToggleRow("Принудительные крылья (только визуал, апгрейд не нужен)", CheatState.ForceWings);
            AddToggleRow("Розовые крылья (второй из двух реальных цветов игры - видно другим)", CheatState.WingsPink);
            AddToggleRow("Цвет глаз (фикс. палитра - видно другим с этим же модом)", CheatState.EyeColor);
            AddCycleRow("Цвет глаз", CheatState.EyeColorName, CosmeticHack.EyeColorNames);
            AddToggleRow("Большие зрачки (видно другим с этим же модом)", CheatState.BigPupils);
            AddNameChangerRow();
            AddToggleRow("Маскировка под хоста (авто, копирует ник хоста себе)", CheatState.MimicHostName);
            AddToggleRow("Маскировка скина под хоста (авто, копирует косметику и цвета)", CheatState.MimicHostSkin);
        });

        // Split out into its own tab (2026-09-11) - was scattered inside "Прочее". Two different
        // directions: what OTHERS hear from you (GlitchyVoice - real, transmitted) vs what YOU
        // hear from THEM (VoiceFilter/ChipmunkVoices - local playback only, nothing sent over the
        // network).
        AddTab("Голос", () =>
        {
            AddSectionHeader("Кого слышно вам (только локально, ни на что в сети не влияет)");
            AddCycleRow("Слышать (кого слышно: все / живые / мёртвые)", CheatState.VoiceFilter, new[] { "All", "Living", "Dead" });
            AddToggleRow("Чужие голоса (бурундуки/демоны - меняет высоту тона для вас)", CheatState.ChipmunkVoices);
            AddFloatStepperRow("  -> Высота тона (>1 выше, <1 ниже)", CheatState.ChipmunkPitch, 0.5f, 3f, 0.1f);

            AddSectionHeader("Что слышат от вас другие (реально передаётся всем)");
            AddToggleRow("Лаганый голос (другие слышат заикания, а внешне вы обычный)", CheatState.GlitchyVoice);
            AddFloatStepperRow("  -> Длина рывка, сек (меньше = резче заикание)", CheatState.GlitchyVoiceInterval, 0.05f, 1f, 0.05f);
        });

        AddTab("Прочее", () =>
        {
            AddButtonRow("Публичная игра (точь-в-точь родная кнопка; только из главного меню)", "Найти", PublicGameHack.JoinRandomPublic);
            AddButtonRow("Сбросить регион на авто (если список серверов пустой из-за закреплённого региона)", "Сбросить", PublicGameHack.ResetToBestRegion);
            AddButtonRow("Форсировать WebSocket-протокол (если сеть режет UDP - долго/не грузится подключение)", "Форсировать", PublicGameHack.ForceWebSocketProtocol);
            AddToggleRow("Всегда взаимодействие (кнопка активна на всё, на что смотрите)", CheatState.AlwaysInteract);
            AddToggleRow("Пропустить ожидание луны (быстрее выбор уровня)", CheatState.SkipMoonWait);
            AddToggleRow("Пропустить экран результатов (сразу к следующему шагу)", CheatState.SkipResultsScreen);
            AddButtonRow("Пересканировать свет/частицы/звук (при глюках после LateJoin)", "Скан", LateJoinFixHack.RescanNow);
            AddButtonRow("Телепорт к ближайшему игроку (если застряли в фантомной комнате)", "Идти", LateJoinFixHack.TeleportToNearestPlayer);
            AddLevelLoaderRow();
            AddModuleLoaderRow();

            AddSectionHeader("Защита от краша");
            AddToggleRow("Анти-краш (гасит краш-пакеты; клиент может лагать, но не падает)", CheatState.AntiCrash);
            AddToggleRow("Карантин нарушителей (флудера временно игнорировать целиком; редкий риск рассинхрона)", CheatState.AntiCrashQuarantine);
            AddIntStepperRow("Лимит RPC/сек на игрока", CheatState.AntiCrashMaxRpcsPerSecond, 20, 1000, 20);
            AddIntStepperRow("Лимит спавнов/сек на игрока", CheatState.AntiCrashMaxInstantiatesPerSecond, 10, 500, 10);
            AddIntStepperRow("Макс. длина строки в RPC", CheatState.AntiCrashMaxArgStringLength, 200, 20000, 200);
            AddIntStepperRow("Карантин, сек", CheatState.AntiCrashQuarantineSeconds, 1, 60, 1);
            AddLiveInfoRow(() =>
                $"Погашено исключений: {AntiCrashHack.ExceptionsSwallowed}   Отброшено RPC: {AntiCrashHack.RpcsDropped}\n" +
                $"Отброшено спавнов: {AntiCrashHack.InstantiatesDropped}   В карантине: {AntiCrashHack.QuarantinedCount(System.Environment.TickCount)}");

            AddSectionHeader("Соседние комнаты");
            AddIntStepperRow("Сколько ещё комнат создавать", CheatState.MultiRoomExtraCount, 1, 10, 1);
            AddIntStepperRow("Лимит игроков в каждой", CheatState.MultiRoomMaxPlayers, 6, 100, 2);
            AddButtonRow("Создать N независимых комнат с именем вашей текущей (нужно уже быть в комнате; " +
                "на время создания вас будет перекидывать между комнатами - см. лог)",
                "Создать", MultiRoomHack.CreateSiblingRooms);
        });

        AddTab("Разработчик", () =>
        {
            AddToggleRow("Тестовый пакет (для отладки сети)", CheatState.SendTestPacket, PacketTestHack.OnToggled);
            AddIntStepperRow("  -> Интервал отправки, мс", CheatState.PacketTestIntervalMs, 50, 5000, 50);

            AddSectionHeader("Сетевая диагностика");
            AddToggleRow("Показывать состояние сети (пинг, регион, игроки, реальный трафик Photon; только чтение)", CheatState.NetworkDiagnostics);
            AddLiveInfoRow(NetworkDiagnostics.GetStatsString);

            AddSectionHeader("Логи событий (RepoCheatMenu_Logs)");
            AddToggleRow("Писать структурированные логи по категориям (спавны с координатами, телепорты, кражи предметов)", CheatState.EventLogging);
            AddLiveInfoRow(() => "Папка сессии: " + (EventLog.SessionDir ?? "ещё не создана"));

            AddSectionHeader("Мост для автономного тестирования (DebugBridge.cs)");
            AddToggleRow("Включить мост (выполняет C# из RepoCheatMenu_Bridge/command.txt рядом с DLL мода)", CheatState.DebugBridgeEnabled);
            AddLiveInfoRow(() => "Только для доверенной сессии разработки - произвольный код с полным доступом к моду и игре.");
        });

        AddTabBarSeparator();

        AddTab("Скрипты", BuildScriptsTab);

        AddTab("Тролль", () =>
        {
            AddSectionHeader("Внешний вид у других");
            AddToggleRow("Анти-аим (у других крутится ваша голова)", CheatState.AntiAimSpin);
            AddToggleRow("Dinner Bone (у других вы перевёрнуты)", CheatState.DinnerBone);
            AddFloatStepperRow("Угол переворота", CheatState.DinnerBoneAngle, 0f, 180f, 10f);
            AddToggleRow("  -> Показать себе (нужен Third Person - вкладка Визуал)", CheatState.DinnerBoneShowSelf);

            AddToggleRow("Флип (непрерывное вращение, не путать с DinnerBone - не включайте вместе)", CheatState.FlipEnabled);
            AddCycleRow("  -> Ось вращения", CheatState.FlipAxis, new[] { "Roll", "Pitch", "Yaw" });
            AddFloatStepperRow("  -> Скорость, град/сек", CheatState.FlipSpeed, 30f, 1080f, 30f);
            AddFloatStepperRow("  -> Радиус орбиты, м (0 = выкл)", CheatState.FlipOrbitRadius, 0f, 5f, 0.5f);
            AddToggleRow("  -> Показать себе (нужен Third Person)", CheatState.FlipShowSelf);
            AddToggleRow("T-поза (другие видят вас скользящим неподвижно)", CheatState.TPose);
            AddToggleRow("Вечный краул (другие видят вас постоянно ползущим, не совмещать с T-позой)", CheatState.GhostCrawl);
            AddToggleRow("Невидимка (у других модель улетает высоко в небо)", CheatState.Invisible);
            AddFloatStepperRow("  -> Высота", CheatState.InvisibleHeight, 500f, 20000f, 500f);
            AddToggleRow("  -> Звучать нормально во время разговора (тогда видно, пока говорите)", CheatState.InvisibleRevealWhileTalking);
            AddToggleRow("Дёрганый фонарик (у других луч мечется в стороны, у вас всё нормально)", CheatState.GlitchyFlashlight);
            AddFloatStepperRow("  -> Радиус болтания, м", CheatState.GlitchyFlashlightRadius, 1f, 10f, 0.5f);
            AddToggleRow("Призрачный наездник (у других вы едете верхом на ближайшем предмете)", CheatState.GhostRider);
            AddLiveInfoRow(() => !CheatState.GhostRider.Value ? "  -> Выключено"
                : GhostRiderHack.HasTarget ? "  -> Цель (видно только другим): " + GhostRiderHack.TargetName
                : "  -> Ищу цель рядом...");

            AddSectionHeader("Спам в чат");
            AddToggleRow("Спам фразами в чат (шлёт по кругу список ниже, реальные сообщения в игровой чат)", CheatState.ChatSpam);
            AddChatSpamTextRow();
            AddFloatStepperRow("Интервал между сообщениями, сек (игра сама не шлёт чаще 1/сек)", CheatState.ChatSpamInterval, 1f, 30f, 0.5f);

            AddSectionHeader("Ловушки (удалённый запуск, реально для всех)");
            AddButtonRow("Ближайшая ловушка к вам", "Запустить", TrapTriggerHack.TriggerNearest);
            AddButtonRow("Все ловушки на уровне сразу", "Запустить все", TrapTriggerHack.TriggerAllInLevel);
        });

        // Was CustomOverlayUI-only until now (2026-09-11) - PlayerHud.cs's always-on gameplay
        // widgets (separate from either menu, drawn every frame regardless of which one is open)
        // had no controls here at all, a real feature gap between the two menus, not just a
        // cosmetic one.
        AddTab("HUD", () =>
        {
            AddToggleRow("HUD включён (виджеты поверх игры, отдельно от меню)", CheatState.HudEnabled);
            AddToggleRow("Режим редактирования (перетаскивание/цвет/вкл-выкл)", CheatState.HudEditMode);
            AddToggleRow("Авто-колонка (виджеты стакаются вплотную)", CheatState.HudAutoStack);
            AddLiveInfoRow(() => "В режиме редактирования: тащите виджет за шапку, \"x\" убирает его, " +
                "цветной квадрат меняет акцентный цвет. Убранные виджеты возвращаются кнопками сверху экрана.");

            AddSectionHeader("Виджет «Игрок» (ник, fps, пинг, координаты)");
            AddToggleRow("Показывать", CheatState.HudPlayerStats.Enabled);
            AddColorCycleRow("  -> Цвет", CheatState.HudPlayerStats.Color);

            AddSectionHeader("Виджет «Цель» (ник, HP, дистанция, иконки предметов)");
            AddToggleRow("Показывать", CheatState.HudTargetInfo.Enabled);
            AddColorCycleRow("  -> Цвет", CheatState.HudTargetInfo.Color);
            AddLiveInfoRow(() => "Цель берётся из общего поля имени на вкладке «Игроки».");

            AddSectionHeader("Виджет «Плеер»");
            AddToggleRow("Показывать", CheatState.HudMediaPlayer.Enabled);
            AddColorCycleRow("  -> Цвет", CheatState.HudMediaPlayer.Color);
            AddToggleRow("  -> Реальный трек из Spotify (по заголовку окна)", CheatState.HudMediaPlayerUseSpotify);
            AddTextFieldRow("  -> Название трека (если Spotify выключен)", CheatState.HudMediaPlayerTitle);

            AddSectionHeader("Виджет «Бинды» (реальные привязанные клавиши)");
            AddToggleRow("Показывать", CheatState.HudKeybinds.Enabled);
            AddColorCycleRow("  -> Цвет", CheatState.HudKeybinds.Color);
            AddLiveInfoRow(() => "Выключен по умолчанию - список тех же биндов, что заданы средним кликом по строкам меню.");
        });
    }

    // iOS/macOS Settings-style grouped section: a small gray all-caps label, then a rounded
    // card that holds every row until the next header (or the tab ends). Starts a fresh card -
    // NewRow lazily opens the first, unlabeled one for tabs that don't call this right away.
    private static void AddSectionHeader(string title)
    {
        var header = CreateText(_content.transform, title.ToUpperInvariant(), 12, TextAnchor.MiddleLeft, SectionHeaderColor);
        header.fontStyle = FontStyle.Bold;
        _contentRows.Add(header.gameObject);
        var le = header.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 18;
        var rt = header.rectTransform;
        rt.offsetMin = new Vector2(rt.offsetMin.x + 4f, rt.offsetMin.y);

        BeginCard();
    }

    private static void BeginCard()
    {
        var card = CreatePanel(_content.transform, CardBg);
        _contentRows.Add(card.gameObject);
        var cardLayout = card.gameObject.AddComponent<VerticalLayoutGroup>();
        cardLayout.padding = new RectOffset(10, 10, 4, 4);
        cardLayout.spacing = 2;
        cardLayout.childControlWidth = true;
        cardLayout.childControlHeight = true;
        cardLayout.childForceExpandWidth = true;
        cardLayout.childForceExpandHeight = false;
        var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _currentCard = card;
    }

    // Thin divider in the tab bar (sidebar), same idea RepoHax uses to split its own sidebar
    // into "about yourself" / "world and other players" / "settings" clusters instead of one
    // undifferentiated list - purely visual, doesn't register a tab.
    private static void AddTabBarSeparator()
    {
        var sep = CreatePanel(_tabBar.transform, new Color(1f, 1f, 1f, 0.08f), rounded: false).gameObject;
        var le = sep.AddComponent<LayoutElement>();
        le.preferredHeight = 1f;
        le.minHeight = 1f;
    }

    // name -> the thin left-edge accent bar shown only while that tab is active, alongside
    // _tabButtons' full-fill color. Matches CustomOverlayUI.DrawSidebar's own marker bar
    // (tabRect.x+3, 6px vertical inset, 3px wide) so the active tab reads the same way in
    // both menus instead of relying on the fill color alone.
    private static readonly Dictionary<string, RectTransform> _tabMarkers = new();

    private static void AddTab(string name, Action buildContent)
    {
        _tabs[name] = buildContent;
        Button btn = CreateButton(_tabBar.transform, name, () => SelectTab(name));
        var le = btn.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 32;
        _tabButtons[name] = btn;
        SetPanelColor(btn, TabIdleColor);

        // Left-aligned, slightly indented label instead of centered text - reads more like a
        // LiquidBounce-style sidebar nav list than a row of generic buttons.
        Text label = btn.GetComponentInChildren<Text>();
        label.alignment = TextAnchor.MiddleLeft;
        label.rectTransform.offsetMin = new Vector2(12f, label.rectTransform.offsetMin.y);

        RectTransform marker = CreatePanel(btn.transform, Color.clear, rounded: false);
        SetRect(marker, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(3f, 6f), new Vector2(6f, -6f));
        _tabMarkers[name] = marker;
    }

    private static void SelectTab(string name)
    {
        StopRepeat();
        _activeTab = name;
        foreach (var row in _contentRows) UnityEngine.Object.Destroy(row);
        _contentRows.Clear();
        _currentCard = null; // first row lazily opens a fresh, unlabeled card via NewRow
        _knobTargets.Clear(); // old cards' knobs are gone - don't keep easing toward destroyed RectTransforms
        _liveTexts.Clear(); // same for live readouts - their Text objects were just destroyed above
        _tabs[name]();

        foreach (var kv in _tabButtons)
        {
            bool active = kv.Key == name;
            SetPanelColor(kv.Value, active ? TabActiveColor : TabIdleColor);
            _tabMarkers[kv.Key].GetComponent<Image>().color = active ? Color.white : Color.clear;
        }
    }

    private static void AddLiveInfoRow(Func<string> source)
    {
        if (_currentCard == null) BeginCard();
        var text = CreateText(_currentCard, source(), 13, TextAnchor.MiddleLeft, SectionHeaderColor, wrap: true);
        _contentRows.Add(text.gameObject);
        var le = text.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 42;
        _liveTexts.Add((text, source));
    }

    private static GameObject NewRow(string label)
    {
        if (_currentCard == null) BeginCard();
        var row = CreatePanel(_currentCard, new Color(0, 0, 0, 0)).gameObject;
        _contentRows.Add(row);

        // Overflow text running past its own 280px column straight onto the switch/button that
        // followed it was the original bug. Wrap fixes that part. A ContentSizeFitter-driven
        // auto-height row was the first attempt at sizing the row to match - reverted, because
        // Text with Wrap computes its preferred height from its CURRENT rect width, which the
        // HorizontalLayoutGroup on this same row only assigns on a later pass; the first
        // calculation reads a stale (pre-layout) width and produces a wildly wrong height. A
        // plain fixed height sidesteps that ordering problem entirely - taller than most single-
        // line rows need, but correct for every label instead of occasionally wrong for one.
        var le = row.AddComponent<LayoutElement>();
        le.preferredHeight = 58;
        var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        // false, not true: force-expand was stretching EVERY child to the row's full 58px height,
        // including the toggle switch (which has its own fixed 26px LayoutElement.preferredHeight) -
        // that's what turned the capsule into a tall oval. false lets each child keep its own
        // natural size (wrapped text sized to its actual line count, switch/button sized to
        // whatever they set for themselves) and just centers them within the row via childAlignment.
        rowLayout.childForceExpandHeight = false;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.spacing = 6;

        // Most labels already carry their explanation as a trailing "(...)" - auto-splitting it
        // onto its own smaller, dimmer line (iOS Settings-style title+subtitle) instead of
        // leaving it crammed into one line was the actual ask here, and doing it in one place
        // means every existing "Title (explanation)" call site gets it for free, no per-row work.
        string title = label;
        string description = null;
        int parenStart = label.IndexOf('(');
        int parenEnd = label.LastIndexOf(')');
        if (parenStart > 0 && parenEnd > parenStart)
        {
            title = label.Substring(0, parenStart).TrimEnd();
            description = label.Substring(parenStart + 1, parenEnd - parenStart - 1).Trim();
        }

        string display = string.IsNullOrEmpty(description)
            ? title
            : $"{title}\n<size=11><color=#9A9AA6>{description}</color></size>";

        var text = CreateText(row.transform, display, 14, TextAnchor.MiddleLeft, Color.white, wrap: true);
        var textLe = text.gameObject.AddComponent<LayoutElement>();
        textLe.preferredWidth = 300;
        return row;
    }

    // iOS-style switch: a capsule track (blue = on, translucent gray = off) with a white knob
    // that eases to the opposite side via UpdateKnobTweens/Tick - reads instantly at a glance
    // without needing to read "ON"/"OFF" text, and matches the one control shape every Apple
    // platform uses for a plain boolean. onToggled (if given) fires after entry.Value flips, on
    // every click - used by rows that need to act immediately (e.g. send a packet) rather than
    // being polled from a per-tick Apply() like the rest of the menu's toggles.
    private const float SwitchTrackW = 46f, SwitchTrackH = 26f, SwitchKnobSize = 20f, SwitchInset = 3f;

    private static void AddToggleRow(string label, ConfigEntry<bool> entry, Action<bool> onToggled = null)
    {
        var row = NewRow(label);

        RectTransform track = CreatePanel(row.transform, entry.Value ? AccentBlue : ButtonIdleColor);
        var trackLe = track.gameObject.AddComponent<LayoutElement>();
        trackLe.preferredWidth = SwitchTrackW;
        trackLe.preferredHeight = SwitchTrackH;
        Button trackBtn = track.gameObject.AddComponent<Button>();
        SetPanelColor(trackBtn, entry.Value ? AccentBlue : ButtonIdleColor);

        RectTransform knob = CreateCircle(track, Color.white);
        knob.anchorMin = knob.anchorMax = new Vector2(0f, 0.5f);
        knob.pivot = new Vector2(0f, 0.5f);
        knob.sizeDelta = new Vector2(SwitchKnobSize, SwitchKnobSize);
        float onX = SwitchTrackW - SwitchKnobSize - SwitchInset;
        float offX = SwitchInset;
        knob.anchoredPosition = new Vector2(entry.Value ? onX : offX, 0f);

        trackBtn.onClick.AddListener(() =>
        {
            entry.Value = !entry.Value;
            SetPanelColor(trackBtn, entry.Value ? AccentBlue : ButtonIdleColor);
            _knobTargets[knob] = entry.Value ? onX : offX;
            onToggled?.Invoke(entry.Value);
        });

        AttachBind(row, track.gameObject, KeybindManager.Find(entry));
    }

    // --- Bind-in-place ---------------------------------------------------------------------
    // Middle-click any row, then press the key you want; Escape while picking clears it. Replaces
    // the old separate "Клавиши" tab, which listed every bindable action a second time under its
    // internal English name and left you matching two parallel lists by eye.
    //
    // The badge sits at the end of the row and is the only visible state: "—" unbound, the key
    // name once bound, "жми клавишу" while capturing. Refreshing it after capture rides the
    // existing _activeKeybindRefresh hook (Tick notices Capturing went back to null) - the
    // capture itself completes in KeybindManager, which polls the OS directly, because
    // UnityEngine.Input doesn't report presses reliably in this game.
    //
    // Attached to BOTH the row and its control: Unity's EventSystem delivers a click to the
    // nearest ancestor that handles it, so a middle-click landing on the switch/button itself
    // would otherwise be swallowed there and never reach the row.
    private static void AttachBind(GameObject row, GameObject control, Keybind bind)
    {
        if (bind == null) return; // nothing registered for this row - no badge, no capture

        Text badge = CreateText(row.transform, BindLabel(bind), 12, TextAnchor.MiddleCenter, SectionHeaderColor);
        var le = badge.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = 78;
        le.preferredHeight = 22;

        Bind(row);
        if (control != null && control != row) Bind(control);

        void Bind(GameObject target)
        {
            var binder = target.AddComponent<MiddleClickBinder>();
            binder.Target = bind;
            binder.Badge = badge;
        }
    }

    private static string BindLabel(Keybind bind) => bind.Vk.Value == 0 ? "—" : Win32.VkName(bind.Vk.Value);

    private class MiddleClickBinder : MonoBehaviour, IPointerClickHandler
    {
        public Keybind Target;
        public Text Badge;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Middle) return;

            KeybindManager.StartCapture(Target);
            if (Badge != null) Badge.text = "жми клавишу";
            _activeKeybindRefresh = () => { if (Badge != null) Badge.text = BindLabel(Target); };
        }
    }

    private static void AddButtonRow(string label, string buttonText, Action onClick)
    {
        var row = NewRow(label);
        Button btn = CreateButton(row.transform, buttonText, onClick);
        var le = btn.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = 100;
        le.preferredHeight = 26;
        SetPanelColor(btn, AccentBlueDim);
        AttachBind(row, btn.gameObject, KeybindManager.Find(onClick));
    }

    private static readonly (string Name, string Hex)[] ColorPalette =
    {
        ("Green", "#00FF00"), ("Red", "#FF3030"), ("Yellow", "#FFFF00"), ("Cyan", "#00FFFF"),
        ("Magenta", "#FF00FF"), ("White", "#FFFFFF"), ("Orange", "#FF8000"), ("Purple", "#9000FF"),
    };

    private static void AddColorCycleRow(string label, ConfigEntry<string> entry)
    {
        var row = NewRow(label);

        int IndexOf()
        {
            for (int i = 0; i < ColorPalette.Length; i++)
                if (string.Equals(ColorPalette[i].Hex, entry.Value, StringComparison.OrdinalIgnoreCase))
                    return i;
            return 0;
        }

        Button btn = CreateButton(row.transform, ColorPalette[IndexOf()].Name, null);
        var le = btn.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = 100;
        le.preferredHeight = 26;

        void Refresh()
        {
            var (name, hex) = ColorPalette[IndexOf()];
            btn.GetComponentInChildren<Text>().text = name;
            if (ColorUtility.TryParseHtmlString(hex, out Color c))
                SetPanelColor(btn, c);
        }

        Refresh();
        btn.onClick.AddListener(() =>
        {
            entry.Value = ColorPalette[(IndexOf() + 1) % ColorPalette.Length].Hex;
            Refresh();
        });
    }

    // Unlike AddCycleRow (fixed options array), this rescans ItemSpawnerHack's item list every
    // click - the shop's own item pool can change between refreshes, so a stale cached list
    // would drift from what's actually spawnable.
    private static void AddItemSpawnerRow()
    {
        var row = NewRow("Спавнер предметов");

        List<string> initial = ItemSpawnerHack.ScanItemNames();
        if (initial.Count > 0 && !initial.Contains(CheatState.ItemSpawnerName.Value))
            CheatState.ItemSpawnerName.Value = initial[0];
        else if (initial.Count == 0 && string.IsNullOrEmpty(CheatState.ItemSpawnerName.Value))
            CheatState.ItemSpawnerName.Value = "None";

        Button cycleBtn = CreateButton(row.transform, CheatState.ItemSpawnerName.Value, null);
        var cycleLe = cycleBtn.gameObject.AddComponent<LayoutElement>();
        cycleLe.preferredWidth = 160;
        cycleLe.preferredHeight = 26;

        cycleBtn.onClick.AddListener(() =>
        {
            List<string> fresh = ItemSpawnerHack.ScanItemNames();
            if (fresh.Count == 0)
            {
                CheatState.ItemSpawnerName.Value = "None";
                cycleBtn.GetComponentInChildren<Text>().text = "None";
                return;
            }
            int idx = fresh.IndexOf(CheatState.ItemSpawnerName.Value);
            string next = fresh[(idx + 1) % fresh.Count];
            CheatState.ItemSpawnerName.Value = next;
            cycleBtn.GetComponentInChildren<Text>().text = next;
        });

        Button spawnBtn = CreateButton(row.transform, "Заспавнить", ItemSpawnerHack.SpawnSelected);
        var spawnLe = spawnBtn.gameObject.AddComponent<LayoutElement>();
        spawnLe.preferredWidth = 90;
        spawnLe.preferredHeight = 26;
    }

    private static void AddLevelLoaderRow()
    {
        var row = NewRow("Загрузить уровень напрямую (хост/соло, минуя консоль разработчика)");

        List<string> initial = LevelLoaderHack.ScanLevelNames();
        if (initial.Count > 0 && !initial.Contains(CheatState.LevelLoaderName.Value))
            CheatState.LevelLoaderName.Value = initial[0];
        else if (initial.Count == 0 && string.IsNullOrEmpty(CheatState.LevelLoaderName.Value))
            CheatState.LevelLoaderName.Value = "None";

        Button cycleBtn = CreateButton(row.transform, CheatState.LevelLoaderName.Value, null);
        var cycleLe = cycleBtn.gameObject.AddComponent<LayoutElement>();
        cycleLe.preferredWidth = 180;
        cycleLe.preferredHeight = 26;

        cycleBtn.onClick.AddListener(() =>
        {
            List<string> fresh = LevelLoaderHack.ScanLevelNames();
            if (fresh.Count == 0)
            {
                CheatState.LevelLoaderName.Value = "None";
                cycleBtn.GetComponentInChildren<Text>().text = "None";
                return;
            }
            int idx = fresh.IndexOf(CheatState.LevelLoaderName.Value);
            string next = fresh[(idx + 1) % fresh.Count];
            CheatState.LevelLoaderName.Value = next;
            cycleBtn.GetComponentInChildren<Text>().text = next;
        });

        Button loadBtn = CreateButton(row.transform, "Загрузить", LevelLoaderHack.LoadSelected);
        var loadLe = loadBtn.gameObject.AddComponent<LayoutElement>();
        loadLe.preferredWidth = 90;
        loadLe.preferredHeight = 26;
    }

    private static void AddModuleLoaderRow()
    {
        var row = NewRow("Принудить модуль текущего уровня (перезапускает сцену, только хост/соло)");

        List<string> initial = LevelLoaderHack.ScanCurrentLevelModuleNames();
        if (initial.Count > 0 && !initial.Contains(CheatState.ModuleLoaderName.Value))
            CheatState.ModuleLoaderName.Value = initial[0];
        else if (initial.Count == 0 && string.IsNullOrEmpty(CheatState.ModuleLoaderName.Value))
            CheatState.ModuleLoaderName.Value = "None";

        Button cycleBtn = CreateButton(row.transform, CheatState.ModuleLoaderName.Value, null);
        var cycleLe = cycleBtn.gameObject.AddComponent<LayoutElement>();
        cycleLe.preferredWidth = 180;
        cycleLe.preferredHeight = 26;

        cycleBtn.onClick.AddListener(() =>
        {
            List<string> fresh = LevelLoaderHack.ScanCurrentLevelModuleNames();
            if (fresh.Count == 0)
            {
                CheatState.ModuleLoaderName.Value = "None";
                cycleBtn.GetComponentInChildren<Text>().text = "None";
                return;
            }
            int idx = fresh.IndexOf(CheatState.ModuleLoaderName.Value);
            string next = fresh[(idx + 1) % fresh.Count];
            CheatState.ModuleLoaderName.Value = next;
            cycleBtn.GetComponentInChildren<Text>().text = next;
        });

        Button forceBtn = CreateButton(row.transform, "Принудить", LevelLoaderHack.ForceCurrentLevelModule);
        var forceLe = forceBtn.gameObject.AddComponent<LayoutElement>();
        forceLe.preferredWidth = 90;
        forceLe.preferredHeight = 26;
    }

    // Shared by every "pick a player, then act on them" row (Target Player for Sound/Cage/Yeet).
    private static void AddPlayerPickerRow(string label, ConfigEntry<string> entry)
    {
        var row = NewRow(label);

        // Only overwrite an already-chosen target when we actually KNOW the roster and it's
        // genuinely not on it - an empty scan (GameDirector.PlayerList not populated yet, e.g.
        // this row redrawing during a level transition) does NOT mean "target is gone", but the
        // old unconditional check treated it that way, silently resetting the saved pick to
        // "None" on the very next tab switch/redraw and making every action that reads this
        // field (Teleport/Cage/Yeet/...) quietly no-op afterward with nothing in the log to
        // explain why.
        List<string> initial = FakeSoundHack.ScanPlayerNames();
        if (initial.Count > 0 && !initial.Contains(entry.Value))
            entry.Value = initial[0];
        else if (initial.Count == 0 && string.IsNullOrEmpty(entry.Value))
            entry.Value = "None";

        Button cycleBtn = CreateButton(row.transform, entry.Value, null);
        var cycleLe = cycleBtn.gameObject.AddComponent<LayoutElement>();
        cycleLe.preferredWidth = 160;
        cycleLe.preferredHeight = 26;

        cycleBtn.onClick.AddListener(() =>
        {
            List<string> fresh = FakeSoundHack.ScanPlayerNames();
            if (fresh.Count == 0)
            {
                entry.Value = "None";
                cycleBtn.GetComponentInChildren<Text>().text = "None";
                return;
            }
            int idx = fresh.IndexOf(entry.Value);
            string next = fresh[(idx + 1) % fresh.Count];
            entry.Value = next;
            cycleBtn.GetComponentInChildren<Text>().text = next;
        });
    }

    private static void AddItemStealRow()
    {
        AddToggleRow("Украсть предмет из рук (по имени, ниже)", CheatState.StealHeldItem);

        var row = NewRow("  -> Предмет для кражи");

        // Same fix as the player picker above - an empty scan (item list not ready yet, e.g. this
        // row redrawing during a level transition) must not wipe an already-chosen target.
        List<string> initial = ItemStealHack.ScanItemNames();
        if (initial.Count > 0 && !initial.Contains(CheatState.StealHeldItemName.Value))
            CheatState.StealHeldItemName.Value = initial[0];
        else if (initial.Count == 0 && string.IsNullOrEmpty(CheatState.StealHeldItemName.Value))
            CheatState.StealHeldItemName.Value = "None";

        Button cycleBtn = CreateButton(row.transform, CheatState.StealHeldItemName.Value, null);
        var cycleLe = cycleBtn.gameObject.AddComponent<LayoutElement>();
        cycleLe.preferredWidth = 160;
        cycleLe.preferredHeight = 26;

        cycleBtn.onClick.AddListener(() =>
        {
            List<string> fresh = ItemStealHack.ScanItemNames();
            if (fresh.Count == 0)
            {
                CheatState.StealHeldItemName.Value = "None";
                cycleBtn.GetComponentInChildren<Text>().text = "None";
                return;
            }
            int idx = fresh.IndexOf(CheatState.StealHeldItemName.Value);
            string next = fresh[(idx + 1) % fresh.Count];
            CheatState.StealHeldItemName.Value = next;
            cycleBtn.GetComponentInChildren<Text>().text = next;
        });
    }

    private static void AddValuableSpawnRow()
    {
        var row = NewRow("Ценность для спавна (по имени, из всех уровней)");

        List<string> initial = EconomyHack.ScanValuableNames();
        if (initial.Count > 0 && !initial.Contains(CheatState.SpawnValuableName.Value))
            CheatState.SpawnValuableName.Value = initial[0];
        else if (initial.Count == 0 && string.IsNullOrEmpty(CheatState.SpawnValuableName.Value))
            CheatState.SpawnValuableName.Value = "None";

        Button cycleBtn = CreateButton(row.transform, CheatState.SpawnValuableName.Value, null);
        var cycleLe = cycleBtn.gameObject.AddComponent<LayoutElement>();
        cycleLe.preferredWidth = 160;
        cycleLe.preferredHeight = 26;

        cycleBtn.onClick.AddListener(() =>
        {
            List<string> fresh = EconomyHack.ScanValuableNames();
            if (fresh.Count == 0)
            {
                CheatState.SpawnValuableName.Value = "None";
                cycleBtn.GetComponentInChildren<Text>().text = "None";
                return;
            }
            int idx = fresh.IndexOf(CheatState.SpawnValuableName.Value);
            string next = fresh[(idx + 1) % fresh.Count];
            CheatState.SpawnValuableName.Value = next;
            cycleBtn.GetComponentInChildren<Text>().text = next;
        });

        AddButtonRow("Заспавнить выбранную ценность в зоне эвакуации (работает у гостя)", "Заспавнить", EconomyHack.SpawnValuable);
    }

    private static void AddCycleRow(string label, ConfigEntry<string> entry, string[] options)
    {
        var row = NewRow(label);

        int IndexOf()
        {
            for (int i = 0; i < options.Length; i++)
                if (string.Equals(options[i], entry.Value, StringComparison.OrdinalIgnoreCase))
                    return i;
            return 0;
        }

        Button btn = CreateButton(row.transform, options[IndexOf()], null);
        var le = btn.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = 100;
        le.preferredHeight = 26;

        btn.onClick.AddListener(() =>
        {
            entry.Value = options[(IndexOf() + 1) % options.Length];
            btn.GetComponentInChildren<Text>().text = entry.Value;
        });
    }

    private static void AddIntStepperRow(string label, ConfigEntry<int> entry, int min, int max, int step)
    {
        var row = NewRow(label);
        AddStepperButtons(row, () => entry.Value.ToString(),
            () => entry.Value = Mathf.Clamp(entry.Value - step, min, max),
            () => entry.Value = Mathf.Clamp(entry.Value + step, min, max));
    }

    private static void AddFloatStepperRow(string label, ConfigEntry<float> entry, float min, float max, float step)
    {
        var row = NewRow(label);
        AddStepperButtons(row, () => entry.Value.ToString("0.0"),
            () => entry.Value = Mathf.Clamp(entry.Value - step, min, max),
            () => entry.Value = Mathf.Clamp(entry.Value + step, min, max));
    }

    // Generic single-line text field row for any string ConfigEntry - added for the HUD tab's
    // media title field (CustomOverlayUI's own equivalent, TextFieldRow, predates this menu having
    // one at all since every earlier free-text row here grew its own hardcoded copy instead).
    private static void AddTextFieldRow(string label, ConfigEntry<string> entry)
    {
        var row = NewRow(label);
        InputField field = CreateInputField(row.transform, entry.Value, multiline: false,
            v => entry.Value = v);
        var le = field.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = 1;
        le.preferredHeight = 26;
    }

    // Single-line free-text field bound straight to CheatState.ChatSpamText - unlike AddCycleRow
    // (fixed options array) or the player pickers, the phrase list is arbitrary user text, so it
    // reuses the same CreateInputField the Scripts tab's editor box is built on.
    private static void AddChatSpamTextRow()
    {
        var row = NewRow("Фразы для спама (через | )");
        InputField field = CreateInputField(row.transform, CheatState.ChatSpamText.Value, multiline: false,
            v => CheatState.ChatSpamText.Value = v);
        var le = field.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = 1;
        le.preferredHeight = 26;
    }

    // Same shape as AddChatSpamTextRow above, bound to CheatState.NameChangerText - the actual
    // network broadcast only happens on demand (NameChangerHack.SetName), not on every keystroke,
    // so this needs its own Apply button rather than firing straight off the field's own onChange.
    private static void AddNameChangerRow()
    {
        var row = NewRow("Новое имя");
        InputField field = CreateInputField(row.transform, CheatState.NameChangerText.Value, multiline: false,
            v => CheatState.NameChangerText.Value = v);
        var le = field.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = 1;
        le.preferredHeight = 26;

        Button apply = CreateButton(row.transform, "Сменить", () => NameChangerHack.SetName(CheatState.NameChangerText.Value));
        var applyLe = apply.gameObject.AddComponent<LayoutElement>();
        applyLe.preferredWidth = 90;
        applyLe.preferredHeight = 26;
    }

    // No preset buttons here. Plain phrase lists only - no rich-text markup. TMP tags like giant
    // <size=...> or full-cover <mark=...> are a rendering exploit against whoever's near the
    // sender (blinds/freezes their chat UI without consent), not something this mod ships a
    // one-click button for - removed outright rather than left as a toggle-off default.

    private static void AddStepperButtons(GameObject row, Func<string> getText, Action onDec, Action onInc)
    {
        Text valueText = null;

        Button dec = CreateButton(row.transform, "-", null);
        var decLe = dec.gameObject.AddComponent<LayoutElement>();
        decLe.preferredWidth = 28;
        decLe.preferredHeight = 26;
        AddHoldToRepeat(dec.gameObject, () => { onDec(); valueText.text = getText(); });

        valueText = CreateText(row.transform, getText(), 16, TextAnchor.MiddleCenter, Color.white);
        var valueLe = valueText.gameObject.AddComponent<LayoutElement>();
        valueLe.preferredWidth = 60;
        valueLe.preferredHeight = 26;

        Button inc = CreateButton(row.transform, "+", null);
        var incLe = inc.gameObject.AddComponent<LayoutElement>();
        incLe.preferredWidth = 28;
        incLe.preferredHeight = 26;
        AddHoldToRepeat(inc.gameObject, () => { onInc(); valueText.text = getText(); });
    }

    // First click fires immediately (via EventTrigger PointerClick); holding down repeats the
    // same action at an accelerating rate via OverlayUI.Tick(), so dragging a slider from 1 to
    // 50 doesn't take fifty individual clicks.
    private static void AddHoldToRepeat(GameObject buttonGo, Action action)
    {
        var trigger = buttonGo.AddComponent<EventTrigger>();

        var downEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        downEntry.callback.AddListener(_ => { action(); StartRepeat(action); });
        trigger.triggers.Add(downEntry);

        var upEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        upEntry.callback.AddListener(_ => StopRepeat());
        trigger.triggers.Add(upEntry);

        var exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exitEntry.callback.AddListener(_ => StopRepeat());
        trigger.triggers.Add(exitEntry);
    }

    private static void SetPanelColor(Button btn, Color baseColor)
    {
        var image = btn.GetComponent<Image>();
        image.color = baseColor;

        var colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        colors.selectedColor = Color.white;
        colors.fadeDuration = 0.05f;
        btn.colors = colors;
    }

    private static RectTransform CreatePanel(Transform parent, Color color, bool rounded = true)
    {
        var go = new GameObject("Panel", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = color;
        if (rounded)
        {
            image.sprite = RoundedSprite;
            image.type = Image.Type.Sliced;
        }
        return go.GetComponent<RectTransform>();
    }

    // Plain (non-sliced) circle - only used for the toggle-switch knob, so it stays perfectly
    // round at its small size instead of going through the capsule's 9-slice corner math.
    private static RectTransform CreateCircle(Transform parent, Color color)
    {
        var go = new GameObject("Circle", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = color;
        image.sprite = CircleSprite;
        image.type = Image.Type.Simple;
        return go.GetComponent<RectTransform>();
    }

    // wrap:true (row labels) lets long descriptive text wrap to multiple lines within its own
    // column instead of overflowing past it - the default (false) keeps every other caller
    // (buttons, values, headers - all short, fixed, single-line text) exactly as before.
    private static Text CreateText(Transform parent, string text, int size, TextAnchor anchor, Color color, bool wrap = false)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        t.fontSize = size;
        t.alignment = anchor;
        t.color = color;
        t.text = text;
        t.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    private static Button CreateButton(Transform parent, string text, Action onClick)
    {
        var go = new GameObject("Button", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = ButtonIdleColor;
        image.sprite = RoundedSprite;
        image.type = Image.Type.Sliced;
        var btn = go.AddComponent<Button>();
        var label = CreateText(go.transform, text, 15, TextAnchor.MiddleCenter, Color.white);
        SetRect(label.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        if (onClick != null) btn.onClick.AddListener(() => onClick());
        SetPanelColor(btn, image.color);
        return btn;
    }

    // Legacy UGUI InputField (this whole overlay is built on UnityEngine.UI, not TMP - matches
    // CreateText's plain Text component) - needs its own Text child assigned as textComponent,
    // same requirement whether it ends up single- or multi-line.
    private static InputField CreateInputField(Transform parent, string initialText, bool multiline, Action<string> onChanged)
    {
        var go = new GameObject("InputField", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.25f);
        image.sprite = RoundedSprite;
        image.type = Image.Type.Sliced;
        var input = go.AddComponent<InputField>();

        var text = CreateText(go.transform, "", 14, multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft, Color.white, wrap: multiline);
        SetRect(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(8, 4), new Vector2(-8, -4));
        input.textComponent = text;
        input.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
        input.text = initialText ?? "";

        if (onChanged != null) input.onValueChanged.AddListener(v => onChanged(v));
        return input;
    }

    // Section header + multi-line source box + name field/save/run buttons + a status line +
    // the saved-module list. Rebuilt from scratch (via SelectTab) after Save/Delete so the list
    // below always reflects ScriptModuleManager.Modules - same pattern every other tab already
    // uses for anything that can change while the menu is open.
    private static void BuildScriptsTab()
    {
        AddSectionHeader("Свой скрипт (C#)");

        var row = NewRow("Пишете обычный C#. Доступны: Local, Controller, Director, Steam, Game, Run, " +
            "Players(), OtherPlayers(), Log(...), GetField/SetField/Call(...), SetCheat(\"Fly\", true), " +
            "GetCheat(\"Fly\"), RenameRoom(\"имя\"). Photon (PhotonNetwork, RoomOptions, TypedLobby и " +
            "т.д.) и Task тоже доступны напрямую. Для ожидания сетевых операций используйте " +
            "\"await Task.Delay(100);\", а не пустой while - иначе подвесите игру. Готовый пример " +
            "создания комнаты по имени лежит в модулях ниже - \"СоздатьЛобби\".");
        InputField editor = CreateInputField(row.transform, _scriptDraft, multiline: true, v => _scriptDraft = v);
        var editorLe = editor.gameObject.AddComponent<LayoutElement>();
        editorLe.flexibleWidth = 1;
        editorLe.preferredHeight = 130;
        editorLe.minHeight = 130;

        var runRow = NewRow("Имя модуля (для сохранения, необязательно для разового запуска)");
        InputField nameField = CreateInputField(runRow.transform, _scriptModuleName, multiline: false, v => _scriptModuleName = v);
        var nameLe = nameField.gameObject.AddComponent<LayoutElement>();
        nameLe.preferredWidth = 140;

        Button runBtn = CreateButton(runRow.transform, "Запустить", () =>
        {
            // Async - see ScriptEngine.StartRun's comment. SetScriptStatus fires later, once the
            // script actually finishes (immediately for a simple script, or after however long
            // its own await chain takes for one that waits on Photon/Steam), not before this
            // click handler returns.
            _scriptStatus = "Выполняется...";
            _scriptStatusOk = true;
            SelectTab("Скрипты"); // shows "Выполняется..." right away instead of only once StartRun's callback lands
            ScriptEngine.StartRun(_scriptDraft, SetScriptStatus);
        });
        var runLe = runBtn.gameObject.AddComponent<LayoutElement>();
        runLe.preferredWidth = 100;
        runLe.preferredHeight = 26;
        SetPanelColor(runBtn, AccentBlueDim);

        Button saveBtn = CreateButton(runRow.transform, "Сохранить", () =>
        {
            ScriptEngine.Result result = ScriptModuleManager.Add(_scriptModuleName.Trim(), _scriptDraft);
            SetScriptStatus(result);
            if (result.Success) SelectTab("Скрипты");
        });
        var saveLe = saveBtn.gameObject.AddComponent<LayoutElement>();
        saveLe.preferredWidth = 100;
        saveLe.preferredHeight = 26;
        SetPanelColor(saveBtn, ButtonIdleColor);

        if (!string.IsNullOrEmpty(_scriptStatus))
        {
            // Straight into _content, not via NewRow - NewRow's own label always claims a fixed
            // 300px column for a HorizontalLayoutGroup pairing with a control alongside it, which
            // would leave this (label-only, full-width) status message squeezed into whatever's
            // left over instead of using the row's actual width.
            var statusText = CreateText(_content.transform, _scriptStatus, 12,
                TextAnchor.UpperLeft, _scriptStatusOk ? new Color(0.4f, 0.9f, 0.5f) : DangerRed, wrap: true);
            _contentRows.Add(statusText.gameObject);
            var statusLe = statusText.gameObject.AddComponent<LayoutElement>();
            statusLe.preferredHeight = 40;
        }

        AddSectionHeader("Сохранённые модули");
        if (ScriptModuleManager.Modules.Count == 0)
        {
            NewRow("Пока ничего не сохранено.");
        }
        foreach (ScriptModule module in ScriptModuleManager.Modules)
        {
            string name = module.Name; // capture per-iteration for the closures below
            var modRow = NewRow(name);

            Button modRunBtn = CreateButton(modRow.transform, "Запустить", () =>
            {
                _scriptStatus = "Выполняется...";
                _scriptStatusOk = true;
                SelectTab("Скрипты");
                ScriptModuleManager.Run(name, SetScriptStatus);
            });
            var modRunLe = modRunBtn.gameObject.AddComponent<LayoutElement>();
            modRunLe.preferredWidth = 90;
            modRunLe.preferredHeight = 26;
            SetPanelColor(modRunBtn, AccentBlueDim);

            Button modDelBtn = CreateButton(modRow.transform, "Удалить", () =>
            {
                ScriptModuleManager.Remove(name);
                SelectTab("Скрипты");
            });
            var modDelLe = modDelBtn.gameObject.AddComponent<LayoutElement>();
            modDelLe.preferredWidth = 90;
            modDelLe.preferredHeight = 26;
            SetPanelColor(modDelBtn, DangerRedDim);
        }
    }

    private static void SetScriptStatus(ScriptEngine.Result result)
    {
        _scriptStatusOk = result.Success;
        _scriptStatus = result.Success ? "OK" : result.Message;
        SelectTab("Скрипты");
    }

    private static void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    private class WindowDragHandler : MonoBehaviour, IDragHandler
    {
        public RectTransform Target;

        public void OnDrag(PointerEventData eventData)
        {
            if (Target == null) return;
            Target.anchoredPosition += eventData.delta / Target.GetComponentInParent<Canvas>().scaleFactor;
        }
    }
}
