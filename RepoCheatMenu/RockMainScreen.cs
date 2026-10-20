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
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace RepoCheatMenu;

// Главный экран R.E.P.O. в духе Rockstar: затемнённый "экран блокировки" с большими часами и датой,
// клик (или Пробел/Enter) смахивает часы вверх и показывает ряд круглых кнопок - те же действия,
// что у кнопок игры (MenuPageMain.ButtonEvent*). Сам экран игры остаётся под оверлеем, живой:
//   * его страница скрывается CanvasGroup'ом (alpha 0), а не SetActive - иначе встанут её
//     анимации входа/выхода и страницу нельзя будет закрыть как обычно;
//   * клики сквозь оверлей по скрытым кнопкам игры глушит GameButtonsBlockPatch
//     (SemiFunc.UIMouseHover для страницы MenuPageMain возвращает false, пока экран виден).
// Показывается, только пока открыта именно главная страница меню (MenuPageIndex.Main) и закрыты
// оба меню мода; настройки/лобби/сохранения открываются игрой как обычно и экран при этом гаснет.
// Размеры - в "единицах Rockstar", 1 единица = высота экрана / 540 (на 1080p это 2 пикселя).
internal static class RockMainScreen
{
    private struct Entry
    {
        public string Label, Icon;
        public Action Run;
        public bool Danger;
    }

    private static readonly Entry[] Entries =
    {
        new() { Label = "Одиночная", Icon = "player", Run = () => MenuPageMain.instance?.ButtonEventSinglePlayer() },
        new() { Label = "Создать", Icon = "other", Run = () => MenuPageMain.instance?.ButtonEventHostGame() },
        new() { Label = "Войти", Icon = "search", Run = () => MenuPageMain.instance?.ButtonEventJoinGame() },
        new() { Label = "Случайная", Icon = "movement", Run = () => MenuPageMain.instance?.ButtonEventPlayRandom() },
        new() { Label = "Обучение", Icon = "check", Run = () => MenuPageMain.instance?.ButtonEventTutorial() },
        new() { Label = "Облик", Icon = "visuals", Run = () => MenuPageMain.instance?.ButtonEventChangeColor() },
        new() { Label = "Настройки", Icon = "setting", Run = () => MenuPageMain.instance?.ButtonEventSettings() },
        new() { Label = "Выход", Icon = "xmark", Run = () => MenuPageMain.instance?.ButtonEventQuit(), Danger = true },
    };

    private static readonly HudKit.Tween Vis = new(350f, HudKit.InOut);
    private static readonly HudKit.Tween Reveal = new(800f, HudKit.Soft);
    private static readonly HudKit.Tween[] Hover = CreateHovers();
    private static readonly IconPool Icons = new();

    private static bool _open;
    private static bool _wasShown;
    private static CanvasGroup _group;
    private static MenuPage _mainPage;
    private static string _user;

    // Читает GameButtonsBlockPatch: пока true, страница главного меню игры не видит курсор.
    internal static bool BlockMainPage { get; private set; }
    internal static MenuPage MainPage => _mainPage;
    // Для LyricsHud: пока экран виден, меню игры скрыто и текст песен уходит в центр, под кнопки.
    internal static bool Visible => Vis.Value > 0.05f;

    private static HudKit.Tween[] CreateHovers()
    {
        var hovers = new HudKit.Tween[Entries.Length];
        for (int i = 0; i < hovers.Length; i++) hovers[i] = new HudKit.Tween(180f, HudKit.InOut);
        return hovers;
    }

    private static bool MainPageIsCurrent()
    {
        if (!SemiFunc.IsMainMenu()) return false;
        MenuManager mm = MenuManager.instance;
        if (mm == null || MenuPageMain.instance == null) return false;
        return GameReflect.Get<MenuPageIndex>(mm, "currentMenuPageIndex") == MenuPageIndex.Main;
    }

    public static void Draw()
    {
        bool want = CheatState.MainScreen.Value && MainPageIsCurrent()
            && !CustomOverlayUI.IsOpen && !OverlayUI.IsOpen;

        float vis = Vis.Set(want);
        if (!want && vis < 0.003f)
        {
            Release();
            return;
        }

        if (want && !_wasShown) _open = CheatState.MainScreenStartOpen.Value;
        _wasShown = want || vis >= 0.003f;

        HidePage(vis);

        float sw = CustomGui.ScreenWidth, sh = CustomGui.ScreenHeight;
        if (sw <= 0f) return;
        float u = sh / 540f;
        float reveal = Reveal.Set(_open ? 1f : 0f);

        HandleInput(want, vis, reveal, sw, sh, u);

        // Размытый кадр под затемнением; нет рендер-текстуры - просто плотнее тёмная подложка.
        bool blurred = ScreenBlur.Render(0.95f * vis, new Color(0.46f, 0.44f, 0.52f, 1f));
        CustomGui.Rect(new Rect(0f, 0f, sw, sh), new Color(0.03f, 0.03f, 0.05f, (blurred ? 0.34f : 0.80f) * vis));
        float shadeH = Mathf.Min(130f * u, sh * 0.38f);
        CustomGui.RectGradient(new Rect(0f, 0f, sw, shadeH), new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0.35f * vis));

