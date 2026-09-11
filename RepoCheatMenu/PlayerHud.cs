using System;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;

namespace RepoCheatMenu;

// Always-on gameplay HUD - separate from both menus (OverlayUI/CustomOverlayUI), drawn every real
// frame via HudRoot.Tick() regardless of whether either menu is open. Built on the same CustomGui
// mesh renderer the new menu uses, sharing its one surface pair (see HudRoot.cs's own comment for
// why there's only ever one shared BeginFrame/EndFrame per real frame now). Item icons are the one
// thing that renderer can't draw (arbitrary per-item Sprites, not the shared white/font texture),
// so those go through a small pool of ordinary Unity Image components parented under HudRoot's
// IconLayer instead - positioned every frame to match a CustomGui-computed Rect.
internal static class PlayerHud
{
    // Height is measured per frame from what the body actually has to show, not a fixed number:
    // with fixed heights an empty "Цель"/"Бинды" card rendered as a mostly-blank box with the text
    // stuck along the top edge, which is the single biggest thing that made this look nothing like
    // the reference HUD (where every card hugs its content). Width stays fixed - a card that also
    // changed width as text came and went would jitter constantly.
    private const float TitleBlockHeight = 30f;
    private const float BodyPadBottom = 8f;

    private sealed class Widget
    {
        public string Id;
        public string Title;
        public CheatState.HudWidgetConfig Cfg;
        public float Width;
        public Func<float> BodyHeight;
        public Action<Rect, Color> DrawBody;
        public Vector2 RenderPos;
        public Vector2 TargetPos;
        public bool Dragging;
        public bool Initialized;

        public Vector2 Size => new(Width, TitleBlockHeight + BodyHeight() + BodyPadBottom);
    }

    private static readonly Widget[] Widgets =
    {
        new() { Id = "stats", Title = "Игрок", Cfg = CheatState.HudPlayerStats, Width = 210, BodyHeight = PlayerStatsHeight, DrawBody = DrawPlayerStatsBody },
        new() { Id = "target", Title = "Цель", Cfg = CheatState.HudTargetInfo, Width = 210, BodyHeight = TargetInfoHeight, DrawBody = DrawTargetInfoBody },
        new() { Id = "media", Title = "Плеер", Cfg = CheatState.HudMediaPlayer, Width = 230, BodyHeight = MediaPlayerHeight, DrawBody = DrawMediaPlayerBody },
        new() { Id = "keybinds", Title = "Бинды", Cfg = CheatState.HudKeybinds, Width = 210, BodyHeight = KeybindsHeight, DrawBody = DrawKeybindsBody },
    };

    // Each of these mirrors exactly what its DrawXxxBody below lays out, for the current state -
    // that's the contract: change the rows a body draws, change its height here too.
    private const float LineH = 18f;

    private static float PlayerStatsHeight() =>
        PlayerTargets.Local == null ? LineH : 32f + 4f + LineH; // avatar block, gap, coords row

    private static float TargetInfoHeight() =>
        PlayerTargets.Picked() == null
            ? LineH * 2f                          // two-line "no target picked" placeholder
            : 20f + LineH + 6f + 28f;             // name, hp/dist, gap, inventory icon row

    private static float MediaPlayerHeight() => 34f + 6f + 22f; // album tile, gap, transport row

    private static float KeybindsHeight()
    {
        int bound = 0;
        foreach (Keybind kb in KeybindManager.All)
            if (kb.Vk.Value != 0) bound++;
        return bound == 0 ? LineH * 2f : Mathf.Min(bound, 5) * LineH;
    }

    // Same 8-preset palette as CustomOverlayUI's own ColorCycle rows - not shared via a common
    // constant since it's a single small literal array, not worth a cross-file dependency for.
    private static readonly string[] Palette = { "#FF3B30", "#FF9500", "#FFCC00", "#34C759", "#0A84FF", "#AF52DE", "#FFFFFF", "#00FFFF" };

