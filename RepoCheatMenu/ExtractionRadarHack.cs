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

// Extraction Radar: a small always-on-screen readout of distance + rough direction to the
// nearest active extraction point - reuses EconomyHack.FindActiveExtractionPoint (same helper
// LootMagnetHack rides). Purely local UI, same ScreenSpaceOverlay Canvas approach as EspOverlay -
// no networking involved at all.
internal static class ExtractionRadarHack
{
    private static Canvas _canvas;
    private static Text _label;

    public static void Apply()
    {
        if (!CheatState.ExtractionRadar.Value)
        {
            if (_canvas != null) _canvas.gameObject.SetActive(false);
            return;
        }

        EnsureBuilt();

        PlayerAvatar local = PlayerAvatar.instance;
        Camera cam = Camera.main;
        bool inGame = local != null && cam != null && GameState.IsInGame();
        _canvas.gameObject.SetActive(inGame);
        if (!inGame) return;

        Vector3 playerPos = local.transform.position;
        ExtractionPoint extraction = EconomyHack.FindActiveExtractionPoint(playerPos);
        string suffix = "";
        if (extraction == null && RoundDirector.instance != null)
        {
            // Nothing running: point at the nearest one still to be done - that is where to go next.
            extraction = SemiFunc.ExtractionPointGetNearestNotActivated(playerPos);
            suffix = " (не активна)";
        }
        if (extraction == null)
        {
            _label.text = "Выход: нет точек";
            return;
        }

        Vector3 toExtraction = extraction.transform.position - playerPos;
        float distance = toExtraction.magnitude;

        Vector3 flatForward = cam.transform.forward;
        flatForward.y = 0f;
        Vector3 flatToExtraction = toExtraction;
        flatToExtraction.y = 0f;

        string arrow = "^";
        if (flatForward.sqrMagnitude > 0.001f && flatToExtraction.sqrMagnitude > 0.001f)
        {
            float signedAngle = Vector3.SignedAngle(flatForward, flatToExtraction, Vector3.up);
            arrow = signedAngle switch
            {
                < -100f => "v",
                < -20f => "<",
                > 100f => "v",
                > 20f => ">",
                _ => "^",
            };
        }

        _label.text = $"Выход {arrow} {distance:0} м{suffix}";
    }

    private static void EnsureBuilt()
    {
        if (_canvas != null) return;

        var go = new GameObject("RepoCheatMenu_ExtractionRadar");
        Object.DontDestroyOnLoad(go);
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 8500;
        go.AddComponent<CanvasScaler>();

        var textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var rect = textGo.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -12f);
        rect.sizeDelta = new Vector2(400f, 30f);

        _label = textGo.AddComponent<Text>();
        _label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        _label.fontSize = 18;
        _label.alignment = TextAnchor.UpperCenter;
        _label.color = Color.cyan;
        _label.horizontalOverflow = HorizontalWrapMode.Overflow;

        var outline = textGo.AddComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(1, -1);

        _canvas.gameObject.SetActive(false);
    }
}