        Icons.Begin();
        try
        {
            DrawClock(sw, sh, u, vis, reveal);
            DrawFooter(sw, sh, u, vis, reveal);
            DrawButtons(sw, sh, u, vis, reveal);
            DrawCursor(u, vis);
        }
        finally
        {
            Icons.End();
        }
        CustomGui.Style = CustomGui.TextStyle.Regular;
    }

    // --- game page ------------------------------------------------------------------------------

    private static void HidePage(float vis)
    {
        MenuPageMain main = MenuPageMain.instance;
        if (main == null)
        {
            _mainPage = null;
            _group = null;
            BlockMainPage = false;
            return;
        }
        if (_mainPage == null) _mainPage = GameReflect.Get<MenuPage>(main, "menuPage");
        if (_group == null) _group = main.GetComponent<CanvasGroup>() ?? main.gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 1f - Mathf.Clamp01(vis * 1.2f);
        BlockMainPage = vis > 0.05f;
    }

    private static void Release()
    {
        _wasShown = false;
        BlockMainPage = false;
        ScreenBlur.Hide();
        if (_group != null) _group.alpha = 1f;
    }

    // --- input ----------------------------------------------------------------------------------

    private static void HandleInput(bool want, float vis, float reveal, float sw, float sh, float u)
    {
        if (!want || vis < 0.8f) return;

        bool keyOpen = false;
        try { keyOpen = Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return); }
        catch (Exception) { }
        if (keyOpen && !_open) { _open = true; return; }

        if (!UiInput.Down(0)) return;

        if (_open && reveal > 0.9f)
        {
            for (int i = 0; i < Entries.Length; i++)
            {
                if (CustomGui.IsHovered(ButtonRect(i, sw, sh, u, reveal)))
                {
                    Entries[i].Run();
                    return;
                }
            }
        }
        _open = !_open;
    }

    // --- drawing --------------------------------------------------------------------------------

    private static void DrawClock(float sw, float sh, float u, float vis, float reveal)
    {
        float a = vis * (1f - reveal);
        if (a < 0.01f) return;

        float yDown = Mathf.Lerp(80f, -120f, reveal) * u;
        int clockSize = Mathf.RoundToInt(65f * u);
        CustomGui.Style = CustomGui.TextStyle.Medium;
        CustomGui.Text(new Rect(0f, sh - (yDown + clockSize * 1.25f), sw, clockSize * 1.25f),
            DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture), clockSize, new Color(1f, 1f, 1f, a), TextAnchor.MiddleCenter);

        int dateSize = Mathf.RoundToInt(16f * u);
        CustomGui.Text(new Rect(0f, sh - (yDown - 23f * u + dateSize * 1.4f), sw, dateSize * 1.4f),
            DateText(), dateSize, new Color(1f, 1f, 1f, a * 0.9f), TextAnchor.MiddleCenter);

        CustomGui.Style = CustomGui.TextStyle.Regular;
        int hintSize = Mathf.RoundToInt(10f * u);
        CustomGui.Text(new Rect(0f, sh - (sh - 70f * u + hintSize * 1.4f), sw, hintSize * 1.4f),
            "Нажмите, чтобы продолжить", hintSize, new Color(1f, 1f, 1f, a * 0.7f), TextAnchor.MiddleCenter);
    }

    private static string DateText()
    {
        string text;
        try { text = DateTime.Now.ToString("dddd, d MMMM", new CultureInfo("ru-RU")); }
        catch (Exception) { text = DateTime.Now.ToString("dddd, d MMMM", CultureInfo.InvariantCulture); }
        return text.Length > 0 ? char.ToUpperInvariant(text[0]) + text.Substring(1) : text;
    }

    private static void DrawFooter(float sw, float sh, float u, float vis, float reveal)
    {
        float a = vis * (1f - reveal);
        if (a < 0.01f) return;

        float size = 26f * u;
        float cx = sw * 0.5f;
        float topDown = sh - 54f * u;
        CustomGui.Circle(new Vector2(cx, sh - (topDown + size * 0.5f)), size * 0.5f, new Color(1f, 1f, 1f, 0.18f * a));

        int nameSize = Mathf.RoundToInt(11f * u);
        CustomGui.Text(new Rect(0f, sh - (sh - 22f * u + nameSize * 1.4f), sw, nameSize * 1.4f),
            UserName(), nameSize, new Color(1f, 1f, 1f, a), TextAnchor.MiddleCenter);
    }

    private static string UserName()
    {
        if (_user != null) return _user;
        try
        {
            if (Steamworks.SteamClient.IsValid) _user = Steamworks.SteamClient.Name;
        }
        catch (Exception) { }
        if (string.IsNullOrEmpty(_user)) return "R.E.P.O.";
        return _user;
    }

    private static Rect ButtonRect(int i, float sw, float sh, float u, float reveal)
    {
        float size = 30f * u, gap = 8f * u;
        float total = Entries.Length * size + (Entries.Length - 1) * gap;
        float x = (sw - total) * 0.5f + i * (size + gap);
        float centerDown = sh * 0.5f + 20f * u - 10f * u * reveal;
        return new Rect(x, sh - (centerDown + size * 0.5f), size, size);
    }

    private static void DrawButtons(float sw, float sh, float u, float vis, float reveal)
    {
        if (reveal < 0.01f) return;

        for (int i = 0; i < Entries.Length; i++)
        {
            // Кнопки выезжают по очереди, как в Rockstar.
            float p = Mathf.Clamp01((reveal - i * 0.05f) / 0.55f) * vis;
            if (p < 0.01f) continue;

            Rect r = ButtonRect(i, sw, sh, u, reveal);
            bool hovered = reveal > 0.9f && vis > 0.8f && CustomGui.IsHovered(r);
            float hv = Hover[i].Set(hovered ? 1f : 0f);

            Rect drawn = Grow(r, 2f * u * hv);
            Color tint = Entries[i].Danger ? new Color(1f, 0.38f, 0.38f) : Color.white;
            CustomGui.Rect(drawn, new Color(tint.r, tint.g, tint.b, (0.10f + 0.16f * hv) * p), 10f * u);
            CustomGui.RoundedRing(drawn, 10f * u, Mathf.Max(1f, 0.6f * u), new Color(1f, 1f, 1f, (0.10f + 0.12f * hv) * p));

            Icons.Draw(Entries[i].Icon, Grow(drawn, -7f * u), new Color(tint.r, tint.g, tint.b, p));

            int labelSize = Mathf.RoundToInt(7f * u);
            CustomGui.Text(new Rect(r.x - 14f * u, r.y - labelSize * 1.6f - 3f * u, r.width + 28f * u, labelSize * 1.6f),
                Entries[i].Label, labelSize, new Color(1f, 1f, 1f, p * (0.55f + 0.45f * hv)), TextAnchor.MiddleCenter);
        }
    }

    // Курсор игры лежит на её собственном канвасе под нашим затемнением и почти не виден, поэтому
    // пока экран показан, рисуем свою стрелку поверх.
    private static void DrawCursor(float u, float vis)
    {
        Vector2 m = CustomGui.MouseLocalPos();
        Vector2 a = m + new Vector2(0f, -13f * u), b = m + new Vector2(9.5f * u, -9.5f * u);
        float o = 1.2f * u;
        CustomGui.Triangle(m + new Vector2(-o, o * 1.4f), a + new Vector2(-o, -o * 1.6f), b + new Vector2(o * 1.6f, -o), new Color(0f, 0f, 0f, 0.7f * vis));
        CustomGui.Triangle(m, a, b, new Color(1f, 1f, 1f, vis));
    }

    private static Rect Grow(Rect r, float by) => new(r.x - by, r.y - by, r.width + by * 2f, r.height + by * 2f);
}

// Пока виден главный экран мода, страница MenuPageMain не должна реагировать на курсор: её кнопки
// лежат ровно под нашими. SemiFunc.UIMouseHover - единственная проверка наведения у кнопок меню.
[HarmonyPatch(typeof(SemiFunc), nameof(SemiFunc.UIMouseHover))]
internal static class GameButtonsBlockPatch
{
    [HarmonyPrefix]
    private static bool Prefix(MenuPage parentPage, ref bool __result)
    {
        if (!RockMainScreen.BlockMainPage || parentPage == null || parentPage != RockMainScreen.MainPage) return true;
        __result = false;
        return false;
    }
}
