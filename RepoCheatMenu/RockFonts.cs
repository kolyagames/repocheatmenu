// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace RepoCheatMenu;

// Rockstar's UI font (SF Pro Display, regular and medium), loaded from the mod's own `fonts` folder
// (the launcher copies it next to the core). `new Font(path)` does not read a file in this Unity build
// (it looked up an OS font by that name and found nothing), so the glyphs come from Unity's TextCore
// FontEngine instead - the same FreeType wrapper TextMeshPro uses - and are packed into our own atlas.
// Everything here is best effort: any failure leaves the property null and CustomGui keeps using Arial.
internal static class RockFonts
{
    private const string RegularFile = "SFProDisplay-Regular.otf";
    private const string MediumFile = "SFProDisplay-Medium.otf";

    private static bool _loaded;
    private static GlyphSource _regular, _medium;

    public static GlyphSource Regular { get { Load(); return _regular; } }
    public static GlyphSource Medium { get { Load(); return _medium; } }

    private static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        _regular = TryLoad(RegularFile);
        _medium = TryLoad(MediumFile);
        Plugin.Log.LogInfo($"RockFonts: regular={(_regular != null ? "ok" : "missing")}, medium={(_medium != null ? "ok" : "missing")}");
    }

    private static GlyphSource TryLoad(string fileName)
    {
        try
        {
            string path = null;
            foreach (string found in Directory.GetFiles(Paths.PluginPath, fileName, SearchOption.AllDirectories))
            {
                path = found;
                break;
            }
            if (path == null)
            {
                Plugin.Log.LogWarning($"RockFonts: {fileName} not found under {Paths.PluginPath}");
                return null;
            }

            var source = new EngineGlyphSource(path);
            // Prove the face opens and draws before CustomGui commits a text surface to it.
            source.Prepare("Aa", 14);
            if (!source.TryGet('A', 14, out GlyphQuad probe) || probe.Advance <= 0f || !probe.HasBitmap)
            {
                Plugin.Log.LogWarning($"RockFonts: {fileName} opened but gave no glyphs ({source.LastError})");
                return null;
            }
            return source;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"RockFonts: {fileName} failed to load: {e}");
            return null;
        }
    }
}

// One drawable glyph at one pixel size, in the same terms Unity's CharacterInfo uses: min/max are offsets
// from the pen position on the baseline (y up), uv corners index the source's atlas texture.
internal struct GlyphQuad
{
    public float MinX, MinY, MaxX, MaxY, Advance;
    public Vector2 UvBL, UvBR, UvTR, UvTL;
    public bool HasBitmap;   // false for spaces: only the advance matters
}

// Where CustomGuiGraphic gets its glyphs: either a Unity dynamic Font (Arial, the fallback) or our own
// FontEngine atlas (SF Pro).
internal abstract class GlyphSource
{
    public abstract Texture Texture { get; }
    public abstract void Prepare(string text, int size);
    public abstract bool TryGet(char c, int size, out GlyphQuad glyph);
}

internal sealed class UnityFontGlyphSource : GlyphSource
{
    private readonly Font _font;
    public UnityFontGlyphSource(Font font) { _font = font; }

    public override Texture Texture => _font.material.mainTexture;

    public override void Prepare(string text, int size) =>
        _font.RequestCharactersInTexture(text, size, FontStyle.Normal);

    public override bool TryGet(char c, int size, out GlyphQuad g)
    {
        g = default;
        if (!_font.GetCharacterInfo(c, out CharacterInfo info, size)) return false;
        g.MinX = info.minX; g.MaxX = info.maxX; g.MinY = info.minY; g.MaxY = info.maxY;
        g.Advance = info.advance;
        g.UvBL = info.uvBottomLeft; g.UvBR = info.uvBottomRight;
        g.UvTR = info.uvTopRight; g.UvTL = info.uvTopLeft;
        g.HasBitmap = true;
        return true;
    }
}

// Glyphs rasterised on demand with TextCore's FreeType wrapper into one Alpha8 atlas, per (size, char).
// Characters the primary face lacks (arrows, check marks, ...) fall through to system fonts, so the
// rest of the UI never has to know which face a glyph came from.
internal sealed class EngineGlyphSource : GlyphSource
{
    private const int AtlasSize = 2048;
    private const int Padding = 2;

    private static readonly string[] FallbackFiles =
    {
        @"C:\Windows\Fonts\segoeui.ttf",
        @"C:\Windows\Fonts\arial.ttf",
        @"C:\Windows\Fonts\seguisym.ttf",
    };

    private readonly string[] _faces;
    private readonly Texture2D _atlas;
    private readonly List<GlyphRect> _free = new();
    private readonly List<GlyphRect> _used = new();
    // key = size << 16 | char. Valid==false marks a char no face has, so it is not retried every frame.
    private readonly Dictionary<int, Entry> _cache = new();
    private readonly List<char> _pending = new();

    private struct Entry { public GlyphQuad Quad; public bool Valid; }

    public string LastError { get; private set; } = "";