    public static void Draw()
    {
        if (!CheatState.HudEnabled.Value) return;

        ResetIcons();
        bool edit = CheatState.HudEditMode.Value;

        if (CheatState.HudAutoStack.Value) RestackColumn();

        foreach (Widget w in Widgets)
        {
            if (!w.Cfg.Enabled.Value) continue;
            DrawWidget(w, edit);
        }

        if (edit) DrawPalette();

        FinishIcons();
    }

    // Now that card height follows content, a per-widget fixed Y can't keep the column tight -
    // hiding a widget or a card shrinking (target deselected, a keybind unbound) leaves a hole,
    // which is exactly what the stored 20/122/244/372 defaults started doing once heights became
    // dynamic. Auto-stack lays every enabled card top-down from the first one's own position with
    // a constant gap, so the column always reads like the reference HUD's tight stack. The first
    // enabled widget stays the anchor, so dragging it in edit mode still moves the whole group;
    // turn the toggle off to place each card independently.
    private const float StackGap = 8f;

    private static void RestackColumn()
    {
        Widget anchor = null;
        float y = 0f;

        foreach (Widget w in Widgets)
        {
            if (!w.Cfg.Enabled.Value) continue;

            if (anchor == null)
            {
                anchor = w;
                y = w.Cfg.PosY.Value + w.Size.y + StackGap;
                continue;
            }

            // Written straight to the config entries the draw path already reads, so the smooth
            // follow in DrawWidget animates cards into place instead of teleporting them.
            w.Cfg.PosX.Value = anchor.Cfg.PosX.Value;
            w.Cfg.PosY.Value = y;
            y += w.Size.y + StackGap;
        }
    }

