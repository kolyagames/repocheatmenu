// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// Часть дизайна, чисел и ресурсов здесь перенесена из клиента Rockstar;
// эти части принадлежат их авторам.
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace RepoCheatMenu;

// Rockstar's Arrow ESP (ArrowEspFeature) for R.E.P.O.: a small arrow on a ring around the screen centre for
// every player / enemy that is out of view, pointing the way to turn. Rockstar draws a 35 px texture with
// additive blending, rotated by (angle to the target - camera yaw) and faded in/out over 300-500 ms; here the
// arrow is a triangle plus a faint halo copy on the CustomGui overlay, and the fade/rotation are smoothed per
// target the same way. Players use their avatar colour, enemies are red; targets on screen get no arrow
// unless EspArrowsOnScreen is on ("hide_on_screen" in Rockstar).
internal static class ArrowEsp
{
    private sealed class Arrow
    {
        public float Alpha;
        public float Angle;
        public bool Seen;
        public Color Color;
        public bool HasAngle;
    }

    private const float Range = 80f;
    private static readonly Dictionary<int, Arrow> Arrows = new();
    private static readonly List<int> Gone = new();
    private static readonly Color EnemyRed = new(1f, 0.33f, 0.33f);

    public static void Draw()
    {
        if (!CheatState.EspArrows.Value || !GameState.IsInGame() || EspOverlay.IsGameMenuOpen())
        {
            if (Arrows.Count > 0) Arrows.Clear();
            return;
        }

        Camera cam = Camera.main;
        if (cam == null || CustomGui.ScreenWidth <= 0f) return;

        foreach (Arrow a in Arrows.Values) a.Seen = false;

        if (CheatState.EspPlayers.Value) Players(cam);
        if (CheatState.EspEnemies.Value) Enemies(cam);

        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        Gone.Clear();
        foreach (KeyValuePair<int, Arrow> kv in Arrows)
        {
            Arrow a = kv.Value;
            a.Alpha = Mathf.MoveTowards(a.Alpha, a.Seen ? 1f : 0f, dt / 0.4f);
            if (!a.Seen && a.Alpha <= 0f) Gone.Add(kv.Key);
        }
        foreach (int id in Gone) Arrows.Remove(id);

        if (!GlowFx.HasRoom) return;
        float cw = CustomGui.ScreenWidth, ch = CustomGui.ScreenHeight;
        float u = Mathf.Max(0.5f, CheatState.HudScale.Value);
        float ring = Mathf.Min(cw, ch) * 0.27f;
        Vector2 centre = new(cw * 0.5f, ch * 0.5f);

        foreach (Arrow a in Arrows.Values)
        {
            if (a.Alpha <= 0.01f || !a.HasAngle) continue;
            Vector2 dir = new(Mathf.Sin(a.Angle), Mathf.Cos(a.Angle)); // 0 = ahead = up the screen
            Vector2 at = centre + dir * ring;
            Vector2 side = new(dir.y, -dir.x);
            float len = 8f * u, half = 5f * u;

            Vector2 tip = at + dir * len * 0.6f;
            Vector2 bl = at - dir * len * 0.4f + side * half;
            Vector2 br = at - dir * len * 0.4f - side * half;
            Vector2 notch = at - dir * len * 0.1f;

            Color c = a.Color;
            float al = a.Alpha;
            // Halo: the same arrow, larger and faint (Rockstar's additive bloom).
            Vector2 hTip = at + dir * len * 0.95f;
            Vector2 hl = at - dir * len * 0.75f + side * half * 1.7f;
            Vector2 hr = at - dir * len * 0.75f - side * half * 1.7f;
            CustomGui.Triangle(hTip, hl, hr, new Color(c.r, c.g, c.b, al * 0.16f));

            CustomGui.Triangle(tip, bl, notch, new Color(c.r, c.g, c.b, al * 0.95f));
            CustomGui.Triangle(tip, notch, br, new Color(c.r * 0.82f, c.g * 0.82f, c.b * 0.82f, al * 0.95f));
        }
    }

    // --- sources --------------------------------------------------------------------------------------------

    private static void Players(Camera cam)
    {
        GameDirector gd = GameDirector.instance;
        List<PlayerAvatar> list = gd != null ? GameReflect.Get<List<PlayerAvatar>>(gd, "PlayerList") : null;
        if (list == null) return;

        foreach (PlayerAvatar p in list)
        {
            if (p == null || p == PlayerAvatar.instance) continue;
            if (GameReflect.Get<bool>(p, "deadSet")) continue;
            Color col;
            try { col = SemiFunc.PlayerGetColorMain(p); }
            catch { col = Color.white; }
            Track(cam, p.GetInstanceID(), PlayerTargets.WorldPosition(p) + Vector3.up * 1f, col);
        }
    }

    private static void Enemies(Camera cam)
    {
        EnemyDirector dir = EnemyDirector.instance;
        List<EnemyParent> list = dir != null ? GameReflect.Get<List<EnemyParent>>(dir, "enemiesSpawned") : null;
        if (list == null) return;

        foreach (EnemyParent parent in list)
        {
            if (parent == null || !GameReflect.Get<bool>(parent, "Spawned")) continue;
            Enemy enemy = GameReflect.Get<Enemy>(parent, "Enemy");
            Transform centre = enemy != null ? GameReflect.Get<Transform>(enemy, "CenterTransform") : null;
            if (centre == null) continue;
            EnemyHealth health = GameReflect.Get<EnemyHealth>(enemy, "Health");
            if (health != null && GameReflect.Get<bool>(health, "dead")) continue;
            Track(cam, parent.GetInstanceID(), centre.position, EnemyRed);
        }
    }

    private static void Track(Camera cam, int id, Vector3 world, Color colour)
    {
        Vector3 d = world - cam.transform.position;
        if (d.magnitude > Range) return;

        Vector3 vp = cam.WorldToViewportPoint(world);
        bool onScreen = vp.z > 0f && vp.x > 0f && vp.x < 1f && vp.y > 0f && vp.y < 1f;
        if (onScreen && !CheatState.EspArrowsOnScreen.Value) return;

        // Angle from "straight ahead" on the horizontal plane, clockwise: the camera's own right/forward.
        Vector3 fwd = cam.transform.forward; fwd.y = 0f;
        Vector3 right = cam.transform.right; right.y = 0f;
        Vector3 flat = d; flat.y = 0f;
        if (flat.sqrMagnitude < 0.01f || fwd.sqrMagnitude < 0.0001f) return;
        float target = Mathf.Atan2(Vector3.Dot(flat, right.normalized), Vector3.Dot(flat, fwd.normalized));

        if (!Arrows.TryGetValue(id, out Arrow a))
        {
            a = new Arrow { Angle = target, HasAngle = true };
            Arrows[id] = a;
        }
        a.Seen = true;
        a.Color = colour;
        // Shortest way round, smoothed.
        float diff = Mathf.DeltaAngle(a.Angle * Mathf.Rad2Deg, target * Mathf.Rad2Deg) * Mathf.Deg2Rad;
        a.Angle += diff * Mathf.Clamp01(Time.unscaledDeltaTime * 14f);
    }
}
