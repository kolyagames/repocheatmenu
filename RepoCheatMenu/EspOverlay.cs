// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace RepoCheatMenu;

// Same idea as RepoHax's esp.cpp, minus their DirectX line-drawing - we just project world
// positions to screen space (Camera.WorldToScreenPoint) and place text labels there on our own
// ScreenSpaceOverlay canvas, which (like OverlayUI) renders every frame via Unity's own
// pipeline regardless of whether our scripts' Update ever fires. Since it's UI drawn without
// any depth test against the 3D scene, it shows through walls for free.
//
// Source lists confirmed from RepoHax's game_classes.h: GameDirector.PlayerList,
// EnemyDirector.enemiesSpawned (List<EnemyParent>, each with .enemy -> Enemy.CenterTransform/
// Health), ValuableDirector.valuableList (List<ValuableObject>, .physGrabObject.midPoint /
// .dollarValueCurrent).
//
// Shader Mode's actual fill (real silhouette, not a box) lives in EspChams.cs - this file just
// collects which targets are in range and hands their root Transforms over to it.
internal static class EspOverlay
{
    private class Marker
    {
        public GameObject Root;
        public Text Label;
        public RectTransform Rect;
    }

    // Player-specific info box: background panel + multi-line text (name/HP/inventory/distance),
    // separate pool from the plain single-line Marker used for enemies/valuables/carts/
    // extraction points since it needs a background Image behind the text.
    private class BoxMarker
    {
        public GameObject Root;
        public RectTransform Rect;
        public Image Background;
        public Text Label;
    }

    private static Canvas _canvas;
    private static readonly List<Marker> _pool = new();
    private static int _used;

    private static readonly List<BoxMarker> _boxPool = new();
    private static int _boxUsed;

    // Reported laggy specifically with Rainbow Mode on, gone the instant it's off - traced to
    // Unity's own Graphic.color setter (confirmed in UGUI source): it no-ops (skips
    // SetVerticesDirty/Canvas rebuild entirely) when the new value bit-equals the old one. A
    // fixed ESP color hits that fast path every single tick for free; a color that's different
    // every frame (Time.time-driven) never does, forcing a real Canvas rebuild on every box
    // marker, every tick (~33/sec) - the more players with ESP up, the worse it gets. Recomputing
    // the color only ~8/sec instead of every tick still reads as a smooth rainbow cycle to the
    // eye while cutting that rebuild frequency by roughly 4x.
    private static Color _cachedRainbowColor = Color.green;
    private static float _nextRainbowUpdate;

    private static Color GetRainbowColor()
    {
        if (Time.unscaledTime >= _nextRainbowUpdate)
        {
            _nextRainbowUpdate = Time.unscaledTime + 0.12f;
            _cachedRainbowColor = Color.HSVToRGB(Mathf.Repeat(Time.time * 0.3f, 1f), 1f, 1f);
        }
        return _cachedRainbowColor;
    }

    // Inventory slots (StatsManager.playerInventorySpot1/2/3, confirmed public
    // Dictionary<string,int> keyed by steamID) store itemName.GetHashCode() - confirmed in the
    // decompiled StatsManager.PlayerInventoryUpdate - not the name itself and not an index, so
    // there's no direct way to turn the stored int back into a display name. What DOES work:
    // every currently-existing ItemAttributes in the scene has its own instanceName (confirmed
    // internal, GameReflect'd below) - hashing THAT and matching against the stored int lets us
    // recover which real item (and its Item.itemName, plus its .icon Sprite - confirmed public,
    // populated via CreateIconFromRenderTexture/LoadSpriteFromFile in ItemAttributes.cs, the same
    // real icon the game's own item-preview UI uses) an inventory slot's hash refers to, as long
    // as that item instance still physically exists somewhere in the level (it does for anything
    // actually equipped - equipping doesn't destroy the object). Rebuilt at most once a second,
    // not every frame - FindObjectsByType over every item in the level isn't cheap enough to
    // justify running it 33/sec.
    public readonly struct InventorySlot
    {
        public readonly string Name;
        public readonly Sprite Icon;
        public InventorySlot(string name, Sprite icon) { Name = name; Icon = icon; }
    }

