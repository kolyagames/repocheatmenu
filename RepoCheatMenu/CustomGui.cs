using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RepoCheatMenu;

// Our own immediate-mode-style 2D primitive renderer, built from Unity's lowest practical drawing
// primitive (a single custom Graphic subclass generating raw mesh geometry every frame) instead of
// assembling a menu out of individual Image/Text/Button GameObjects the way the older OverlayUI.cs
// does. Nothing here is derived from any third-party mod's code - the mesh math (rounded-rect
// corner arcs, glyph quad layout, gradients, triangles) is our own, and the only external input is
// Unity's own public Font API (RequestCharactersInTexture/characterInfo - the same mechanism
// Unity's built-in UI Text component itself uses internally, documented public API, not anyone's
// proprietary renderer).
//
// Two things draw through this now: CustomOverlayUI.cs (the F6 menu) and PlayerHud.cs (the
// always-on gameplay HUD) - both share the ONE surface pair this file owns, inside a single
// BeginFrame/EndFrame per real frame orchestrated by HudRoot.cs. See HudRoot's own comment for why
// that has to be one shared cycle, not one per drawing system.
//
// Usage per frame: BeginFrame() once, then any number of Rect(...)/RectGradient(...)/Circle(...)/
// Triangle(...)/Text(...) calls, then EndFrame() to push everything into two draw calls (rects+
// shapes on one surface, text on the other - see CustomGuiGraphic.mainTexture for why they can't
// share one).
internal static class CustomGui
{
    private static CustomGuiGraphic _rectGraphic;
    private static CustomGuiGraphic _textGraphic;

    public static void Init(Transform canvasParent)
    {
        if (_rectGraphic != null) return;

        _rectGraphic = CreateSurface(canvasParent, "CustomGuiRects", isText: false);
        _textGraphic = CreateSurface(canvasParent, "CustomGuiText", isText: true);
    }

    private static CustomGuiGraphic CreateSurface(Transform canvasParent, string name, bool isText)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(canvasParent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        // Pivot (0,0) instead of the default (0.5,0.5) - OnPopulateMesh vertex positions are in
        // this RectTransform's local space, which is centered on the pivot. Without this, local
        // (0,0) would be screen-center, not the bottom-left corner our Rect()/Text() callers
        // assume - everything we drew was landing off in the wrong quadrant entirely.
        rect.pivot = Vector2.zero;

        var graphic = go.AddComponent<CustomGuiGraphic>();
        graphic.raycastTarget = false;
        graphic.IsTextSurface = isText;
        return graphic;
    }

    // The CanvasScaler (ScaleWithScreenSize) means this surface's LOCAL rect size is the scaled
    // reference-resolution-equivalent size, not raw Screen.width/height pixels - callers need
    // these, not Screen.*, to place anything correctly.
    public static float ScreenWidth => _rectGraphic != null ? _rectGraphic.rectTransform.rect.width : 0f;
    public static float ScreenHeight => _rectGraphic != null ? _rectGraphic.rectTransform.rect.height : 0f;

    public static void BeginFrame()
    {
        _rectGraphic?.Clear();
        _textGraphic?.Clear();
    }

    public static void Rect(Rect area, Color color, float rounding = 0f)
    {
        _rectGraphic?.AddRoundedRect(area, color, rounding);
    }

    // Top/bottom gradient fill - see CustomGuiGraphic.AddRoundedRectGradient's own comment.
    public static void RectGradient(Rect area, Color top, Color bottom, float rounding = 0f)
    {
        _rectGraphic?.AddRoundedRectGradient(area, top, bottom, rounding);
    }

