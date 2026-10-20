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
using UnityEngine;

namespace RepoCheatMenu;

// The numbers and maths behind the HUD cards, taken from Rockstar's own HUD so the cards match it
// 1:1: its easing curves, its AnimatedValue tween, its spring, its colours, and the exact squircle
// signed-distance function its card shader evaluates (a rounded box whose corner is a p-norm
// superellipse, corner radius = radius * exponent / 2). Sizes are in Rockstar GUI units; HudCtx
// turns them into canvas pixels with CheatState.HudScale (2 = Rockstar at GUI scale 2 on 1080p).
internal static class HudKit
{
    // --- easing ---------------------------------------------------------------------------------

    private sealed class Bezier
    {
        private readonly float _x1, _y1, _x2, _y2;

        public Bezier(float x1, float y1, float x2, float y2) { _x1 = x1; _y1 = y1; _x2 = x2; _y2 = y2; }

        public float Ease(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            // Newton on x(s) = t, same 8 iterations and tolerances as Rockstar's solver.
            float s = t;
            for (int i = 0; i < 8; i++)
            {
                float x = Sample(s, _x1, _x2) - t;
                float d = Slope(s, _x1, _x2);
                if (Mathf.Abs(x) < 1e-5f || Mathf.Abs(d) < 1e-6f) break;
                s = Mathf.Clamp01(s - x / d);
            }
            return Sample(s, _y1, _y2);
        }

        private static float Sample(float t, float a, float b) =>
            3f * (1f - t) * (1f - t) * t * a + 3f * (1f - t) * t * t * b + t * t * t;

        private static float Slope(float t, float a, float b) =>
            3f * ((1f - t) * (1f - 3f * t) * a + (2f * t - 3f * t * t) * b) + 3f * t * t;
    }

    // Names are what Rockstar uses them for: Pop = element appearing (overshoots), Retreat = element leaving
    // (dips back first), Soft = the staggered inner content, InOut = plain ease-in-out.
    public static readonly Func<float, float> Pop = new Bezier(0.45f, 1.45f, 0.49f, 1.15f).Ease;
    public static readonly Func<float, float> Retreat = new Bezier(0.62f, -0.16f, 0.8f, 0.37f).Ease;
    public static readonly Func<float, float> Soft = new Bezier(0.27f, 1.09f, 0.49f, 1.06f).Ease;
    public static readonly Func<float, float> InOut = new Bezier(0.42f, 0f, 0.58f, 1f).Ease;
    public static float OutCubic(float t) { t -= 1f; return t * t * t + 1f; }

    // Port of Rockstar's AnimatedValue: a fixed-duration tween that restarts from wherever it is
    // whenever the target changes.
    public sealed class Tween
    {
        private readonly float _duration;
        private float _from, _to, _cur, _start;
        public Func<float, float> Easing;

        public Tween(float ms, Func<float, float> easing, float initial = 0f)
        {
            _duration = ms / 1000f;
            Easing = easing;
            _from = _to = _cur = initial;
        }

        public float Value => _cur;

        public float Set(float target)
        {
            float now = Time.unscaledTime;
            if (target != _to) { _from = _cur; _to = target; _start = now; }
            float elapsed = now - _start;
            _cur = elapsed >= _duration ? _to : _from + (_to - _from) * Easing(elapsed / _duration);
            return _cur;
        }

        public float Set(bool on) => Set(on ? 1f : 0f);

        public void Snap(float v) { _from = _to = _cur = v; }
    }

    // Rockstar's layout motion (stiffness 220, damping 24): cards and rows glide to a new position.
    public sealed class Spring
    {
        public float Value, Velocity, Target;
        private readonly float _stiffness, _damping;

        public Spring(float value, float stiffness = 220f, float damping = 24f)
        {
            Value = Target = value;
            _stiffness = stiffness;
            _damping = damping;
        }