    private static void DrawWidget(Widget w, bool edit)
    {
        if (!w.Initialized)
        {
            w.RenderPos = new Vector2(w.Cfg.PosX.Value, w.Cfg.PosY.Value);
            w.TargetPos = w.RenderPos;
            w.Initialized = true;
        }

        // Rect from BEFORE this frame's position update - same one-frame-lag-on-the-hitbox
        // tradeoff CustomOverlayUI's own window drag already makes, imperceptible in practice.
        float top = CustomGui.ScreenHeight - w.RenderPos.y;
        Rect rect = new(w.RenderPos.x, top - w.Size.y, w.Size.x, w.Size.y);
        Rect headerRect = new(rect.x, rect.yMax - 26f, rect.width, 26f);

        if (edit)
        {
            bool overHeader = CustomGui.IsHovered(headerRect);
            if (overHeader && Input.GetMouseButtonDown(0) && !w.Dragging)
            {
                w.Dragging = true;
                w.TargetPos = w.RenderPos;
            }
            if (Input.GetMouseButtonUp(0) && w.Dragging)
            {
                w.Dragging = false;
                w.Cfg.PosX.Value = w.TargetPos.x;
                w.Cfg.PosY.Value = w.TargetPos.y;
            }
            if (w.Dragging)
            {
                Vector2 delta = new(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
                w.TargetPos.x += delta.x * (CustomGui.ScreenWidth / Mathf.Max(Screen.width, 1));
                w.TargetPos.y -= delta.y * (CustomGui.ScreenHeight / Mathf.Max(Screen.height, 1));
            }
        }
        else
        {
            w.Dragging = false;
        }

        if (!w.Dragging)
            w.TargetPos = new Vector2(w.Cfg.PosX.Value, w.Cfg.PosY.Value);

        // Smooth trailing follow instead of the menu window's instant 1:1 drag - a deliberately
        // different feel from CustomOverlayUI.HandleDrag, matching what "перемещать с плавной
        // анимацией" (move with smooth animation) actually asked for.
        w.RenderPos = Vector2.Lerp(w.RenderPos, w.TargetPos, 0.25f);

        Color accent = ParseColor(w.Cfg.Color.Value);

        // Layered border trick (CustomGui has no stroke-only rect) - a slightly bigger, lighter
        // rect behind a smaller, solid one reads as a ~1.5px edge, and the inner fill is a
        // top-lighter/bottom-darker gradient (RectGradient - see CustomGui.cs). Pushed bluer/more
        // saturated (was a fairly neutral dark gray-navy) and rounder (18 vs 14) to read closer to
        // the reference screenshots' violet-indigo "glass panel" look instead of a generic dark box.
        CustomGui.Rect(rect, new Color(0.55f, 0.6f, 0.85f, 0.35f), 18f);
        Rect innerRect = new(rect.x + 1.5f, rect.y + 1.5f, rect.width - 3f, rect.height - 3f);
        CustomGui.RectGradient(innerRect, new Color(0.176f, 0.184f, 0.318f, edit ? 0.98f : 0.95f), new Color(0.098f, 0.102f, 0.192f, edit ? 0.98f : 0.95f), 17f);
        CustomGui.Rect(new Rect(rect.x, rect.yMax - 3f, rect.width, 3f), accent, 1.5f);

        // Title "icon" is a filled rounded-square chip (not a plain dot) - closer to the reference
        // screenshots' colored icon-chip treatment than a bare bullet, still just mesh shapes so
        // it can't hit the emoji-glyph problem the 🧍/🎯/🎵 prefixes tried here originally had
        // (Unity's built-in Arial font carries no emoji glyphs - GetCharacterInfo just fails and
        // AddText silently skips the character instead of drawing anything).
        CustomGui.Rect(new Rect(rect.x + 10f, rect.yMax - 26f, 18f, 18f), accent, 6f);
        CustomGui.Text(new Rect(rect.x + 34f, rect.yMax - 25f, rect.width - 44f, 18f), w.Title, 13, Color.white, TextAnchor.MiddleLeft);

        Rect bodyRect = new(rect.x + 10f, rect.y + BodyPadBottom, rect.width - 20f,
            rect.height - TitleBlockHeight - BodyPadBottom);
        w.DrawBody(bodyRect, accent);

        if (!edit) return;

        Rect closeBtn = new(rect.xMax - 20f, rect.yMax - 18f, 14f, 14f);
        if (CustomGui.Button(closeBtn, "x", new Color(0.6f, 0.12f, 0.12f, 0.85f), new Color(0.8f, 0.16f, 0.16f, 0.95f), Color.white, 10, 4f))
            w.Cfg.Enabled.Value = false;

        Rect colorBtn = new(rect.xMax - 40f, rect.yMax - 18f, 14f, 14f);
        if (CustomGui.Button(colorBtn, "", accent, accent, Color.clear, 8, 4f))
        {
            int idx = Array.IndexOf(Palette, w.Cfg.Color.Value);
            w.Cfg.Color.Value = Palette[(idx + 1 + Palette.Length) % Palette.Length];
        }
    }

    // Edit-mode-only strip listing any removed widgets so they can be added back - lives at a
    // fixed top-of-screen spot rather than wherever the widget used to be, since that position is
    // exactly what got removed.
    private static void DrawPalette()
    {
        List<Widget> disabled = null;
        foreach (Widget w in Widgets)
            if (!w.Cfg.Enabled.Value) (disabled ??= new List<Widget>()).Add(w);
        if (disabled == null) return;

        const float chipWidth = 150f;
        float x = 20f;
        float y = CustomGui.ScreenHeight - 20f;

        CustomGui.Rect(new Rect(x - 8f, y - 34f, chipWidth * disabled.Count + 8f, 34f), new Color(0f, 0f, 0f, 0.55f), 8f);

        for (int i = 0; i < disabled.Count; i++)
        {
            Rect chip = new(x + i * chipWidth, y - 28f, chipWidth - 8f, 22f);
            if (CustomGui.Button(chip, "+ " + disabled[i].Title, new Color(1f, 1f, 1f, 0.08f), new Color(1f, 1f, 1f, 0.18f), Color.white, 12, 5f))
                disabled[i].Cfg.Enabled.Value = true;
        }
    }

    private static Color ParseColor(string hex)
    {
        if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out Color c)) return c;
        return Color.white;
    }

    // Steam display names can run well past what a ~150px-wide card column fits at 14px - same
    // rough character-count cap PollSpotify already uses for the same reason (no text-measuring
    // API exposed out of CustomGuiGraphic, only it can query the font).
    private static string Truncate(string s, int maxChars) =>
        !string.IsNullOrEmpty(s) && s.Length > maxChars ? s[..maxChars] + "…" : s;

    // --- Widget bodies -----------------------------------------------------------------------

    // PlayerAvatar.instance only ever exists once the game has actually spawned into a level,
    // which is well after NetworkConnect.Start() has configured PhotonNetwork - gating every
    // PhotonNetwork.* read in this file behind "local player exists" is the same safe pattern the
    // rest of this mod already relies on for the documented premature-PhotonNetwork-touch bug
    // (see CLAUDE.md) - it just happens to also be the natural "nothing meaningful to show yet"
    // condition for a gameplay HUD.
    private static void DrawPlayerStatsBody(Rect body, Color accent)
    {
        PlayerAvatar local = PlayerTargets.Local;
        if (local == null)
        {
            CustomGui.Text(body, "Не в игре", 12, new Color(0.6f, 0.62f, 0.68f, 1f), TextAnchor.UpperLeft);
            return;
        }

        string name = PlayerTargets.NameOf(local);
        float fps = 1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
        int ping = PhotonNetwork.InRoom ? PhotonNetwork.GetPing() : 0;
        Vector3 pos = local.transform.position;

        // Round "avatar" placeholder - no real player skin/portrait field exists anywhere in the
        // decompiled source (see DrawTargetInfoBody's own comment on that), so this is just the
        // widget's own accent color rather than a fake likeness.
        float avatarRadius = 16f;
        CustomGui.Circle(new Vector2(body.x + avatarRadius, body.yMax - avatarRadius), avatarRadius, accent);
        CustomGui.Circle(new Vector2(body.x + avatarRadius, body.yMax - avatarRadius), avatarRadius - 5f, new Color(1f, 1f, 1f, 0.85f));

        float textX = body.x + avatarRadius * 2f + 10f;
        float textW = body.width - avatarRadius * 2f - 10f;
        CustomGui.Text(new Rect(textX, body.yMax - 16f, textW, 18f), Truncate(name, 16), 14, Color.white, TextAnchor.UpperLeft);
        CustomGui.Text(new Rect(textX, body.yMax - 34f, textW, 16f), $"{fps:0} fps   {ping} ms", 12, accent, TextAnchor.UpperLeft);

        CustomGui.Rect(new Rect(body.x, body.y + 2f, 10f, 10f), new Color(1f, 1f, 1f, 0.18f), 3f);
        CustomGui.Text(new Rect(body.x + 16f, body.y, body.width - 16f, 16f), $"x{pos.x:0} y{pos.y:0} z{pos.z:0}", 11, new Color(0.75f, 0.78f, 0.85f, 1f), TextAnchor.LowerLeft);
    }

    // Target = whoever's picked in the shared name field (Игроки tab) - reuses PlayerTargets.
    // Picked() instead of a new aim/raycast-based "looked at" system, same target every other
    // per-player action (Cage/Yeet/Sound) in this mod already drives from. Inventory icons come
    // from EspOverlay.ResolveInventory (StatsManager.playerInventorySpot1/2/3 hash-matched against
    // scene ItemAttributes.icon - see that file's own comment). The square "avatar" chip is the
    // target's synced eye color if they're running this same mod (CosmeticHack.
    // GetSyncedEyeColor), falling back to the widget's own accent color otherwise - the closest
    // real per-player visual identity data this mod tracks; there's no player skin/color field
    // anywhere in the decompiled source, and a live 3D headshot would need its own
    // always-tracking camera per target, which this doesn't attempt.
    private static void DrawTargetInfoBody(Rect body, Color accent)
    {
        PlayerAvatar target = PlayerTargets.Picked();
        if (target == null)
        {
            CustomGui.Text(body, "Цель не выбрана\n(«Игроки» -> имя цели)", 12, new Color(0.6f, 0.62f, 0.68f, 1f), TextAnchor.UpperLeft);
            return;
        }

        string name = PlayerTargets.NameOf(target);
        bool dead = GameReflect.Get<bool>(target, "deadSet");
        int hp = !dead && target.playerHealth != null ? GameReflect.Get<int>(target.playerHealth, "health") : 0;
        PlayerAvatar local = PlayerTargets.Local;
        float dist = local != null ? Vector3.Distance(local.transform.position, target.transform.position) : 0f;
        Color avatarColor = CosmeticHack.GetSyncedEyeColor(target) ?? accent;

        // Square "avatar" chip + name + a big bold HP number on the right - mirrors the reference
        // screenshots' own "[face] Player22   20hp" identity row more directly than a small color
        // dot did.
        const float avatarSize = 26f;
        CustomGui.Rect(new Rect(body.x, body.yMax - avatarSize, avatarSize, avatarSize), avatarColor, 7f);

        string displayName = Truncate(name, 14);
        CustomGui.Text(new Rect(body.x + avatarSize + 8f, body.yMax - 18f, body.width - avatarSize - 48f, 18f),
            dead ? $"{displayName} (мёртв)" : displayName, 13, Color.white, TextAnchor.UpperLeft);

        if (!dead)
            CustomGui.Text(new Rect(body.xMax - 40f, body.yMax - 20f, 40f, 20f), $"{hp}hp", 14, accent, TextAnchor.MiddleRight);

        CustomGui.Text(new Rect(body.x + avatarSize + 8f, body.yMax - avatarSize + 2f, body.width - avatarSize - 8f, 14f),
            $"{dist:0}m", 11, new Color(0.7f, 0.72f, 0.78f, 1f), TextAnchor.UpperLeft);

        EspOverlay.InventorySlot[] slots = EspOverlay.ResolveInventory(target);
        const float iconSize = 28f;
        for (int i = 0; i < slots.Length; i++)
        {
            Rect slotRect = new(body.x + i * (iconSize + 6f), body.y, iconSize, iconSize);
            CustomGui.Rect(slotRect, new Color(1f, 1f, 1f, 0.12f), 7f);
            Rect slotInner = new(slotRect.x + 1f, slotRect.y + 1f, slotRect.width - 2f, slotRect.height - 2f);
            CustomGui.Rect(slotInner, new Color(0f, 0f, 0f, 0.25f), 6f);
            if (slots[i].Icon != null) DrawIcon(slotInner, slots[i].Icon);
        }
    }

    // Real bound keys from KeybindManager.All (Vk.Value != 0, the same list middle-click-to-bind
    // in the menu writes to) - not decorative like the media player widget, an actual on-screen
    // reminder of what's currently bound to what. Matches the reference screenshots' own
    // "Keybinds" card in spirit; row count is however many fit the card, with a "+N ещё" footer
    // if more are bound than there's room to list.
    private static void DrawKeybindsBody(Rect body, Color accent)
    {
        List<Keybind> bound = null;
        foreach (Keybind kb in KeybindManager.All)
            if (kb.Vk.Value != 0) (bound ??= new List<Keybind>()).Add(kb);

        if (bound == null)
        {
            CustomGui.Text(body, "Нет привязанных клавиш\n(средний клик по строке в меню)", 11, new Color(0.6f, 0.62f, 0.68f, 1f), TextAnchor.UpperLeft);
            return;
        }

        const float rowH = 18f;
        int maxRows = Mathf.Max(1, Mathf.FloorToInt(body.height / rowH));
        bool truncated = bound.Count > maxRows;
        int shown = Mathf.Max(0, truncated ? maxRows - 1 : bound.Count);

        for (int i = 0; i < shown; i++)
        {
            float y = body.yMax - rowH * (i + 1);
            CustomGui.Circle(new Vector2(body.x + 4f, y + rowH * 0.5f), 3f, accent);
            CustomGui.Text(new Rect(body.x + 12f, y, body.width - 60f, rowH), Truncate(bound[i].Label, 14), 11, new Color(0.85f, 0.87f, 0.92f, 1f), TextAnchor.MiddleLeft);
            CustomGui.Text(new Rect(body.xMax - 48f, y, 48f, rowH), Win32.VkName(bound[i].Vk.Value), 11, accent, TextAnchor.MiddleRight);
        }

        if (truncated)
            CustomGui.Text(new Rect(body.x, body.yMax - rowH * (shown + 1), body.width, rowH), $"+{bound.Count - shown} ещё", 10, new Color(0.55f, 0.57f, 0.62f, 1f), TextAnchor.MiddleLeft);
    }

    // Real Spotify track when CheatState.HudMediaPlayerUseSpotify is on (Win32.
    // GetSpotifyWindowTitle - see that method's own comment for why WinRT's now-playing API was
    // deliberately avoided). Spotify's window title is "Spotify" while paused/idle and
    // "Artist - Track" while something's actually playing - no timestamp in either case, so there
    // is no real progress bar to show, only a play/pause state line. Polled once a second, not
    // every frame - GetWindowText is cheap but pointless to call 60x/sec for text that only
    // changes on a song/pause change.
    private static string _spotifyStatus = "…";
    private static bool _spotifyPlaying;
    private static float _nextSpotifyPoll;

    // Falls back to the manual/decorative text + a paused-look fake progress bar when Spotify
    // integration is off or Spotify isn't running - no real audio hook in that case either way,
    // same honest "static prop" as before (see HudMediaPlayerTitle's own config description).
    private static float _mediaElapsed;
    private static bool _mediaPaused;
    private const float MediaFakeDuration = 208f; // 3:28

    private static void DrawMediaPlayerBody(Rect body, Color accent)
    {
        // "Album art" placeholder - a gradient tile with a small hand-drawn note mark (two mesh
        // primitives, not a glyph - see DrawNoteIcon). There's no real artwork source (Win32's
        // window-title read has no image, and the Spotify Web API needs an OAuth app registration
        // this personal mod isn't set up for), so this is honestly decorative, same as the rest of
        // this widget's chrome.
        const float artSize = 34f;
        Rect artRect = new(body.x, body.yMax - artSize, artSize, artSize);
        CustomGui.RectGradient(artRect, Color.Lerp(accent, Color.white, 0.3f), Color.Lerp(accent, Color.black, 0.45f), 6f);
        DrawNoteIcon(artRect);

        float textX = artRect.xMax + 8f;
        float textW = body.width - artSize - 8f;

        if (CheatState.HudMediaPlayerUseSpotify.Value)
        {
            PollSpotify();
            CustomGui.Text(new Rect(textX, body.yMax - 14f, textW, 16f), _spotifyStatus, 12, Color.white, TextAnchor.UpperLeft);
            Color stateColor = _spotifyPlaying ? new Color(0.3f, 0.85f, 0.4f, 1f) : new Color(0.55f, 0.57f, 0.62f, 1f);
            CustomGui.Circle(new Vector2(textX + 4f, body.yMax - artSize + 7f), 4f, stateColor);
            CustomGui.Text(new Rect(textX + 12f, body.yMax - artSize + 1f, textW - 12f, 14f), _spotifyPlaying ? "Играет" : "Пауза", 11, new Color(0.75f, 0.78f, 0.85f, 1f), TextAnchor.UpperLeft);

            // Real transport row - OS media keys (Win32.MediaPlayPause/Next/Prev), not just a
            // bigger decorative row: matches the reference screenshots' own prev/play/next
            // control set, and actually works because Spotify (like any app) responds to the
            // same hardware media keys a physical keyboard's media row would send.
            const float btnW = 28f, btnH = 22f, gap = 6f;
            Rect prevBtn = new(body.x, body.y, btnW, btnH);
            Rect playPauseBtn = new(prevBtn.xMax + gap, body.y, btnW, btnH);
            Rect nextBtn = new(playPauseBtn.xMax + gap, body.y, btnW, btnH);

            DrawTransportButton(prevBtn, () => DrawSkipIcon(prevBtn, false), Win32.MediaPrevTrack);
            DrawTransportButton(playPauseBtn, () => DrawPlayPauseIcon(playPauseBtn, !_spotifyPlaying), Win32.MediaPlayPause);
            DrawTransportButton(nextBtn, () => DrawSkipIcon(nextBtn, true), Win32.MediaNextTrack);
            return;
        }

        if (!_mediaPaused) _mediaElapsed = (_mediaElapsed + Time.unscaledDeltaTime) % MediaFakeDuration;
        float t = _mediaElapsed / MediaFakeDuration;

        string title = CheatState.HudMediaPlayerTitle.Value;
        CustomGui.Text(new Rect(textX, body.yMax - 14f, textW, 16f), string.IsNullOrEmpty(title) ? "—" : title, 12, Color.white, TextAnchor.UpperLeft);

        int elapsed = Mathf.RoundToInt(_mediaElapsed);
        string timeText = $"{elapsed / 60}:{elapsed % 60:00} / {(int)MediaFakeDuration / 60}:{(int)MediaFakeDuration % 60:00}";
        CustomGui.Text(new Rect(textX, body.yMax - artSize + 1f, textW, 14f), timeText, 11, new Color(0.75f, 0.78f, 0.85f, 1f), TextAnchor.UpperLeft);

        // Play/pause as a real triangle/two-bar mesh shape, not a text glyph - plain ASCII ">"/
        // "||" would likely have rendered fine (Arial definitely has those), but a vector shape
        // can never depend on font glyph coverage the way even "safe-looking" characters can.
        Rect playBtn = new(body.x, body.y, 22f, 18f);
        bool hoveredPlay = CustomGui.IsHovered(playBtn);
        CustomGui.Rect(playBtn, hoveredPlay ? new Color(1f, 1f, 1f, 0.18f) : new Color(1f, 1f, 1f, 0.08f), 5f);
        DrawPlayPauseIcon(playBtn, _mediaPaused);
        if (hoveredPlay && Input.GetMouseButtonDown(0)) _mediaPaused = !_mediaPaused;

        Rect barBg = new(playBtn.xMax + 8f, body.y + 6f, body.width - playBtn.width - 13f, 6f);
        CustomGui.Rect(barBg, new Color(1f, 1f, 1f, 0.14f), 3f);
        if (t > 0f) CustomGui.Rect(new Rect(barBg.x, barBg.y, barBg.width * t, barBg.height), accent, 3f);
        CustomGui.Circle(new Vector2(barBg.x + barBg.width * t, barBg.y + barBg.height * 0.5f), 4f, Color.white);
    }

    private static void DrawNoteIcon(Rect area)
    {
        Color c = new(1f, 1f, 1f, 0.85f);
        float headR = area.width * 0.13f;
        float headX = area.center.x - area.width * 0.08f;
        float headY = area.y + area.height * 0.3f;
        CustomGui.Circle(new Vector2(headX, headY), headR, c);
        CustomGui.Rect(new Rect(headX + headR * 0.6f, headY, area.width * 0.07f, area.height * 0.5f), c, 1f);
    }

    private static void DrawPlayPauseIcon(Rect area, bool paused)
    {
        Vector2 c = area.center;
        float s = Mathf.Min(area.width, area.height) * 0.32f;
        if (paused)
        {
            CustomGui.Triangle(new Vector2(c.x - s * 0.6f, c.y + s), new Vector2(c.x - s * 0.6f, c.y - s), new Vector2(c.x + s * 0.85f, c.y), Color.white);
        }
        else
        {
            float barW = s * 0.55f;
            CustomGui.Rect(new Rect(c.x - barW * 1.8f, c.y - s, barW, s * 2f), Color.white, 1f);
            CustomGui.Rect(new Rect(c.x + barW * 0.8f, c.y - s, barW, s * 2f), Color.white, 1f);
        }
    }

    // Two offset triangles pointing the same direction - the standard "skip" chevron-pair shape,
    // for the real prev/next transport buttons.
    private static void DrawSkipIcon(Rect area, bool forward)
    {
        Vector2 c = area.center;
        float s = Mathf.Min(area.width, area.height) * 0.3f;
        float dir = forward ? 1f : -1f;
        float spacing = s * 0.9f;

        for (int i = -1; i <= 0; i++)
        {
            float cx = c.x + dir * spacing * i;
            CustomGui.Triangle(
                new Vector2(cx - dir * s * 0.5f, c.y + s),
                new Vector2(cx - dir * s * 0.5f, c.y - s),
                new Vector2(cx + dir * s * 0.6f, c.y),
                Color.white);
        }
    }

    private static void DrawTransportButton(Rect area, Action drawIcon, Action onClick)
    {
        bool hovered = CustomGui.IsHovered(area);
        CustomGui.Rect(area, hovered ? new Color(1f, 1f, 1f, 0.18f) : new Color(1f, 1f, 1f, 0.08f), 5f);
        drawIcon();
        if (hovered && Input.GetMouseButtonDown(0)) onClick();
    }

    private static void PollSpotify()
    {
        if (Time.unscaledTime < _nextSpotifyPoll) return;
        _nextSpotifyPoll = Time.unscaledTime + 1f;

        string title = Win32.GetSpotifyWindowTitle();
        if (title == null) { _spotifyStatus = "Spotify не запущен"; _spotifyPlaying = false; }
        else if (title == "Spotify" || title.Length == 0) { _spotifyStatus = "Пауза"; _spotifyPlaying = false; }
        else
        {
            // No text-measuring API exposed out of CustomGuiGraphic (only it can query the font),
            // so this is a rough character-count cap rather than a pixel-accurate fit - good
            // enough to stop a long "Artist - Track" title from overflowing the card's width.
            _spotifyStatus = title.Length > 34 ? title[..34] + "…" : title;
            _spotifyPlaying = true;
        }
    }

    // --- Item icon pool (ordinary Image components - see this file's top comment) -------------

    private static readonly List<Image> _iconPool = new();
    private static int _iconUsed;

    private static void ResetIcons() => _iconUsed = 0;

    private static void FinishIcons()
    {
        for (int i = _iconUsed; i < _iconPool.Count; i++)
            _iconPool[i].gameObject.SetActive(false);
    }

    private static void DrawIcon(Rect area, Sprite sprite)
    {
        Image img = _iconUsed < _iconPool.Count ? _iconPool[_iconUsed] : CreateIcon();
        _iconUsed++;

        img.sprite = sprite;
        img.gameObject.SetActive(true);
        RectTransform rt = img.rectTransform;
        rt.anchoredPosition = new Vector2(area.x, area.y);
        rt.sizeDelta = new Vector2(area.width, area.height);
    }

    private static Image CreateIcon()
    {
        var go = new GameObject("HudIcon", typeof(RectTransform));
        go.transform.SetParent(HudRoot.IconLayer, false);

        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        img.rectTransform.pivot = Vector2.zero;
        img.rectTransform.anchorMin = Vector2.zero;
        img.rectTransform.anchorMax = Vector2.zero;

        _iconPool.Add(img);
        return img;
    }
}
