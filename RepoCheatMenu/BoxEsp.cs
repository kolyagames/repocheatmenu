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

// Rockstar's Box ESP (BoxEspFeature) for R.E.P.O.: a 3D box around every target, edges plus a translucent fill.
// Rockstar draws it in world space with additive blending through walls; here the eight corners of the target's
// world bounds are projected and drawn on the CustomGui overlay (so it is through walls too). Modes follow
// Rockstar's multi-select: fill + outline (fill 8 %), outline only, fill only (fill 28 %). Players use their avatar colour, enemies red, valuables gold.
// Bounds come from the target's renderers (GlowFx.BoundsOf) and are refreshed four times a second; between
// refreshes the box just follows the target's transform.
internal static class BoxEsp
{
    private sealed class Entry
    {
        public Bounds Bounds;
        public Vector3 Anchor;
        public float NextRefresh;
        public float Seen;
    }

    private static readonly Dictionary<int, Entry> Cache = new();
    private static readonly List<int> Stale = new();
    private static readonly Vector3[] Corner = new Vector3[8];
    private static readonly Vector2[] Pt = new Vector2[8];
    private static readonly int[,] Edges =
    {
        { 0, 1 }, { 1, 3 }, { 3, 2 }, { 2, 0 },
        { 4, 5 }, { 5, 7 }, { 7, 6 }, { 6, 4 },
        { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 },
    };
    private static readonly int[,] Faces =
    {
        { 0, 1, 3, 2 }, { 4, 5, 7, 6 }, { 0, 1, 5, 4 }, { 2, 3, 7, 6 }, { 0, 2, 6, 4 }, { 1, 3, 7, 5 },
    };
    private static readonly Color Gold = new(1f, 0.85f, 0.25f);
    private static readonly Color EnemyRed = new(1f, 0.33f, 0.33f);

    public static void Draw()
    {
        if (!CheatState.EspBoxes.Value || !GameState.IsInGame() || EspOverlay.IsGameMenuOpen())
        {
            if (Cache.Count > 0) Cache.Clear();
            return;
        }

        Camera cam = Camera.main;
        if (cam == null || CustomGui.ScreenWidth <= 0f) return;

        float range = CheatState.EspMaxDistance.Value;
        if (CheatState.EspPlayers.Value) Players(cam, range);
        if (CheatState.EspEnemies.Value) Enemies(cam, range);
        if (CheatState.EspValuables.Value) Valuables(cam, range);

        float now = Time.unscaledTime;
        Stale.Clear();
        foreach (KeyValuePair<int, Entry> kv in Cache)
            if (now - kv.Value.Seen > 2f) Stale.Add(kv.Key);
        foreach (int id in Stale) Cache.Remove(id);
    }

    private static void Players(Camera cam, float range)
    {
        GameDirector gd = GameDirector.instance;
        List<PlayerAvatar> list = gd != null ? GameReflect.Get<List<PlayerAvatar>>(gd, "PlayerList") : null;
        if (list == null) return;

        foreach (PlayerAvatar p in list)
        {
            if (p == null || p == PlayerAvatar.instance || GameReflect.Get<bool>(p, "deadSet")) continue;
            Color col;
            try { col = SemiFunc.PlayerGetColorMain(p); }
            catch { col = Color.white; }
            One(cam, range, p.GetInstanceID(), p.gameObject, col);
        }
    }

    private static void Enemies(Camera cam, float range)
    {
        EnemyDirector dir = EnemyDirector.instance;
        List<EnemyParent> list = dir != null ? GameReflect.Get<List<EnemyParent>>(dir, "enemiesSpawned") : null;
        if (list == null) return;

        foreach (EnemyParent parent in list)
        {
            if (parent == null || !GameReflect.Get<bool>(parent, "Spawned")) continue;
            Enemy enemy = GameReflect.Get<Enemy>(parent, "Enemy");
            EnemyHealth health = enemy != null ? GameReflect.Get<EnemyHealth>(enemy, "Health") : null;
            if (health != null && GameReflect.Get<bool>(health, "dead")) continue;
            One(cam, range, parent.GetInstanceID(), parent.gameObject, EnemyRed);
        }
    }

    private static void Valuables(Camera cam, float range)
    {
        ValuableDirector dir = ValuableDirector.instance;
        List<ValuableObject> list = dir != null ? GameReflect.Get<List<ValuableObject>>(dir, "valuableList") : null;
        if (list == null) return;

        foreach (ValuableObject v in list)
        {
            if (v == null) continue;
            One(cam, range, v.GetInstanceID(), v.gameObject, Gold);
        }
    }

    private static void One(Camera cam, float range, int id, GameObject go, Color colour)
    {
        if (!GlowFx.HasRoom) return;
        float now = Time.unscaledTime;
        if ((go.transform.position - cam.transform.position).sqrMagnitude > range * range) return;

        if (!Cache.TryGetValue(id, out Entry e))
        {
            e = new Entry { NextRefresh = -1f };
            Cache[id] = e;
        }
        e.Seen = now;
        if (now >= e.NextRefresh)
        {
            e.Bounds = GlowFx.BoundsOf(go);
            e.Anchor = go.transform.position;
            e.NextRefresh = now + 0.25f;
        }

        Bounds b = e.Bounds;
        Vector3 c = b.center + (go.transform.position - e.Anchor);
        Vector3 h = b.extents;
        for (int i = 0; i < 8; i++)
        {
            Corner[i] = c + new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 4) == 0 ? -h.y : h.y, (i & 2) == 0 ? -h.z : h.z);
            if (!GlowFx.Project(cam, Corner[i], out Pt[i], out _)) return; // partly behind the camera: skip the box
        }

        string mode = CheatState.EspBoxMode.Value;
        bool fill = mode != "Контур";
        bool outline = mode != "Заливка";

        if (fill)
        {
            float fa = outline ? 0.08f : 0.28f;
            Color fc = new(colour.r, colour.g, colour.b, fa);
            for (int f = 0; f < 6; f++)
            {
                CustomGui.Triangle(Pt[Faces[f, 0]], Pt[Faces[f, 1]], Pt[Faces[f, 2]], fc);
                CustomGui.Triangle(Pt[Faces[f, 0]], Pt[Faces[f, 2]], Pt[Faces[f, 3]], fc);
            }
        }

        if (outline)
        {
            float w = Mathf.Max(0.8f, CheatState.HudScale.Value * 0.55f);
            Color glow = new(colour.r, colour.g, colour.b, 0.18f);
            Color line = new(colour.r, colour.g, colour.b, 0.95f);
            for (int i = 0; i < 12; i++)
            {
                Line(Pt[Edges[i, 0]], Pt[Edges[i, 1]], w * 3.2f, glow);
                Line(Pt[Edges[i, 0]], Pt[Edges[i, 1]], w, line);
            }
        }
    }

    private static void Line(Vector2 a, Vector2 b, float width, Color c)
    {
        Vector2 d = b - a;
        float len = d.magnitude;
        if (len < 0.01f) return;
        Vector2 n = new Vector2(-d.y, d.x) / len * (width * 0.5f);
        CustomGui.Triangle(a - n, a + n, b + n, c);
        CustomGui.Triangle(a - n, b + n, b - n, c);
    }
}
