// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace RepoCheatMenu;

// Pings between friends who all run this mod: press the bound key and a marker appears where you are
// looking - for you and for every other player in the room who has the mod - with your name and the
// distance to it, for PingSeconds. What the ray hits decides the kind: a valuable becomes "Лут $value",
// an enemy "Опасность: <name>", anything else "Сюда".
//
// Transport: a custom Photon event (EventCode, inside the 0..199 range PhotonNetwork.RaiseEvent accepts for
// application events; the game itself only uses 123, 124 and 199) sent to everyone else in the room. Players
// without the mod receive an event nobody listens to and nothing happens on their side. Incoming events are
// read in the same deferred PhotonNetwork.OnEvent prefix as the anti-crash/event log (Patches.cs), checked
// for shape and NaN, and rate-limited per sender so nobody can flood the screen.
internal static class PingSystem
{
    public const byte EventCode = 87;
    private const float RayLength = 80f;
    private const int MaxActive = 12;

    private enum Kind { Here = 0, Danger = 1, Loot = 2 }

    private sealed class Ping
    {
        public Vector3 Pos;
        public Kind Kind;
        public string Label;
        public string From;
        public Color Color;
        public float Born;
    }

    private static readonly List<Ping> Active = new();
    private static readonly Dictionary<int, Queue<float>> RecentBySender = new();
    private static float _nextLocalPing;

    // --- sending -------------------------------------------------------------------------------------

    public static void PingHere()
    {
        if (!CheatState.Pings.Value) return;
        if (Time.unscaledTime < _nextLocalPing) return;
        _nextLocalPing = Time.unscaledTime + 0.4f;

        Camera cam = Camera.main;
        if (cam == null || PlayerTargets.Local == null) return;

        int mask = LayerMask.GetMask("Default", "PhysGrabObject", "PhysGrabObjectCart", "PhysGrabObjectHinge", "Enemy");
        Vector3 origin = cam.transform.position + cam.transform.forward * 0.3f;
        if (!Physics.Raycast(origin, cam.transform.forward, out RaycastHit hit, RayLength, mask, QueryTriggerInteraction.Ignore))
        {
            DynamicIsland.Notify("Пинг", "Слишком далеко - смотрите на поверхность ближе 80 м", false);
            return;
        }

        Kind kind = Kind.Here;
        string label = "Сюда";
        ValuableObject valuable = hit.collider.GetComponentInParent<ValuableObject>();
        EnemyParent enemy = hit.collider.GetComponentInParent<EnemyParent>();
        if (valuable != null)
        {
            kind = Kind.Loot;
            label = "Лут $" + Mathf.RoundToInt(GameReflect.Get<float>(valuable, "dollarValueCurrent"));
        }
        else if (enemy != null)
        {
            kind = Kind.Danger;
            string name = GameReflect.Get<string>(enemy, "enemyName");
            label = "Опасность" + (string.IsNullOrEmpty(name) ? "" : ": " + name);
        }

        Vector3 p = hit.point + hit.normal * 0.15f;
        Add(p, kind, label, "Вы", OwnColor());

        if (!GameManager.Multiplayer() || !PhotonNetwork.InRoom) return;
        // A ping from nobody in the player list would give the ghost away to anyone running this mod.
        if (GhostObserverHack.SessionGhost) return;
        object[] content = { p.x, p.y, p.z, (int)kind, label };
        PhotonNetwork.RaiseEvent(EventCode, content,
            new RaiseEventOptions { Receivers = ReceiverGroup.Others }, SendOptions.SendReliable);
    }

    public static void ClearAll() => Active.Clear();

    // --- receiving -----------------------------------------------------------------------------------

    // From the deferred PhotonNetwork.OnEvent prefix. Never throws, never blocks the event.
    public static void OnEvent(EventData e)
    {
        try
        {
            if (e == null || e.Code != EventCode || !CheatState.Pings.Value) return;
            if (!(e.CustomData is object[] data) || data.Length < 5) return;
            if (!(data[0] is float x) || !(data[1] is float y) || !(data[2] is float z) || !(data[3] is int k)) return;
            if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z) || Mathf.Abs(x) > 10000f || Mathf.Abs(y) > 10000f || Mathf.Abs(z) > 10000f) return;
            if (!AllowFrom(e.Sender)) return;

            string label = data[4] as string ?? "Сюда";
            if (label.Length > 40) label = label.Substring(0, 40);
            label = AntiCrashTextSanitize.Strip(label);
            Kind kind = k >= 0 && k <= 2 ? (Kind)k : Kind.Here;