    public EngineGlyphSource(string primaryFile)
    {
        var faces = new List<string> { primaryFile };
        foreach (string f in FallbackFiles)
            if (File.Exists(f)) faces.Add(f);
        _faces = faces.ToArray();

        _atlas = new Texture2D(AtlasSize, AtlasSize, TextureFormat.Alpha8, false)
        {
            name = "RockFontAtlas",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        ResetAtlas();
    }

    public override Texture Texture => _atlas;

    private static int Key(char c, int size) => (size << 16) | c;

    private void ResetAtlas()
    {
        _atlas.SetPixelData(new byte[AtlasSize * AtlasSize], 0);
        _atlas.Apply(false, false);
        _free.Clear();
        _used.Clear();
        _free.Add(new GlyphRect(0, 0, AtlasSize - 1, AtlasSize - 1));
        _cache.Clear();
    }

    public override void Prepare(string text, int size)
    {
        if (string.IsNullOrEmpty(text)) return;
        _pending.Clear();
        foreach (char c in text)
        {
            if (c == '\n' || c == '\r') continue;
            if (!_cache.ContainsKey(Key(c, size)) && !_pending.Contains(c))
                _pending.Add(c);
        }
        if (_pending.Count > 0) Rasterize(size);
    }

    public override bool TryGet(char c, int size, out GlyphQuad glyph)
    {
        if (!_cache.TryGetValue(Key(c, size), out Entry e))
        {
            _pending.Clear();
            _pending.Add(c);
            Rasterize(size);
            if (!_cache.TryGetValue(Key(c, size), out e)) { glyph = default; return false; }
        }
        glyph = e.Quad;
        return e.Valid;
    }

    private void Rasterize(int size)
    {
        // Leftover chars after each face is tried; the first face that has a glyph wins.
        var todo = new List<char>(_pending);
        bool added = false;

        for (int f = 0; f < _faces.Length && todo.Count > 0; f++)
        {
            if (!LoadFace(_faces[f], size)) continue;

            for (int i = todo.Count - 1; i >= 0; i--)
            {
                char c = todo[i];
                if (!FontEngine.TryGetGlyphWithUnicodeValue(c, GlyphLoadFlags.LOAD_NO_BITMAP, out Glyph g)) continue;
                if (!Pack(g, size, out GlyphQuad quad, ref added))
                {
                    // Atlas full: start over (everything is cheap to rebuild) and reload this face.
                    ResetAtlas();
                    added = false;
                    if (!LoadFace(_faces[f], size) || !Pack(g, size, out quad, ref added)) continue;
                }
                _cache[Key(c, size)] = new Entry { Quad = quad, Valid = true };
                todo.RemoveAt(i);
            }
        }

        foreach (char c in todo)
            _cache[Key(c, size)] = new Entry { Valid = false };

        if (added) _atlas.Apply(false, false);
    }

    private bool LoadFace(string path, int size)
    {
        FontEngineError err = FontEngine.LoadFontFace(path, size);
        if (err == FontEngineError.Invalid_Library || err == FontEngineError.Invalid_Library_or_Face)
        {
            FontEngine.InitializeFontEngine();
            err = FontEngine.LoadFontFace(path, size);
        }
        if (err != FontEngineError.Success)
        {
            LastError = $"LoadFontFace({Path.GetFileName(path)}, {size}) = {err}";
            return false;
        }
        return true;
    }

    // FontEngine.TryAddGlyphToTexture is public in the reference assembly the mod compiles against but
    // `internal` in the game's real UnityEngine.TextCoreFontEngineModule (checked by decompiling it:
    // a direct call dies with MethodAccessException when first jitted). Reflection is not subject to that,
    // so the method is looked up once and invoked through MethodInfo. Everything else used from FontEngine
    // (LoadFontFace, SetFaceSize, TryGetGlyphWithUnicodeValue, the Glyph/GlyphRect/GlyphMetrics members)
    // is public in the real assembly.
    private static readonly System.Reflection.MethodInfo AddGlyphMethod = typeof(FontEngine).GetMethod(
        "TryAddGlyphToTexture",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
        null,
        new[]
        {
            typeof(uint), typeof(int), typeof(GlyphPackingMode), typeof(List<GlyphRect>), typeof(List<GlyphRect>),
            typeof(GlyphRenderMode), typeof(Texture2D), typeof(Glyph).MakeByRefType(),
        },
        null);

    private bool TryAddToAtlas(uint glyphIndex, out Glyph placed)
    {
        placed = null;
        if (AddGlyphMethod == null)
        {
            LastError = "FontEngine.TryAddGlyphToTexture not found";
            return false;
        }
        object[] args =
        {
            glyphIndex, Padding, GlyphPackingMode.ContactPointRule, _free, _used,
            GlyphRenderMode.SMOOTH_HINTED, _atlas, null,
        };
        bool ok = (bool)AddGlyphMethod.Invoke(null, args);
        placed = args[7] as Glyph;
        return ok && placed != null;
    }

    private bool Pack(Glyph g, int size, out GlyphQuad quad, ref bool added)
    {
        quad = default;
        GlyphMetrics m = g.metrics;
        quad.Advance = m.horizontalAdvance;

        // Space and friends: nothing to draw.
        if (m.width <= 0f || m.height <= 0f) return true;

        if (!TryAddToAtlas(g.index, out Glyph placed)) return false;
        added = true;

        GlyphRect r = placed.glyphRect;
        quad.MinX = m.horizontalBearingX;
        quad.MaxX = m.horizontalBearingX + m.width;
        quad.MaxY = m.horizontalBearingY;
        quad.MinY = m.horizontalBearingY - m.height;
        float u0 = (float)r.x / AtlasSize, u1 = (float)(r.x + r.width) / AtlasSize;
        float v0 = (float)r.y / AtlasSize, v1 = (float)(r.y + r.height) / AtlasSize;
        quad.UvBL = new Vector2(u0, v0);
        quad.UvBR = new Vector2(u1, v0);
        quad.UvTR = new Vector2(u1, v1);
        quad.UvTL = new Vector2(u0, v1);
        quad.HasBitmap = true;
        return true;
    }
}