    private static readonly Dictionary<int, InventorySlot> _itemHashLookup = new();
    private static float _nextItemScan;

    // Public entry point for anything outside the ESP box markers that wants the same resolved
    // slots (PlayerHud's target widget) - triggers the same throttled scan DrawPlayers already
    // relies on, independent of whichever ESP toggles happen to be on.
    internal static InventorySlot[] ResolveInventory(PlayerAvatar avatar)
    {
        RefreshItemHashLookup();
        if (avatar == null) return System.Array.Empty<InventorySlot>();

        StatsManager stats = StatsManager.instance;
        string steamID = GameReflect.Get<string>(avatar, "steamID");
        return new[]
        {
            ResolveSlot(stats?.playerInventorySpot1, steamID),
            ResolveSlot(stats?.playerInventorySpot2, steamID),
            ResolveSlot(stats?.playerInventorySpot3, steamID),
        };
    }

    private static InventorySlot ResolveSlot(Dictionary<string, int> spotDict, string steamID)
    {
        if (spotDict == null || string.IsNullOrEmpty(steamID)) return default;
        if (!spotDict.TryGetValue(steamID, out int hash)) return default;
        return _itemHashLookup.TryGetValue(hash, out InventorySlot slot) ? slot : default;
    }

    public static void Apply()
    {
        bool anyEsp = CheatState.EspPlayers.Value || CheatState.EspEnemies.Value || CheatState.EspValuables.Value || CheatState.EspCarts.Value || CheatState.EspExtractionPoints.Value;
        EnsureBuilt();

        bool inGame = anyEsp && GameState.IsInGame() && !IsGameMenuOpen();
        _canvas.gameObject.SetActive(inGame);
        if (!inGame)
        {
            EspChams.Apply(null, null, null, null);
            return;
        }

        Camera cam = Camera.main;
        _used = 0;
        _boxUsed = 0;

        bool shader = CheatState.EspShaderMode.Value;
        List<Transform> playerRoots = shader && CheatState.EspPlayers.Value ? new List<Transform>() : null;
        List<Transform> enemyRoots = shader && CheatState.EspEnemies.Value ? new List<Transform>() : null;
        List<Transform> valuableRoots = shader && CheatState.EspValuables.Value ? new List<Transform>() : null;
        List<Transform> cartRoots = shader && CheatState.EspCarts.Value ? new List<Transform>() : null;

        if (cam != null)
        {
            if (CheatState.EspPlayers.Value) DrawPlayers(cam, playerRoots);
            if (CheatState.EspEnemies.Value) DrawEnemies(cam, enemyRoots);
            if (CheatState.EspValuables.Value) DrawValuables(cam, valuableRoots);
            if (CheatState.EspCarts.Value) DrawCarts(cam, cartRoots);
            if (CheatState.EspExtractionPoints.Value) DrawExtractionPoints(cam);
        }

        for (int i = _used; i < _pool.Count; i++)
            _pool[i].Root.SetActive(false);
        for (int i = _boxUsed; i < _boxPool.Count; i++)
            _boxPool[i].Root.SetActive(false);

        EspChams.Apply(playerRoots, enemyRoots, valuableRoots, cartRoots);
    }

    private static void DrawCarts(Camera cam, List<Transform> rootsOut)
    {
        foreach (PhysGrabCart cart in CartHack.GetCarts())
        {
            if (cart == null) continue;
            if (Vector3.Distance(cam.transform.position, cart.transform.position) > CheatState.EspMaxDistance.Value) continue;

            Draw(cam, cart.transform.position + Vector3.up, "Cart", Color.cyan);
            rootsOut?.Add(cart.transform);
        }
    }

    private static void RefreshItemHashLookup()
    {
        if (Time.unscaledTime < _nextItemScan) return;
        _nextItemScan = Time.unscaledTime + 1f;

        _itemHashLookup.Clear();
        foreach (ItemAttributes attributes in Object.FindObjectsByType<ItemAttributes>(FindObjectsSortMode.None))
        {
            if (attributes == null || attributes.item == null) continue;
            string instanceName = GameReflect.Get<string>(attributes, "instanceName");
            if (string.IsNullOrEmpty(instanceName)) continue;
            _itemHashLookup[instanceName.GetHashCode()] = new InventorySlot(attributes.item.itemName, attributes.icon);
        }
    }

