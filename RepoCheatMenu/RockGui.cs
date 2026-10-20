// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// Часть дизайна, чисел и ресурсов здесь перенесена из клиента Rockstar;
// эти части принадлежат их авторам.
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace RepoCheatMenu;

// Widget visuals for the menu window, taken from Rockstar's "Modern" ClickGUI (sizes in its GUI
// units, `u` = pixels per unit, same CheatState.HudScale the HUD cards use): the switch is a 13x8
// track with a 7x7 knob that slides 5 units, the slider is a 2-unit track with a 6-unit thumb,
// labels are font 8 and values font 7, every hover/enable change is a 300 ms ease-in-out tween,
// rows are separated by a 0.5-unit hairline at 2 % white. Everything draws through CustomGui, so the
// window's open/close transform applies for free. Behaviour only - no game state in here.
internal static class RockGui
{
    public static readonly Color Accent = new(144f / 255f, 107f / 255f, 1f, 1f);
    public static readonly Color Panel6 = new(26f / 255f, 23f / 255f, 31f / 255f, 1f);
    public static readonly Color Border = new(61f / 255f, 54f / 255f, 71f / 255f, 0.35f);
    private static readonly Color TrackOff = new(36f / 255f, 32f / 255f, 43f / 255f, 1f);

    private sealed class Anim
    {
        public readonly HudKit.Tween Hover = new(160f, HudKit.InOut);
        public readonly HudKit.Tween On = new(300f, HudKit.InOut);
    }

    private static readonly Dictionary<object, Anim> Anims = new();
    private static object _dragKey;

    private static Anim Get(object key)
    {
        if (!Anims.TryGetValue(key, out Anim a))
            Anims[key] = a = new Anim();
        return a;
    }

    // Eased 0..1 hover amount for any keyed element (rail icons, list items).
    public static float HoverAmount(object key, bool hovered) => Get(key).Hover.Set(hovered);
    public static float OnAmount(object key, bool on) => Get(key).On.Set(on);

    public static Color White(float a = 1f) => new(1f, 1f, 1f, a);

    public static void Hairline(Rect row, float u)
    {
        CustomGui.Rect(new Rect(row.x, row.y - 0.25f * u, row.width, 0.5f * u), White(0.02f));
    }

    public static int Font(float units, float u) => Mathf.Max(8, Mathf.RoundToInt(units * u));

    // Label on the left of a row, font 8, brightened by hover / enabled state.
    public static void Label(Rect row, string label, float u, float alpha, float rightReserve)
    {
        Rect area = new(row.x + 6f * u, row.y, row.width - 6f * u - rightReserve, row.height);
        CustomGui.TextClipped(area, label, Font(8f, u), White(alpha), TextAnchor.MiddleLeft);
    }

    // --- switch ---------------------------------------------------------------------------------

    // Middle-click a row to bind a key to it (the old menu's gesture): the key shows left of the switch,
    // "..." while waiting for the press.
    private static float BindChip(Rect row, Keybind kb, float rightEdge, float u)
    {
        if (kb == null) return 0f;
        bool capturing = ReferenceEquals(KeybindManager.Capturing, kb);
        string text = capturing ? "..." : kb.Vk.Value != 0 ? Win32.VkName(kb.Vk.Value) : null;

        if (CustomGui.IsHovered(row) && UiInput.Down(2) && KeybindManager.Capturing == null)
            KeybindManager.StartCapture(kb);
        if (text == null) return 0f;

        float w = CustomGui.MeasureText(text, Font(6.5f, u)) + 8f * u;
        Rect chip = new(rightEdge - w, row.y + (row.height - 10f * u) * 0.5f, w, 10f * u);
        CustomGui.Rect(chip, capturing ? new Color(Accent.r, Accent.g, Accent.b, 0.5f) : White(0.09f), 3f * u);
        CustomGui.Text(chip, text, Font(6.5f, u), White(capturing ? 1f : 0.7f), TextAnchor.MiddleCenter);
        return w + 4f * u;
    }

