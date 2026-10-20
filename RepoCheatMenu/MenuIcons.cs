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
using UnityEngine.UI;

namespace RepoCheatMenu;

// Rockstar's own icons (category icons, logo, search, gear, notification marks) drawn as tinted sprites.
// CustomGui only makes flat-coloured meshes, so these go through small pools of ordinary Image components
// under HudRoot.IconLayer (the same trick PlayerHud uses for item icons), positioned from the same pixel
// rectangles the rest of the UI uses and run through CustomGui's window transform, so they follow the
// open/close animation. The bitmaps are Rockstar's SVGs rasterised ahead of time by tools/make-icons.py.
//
// Each user of icons owns an IconPool: Begin() before drawing, End() after (End hides the ones this frame
// did not use), so the menu and the notifications never hide each other's sprites.
internal sealed class IconPool
{
    private static readonly Dictionary<string, Sprite> Sprites = new();
    private readonly List<Image> _pool = new();
    private int _used;
    private bool _anyShown;

    public void Begin() => _used = 0;

    public void End()
    {
        for (int i = _used; i < _pool.Count; i++)
            if (_pool[i] != null && _pool[i].gameObject.activeSelf) _pool[i].gameObject.SetActive(false);
        _anyShown = _used > 0;
    }

    public void HideAll()
    {
        if (!_anyShown) return;
        _used = 0;
        End();
    }

    public void Draw(string name, Rect area, Color color)
    {
        if (HudRoot.IconLayer == null) return;
        Sprite sprite = Get(name);
        if (sprite == null) return;

        Image img = Next();
        img.sprite = sprite;
        Rect r = CustomGui.TransformRect(area);
        Color c = color;
        c.a *= CustomGui.TransformAlpha;
        img.color = c;
        RectTransform rt = img.rectTransform;
        rt.anchoredPosition = r.position;
        rt.sizeDelta = r.size;
    }

    private Image Next()
    {
        if (_used < _pool.Count && _pool[_used] != null)
        {
            Image existing = _pool[_used++];
            if (!existing.gameObject.activeSelf) existing.gameObject.SetActive(true);
            return existing;
        }

        var go = new GameObject("RepoCheatMenu_MenuIcon", typeof(RectTransform));
        go.transform.SetParent(HudRoot.IconLayer, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        img.preserveAspect = true;
        if (_used < _pool.Count) _pool[_used] = img; else _pool.Add(img);
        _used++;
        return img;
    }

    private static Sprite Get(string name)
    {
        if (Sprites.TryGetValue(name, out Sprite cached) && cached != null) return cached;
        if (!RockIconData.Alpha.TryGetValue(name, out string b64)) return null;

        int size = RockIconData.Size;
        // White RGBA with the bitmap as alpha (an Alpha8 texture would sample as black without the UI
        // shader's alpha-texture add, which a plain Image does not get).
        byte[] alpha = Convert.FromBase64String(b64);
        var pixels = new Color32[size * size];
        for (int i = 0; i < pixels.Length && i < alpha.Length; i++)
            pixels[i] = new Color32(255, 255, 255, alpha[i]);
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        tex.SetPixels32(pixels);
        tex.Apply();
        Sprite sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        Sprites[name] = sprite;
        return sprite;
    }
}

// The menu window's pool.
internal static class MenuIcons
{
    private static readonly IconPool Pool = new();

    public static void Begin() => Pool.Begin();
    public static void End() => Pool.End();
    public static void HideAll() => Pool.HideAll();
    public static void Draw(string name, Rect area, Color color) => Pool.Draw(name, area, color);
}