    // A rounded rect with rounding == half its own width/height is a circle (the corner arcs meet
    // seamlessly) - no separate circle mesh code needed.
    public static void Circle(Vector2 center, float radius, Color color)
    {
        _rectGraphic?.AddRoundedRect(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), color, radius);
    }

    public static void Triangle(Vector2 a, Vector2 b, Vector2 c, Color color)
    {
        _rectGraphic?.AddTriangle(a, b, c, color);
    }

    public static void Text(Rect area, string text, int fontSize, Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
    {
        _textGraphic?.AddText(area, text, fontSize, color, anchor);
    }

    public static float MeasureText(string text, int fontSize) =>
        _textGraphic != null ? _textGraphic.MeasureWidth(text, fontSize) : 0f;

    // Text that is trimmed with a trailing ellipsis until it fits the rect's width. Nothing here
    // clips at draw time (no scissor rect), so a long string otherwise runs straight off the panel
    // and over the game behind it - which is exactly what a long file path in a LiveInfo row did.
    public static void TextClipped(Rect area, string text, int fontSize, Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
    {
        if (!string.IsNullOrEmpty(text) && MeasureText(text, fontSize) > area.width)
        {
            // Linear trim from the end: these strings are short enough (a row of text) that a
            // binary search would just be more code for no measurable gain.
            while (text.Length > 1 && MeasureText(text + "…", fontSize) > area.width)
                text = text[..^1];
            text += "…";
        }
        Text(area, text, fontSize, color, anchor);
    }

    public static void EndFrame()
    {
        _rectGraphic?.CommitMesh();
        _textGraphic?.CommitMesh();
    }

    // Own hit-testing, no Unity EventSystem/GraphicRaycaster involved at all (both our surfaces
    // have raycastTarget = false) - the standard immediate-mode pattern: ask "is the mouse over
    // this rect, and did a click just happen" fresh every call, nothing persists between frames.
    // RectTransformUtility.ScreenPointToLocalPointInRectangle is the correct Unity API for
    // converting a raw screen-space mouse position into this surface's local (CanvasScaler-scaled)
    // space - doing that scaling by hand was the other half of why the first demo didn't line up.
    private static Vector2 MouseLocal()
    {
        if (_rectGraphic == null) return Vector2.zero;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _rectGraphic.rectTransform, Input.mousePosition, null, out Vector2 local);
        return local;
    }

    public static bool IsHovered(Rect area) => area.Contains(MouseLocal());

    // Immediate-mode button: draws itself (different fill when hovered) and reports whether it
    // was clicked THIS frame - no persistent Button/onClick object anywhere, matches how the rest
    // of this API works.
    public static bool Button(Rect area, string label, Color bg, Color bgHover, Color textColor,
        int fontSize = 16, float rounding = 8f)
    {
        bool hovered = IsHovered(area);
        Rect(area, hovered ? bgHover : bg, rounding);
        Text(area, label, fontSize, textColor, TextAnchor.MiddleCenter);
        return hovered && Input.GetMouseButtonDown(0);
    }

    // Toggle: draws label + on/off pill, flips and returns the new value on click - the building
    // block every Toggle row in CustomOverlayUI/PlayerHud is built on.
    public static bool Toggle(Rect area, string label, bool value, Color onColor, Color offColor, Color textColor)
    {
        float pillWidth = 44f;
        Rect labelArea = new(area.x, area.y, area.width - pillWidth - 8f, area.height);
        Rect pillArea = new(area.xMax - pillWidth, area.y + area.height * 0.5f - 10f, pillWidth, 20f);

        Text(labelArea, label, 15, textColor, TextAnchor.MiddleLeft);

        bool hovered = IsHovered(pillArea) || IsHovered(area);

        // RepoHax draws an animated glow ring that eases in/out on hover (widgets.cpp's Toggle,
        // LinearAnim over ~0.1s) - no per-widget animation state here (nothing else in this
        // renderer tracks state across frames by id, and adding that just for one ring isn't worth
        // it), so this is the static equivalent: full ring the instant the mouse is over it, gone
        // the instant it isn't. Drawn as a slightly larger rounded rect BEHIND the pill (same
        // technique Circle already uses - rounding == half extent gives a fully round outline).
        if (hovered)
        {
            float glow = 2f;
            Rect(new Rect(pillArea.x - glow, pillArea.y - glow, pillArea.width + glow * 2f, pillArea.height + glow * 2f),
                new Color(1f, 1f, 1f, 0.18f), 10f + glow);
        }

        Rect(pillArea, value ? onColor : offColor, 10f);

        float knobRadius = 7f;
        float knobX = value ? pillArea.xMax - knobRadius - 3f : pillArea.xMin + knobRadius + 3f;
        Rect(new Rect(knobX - knobRadius, pillArea.y + pillArea.height * 0.5f - knobRadius, knobRadius * 2f, knobRadius * 2f),
            Color.white, knobRadius);

        if (hovered && Input.GetMouseButtonDown(0))
            return !value;
        return value;
    }

    // Numeric stepper: label, current value, and -/+ buttons. Returns the (possibly changed)
    // value - same "draw it and hand back the new state" shape as Toggle.
    public static float FloatStepper(Rect area, string label, float value, float min, float max, float step,
        Color panelColor, Color textColor)
    {
        const float btnWidth = 26f;
        Rect labelArea = new(area.x, area.y, area.width - btnWidth * 2f - 70f, area.height);
        Rect valueArea = new(labelArea.xMax, area.y, 60f, area.height);
        Rect minusArea = new(valueArea.xMax + 4f, area.y + area.height * 0.5f - 11f, btnWidth, 22f);
        Rect plusArea = new(minusArea.xMax + 4f, area.y + area.height * 0.5f - 11f, btnWidth, 22f);

        Text(labelArea, label, 14, textColor, TextAnchor.MiddleLeft);
        Text(valueArea, value.ToString("0.##"), 14, textColor, TextAnchor.MiddleRight);

        if (Button(minusArea, "-", panelColor, new Color(panelColor.r, panelColor.g, panelColor.b, panelColor.a + 0.1f), textColor, 14, 6f))
            value = Mathf.Clamp(value - step, min, max);
        if (Button(plusArea, "+", panelColor, new Color(panelColor.r, panelColor.g, panelColor.b, panelColor.a + 0.1f), textColor, 14, 6f))
            value = Mathf.Clamp(value + step, min, max);

        return value;
    }

    public static int IntStepper(Rect area, string label, int value, int min, int max, int step,
        Color panelColor, Color textColor)
        => Mathf.RoundToInt(FloatStepper(area, label, value, min, max, step, panelColor, textColor));

    // Cycle: label + a button showing the current option, click advances to the next one and
    // wraps around - same building block AddCycleRow/AddItemStealRow use in the old menu.
    public static string Cycle(Rect area, string label, string current, string[] options,
        Color panelColor, Color panelHoverColor, Color textColor)
    {
        if (options == null || options.Length == 0) return current;

        Rect labelArea = new(area.x, area.y, area.width - 150f, area.height);
        Rect btnArea = new(labelArea.xMax + 4f, area.y + area.height * 0.5f - 13f, 146f, 26f);

        Text(labelArea, label, 14, textColor, TextAnchor.MiddleLeft);

        int idx = System.Array.IndexOf(options, current);
        if (idx < 0) idx = 0;

        if (Button(btnArea, options[idx], panelColor, panelHoverColor, textColor, 14, 6f))
            idx = (idx + 1) % options.Length;

        return options[idx];
    }

    // Own text input, no Unity InputField anywhere - click to focus, Input.inputString feeds
    // typed characters in per frame (Unity's own per-frame typed-char buffer, the standard way to
    // build a text field without the built-in InputField component), backspace trims the end,
    // Enter/click-away un-focuses. Single-field-at-a-time focus tracked by the caller-supplied id
    // string, so multiple text fields (target name, chat spam text, script editor) don't fight
    // over the same input.
    private static string _focusedFieldId;
    private static int _cursorBlinkStart;

    public static string TextField(Rect area, string id, string value, Color bg, Color textColor, Color focusColor)
    {
        value ??= "";
        bool hovered = IsHovered(area);
        bool focused = _focusedFieldId == id;

        if (Input.GetMouseButtonDown(0))
        {
            if (hovered) { focused = true; _focusedFieldId = id; _cursorBlinkStart = Environment.TickCount; }
            else if (focused) { focused = false; _focusedFieldId = null; }
        }

        if (focused)
        {
            foreach (char c in Input.inputString)
            {
                if (c is '\b')
                {
                    if (value.Length > 0) value = value[..^1];
                }
                else if (c is '\n' or '\r')
                {
                    focused = false;
                    _focusedFieldId = null;
                }
                else if (!char.IsControl(c))
                {
                    value += c;
                }
            }
        }

        Rect(area, focused ? focusColor : bg, 6f);
        bool caretOn = (Environment.TickCount - _cursorBlinkStart) % 1000 < 500;
        string shown = focused && caretOn ? value + "|" : value;
        Text(new Rect(area.x + 8f, area.y, area.width - 16f, area.height), shown, 14, textColor, TextAnchor.MiddleLeft);

        return value;
    }

    // Multi-line variant for the script editor - same focus/Input.inputString approach as
    // TextField, except Enter inserts a newline instead of un-focusing (Shift+Enter isn't needed
    // since single-line fields use TextField instead), and each '\n'-split line renders on its
    // own row top-down. No line-wrap, no mid-text cursor - append/backspace only, same as
    // TextField - a real code editor's worth of scope (selection, wrapping, syntax highlight)
    // isn't what this needs to be useful for pasting/writing a short script.
    public static string TextArea(Rect area, string id, string value, Color bg, Color textColor, Color focusColor, int fontSize = 14)
    {
        value ??= "";
        bool hovered = IsHovered(area);
        bool focused = _focusedFieldId == id;

        if (Input.GetMouseButtonDown(0))
        {
            if (hovered) { focused = true; _focusedFieldId = id; _cursorBlinkStart = Environment.TickCount; }
            else if (focused) { focused = false; _focusedFieldId = null; }
        }

        if (focused)
        {
            foreach (char c in Input.inputString)
            {
                if (c is '\b')
                {
                    if (value.Length > 0) value = value[..^1];
                }
                else if (c is '\n' or '\r')
                {
                    value += '\n';
                }
                else if (!char.IsControl(c))
                {
                    value += c;
                }
            }
        }

        Rect(area, focused ? focusColor : bg, 6f);

        bool caretOn = (Environment.TickCount - _cursorBlinkStart) % 1000 < 500;
        string shown = focused && caretOn ? value + "|" : value;
        string[] lines = shown.Split('\n');
        float lineHeight = fontSize * 1.3f;
        float y = area.yMax - 6f;
        foreach (string line in lines)
        {
            if (y - lineHeight < area.yMin) break; // simple clip: stop once we'd draw below the box
            Text(new Rect(area.x + 8f, y - lineHeight, area.width - 16f, lineHeight), line, fontSize, textColor, TextAnchor.UpperLeft);
            y -= lineHeight;
        }

        return value;
    }

    // Section header - just a dimmer label, no interaction, matches the old menu's AddSectionHeader.
    // Clipped: several of these carry a long explanatory sentence, which without trimming drew
    // straight past the panel and over the game behind it.
    public static void SectionHeader(Rect area, string label, Color color)
        => TextClipped(area, label, 13, color, TextAnchor.MiddleLeft);
}