    private static string ResolveInventorySlotName(Dictionary<string, int> spotDict, string steamID)
    {
        InventorySlot slot = ResolveSlot(spotDict, steamID);
        return string.IsNullOrEmpty(slot.Name) ? "?" : slot.Name;
    }

    private static void DrawPlayers(Camera cam, List<Transform> rootsOut)
    {
        GameDirector director = GameDirector.instance;
        if (director == null) return;

        List<PlayerAvatar> players = GameReflect.Get<List<PlayerAvatar>>(director, "PlayerList");
        if (players == null) return;

        if (CheatState.EspLoadout.Value)
            RefreshItemHashLookup();

        StatsManager stats = StatsManager.instance;

        foreach (PlayerAvatar avatar in players)
        {
            if (avatar == null || avatar == PlayerAvatar.instance) continue;

            // PlayerAvatar.playerTransform is only ever assigned for the LOCAL player
            // (PlayerAvatar.cs: "if (!IsMultiplayer() || photonView.IsMine) playerTransform =
            // PlayerController.instance.transform") - for every other player it's always null,
            // which is why this never drew anything before. avatar.transform (the component's
            // own transform, synced over the network regardless of ownership) is what we want.
            // Dead: the label follows the death head, not the death spot the avatar stays at.
            bool deadNow = GameReflect.Get<bool>(avatar, "deadSet");
            Vector3 at = PlayerTargets.WorldPosition(avatar);
            float dist = Vector3.Distance(cam.transform.position, at);
            if (dist > CheatState.EspMaxDistance.Value) continue;

            Vector3? screen = WorldToScreen(cam, at + Vector3.up * (deadNow ? 0.6f : 2.2f));
            if (screen == null) continue;

            bool dead = GameReflect.Get<bool>(avatar, "deadSet");
            string name = CheatState.StreamerMode.Value ? PlayerTargets.DisplayName(avatar) : GameReflect.Get<string>(avatar, "playerName");

            var sb = new StringBuilder();
            sb.Append(string.IsNullOrEmpty(name) ? "Player" : name);

            if (dead)
            {
                sb.Append("\nDEAD");
            }
            else
            {
                PlayerHealth health = avatar.playerHealth;
                int hp = health != null ? GameReflect.Get<int>(health, "health") : 0;
                sb.Append($"\nHP: {hp}");

                if (CheatState.EspLoadout.Value)
                {
                    string steamID = GameReflect.Get<string>(avatar, "steamID");
                    string slot1 = ResolveInventorySlotName(stats?.playerInventorySpot1, steamID) ?? "-";
                    string slot2 = ResolveInventorySlotName(stats?.playerInventorySpot2, steamID) ?? "-";
                    string slot3 = ResolveInventorySlotName(stats?.playerInventorySpot3, steamID) ?? "-";
                    sb.Append($"\n[{slot1}] [{slot2}] [{slot3}]");

                    PhysGrabber grabber = avatar.physGrabber;
                    bool grabbed = grabber != null && GameReflect.Get<bool>(grabber, "grabbed");
                    PhysGrabObject held = grabbed ? GameReflect.Get<PhysGrabObject>(grabber, "grabbedPhysGrabObject") : null;
                    ItemAttributes heldAttributes = held != null ? held.GetComponent<ItemAttributes>() : null;
                    if (heldAttributes != null && heldAttributes.item != null)
                        sb.Append($"\nHolding: {heldAttributes.item.itemName}");
                }
            }

            sb.Append($"\n{dist:0}m");

            Color color = CheatState.EspRainbow.Value
                ? GetRainbowColor()
                : dead ? new Color(0.6f, 0.6f, 0.6f) : Color.green;

            DrawBox(screen.Value, sb.ToString(), color);
            rootsOut?.Add(avatar.transform);
            // The death head is a sibling of the avatar (PlayerDeathHead.SetupDone reparents it), not a child,
            // so it has to be listed on its own for the chams to show a dead player.
            if (deadNow)
            {
                PlayerDeathHead head = GameReflect.Get<PlayerDeathHead>(avatar, "playerDeathHead");
                if (head != null) rootsOut?.Add(head.transform);
            }
        }
    }