    public static bool Toggle(object key, Rect row, string label, bool value, float u, Keybind kb = null)
    {
        Anim a = Get(key);
        bool hovered = CustomGui.IsHovered(row);
        float hover = a.Hover.Set(hovered);
        float on = a.On.Set(value);

        Rect track = new(row.xMax - 13f * u - 6f * u, row.y + (row.height - 8f * u) * 0.5f, 13f * u, 8f * u);
        float chipW = BindChip(row, kb, track.x - 4f * u, u);
        Label(row, label, u, 0.75f + 0.25f * on + 0.25f * hover, 22f * u + chipW);

        Color c = Color.Lerp(TrackOff, Accent, on);
        CustomGui.Rect(track, c, 3f * u);

        float knobX = track.x + 0.5f * u + 5f * on * u;
        Rect knob = new(knobX, track.y + 0.5f * u, 7f * u, 7f * u);
        CustomGui.Rect(knob, White(0.75f + 0.25f * on), 3.5f * u);

        return hovered && UiInput.Down(0) ? !value : value;
    }

    // --- slider ---------------------------------------------------------------------------------

    // Two-line row (label + value above, track below). Dragging snaps to `step`; returns the new value.
    public static float Slider(object key, Rect row, string label, float value, float min, float max, float step, string valueText, float u)
    {
        Anim a = Get(key);
        bool hovered = CustomGui.IsHovered(row);
        float hover = a.Hover.Set(hovered || _dragKey == key);

        float lineH = 12f * u;
        Rect top = new(row.x, row.yMax - lineH - 1f * u, row.width, lineH);
        float valueW = CustomGui.MeasureText(valueText, Font(7f, u)) + 4f * u;
        Label(top, label, u, 0.75f + 0.25f * hover, valueW + 8f * u);
        CustomGui.Text(new Rect(top.xMax - valueW - 6f * u, top.y, valueW, top.height), valueText, Font(7f, u), White(0.75f + 0.25f * hover), TextAnchor.MiddleRight);

        float tx = row.x + 9f * u, tw = row.width - 18f * u;
        Rect track = new(tx, row.y + 6f * u, tw, 2f * u);
        float span = Mathf.Max(max - min, 1e-6f);

        if (UiInput.Down(0) && CustomGui.IsHovered(new Rect(tx - 4f * u, row.y, tw + 8f * u, 14f * u)))
            _dragKey = key;
        if (!UiInput.Held(0) && _dragKey == key)
            _dragKey = null;

        if (_dragKey == key)
        {
            float t = Mathf.Clamp01((CustomGui.MouseLocalPos().x - tx) / Mathf.Max(tw, 1f));
            float raw = min + t * span;
            value = step > 0f ? Mathf.Clamp(Mathf.Round((raw - min) / step) * step + min, min, max) : raw;
        }

        float frac = Mathf.Clamp01((value - min) / span);
        CustomGui.Rect(track, new Color(0.17f, 0.15f, 0.2f, 1f), 0.5f * u);
        if (frac > 0.001f)
            CustomGui.Rect(new Rect(track.x, track.y, tw * frac, track.height), Accent, 0.5f * u);

        float grow = _dragKey == key ? 1f : hover;
        float thumbW = 6f * u + 3f * u * grow;
        float thumbH = 6f * u + 1f * u * grow;
        CustomGui.Rect(new Rect(track.x + tw * frac - thumbW * 0.5f, track.center.y - thumbH * 0.5f, thumbW, thumbH), White(), thumbH * 0.5f);

        return value;
    }

    // --- buttons --------------------------------------------------------------------------------

    public enum Kind { Normal, Primary, Danger }

