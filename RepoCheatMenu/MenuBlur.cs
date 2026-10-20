// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RepoCheatMenu;

// Frosted-glass backdrop for the menu window: Rockstar draws its ClickGUI over a blur of the game
// frame. Same technique as IslandBlur (progressive bilinear down-sampling of the game's render
// texture, shown through a Mask), only the stencil is one rounded rectangle - the window - instead of
// the island's shapes. Own render targets so the island and the window can be on screen together.
internal static class MenuBlur
{
    private static GameObject _root;
    private static MenuMaskGraphic _mask;
    private static RawImage _image;
    private static RenderTexture _half, _quarter, _eighth, _back;
    private static int _srcW, _srcH;

    public static void Hide()
    {
        if (_root != null && _root.activeSelf) _root.SetActive(false);
    }

    // `area` is in CustomGui space (pixels, origin bottom-left), already including any animation scale.
    // Returns false when there is nothing to blur (no game render texture in this scene).
    public static bool Render(Rect area, float radius, float alpha, Color tint)
    {
        RenderTexture src = null;
        try
        {
            RenderTextureMain main = RenderTextureMain.instance;
            if (main != null) src = main.renderTexture;
        }
        catch (Exception) { }
        if (src == null || !src.IsCreated() || HudRoot.IconLayer == null) return false;

        Ensure(HudRoot.IconLayer.parent);
        EnsureTargets(src.width, src.height);

        Graphics.Blit(src, _half);
        Graphics.Blit(_half, _quarter);
        Graphics.Blit(_quarter, _eighth);
        Graphics.Blit(_eighth, _back);

        _image.texture = _back;
        _image.color = new Color(tint.r, tint.g, tint.b, alpha);
        _mask.Set(area, radius);
        if (!_root.activeSelf) _root.SetActive(true);
        return true;
    }

    private static void Ensure(Transform canvas)
    {
        if (_root != null) return;

        _root = new GameObject("RepoCheatMenu_MenuBlur", typeof(RectTransform));
        _root.transform.SetParent(canvas, false);
        _root.transform.SetSiblingIndex(0); // under CustomGui's shapes and text
        Stretch(_root.GetComponent<RectTransform>());
        _mask = _root.AddComponent<MenuMaskGraphic>();
        _mask.raycastTarget = false;
        _root.AddComponent<Mask>().showMaskGraphic = false;

        var img = new GameObject("Blur", typeof(RectTransform));
        img.transform.SetParent(_root.transform, false);
        Stretch(img.GetComponent<RectTransform>());
        _image = img.AddComponent<RawImage>();
        _image.raycastTarget = false;
    }

    // Same layout as CustomGui's surfaces (full stretch, pivot bottom-left).
    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = Vector2.zero;
    }

    private static void EnsureTargets(int w, int h)
    {
        if (_half != null && _srcW == w && _srcH == h) return;
        Release();
        _srcW = w;
        _srcH = h;
        _half = Make(w / 2, h / 2);
        _quarter = Make(w / 4, h / 4);
        _eighth = Make(w / 8, h / 8);
        _back = Make(w / 4, h / 4);
    }

    private static RenderTexture Make(int w, int h)
    {
        var rt = new RenderTexture(Mathf.Max(8, w), Mathf.Max(8, h), 0) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        rt.Create();
        return rt;
    }

    private static void Release()
    {
        foreach (RenderTexture rt in new[] { _half, _quarter, _eighth, _back })
            if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); }
        _half = _quarter = _eighth = _back = null;
    }
}

// Stencil for MenuBlur: one filled rounded rectangle.
internal sealed class MenuMaskGraphic : MaskableGraphic
{
    private Rect _area;
    private float _radius;

    public void Set(Rect area, float radius)
    {
        if (area == _area && Mathf.Approximately(radius, _radius)) return;
        _area = area;
        _radius = radius;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (_area.width <= 1f || _area.height <= 1f) return;

        float r = Mathf.Clamp(_radius, 0f, Mathf.Min(_area.width, _area.height) * 0.5f);
        var pts = new List<Vector2>();
        void Corner(Vector2 c, float startDeg)
        {
            for (int i = 0; i <= 8; i++)
            {
                float a = (startDeg + 90f * i / 8f) * Mathf.Deg2Rad;
                pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }
        }
        Corner(new Vector2(_area.xMax - r, _area.yMin + r), 270f);
        Corner(new Vector2(_area.xMax - r, _area.yMax - r), 0f);
        Corner(new Vector2(_area.xMin + r, _area.yMax - r), 90f);
        Corner(new Vector2(_area.xMin + r, _area.yMin + r), 180f);

        UIVertex v = UIVertex.simpleVert;
        v.color = Color.white;
        v.position = _area.center;
        vh.AddVert(v);
        foreach (Vector2 p in pts)
        {
            v.position = p;
            vh.AddVert(v);
        }
        for (int i = 0; i < pts.Count; i++)
            vh.AddTriangle(0, 1 + i, 1 + (i + 1) % pts.Count);
    }
}