    private static void DrawEnemies(Camera cam, List<Transform> rootsOut)
    {
        EnemyDirector director = EnemyDirector.instance;
        if (director == null) return;

        List<EnemyParent> enemies = GameReflect.Get<List<EnemyParent>>(director, "enemiesSpawned");
        if (enemies == null) return;

        foreach (EnemyParent parent in enemies)
        {
            if (parent == null || !GameReflect.Get<bool>(parent, "Spawned")) continue; // despawned: not on the map

            // Field is "Enemy" (capital E) in the currently installed game build - confirmed live
            // via the debug bridge (2026-09-08): GameReflect.Get<Enemy>(parent, "enemy") (lowercase)
            // silently returned null for every enemy, since GameReflect.Get fails soft (no
            // exception, no log) when the field name doesn't match. That means enemy ESP has been
            // a silent no-op - drawing nothing, never erroring - for however long the game's own
            // field capitalization changed out from under this string. Same class of breakage
            // HealHack.cs already documented for photonView going public->private in an update -
            // add the field name here as ground truth if a future game patch renames it again.
            Enemy enemy = GameReflect.Get<Enemy>(parent, "Enemy");
            if (enemy == null) continue;

            Transform center = GameReflect.Get<Transform>(enemy, "CenterTransform");
            if (center == null) continue;
            if (Vector3.Distance(cam.transform.position, center.position) > CheatState.EspMaxDistance.Value) continue;

            string name = GameReflect.Get<string>(parent, "enemyName");
            Draw(cam, center.position, string.IsNullOrEmpty(name) ? "Enemy" : name, Color.red);
            rootsOut?.Add(parent.transform);
        }
    }

    private static void DrawValuables(Camera cam, List<Transform> rootsOut)
    {
        ValuableDirector director = ValuableDirector.instance;
        if (director == null) return;

        List<ValuableObject> valuables = GameReflect.Get<List<ValuableObject>>(director, "valuableList");
        if (valuables == null) return;

        foreach (ValuableObject valuable in valuables)
        {
            if (valuable == null) continue;

            PhysGrabObject physObject = GameReflect.Get<PhysGrabObject>(valuable, "physGrabObject");
            if (physObject == null) continue;

            Vector3 pos = GameReflect.Get<Vector3>(physObject, "midPoint");
            if (Vector3.Distance(cam.transform.position, pos) > CheatState.EspMaxDistance.Value) continue;

            float value = GameReflect.Get<float>(valuable, "dollarValueCurrent");
            Draw(cam, pos, $"${value:0}", Color.yellow);
            rootsOut?.Add(physObject.transform);
        }
    }

    private static void DrawExtractionPoints(Camera cam)
    {
        RoundDirector director = RoundDirector.instance;
        if (director == null) return;

        List<GameObject> points = GameReflect.Get<List<GameObject>>(director, "extractionPointList");
        if (points == null) return;

        foreach (GameObject go in points)
        {
            if (go == null) continue;
            if (Vector3.Distance(cam.transform.position, go.transform.position) > CheatState.EspMaxDistance.Value) continue;

            ExtractionPoint point = go.GetComponent<ExtractionPoint>();
            string state = point != null ? GameReflect.Get<ExtractionPoint.State>(point, "currentState").ToString() : "?";
            Draw(cam, go.transform.position + Vector3.up * 2f, $"Extraction ({state})", Color.magenta);
        }
    }

    // The ESC pause menu (and other menu pages - inventory, map, etc.) are a UI overlay on top
    // of the still-running level, not a separate GameDirector state, so GameState.IsInGame()
    // alone doesn't know about them. MenuManager.currentMenuState (0 = Open, 1 = Closed per its
    // own MenuState enum) is the same flag every menu page already drives itself from, so this
    // covers any of them generically instead of special-casing MenuPageEsc. Without this, our
    // Canvas (sortingOrder 9000) painted right over the pause menu's own UI.
    internal static bool IsGameMenuOpen()
    {
        MenuManager mgr = MenuManager.instance;
        return mgr != null && GameReflect.Get<int>(mgr, "currentMenuState") == 0;
    }