        public void Step(float dt)
        {
            // Sub-stepped: a long frame must not make the integrator blow up.
            dt = Mathf.Min(dt, 0.1f);
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / 0.008f));
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                Velocity += ((Target - Value) * _stiffness - Velocity * _damping) * h;
                Value += Velocity * h;
            }
        }

        public void Snap(float v) { Value = Target = v; Velocity = 0f; }
    }

    // --- palette (Rockstar's dark theme) --------------------------------------------------------

    public static Color Panel(float alpha) => new(24f / 255f, 21f / 255f, 29f / 255f, alpha);
    public static readonly Color Track = new(26f / 255f, 23f / 255f, 31f / 255f, 1f);
    public static readonly Color Glow = new(77f / 255f, 0f, 1f, 1f);
    public const float CardAlpha = 0.8f;  // header / target card (Rockstar: 0.9 under a blur layer we do not have)
    public const float NameAlpha = 0.8f;  // keybind name pill  (0.9 * 0.889)
    public const float ValueAlpha = 0.846f; // keybind key pill (0.9 * 0.94)

    // --- squircle outline -----------------------------------------------------------------------

    private static readonly Dictionary<long, Vector2[]> OutlineCache = new();

    // Outline (counter-clockwise, y up, origin at the centre) of Rockstar's squircle for a w x h card.
    // The signed distance below is the one in its squircle fragment shader; the boundary is found by
    // marching rays out of the corner-arc centre, so the corner gets the vertices, not the straight edges.
    public static Vector2[] Outline(float w, float h, float radius, float exponent)
    {
        long key = ((long)Mathf.RoundToInt(w * 16f) << 48) ^ ((long)Mathf.RoundToInt(h * 16f) << 32)
                   ^ ((long)Mathf.RoundToInt(radius * 16f) << 16) ^ Mathf.RoundToInt(exponent * 16f);
        if (OutlineCache.TryGetValue(key, out Vector2[] cached)) return cached;
        if (OutlineCache.Count > 512) OutlineCache.Clear();

        float bx = w * 0.5f, by = h * 0.5f;
        float s = Mathf.Max(exponent, 1f);
        float r = Mathf.Min(radius * s * 0.5f, Mathf.Min(bx, by) * s * 0.5f);
        float ox = Mathf.Max(bx - r, 0f), oy = Mathf.Max(by - r, 0f);
        bool circular = Mathf.Abs(s - 2f) < 0.01f;
        const int n = 10;

        var quarter = new Vector2[n + 1];
        for (int i = 0; i <= n; i++)
        {
            float a = Mathf.PI * 0.5f * i / n;
            Vector2 dir = new(Mathf.Cos(a), Mathf.Sin(a));
            if (circular)
            {
                quarter[i] = new Vector2(ox, oy) + dir * r;
                continue;
            }

            float lo = 0f, hi = 2f * (bx + by);
            for (int it = 0; it < 14; it++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Sdf(ox + dir.x * mid, oy + dir.y * mid, bx, by, r, s) < 0f) lo = mid; else hi = mid;
            }
            quarter[i] = new Vector2(ox + dir.x * lo, oy + dir.y * lo);
        }

        var pts = new Vector2[(n + 1) * 4];
        int k = 0;
        for (int i = 0; i <= n; i++) pts[k++] = new Vector2(quarter[i].x, quarter[i].y);
        for (int i = n; i >= 0; i--) pts[k++] = new Vector2(-quarter[i].x, quarter[i].y);
        for (int i = 0; i <= n; i++) pts[k++] = new Vector2(-quarter[i].x, -quarter[i].y);
        for (int i = n; i >= 0; i--) pts[k++] = new Vector2(quarter[i].x, -quarter[i].y);

        OutlineCache[key] = pts;
        return pts;
    }

    private static float Sdf(float px, float py, float bx, float by, float r, float s)
    {
        float qx = Mathf.Abs(px) - bx + r, qy = Mathf.Abs(py) - by + r;
        float mx = Mathf.Max(qx, 0f), my = Mathf.Max(qy, 0f);
        float len = Mathf.Pow(Mathf.Pow(mx, s) + Mathf.Pow(my, s), 1f / s);
        return Mathf.Min(Mathf.Max(qx, qy), 0f) + len - r;
    }
}

// One card's drawing space. Callers lay everything out in Rockstar GUI units from the card's
// top-left corner (y grows downward, like Rockstar); this turns that into CustomGui's pixel space,
// applying the card's pop-in scale (around its centre) and fade on the way.
internal sealed class HudCtx
{
    public float U { get; private set; }
    public float Alpha;
    private float _x, _y, _cx, _cy, _s, _screenH;
    private Vector2[] _buf = new Vector2[64];

    public void Begin(float x, float y, float wUnits, float hUnits, float u, float scale, float alpha)
    {
        _x = x; _y = y; U = u; _s = scale; Alpha = alpha;
        _cx = x + wUnits * u * 0.5f;
        _cy = y + hUnits * u * 0.5f;
        _screenH = CustomGui.ScreenHeight;
    }