// The actual Graphic - Unity calls OnPopulateMesh whenever the Canvas needs to redraw it, and we
// hand it exactly the triangles we built up this frame. SetVerticesDirty() each CommitMesh() call
// is what triggers that redraw - without it Unity would just keep showing last frame's geometry.
internal sealed class CustomGuiGraphic : MaskableGraphic
{
    private readonly List<UIVertex> _verts = new();
    private readonly List<int> _tris = new();
    private static Font _font;

    protected override void Awake()
    {
        base.Awake();
        _font ??= Resources.GetBuiltinResource<Font>("Arial.ttf") ?? Font.CreateDynamicFontFromOSFont("Arial", 32);
        material = Graphic.defaultGraphicMaterial;
    }

    // Rects and text can't share one texture: rects need a plain white sample everywhere (any UV
    // works, so long as the pixel really is opaque white), text needs the font's own glyph atlas.
    // Font.RequestCharactersInTexture repacks that atlas on demand and doesn't reserve a
    // guaranteed-white texel anywhere documented, so mixing both into a single draw call risked
    // sampling rect fills into a transparent corner of the font atlas - which is exactly why nothing
    // showed up in-game the first time. Texture2D.whiteTexture is a real Unity built-in, always
    // opaque white at any UV, so rects use that and text uses the font atlas - two draw calls
    // instead of one, negligible cost for a menu overlay.
    public bool IsTextSurface;