    // Camera.main renders into a small offscreen RenderTexture (confirmed in the decompiled
    // RenderTextureMain.cs - this game's whole low-res "chunky" look comes from rendering at a
    // fraction of real resolution, then a RawImage stretches that texture to fill the actual
    // screen). WorldToScreenPoint returns coordinates in THAT texture's own resolution, not the
    // real display's - our Canvas is a normal ScreenSpaceOverlay one sized to the real screen, so
    // without rescaling every label landed wherever that point fell in the small texture's pixel
    // grid, nowhere near the actual stretched-out geometry on screen (this is exactly why Shader
    // Mode's silhouettes - which render straight into that same RenderTexture, see EspChams.cs -
    // always lined up fine while these text labels didn't). Returns null if the point is behind
    // the camera or off the real screen.
    private static Vector3? WorldToScreen(Camera cam, Vector3 worldPos)
    {
        Vector3 screen = cam.WorldToScreenPoint(worldPos);
        if (screen.z <= 0) return null;

        if (cam.targetTexture != null)
        {
            screen.x *= (float)Screen.width / cam.targetTexture.width;
            screen.y *= (float)Screen.height / cam.targetTexture.height;
        }

        if (screen.x < 0 || screen.x > Screen.width || screen.y < 0 || screen.y > Screen.height) return null;
        return screen;
    }

    private static void Draw(Camera cam, Vector3 worldPos, string label, Color color)
    {
        if (RockEsp.Active) return; // the Rockstar-style tags (RockEsp) draw the labels instead
        Vector3? screen = WorldToScreen(cam, worldPos);
        if (screen == null) return;

        Marker marker = GetMarker();
        marker.Root.SetActive(true);
        marker.Rect.position = screen.Value;
        marker.Label.text = label;
        marker.Label.color = color;
    }

    private static void DrawBox(Vector3 screen, string text, Color color)
    {
        if (RockEsp.Active) return;
        BoxMarker marker = GetBoxMarker();
        marker.Root.SetActive(true);
        marker.Rect.position = screen;
        marker.Label.text = text;
        marker.Label.color = Color.white;
        marker.Background.color = new Color(color.r, color.g, color.b, 0.35f);
        marker.Background.rectTransform.sizeDelta = new Vector2(180f, 22f * (text.Split('\n').Length + 0.3f));
    }

    private static Marker GetMarker()
    {
        if (_used < _pool.Count) return _pool[_used++];

        var go = new GameObject("EspMarker", typeof(RectTransform));
        go.transform.SetParent(_canvas.transform, false);

        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(200, 20);

        var text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = 14;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        var outline = go.AddComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(1, -1);

        var marker = new Marker { Root = go, Label = text, Rect = rt };
        _pool.Add(marker);
        _used++;
        return marker;
    }

    private static BoxMarker GetBoxMarker()
    {
        if (_boxUsed < _boxPool.Count) return _boxPool[_boxUsed++];

        var go = new GameObject("EspBox", typeof(RectTransform));
        go.transform.SetParent(_canvas.transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(180, 80);

        var bgGo = new GameObject("Background", typeof(RectTransform));
        bgGo.transform.SetParent(go.transform, false);
        var background = bgGo.AddComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.35f);
        var bgRect = background.rectTransform;
        bgRect.anchorMin = new Vector2(0.5f, 0.5f);
        bgRect.anchorMax = new Vector2(0.5f, 0.5f);
        bgRect.pivot = new Vector2(0.5f, 0.5f);

        var textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var text = textGo.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = 13;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        var textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;

        var outline = textGo.AddComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(1, -1);

        var marker = new BoxMarker { Root = go, Rect = rt, Background = background, Label = text };
        _boxPool.Add(marker);
        _boxUsed++;
        return marker;
    }

    private static void EnsureBuilt()
    {
        if (_canvas != null) return;

        var go = new GameObject("RepoCheatMenu_Esp");
        Object.DontDestroyOnLoad(go);
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 9000;
        go.AddComponent<CanvasScaler>();
        _canvas.gameObject.SetActive(false);
    }
}