    private float Px(float ux) => _cx + (_x + ux * U - _cx) * _s;
    private float Py(float uy) => _cy + (_y + uy * U - _cy) * _s;
    private Vector2 Pt(float ux, float uy) => new(Px(ux), _screenH - Py(uy));
    private Color Mul(Color c) { c.a *= Alpha; return c; }

    public Rect Map(float ux, float uy, float uw, float uh)
    {
        float w = uw * U * _s, h = uh * U * _s;
        return new Rect(Px(ux), _screenH - (Py(uy) + h), w, h);
    }

    public bool Hovered(float ux, float uy, float uw, float uh) => CustomGui.IsHovered(Map(ux, uy, uw, uh));

    // Plain rounded rectangle (circular corners) - Rockstar's pills.
    public void Rect(float ux, float uy, float uw, float uh, Color c, float radius = 0f)
    {
        CustomGui.Rect(Map(ux, uy, uw, uh), Mul(c), radius * U * _s);
    }

    public void Circle(float ux, float uy, float radius, Color c)
    {
        CustomGui.Circle(Pt(ux, uy), radius * U * _s, Mul(c));
    }

    public void Triangle(Vector2 a, Vector2 b, Vector2 c, Color col)
    {
        CustomGui.Triangle(Pt(a.x, a.y), Pt(b.x, b.y), Pt(c.x, c.y), Mul(col));
    }

    // Rockstar's squircle: top/bottom colours let the card header carry its soft accent glow.
    public void Squircle(float ux, float uy, float uw, float uh, float radius, float exponent, Color top, Color bottom)
    {
        Vector2[] outline = HudKit.Outline(uw, uh, radius, exponent);
        if (_buf.Length < outline.Length) _buf = new Vector2[outline.Length];

        float cxu = ux + uw * 0.5f, cyu = uy + uh * 0.5f;
        for (int i = 0; i < outline.Length; i++)
            _buf[i] = Pt(cxu + outline[i].x, cyu - outline[i].y);

        Vector2 centre = Pt(cxu, cyu);
        float half = Mathf.Max(uh * 0.5f, 0.0001f);
        Color tc = Mul(top), bc = Mul(bottom), mid = Color.Lerp(tc, bc, 0.5f);
        bool flat = top == bottom;

        for (int i = 0; i < outline.Length; i++)
        {
            int j = (i + 1) % outline.Length;
            if (flat)
            {
                CustomGui.Triangle(centre, _buf[i], _buf[j], tc);
                continue;
            }
            Color ci = Color.Lerp(bc, tc, Mathf.Clamp01(0.5f + outline[i].y / (half * 2f)));
            Color cj = Color.Lerp(bc, tc, Mathf.Clamp01(0.5f + outline[j].y / (half * 2f)));
            CustomGui.Triangle(centre, _buf[i], _buf[j], mid, ci, cj);
        }
    }

    public void Squircle(float ux, float uy, float uw, float uh, float radius, float exponent, Color c) =>
        Squircle(ux, uy, uw, uh, radius, exponent, c, c);

    public int FontPx(float fontUnits) => Mathf.Max(6, Mathf.RoundToInt(fontUnits * U * _s));

    // Width in units at the unscaled size, so layout does not wobble while a card pops in.
    public float Measure(string text, float fontUnits) =>
        string.IsNullOrEmpty(text) ? 0f : CustomGui.MeasureText(text, Mathf.Max(6, Mathf.RoundToInt(fontUnits * U))) / U;

    // Same, for callers that measure before the first Begin (the ESP tags are sized, then placed).
    public float Measure(string text, float fontUnits, float u) =>
        string.IsNullOrEmpty(text) ? 0f : CustomGui.MeasureText(text, Mathf.Max(6, Mathf.RoundToInt(fontUnits * u))) / u;

    public void Text(float ux, float uy, float uw, float uh, string text, float fontUnits, Color c, TextAnchor anchor)
    {
        if (string.IsNullOrEmpty(text)) return;
        CustomGui.Text(Map(ux, uy, uw, uh), text, FontPx(fontUnits), Mul(c), anchor);
    }

    public void TextClipped(float ux, float uy, float uw, float uh, string text, float fontUnits, Color c, TextAnchor anchor)
    {
        if (string.IsNullOrEmpty(text)) return;
        CustomGui.TextClipped(Map(ux, uy, uw, uh), text, FontPx(fontUnits), Mul(c), anchor);
    }
}