    public static bool Button(object key, Rect r, string text, Kind kind, float u, float fontUnits = 7f)
    {
        float hover = Get(key).Hover.Set(CustomGui.IsHovered(r));
        Color bg = kind switch
        {
            Kind.Primary => Color.Lerp(new Color(Accent.r, Accent.g, Accent.b, 0.45f), new Color(Accent.r, Accent.g, Accent.b, 0.8f), hover),
            Kind.Danger => Color.Lerp(new Color(0.8f, 0.2f, 0.2f, 0.4f), new Color(0.9f, 0.25f, 0.25f, 0.75f), hover),
            _ => Color.Lerp(White(0.07f), White(0.14f), hover),
        };
        CustomGui.Rect(r, bg, Mathf.Min(r.height * 0.5f, 4f * u));
        CustomGui.TextClipped(new Rect(r.x + 3f * u, r.y, r.width - 6f * u, r.height), text, Font(fontUnits, u),
            White(0.8f + 0.2f * hover), TextAnchor.MiddleCenter);
        return CustomGui.IsHovered(r) && UiInput.Down(0);
    }

    // Button row: label on the left, one pill on the right.
    public static bool ButtonRow(object key, Rect row, string label, string text, float u, Keybind kb = null)
    {
        float w = Mathf.Clamp(CustomGui.MeasureText(text, Font(7f, u)) + 14f * u, 40f * u, 90f * u);
        Rect pill = new(row.xMax - w - 6f * u, row.y + (row.height - 11f * u) * 0.5f, w, 11f * u);
        bool hovered = CustomGui.IsHovered(row);
        float chipW = BindChip(row, kb, pill.x - 4f * u, u);
        Label(row, label, u, 0.75f + 0.25f * Get(key).Hover.Set(hovered), w + 10f * u + chipW);
        return Button((key, "pill"), pill, text, Kind.Primary, u);
    }

    // Row that shows its current value as a pill and advances on click (mode / cycle settings).
    public static bool CycleRow(object key, Rect row, string label, string current, float u, Color? swatch = null)
    {
        float extra = swatch.HasValue ? 9f * u : 0f;
        float w = Mathf.Clamp(CustomGui.MeasureText(current, Font(7f, u)) + 14f * u + extra, 40f * u, 110f * u);
        Rect pill = new(row.xMax - w - 6f * u, row.y + (row.height - 11f * u) * 0.5f, w, 11f * u);
        bool hovered = CustomGui.IsHovered(row);
        Label(row, label, u, 0.75f + 0.25f * Get(key).Hover.Set(hovered), w + 10f * u);
        bool clicked = Button((key, "pill"), new Rect(pill.x + extra, pill.y, pill.width - extra, pill.height), current, Kind.Normal, u);
        if (swatch.HasValue)
        {
            CustomGui.Rect(new Rect(pill.x, pill.y, pill.width, pill.height), White(0.07f), 4f * u);
            CustomGui.Circle(new Vector2(pill.x + 5.5f * u, pill.center.y), 2.6f * u, swatch.Value);
            clicked |= CustomGui.IsHovered(pill) && UiInput.Down(0);
        }
        return clicked;
    }

    // --- text input -----------------------------------------------------------------------------

    public static string TextBox(string id, Rect r, string value, float u)
    {
        bool focused = CustomGui.IsFocused(id);
        CustomGui.Rect(r, focused ? new Color(Accent.r, Accent.g, Accent.b, 0.22f) : new Color(0f, 0f, 0f, 0.28f), 3f * u);
        return CustomGui.TextField(new Rect(r.x - 2f * u, r.y, r.width, r.height), id, value, Color.clear, White(), Color.clear);
    }

    public static string TextRow(string id, Rect row, string label, string value, float u, float boxUnits = 120f)
    {
        float w = Mathf.Min(boxUnits * u, row.width * 0.55f);
        Rect box = new(row.xMax - w - 6f * u, row.y + (row.height - 12f * u) * 0.5f, w, 12f * u);
        Label(row, label, u, 0.85f, w + 10f * u);
        return TextBox(id, box, value, u);
    }
}