    public override Texture mainTexture => IsTextSurface && _font != null ? _font.material.mainTexture : Texture2D.whiteTexture;

    public void Clear()
    {
        _verts.Clear();
        _tris.Clear();
    }

    public void CommitMesh() => SetVerticesDirty();

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        for (int i = 0; i < _verts.Count; i++)
            vh.AddVert(_verts[i]);
        for (int i = 0; i + 2 < _tris.Count + 1 && i + 2 < _tris.Count; i += 3)
            vh.AddTriangle(_tris[i], _tris[i + 1], _tris[i + 2]);
    }

    // Rounded rectangle: an 8-point-per-corner arc fan, own math (not a 9-sliced sprite like the
    // rest of the menu uses) - the actual geometry is generated fresh here every call, no texture
    // involved for the rect case at all.
    public void AddRoundedRect(Rect area, Color color, float rounding) => AddRoundedRect(area, _ => color, rounding);

    // Vertical gradient variant - a real per-vertex color instead of a flat fill, closer to the
    // soft "glassmorphism" look reference HUD screenshots use for their panels than a single flat
    // navy ever could. UIVertex.color already interpolates smoothly across a triangle under
    // Unity's default UI shader, so this is just AddRoundedRect with the color computed per point
    // instead of passed once.
    public void AddRoundedRectGradient(Rect area, Color top, Color bottom, float rounding) =>
        AddRoundedRect(area, p => Color.Lerp(bottom, top, Mathf.InverseLerp(area.yMin, area.yMax, p.y)), rounding);

    private void AddRoundedRect(Rect area, Func<Vector2, Color> colorAt, float rounding)
    {
        rounding = Mathf.Clamp(rounding, 0f, Mathf.Min(area.width, area.height) * 0.5f);
        if (rounding <= 0.01f)
        {
            AddQuad(area, colorAt);
            return;
        }

        int baseIndex = _verts.Count;
        const int arcSteps = 6;
        var points = new List<Vector2>();

        void Corner(Vector2 center, float startDeg)
        {
            for (int i = 0; i <= arcSteps; i++)
            {
                float deg = startDeg + 90f * i / arcSteps;
                float rad = deg * Mathf.Deg2Rad;
                points.Add(center + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * rounding);
            }
        }

        // Each corner's sweep has to land ON the rectangle's own boundary at both ends (one end
        // where the straight edge before it stops, one end where the straight edge after it
        // starts) - these four start angles were each off by 90° (e.g. the bottom-right corner
        // swept 0->90, which starts correctly on the right edge but ends at a point floating in
        // the rect's INTERIOR instead of on the bottom edge). The fan-from-center triangulation
        // below turned that into a self-intersecting bowtie instead of a rounded corner - looked
        // fine at the small rounding/size ratios buttons and pills use, but produced a visible
        // diagonal X tear across bigger panels like PlayerHud's cards. Verified by hand against
        // concrete coordinates before changing (see PR/session notes) - 270/0/90/180 is correct.
        Corner(new Vector2(area.xMax - rounding, area.yMin + rounding), 270f);
        Corner(new Vector2(area.xMax - rounding, area.yMax - rounding), 0f);
        Corner(new Vector2(area.xMin + rounding, area.yMax - rounding), 90f);
        Corner(new Vector2(area.xMin + rounding, area.yMin + rounding), 180f);

        Vector2 pivot = area.center;
        foreach (var p in points)
            AddVertRaw(p, colorAt(p));
        int pivotIndex = _verts.Count;
        AddVertRaw(pivot, colorAt(pivot));

        for (int i = 0; i < points.Count; i++)
        {
            int a = baseIndex + i;
            int b = baseIndex + (i + 1) % points.Count;
            _tris.Add(pivotIndex);
            _tris.Add(a);
            _tris.Add(b);
        }
    }

    private void AddQuad(Rect area, Color color) => AddQuad(area, _ => color);

    private void AddQuad(Rect area, Func<Vector2, Color> colorAt)
    {
        int baseIndex = _verts.Count;
        Vector2[] corners = {
            new(area.xMin, area.yMin), new(area.xMax, area.yMin),
            new(area.xMax, area.yMax), new(area.xMin, area.yMax),
        };
        foreach (var c in corners)
            AddVertRaw(c, colorAt(c));
        _tris.Add(baseIndex); _tris.Add(baseIndex + 1); _tris.Add(baseIndex + 2);
        _tris.Add(baseIndex); _tris.Add(baseIndex + 2); _tris.Add(baseIndex + 3);
    }

    // Arbitrary filled triangle - the one primitive rects/circles can't give you, for the play/
    // pause/skip glyph shapes PlayerHud draws by hand instead of relying on font glyph coverage
    // (see AddText's own comment on why: Arial has no emoji, and even simple dingbat/symbol
    // Unicode blocks aren't guaranteed present in whatever font this game's Font.
    // CreateDynamicFontFromOSFont fallback actually resolves to).
    public void AddTriangle(Vector2 a, Vector2 b, Vector2 c, Color color)
    {
        int baseIndex = _verts.Count;
        AddVertRaw(a, color);
        AddVertRaw(b, color);
        AddVertRaw(c, color);
        _tris.Add(baseIndex); _tris.Add(baseIndex + 1); _tris.Add(baseIndex + 2);
    }

    private void AddVertRaw(Vector2 pos, Color color)
    {
        var v = UIVertex.simpleVert;
        v.color = color;
        v.position = pos;
        _verts.Add(v);
    }

    // Own glyph layout using Unity's public Font.RequestCharactersInTexture/characterInfo - the
    // same lookup Unity's built-in Text component uses internally, sampling the font's own shared
    // atlas texture rather than any pre-rendered string image.
    //
    // '\n' used to have no real handling here at all - GetCharacterInfo('\n') just fails like any
    // other missing glyph, so the old single-pass loop silently treated a newline as a small
    // sideways cursor bump instead of starting a new line. Nothing broke loudly because it just
    // produced one long overflowing line instead of two - PlayerHud's "Цель не выбрана\n(«Игроки»
    // -> имя цели)" placeholder and, more widely, every LiveInfo row in CustomOverlayUI
    // (NetworkDiagnostics/AntiCrash stats, both built as two explicit "\n"-joined lines) were all
    // quietly relying on wrapping that never happened.
    public void AddText(Rect area, string text, int fontSize, Color color, TextAnchor anchor)
    {
        if (string.IsNullOrEmpty(text) || _font == null) return;

        if (text.IndexOf('\n') < 0)
        {
            AddSingleLineText(area, text, fontSize, color, anchor);
            return;
        }

        string[] lines = text.Split('\n');
        float lineHeight = fontSize * 1.25f;
        float blockHeight = lineHeight * lines.Length;

        float top = anchor is TextAnchor.UpperLeft or TextAnchor.UpperCenter or TextAnchor.UpperRight
            ? area.yMax
            : anchor is TextAnchor.LowerLeft or TextAnchor.LowerCenter or TextAnchor.LowerRight
                ? area.yMin + blockHeight
                : area.center.y + blockHeight * 0.5f;

        for (int i = 0; i < lines.Length; i++)
        {
            Rect lineArea = new(area.x, top - lineHeight * (i + 1), area.width, lineHeight);
            AddSingleLineText(lineArea, lines[i], fontSize, color, anchor);
        }
    }

    // Width the given text would occupy - same advance sum AddSingleLineText walks, exposed so
    // callers can fit/trim text to a rect instead of letting it run off the panel (this renderer
    // has no scissor rect, so an over-long string simply draws straight over whatever's beside it).
    public float MeasureWidth(string text, int fontSize)
    {
        if (string.IsNullOrEmpty(text) || _font == null) return 0f;

        _font.RequestCharactersInTexture(text, fontSize, FontStyle.Normal);
        float total = 0f;
        foreach (char c in text)
        {
            if (_font.GetCharacterInfo(c, out CharacterInfo info, fontSize)) total += info.advance;
            else total += fontSize * 0.5f; // same fallback advance AddSingleLineText uses
        }
        return total;
    }

    private void AddSingleLineText(Rect area, string text, int fontSize, Color color, TextAnchor anchor)
    {
        if (string.IsNullOrEmpty(text)) return;

        _font.RequestCharactersInTexture(text, fontSize, FontStyle.Normal);

        float totalWidth = 0f;
        foreach (char c in text)
        {
            if (_font.GetCharacterInfo(c, out CharacterInfo info, fontSize))
                totalWidth += info.advance;
        }

        float startX = anchor is TextAnchor.MiddleLeft or TextAnchor.UpperLeft or TextAnchor.LowerLeft
            ? area.xMin
            : anchor is TextAnchor.MiddleRight or TextAnchor.UpperRight or TextAnchor.LowerRight
                ? area.xMax - totalWidth
                : area.center.x - totalWidth * 0.5f;

        float baseline = area.center.y - fontSize * 0.35f;
        float cursor = startX;

        foreach (char c in text)
        {
            if (!_font.GetCharacterInfo(c, out CharacterInfo info, fontSize))
            {
                cursor += fontSize * 0.5f;
                continue;
            }

            AddGlyphQuad(cursor, baseline, info, color);
            cursor += info.advance;
        }
    }

    private void AddGlyphQuad(float x, float y, CharacterInfo info, Color color)
    {
        int baseIndex = _verts.Count;

        Vector2 min = new(x + info.minX, y + info.minY);
        Vector2 max = new(x + info.maxX, y + info.maxY);

        AddGlyphVert(new Vector2(min.x, min.y), new Vector2(info.uvBottomLeft.x, info.uvBottomLeft.y), color);
        AddGlyphVert(new Vector2(max.x, min.y), new Vector2(info.uvBottomRight.x, info.uvBottomRight.y), color);
        AddGlyphVert(new Vector2(max.x, max.y), new Vector2(info.uvTopRight.x, info.uvTopRight.y), color);
        AddGlyphVert(new Vector2(min.x, max.y), new Vector2(info.uvTopLeft.x, info.uvTopLeft.y), color);

        _tris.Add(baseIndex); _tris.Add(baseIndex + 1); _tris.Add(baseIndex + 2);
        _tris.Add(baseIndex); _tris.Add(baseIndex + 2); _tris.Add(baseIndex + 3);
    }

    private void AddGlyphVert(Vector2 pos, Vector2 uv, Color color)
    {
        var v = UIVertex.simpleVert;
        v.color = color;
        v.position = pos;
        v.uv0 = uv;
        _verts.Add(v);
    }
}