            PlayerAvatar from = AvatarOf(e.Sender);
            string name = from != null ? PlayerTargets.DisplayName(from)
                : CheatState.StreamerMode.Value ? "Игрок" : PhotonNetwork.CurrentRoom?.GetPlayer(e.Sender)?.NickName ?? "Игрок";
            Color color = from != null ? (CosmeticHack.GetSyncedEyeColor(from) ?? KindColor(kind)) : KindColor(kind);
            Add(new Vector3(x, y, z), kind, label, name, color);
        }
        catch
        {
            // a malformed ping is simply not shown
        }
    }

    // At most 3 pings per sender per 2 seconds.
    private static bool AllowFrom(int actor)
    {
        if (!RecentBySender.TryGetValue(actor, out Queue<float> q)) RecentBySender[actor] = q = new Queue<float>();
        float now = Time.unscaledTime;
        while (q.Count > 0 && now - q.Peek() > 2f) q.Dequeue();
        if (q.Count >= 3) return false;
        q.Enqueue(now);
        return true;
    }

    private static PlayerAvatar AvatarOf(int actor)
    {
        GameDirector director = GameDirector.instance;
        List<PlayerAvatar> list = director != null ? GameReflect.Get<List<PlayerAvatar>>(director, "PlayerList") : null;
        if (list == null) return null;
        foreach (PlayerAvatar p in list)
        {
            if (p == null) continue;
            PhotonView view = GameReflect.Get<PhotonView>(p, "photonView");
            if (view != null && view.OwnerActorNr == actor) return p;
        }
        return null;
    }

    private static Color OwnColor()
    {
        PlayerAvatar me = PlayerTargets.Local;
        return me != null ? (CosmeticHack.GetSyncedEyeColor(me) ?? RockGui.Accent) : RockGui.Accent;
    }

    private static Color KindColor(Kind k) => k switch
    {
        Kind.Danger => new Color(1f, 0.25f, 0.2f),
        Kind.Loot => new Color(1f, 0.8f, 0.1f),
        _ => RockGui.Accent,
    };

    private static void Add(Vector3 pos, Kind kind, string label, string from, Color color)
    {
        Active.Add(new Ping { Pos = pos, Kind = kind, Label = label, From = from, Color = color, Born = Time.unscaledTime });
        if (Active.Count > MaxActive) Active.RemoveAt(0);
    }

    // --- drawing (HudRoot, every frame) -------------------------------------------------------------

    public static void Draw()
    {
        if (Active.Count == 0) return;
        float life = Mathf.Clamp(CheatState.PingSeconds.Value, 2f, 30f);
        float now = Time.unscaledTime;
        Active.RemoveAll(p => now - p.Born > life);
        if (Active.Count == 0 || !CheatState.Pings.Value) return;

        Camera cam = Camera.main;
        PlayerAvatar me = PlayerTargets.Local;
        if (cam == null) return;

        float u = Mathf.Max(0.5f, CheatState.HudScale.Value);
        float sw = CustomGui.ScreenWidth, sh = CustomGui.ScreenHeight;
        float margin = 18f * u;

        foreach (Ping p in Active)
        {
            float age = now - p.Born;
            float alpha = Mathf.Clamp01((life - age) / 1f) * Mathf.Clamp01(age / 0.15f);
            if (!ScreenPos(cam, p.Pos, out Vector2 pos, out bool behind)) continue;

            bool offscreen = behind || pos.x < margin || pos.x > sw - margin || pos.y < margin || pos.y > sh - margin;
            pos.x = Mathf.Clamp(pos.x, margin, sw - margin);
            pos.y = Mathf.Clamp(pos.y, margin, sh - margin);

            Color c = p.Color;
            float pulse = 1f + 0.15f * Mathf.Sin(age * 6f);
            float r = (offscreen ? 3.5f : 5f) * u * pulse;
            CustomGui.Circle(pos, r * 1.9f, new Color(c.r, c.g, c.b, 0.18f * alpha));
            CustomGui.RoundedRing(new Rect(pos.x - r, pos.y - r, r * 2f, r * 2f), r, Mathf.Max(1f, 0.9f * u), new Color(c.r, c.g, c.b, 0.95f * alpha));
            CustomGui.Circle(pos, r * 0.35f, new Color(c.r, c.g, c.b, alpha));

            float dist = me != null ? Vector3.Distance(me.transform.position, p.Pos) : 0f;
            string text = $"{p.From}: {p.Label}  {dist:0} м";
            int font = Mathf.RoundToInt(6.5f * u);
            float tw = CustomGui.MeasureText(text, font) + 8f * u;
            float tx = Mathf.Clamp(pos.x - tw * 0.5f, 4f, sw - tw - 4f);
            Rect box = new(tx, pos.y + r + 3f * u, tw, 9f * u);
            if (box.yMax > sh - 4f) box.y = pos.y - r - 3f * u - box.height;
            CustomGui.Rect(box, new Color(0f, 0f, 0f, 0.55f * alpha), 3f * u);
            CustomGui.Text(box, text, font, new Color(1f, 1f, 1f, alpha), TextAnchor.MiddleCenter);
        }
    }

    // Like GlowFx.Project, but also for points behind the camera (mirrored, so they clamp to the right edge).
    private static bool ScreenPos(Camera cam, Vector3 world, out Vector2 canvas, out bool behind)
    {
        canvas = default;
        behind = false;
        float cw = CustomGui.ScreenWidth, ch = CustomGui.ScreenHeight;
        if (cw <= 0f) return false;
        Vector3 s = cam.WorldToScreenPoint(world);
        if (cam.targetTexture != null)
        {
            s.x *= (float)Screen.width / cam.targetTexture.width;
            s.y *= (float)Screen.height / cam.targetTexture.height;
        }
        if (s.z < 0f)
        {
            behind = true;
            s.x = Screen.width - s.x;
            s.y = Screen.height - s.y;
        }
        canvas = new Vector2(s.x / Screen.width * cw, s.y / Screen.height * ch);
        return true;
    }
}
