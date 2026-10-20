// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using UnityEngine;
using UnityEngine.UI;

namespace RepoCheatMenu;

// Размытие всего кадра для главного экрана Rockstar. Тот же приём, что у IslandBlur/MenuBlur
// (последовательное билинейное уменьшение RenderTextureMain.renderTexture на GPU, без чтения
// обратно), но без маски - картинка растянута на весь канвас - и со своими рендер-текстурами:
// MenuBlur каждый кадр прячет сам CustomOverlayUI.DrawIfVisible, общий экземпляр не годится.
internal static class ScreenBlur
{
    private static GameObject _root;
    private static RawImage _image;
    private static RenderTexture _half, _quarter, _eighth, _back;
    private static int _srcW, _srcH;

    public static void Hide()
    {
        if (_root != null && _root.activeSelf) _root.SetActive(false);
    }

    // false - в этой сцене нечего размывать (нет рендер-текстуры игры); тогда экран просто темнее.
    public static bool Render(float alpha, Color tint)
    {
        RenderTexture src = null;
        try
        {
            RenderTextureMain main = RenderTextureMain.instance;
            if (main != null) src = main.renderTexture;
        }
        catch (System.Exception) { }
        if (src == null || !src.IsCreated() || HudRoot.IconLayer == null) return false;

        Ensure(HudRoot.IconLayer.parent);
        EnsureTargets(src.width, src.height);

        Graphics.Blit(src, _half);
        Graphics.Blit(_half, _quarter);
        Graphics.Blit(_quarter, _eighth);
        Graphics.Blit(_eighth, _back);

        _image.texture = _back;
        _image.color = new Color(tint.r, tint.g, tint.b, alpha);
        if (!_root.activeSelf) _root.SetActive(true);
        return true;
    }

    private static void Ensure(Transform canvas)
    {
        if (_root != null) return;

        _root = new GameObject("RepoCheatMenu_ScreenBlur", typeof(RectTransform));
        _root.transform.SetParent(canvas, false);
        _root.transform.SetSiblingIndex(0); // под фигурами и текстом CustomGui
        var rt = _root.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = Vector2.zero;

        _image = _root.AddComponent<RawImage>();
        _image.raycastTarget = false;
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
            if (rt != null) { rt.Release(); Object.Destroy(rt); }
        _half = _quarter = _eighth = _back = null;
    }
}
