// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RepoCheatMenu;

// Shared canvas + CustomGui surface pair for everything drawn with the custom mesh renderer -
// both CustomOverlayUI (the F6 menu) and PlayerHud (the always-on HUD widgets) draw into this
// same pair of surfaces now, inside one shared BeginFrame/EndFrame per real frame. They used to
// each get their own canvas and their own Begin/End pair; CustomGuiGraphic.Clear() wipes the
// WHOLE mesh on every BeginFrame, so two independent Begin/End cycles in the same visual frame
// would just have the second one erase the first's drawing instead of layering over it. One
// shared cycle, draw order = paint order (HUD first, so an open menu visually sits on top of it).
internal static class HudRoot
{
    private static bool _built;

    // A plain Unity Image can't be drawn through CustomGuiGraphic (that mesh only ever samples
    // Texture2D.whiteTexture or the font atlas - see CustomGui.cs's own comment on why rects and
    // text already need separate surfaces). Item icons are arbitrary per-item Sprites, so they go
    // through ordinary pooled Image components instead (PlayerHud.cs) - this transform gives them
    // the exact same local coordinate space CustomGui's own surfaces use (full-stretch, pivot
    // (0,0), bottom-left origin) so a screen Rect computed for CustomGui.Rect/Text lines up with
    // an icon Image's anchoredPosition/sizeDelta without any extra conversion.
    public static Transform IconLayer { get; private set; }

    public static void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        if (Object.FindObjectOfType<EventSystem>() == null)
        {
            var esGo = new GameObject("RepoCheatMenu_CustomUI_EventSystem");
            Object.DontDestroyOnLoad(esGo);
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<StandaloneInputModule>();
        }

        var canvasGo = new GameObject("RepoCheatMenu_HudRoot");
        Object.DontDestroyOnLoad(canvasGo);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10001; // above OverlayUI's own uGUI canvas (10000)
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        CustomGui.Init(canvasGo.transform);

        var iconLayerGo = new GameObject("CustomGuiIcons", typeof(RectTransform));
        iconLayerGo.transform.SetParent(canvasGo.transform, false);
        var iconRect = iconLayerGo.GetComponent<RectTransform>();
        iconRect.anchorMin = Vector2.zero;
        iconRect.anchorMax = Vector2.one;
        iconRect.offsetMin = Vector2.zero;
        iconRect.offsetMax = Vector2.zero;
        iconRect.pivot = Vector2.zero;
        IconLayer = iconLayerGo.transform;
    }

    // Called once per real frame (from Patches.cs, replacing the old direct CustomOverlayUI.Tick()
    // call) - PlayerHud draws first (always-on, display or edit mode), CustomOverlayUI draws on
    // top of it only while its own window is open.
    public static void Tick()
    {
        EnsureBuilt();
        TickEditModeCursor();

        Guard("PhotoMode", PhotoModeHack.Tick); // decides what is visible this frame, before anything renders
        CustomGui.BeginFrame();
        // Each part on its own: one of them throwing must not stop the others (or EndFrame) from running.
        // World effects first, so every HUD part is drawn over them.
        Guard("KillFx", KillFx.Draw);
        Guard("TargetFx", TargetFx.Draw);
        Guard("CustomFog", CustomFogHack.Apply);
        Guard("Pings", PingSystem.Draw);
        Guard("RockMainScreen", RockMainScreen.Draw);
        Guard("BoxEsp", BoxEsp.Draw);
        Guard("FriendMarkers", FriendMarkers.Draw);
        Guard("RockEsp", RockEsp.Draw);
        Guard("ArrowEsp", ArrowEsp.Draw);
        Guard("JumpCircles", JumpCircles.Draw);
        Guard("PlayerHud", PlayerHud.Draw);
        Guard("DynamicIsland", DynamicIsland.Draw);
        Guard("LyricsHud", LyricsHud.Draw);
        Guard("RockNotify", RockNotify.Draw);
        Guard("OptimizationHack", OptimizationHack.Tick);
        Guard("CustomOverlayUI", CustomOverlayUI.DrawIfVisible);
        CustomGui.EndFrame();
    }

    private static float _nextGuardLog;

    private static void Guard(string name, System.Action part)
    {
        try
        {
            part();
        }
        catch (System.Exception e)
        {
            CustomGui.ClearTransform();
            CustomGui.NoInput = false;
            if (Time.unscaledTime >= _nextGuardLog)
            {
                _nextGuardLog = Time.unscaledTime + 5f;
                Plugin.Log.LogError($"Exception in {name}: {e}");
            }
        }
    }

    // HUD edit mode is toggled from inside a menu (so the cursor is already free at that point),
    // but the whole point is to keep dragging widgets after closing the menu - this claims the
    // cursor for itself only once neither menu already owns it, leaves it alone while a menu is open,
    // and gives it back once edit mode is off and no menu is open, instead of each of the three systems independently
    // saving/restoring Cursor state (which stacks incorrectly once more than one is ever active
    // at the same time).
    private static bool _editCursorApplied;
    private static bool _editCursorWasVisible;
    private static CursorLockMode _editCursorWasLock;

    private static void TickEditModeCursor()
    {
        // While a menu is open it owns the cursor (it saved the state on Show and restores it on Hide).
        // This used to treat "menu open" like "edit mode off" and restore the pre-edit state - locked and
        // hidden - every frame, so the menu lost its cursor, and the menu's own Hide then put back the
        // edit-mode cursor, which stayed visible in normal play after edit mode was switched off.
        if (OverlayUI.IsOpen || CustomOverlayUI.IsOpen) return;

        bool wantsCursor = CheatState.HudEditMode.Value;

        if (wantsCursor)
        {
            if (!_editCursorApplied)
            {
                _editCursorWasVisible = Cursor.visible;
                _editCursorWasLock = Cursor.lockState;
                _editCursorApplied = true;
            }
            Cursor.lockState = CursorLockMode.Confined;
            Cursor.visible = true;
            if (InputManager.instance != null) InputManager.instance.DisableAiming(0.5f);
        }
        else if (_editCursorApplied)
        {
            Cursor.lockState = _editCursorWasLock;
            Cursor.visible = _editCursorWasVisible;
            _editCursorApplied = false;
        }
    }
}
