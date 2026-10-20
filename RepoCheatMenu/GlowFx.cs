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

// Rockstar's world effects (kill particles, the target marker) ported to R.E.P.O.: it draws additive
// "bloom" sprites in 3D, here every glow is a stack of translucent discs drawn on the 2D overlay at the
// projected position, which is all CustomGui can do (no additive blending, no depth test). The numbers -
// particle counts, speeds, lifetimes, ring radii, fade curves - are Rockstar's own.

internal static class GlowFx
{
    // Meshes above this many vertices stop taking effects: one UI mesh holds at most 65535 vertices and the
    // HUD and menu share it.
    private const int VertexBudget = 52000;

    public static bool HasRoom => CustomGui.RectVertexCount < VertexBudget;

    public static Color FxColor()
    {
        if (CheatState.FxUseTheme.Value) return RockGui.Accent;
        return ColorUtility.TryParseHtmlString(CheatState.FxColor.Value, out Color c) ? c : RockGui.Accent;
    }

    // World point -> overlay pixels (CustomGui space). The game camera renders into a small offscreen texture
    // that is stretched over the screen, so WorldToScreenPoint has to be rescaled first (see EspOverlay).
    public static bool Project(Camera cam, Vector3 world, out Vector2 canvas, out float pxPerUnit)
    {
        canvas = default;
        pxPerUnit = 0f;
        float cw = CustomGui.ScreenWidth, ch = CustomGui.ScreenHeight;
        if (cam == null || cw <= 0f) return false;

        Vector3 s = cam.WorldToScreenPoint(world);
        if (s.z <= 0.05f) return false;
        if (cam.targetTexture != null)
        {
            s.x *= (float)Screen.width / cam.targetTexture.width;
            s.y *= (float)Screen.height / cam.targetTexture.height;
        }

        canvas = new Vector2(s.x / Screen.width * cw, s.y / Screen.height * ch);
        pxPerUnit = ch / (2f * Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad * 0.5f) * s.z);
        return canvas.x > -300f && canvas.x < cw + 300f && canvas.y > -300f && canvas.y < ch + 300f;
    }

    // A glow point of `worldRadius` metres: wide faint halo, mid ring, bright core.
    public static void Glow(Vector2 c, float worldRadius, float pxPerUnit, Color color, float alpha, bool halo = true)
    {
        if (alpha <= 0.004f) return;
        float r = Mathf.Max(1.2f, worldRadius * pxPerUnit);
        if (halo)
        {
            CustomGui.Disc(c, r, new Color(color.r, color.g, color.b, alpha * 0.16f), 10);
            CustomGui.Disc(c, r * 0.55f, new Color(color.r, color.g, color.b, alpha * 0.35f), 10);
        }
        CustomGui.Disc(c, Mathf.Max(1f, r * 0.22f), new Color(color.r, color.g, color.b, Mathf.Min(1f, alpha)), 8);
    }

    public static Bounds BoundsOf(GameObject root)
    {
        Bounds b = default;
        bool any = false;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        if (!any) b = new Bounds(root.transform.position + Vector3.up, new Vector3(0.8f, 1.8f, 0.8f));
        return b;
    }
}

// Kill effects. Port of Rockstar's KillEffects module: on a kill it either bursts ~230 glow particles
// shaped like the body (head cloud, torso, two arms, two legs; scatter = slow drift, gravity = fall and
// bounce on the floor) or raises a lightning column of 200 glow points that fades in and out.
internal static class KillFx
{
    private sealed class Particle
    {
        public Vector3 Pos, Vel;
        public float Born, Life;
        public float Gravity, Friction;
        public bool Falls;
    }

    private sealed class Bolt
    {
        public Vector3[] Points;
        public float Y0, Born;
        public readonly HudKit.Tween Anim = new(500f, HudKit.InOut);
        public bool Rising = true;
    }

    private static readonly List<Particle> Particles = new();
    private static readonly List<Bolt> Bolts = new();
    private static readonly Dictionary<int, bool> EnemyDead = new();
    private static readonly Dictionary<int, bool> PlayerDead = new();
    private static float _nextScan;
    private const int MaxParticles = 700;

    public static void Draw()
    {
        if (!CheatState.KillFx.Value)
        {
            if (Particles.Count > 0 || Bolts.Count > 0) { Particles.Clear(); Bolts.Clear(); }
            EnemyDead.Clear();
            PlayerDead.Clear();
            return;
        }

        if (Time.unscaledTime >= _nextScan)
        {
            _nextScan = Time.unscaledTime + 0.1f;
            Scan();
        }

        Camera cam = Camera.main;
        if (cam == null) return;
        Color color = GlowFx.FxColor();
        float dtTicks = Mathf.Min(5f, Time.unscaledDeltaTime * 60f);

        for (int i = Particles.Count - 1; i >= 0; i--)
        {
            Particle p = Particles[i];
            float age = Time.unscaledTime - p.Born;
            if (age >= p.Life) { Particles.RemoveAt(i); continue; }

            if (p.Falls) p.Vel.y -= p.Gravity * dtTicks;
            p.Vel *= Mathf.Pow(p.Friction, dtTicks);
            Vector3 next = p.Pos + p.Vel * dtTicks;

            if (p.Falls)
            {
                // The floor: bounce at half speed, like Rockstar's block test. One short ray down per particle.
                if (p.Vel.y < 0f && Physics.Raycast(p.Pos, Vector3.down, out RaycastHit hit, 0.08f - p.Vel.y * dtTicks, ~0, QueryTriggerInteraction.Ignore))
                {
                    p.Vel.y = -p.Vel.y * 0.5f;
                    next.y = p.Pos.y;
                }
                if (Mathf.Abs(p.Vel.y) < 1e-4f) { p.Vel.x = 0f; p.Vel.z = 0f; }
            }
            p.Pos = next;

            if (!GlowFx.HasRoom || !GlowFx.Project(cam, p.Pos, out Vector2 c, out float ppu)) continue;
            float fade = 1f - age / p.Life;
            // Rockstar: a 0.1 core at 0.9 alpha and a 0.5 halo at 0.15 alpha.
            CustomGui.Disc(c, Mathf.Max(0.9f, 0.05f * ppu), new Color(color.r, color.g, color.b, 0.9f * fade), 6);
            CustomGui.Disc(c, Mathf.Max(1.5f, 0.25f * ppu), new Color(color.r, color.g, color.b, 0.12f * fade), 8);
        }

        for (int i = Bolts.Count - 1; i >= 0; i--)
        {
            Bolt b = Bolts[i];
            float a = b.Anim.Set(b.Rising);
            if (b.Rising && a >= 0.999f) b.Rising = false;
            if (!b.Rising && a <= 0.002f) { Bolts.RemoveAt(i); continue; }

            foreach (Vector3 pt in b.Points)
            {
                if (!GlowFx.HasRoom) break;
                if (!GlowFx.Project(cam, pt, out Vector2 c, out float ppu)) continue;
                float size = 2f + 5f * (pt.y - b.Y0) / 50f;
                float r = Mathf.Min(size * 0.5f * ppu, 260f);
                CustomGui.Disc(c, r, new Color(color.r, color.g, color.b, 0.4f * a * 0.07f), 10);
                CustomGui.Disc(c, r * 0.3f, new Color(color.r, color.g, color.b, 0.4f * a * 0.2f), 8);
            }
        }
    }

    // --- detection -----------------------------------------------------------------------------------------

    private static void Scan()
    {
        EnemyDirector director = EnemyDirector.instance;
        List<EnemyParent> enemies = director != null ? GameReflect.Get<List<EnemyParent>>(director, "enemiesSpawned") : null;
        if (enemies != null)
        {
            foreach (EnemyParent parent in enemies)
            {
                if (parent == null) continue;
                Enemy enemy = GameReflect.Get<Enemy>(parent, "Enemy");
                EnemyHealth health = enemy != null ? GameReflect.Get<EnemyHealth>(enemy, "Health") : null;
                if (health == null) continue;

                bool dead = GameReflect.Get<bool>(health, "dead");
                int id = parent.GetInstanceID();
                if (EnemyDead.TryGetValue(id, out bool wasDead) && !wasDead && dead)
                    SpawnAt(GlowFx.BoundsOf(parent.gameObject), parent.transform.eulerAngles.y);
                EnemyDead[id] = dead;
            }
        }

        if (!CheatState.KillFxPlayers.Value) { PlayerDead.Clear(); return; }
        GameDirector gd = GameDirector.instance;
        List<PlayerAvatar> players = gd != null ? GameReflect.Get<List<PlayerAvatar>>(gd, "PlayerList") : null;
        if (players == null) return;
        foreach (PlayerAvatar p in players)
        {
            if (p == null) continue;
            bool dead = GameReflect.Get<bool>(p, "deadSet");
            int id = p.GetInstanceID();
            if (PlayerDead.TryGetValue(id, out bool wasDead) && !wasDead && dead)
            {
                Vector3 pos = p.transform.position;
                SpawnAt(new Bounds(pos + Vector3.up * 0.3f, new Vector3(0.6f, 1.7f, 0.6f)), p.transform.eulerAngles.y);
            }
            PlayerDead[id] = dead;
        }
    }

    // --- spawning (Rockstar's KillEffectsModule, same shapes and counts) ------------------------------------

    private static void SpawnAt(Bounds bounds, float yawDegrees)
    {
        Vector3 feet = new(bounds.center.x, bounds.min.y, bounds.center.z);
        float height = Mathf.Max(0.4f, bounds.size.y);
        float width = Mathf.Max(0.25f, Mathf.Max(bounds.size.x, bounds.size.z));

        if (CheatState.KillFxMode.Value == "Молния")
        {
            var bolt = new Bolt { Y0 = feet.y, Born = Time.unscaledTime };
            var pts = new Vector3[200];
            Vector3 p = feet;
            for (int i = 0; i < pts.Length; i++)
            {
                p += new Vector3(Random.Range(-0.4f, 0.4f), 0.25f, Random.Range(-0.4f, 0.4f));
                pts[i] = p;
            }
            bolt.Points = pts;
            Bolts.Add(bolt);
            return;
        }

        bool gravity = CheatState.KillFxPhysics.Value == "Гравитация";
        float yaw = (-yawDegrees + 90f) * Mathf.Deg2Rad;
        const int total = 250;

        Cloud(feet + Vector3.up * (height - 0.2f), width * 0.4f, total / 10, gravity);

        float bodyLow = height * 0.4f, bodyHigh = height * 0.85f;
        Body(feet, bodyLow, bodyHigh, width * 0.4f, width * 0.2f, yaw, total / 4, gravity);

        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 arm = new(Mathf.Sin(yaw) * width * 0.5f * side, height * 0.75f, Mathf.Cos(yaw) * width * 0.5f * side);
            Limb(feet + arm, height * 0.4f, width * 0.15f, total / 8, gravity);
        }
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 leg = new(Mathf.Sin(yaw) * width * 0.15f * side, height * 0.4f, Mathf.Cos(yaw) * width * 0.15f * side);
            Limb(feet + leg, height * 0.45f, width * 0.15f, total / 6, gravity);
        }
    }

    private static void Add(Vector3 pos, bool gravity)
    {
        if (Particles.Count >= MaxParticles) return;
        float drift = gravity ? 0.04f : 0.03f;
        Particles.Add(new Particle
        {
            Pos = pos,
            Vel = new Vector3(
                (Random.value - 0.5f) * drift,
                gravity ? 0.025f + Random.value * 0.035f : (Random.value - 0.5f) * 0.03f,
                (Random.value - 0.5f) * drift),
            Born = Time.unscaledTime,
            Life = (1500f + Random.Range(0, 1500)) / 1000f,
            Gravity = 0.005f + Random.value * 0.005f,
            Friction = gravity ? 0.999f : 0.995f,
            Falls = gravity,
        });
    }

    private static void Cloud(Vector3 center, float radius, int count, bool gravity)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 offset = Random.insideUnitSphere * radius;
            Add(center + offset, gravity);
        }
    }

    private static void Body(Vector3 feet, float low, float high, float halfW, float halfD, float yaw, int count, bool gravity)
    {
        for (int i = 0; i < count; i++)
        {
            float lx = (Random.value - 0.5f) * halfW * 2f;
            float lz = (Random.value - 0.5f) * halfD * 2f;
            float x = lx * Mathf.Cos(yaw) - lz * Mathf.Sin(yaw);
            float z = lx * Mathf.Sin(yaw) + lz * Mathf.Cos(yaw);
            Add(feet + new Vector3(x, Random.Range(low, high), z), gravity);
        }
    }

    private static void Limb(Vector3 top, float length, float halfW, int count, bool gravity)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 offset = new((Random.value - 0.5f) * halfW * 2f, -Random.value * length, (Random.value - 0.5f) * halfW * 2f);
            Add(top + offset, gravity);
        }
    }
}

// Target marker. Port of Rockstar's TargetESP modes (Souls, Crystals, Circle, Jello) around the player picked
// in the "Игроки" tab (or, optionally, the nearest enemy): a ring of glow points that tightens in as the target
// fades in, spins, and (Circle/Jello) bobs up and down the body.
internal static class TargetFx
{
    private static readonly HudKit.Tween Fade = new(300f, HudKit.InOut);
    private static float _rot;
    private static Vector3 _feet;
    private static float _height = 1.7f, _width = 0.6f;
    private static bool _haveTarget;

    public static void Draw()
    {
        bool want = false;
        if (CheatState.TargetFx.Value)
            want = FindTarget();

        float f = Fade.Set(want);
        if (f < 0.01f || !_haveTarget) return;

        Camera cam = Camera.main;
        if (cam == null || !GlowFx.HasRoom) return;
        _rot += 540f * Time.unscaledDeltaTime;
        if (_rot > 360000f) _rot = 0f;

        Color color = GlowFx.FxColor();
        switch (CheatState.TargetFxMode.Value)
        {
            case "Круг": DrawCircle(cam, color, f); break;
            case "Желе": DrawJello(cam, color, f); break;
            case "Кристаллы": DrawCrystals(cam, color, f); break;
            default: DrawSouls(cam, color, f); break;
        }
    }

    private static bool FindTarget()
    {
        PlayerAvatar picked = PlayerTargets.Picked();
        if (picked != null && picked != PlayerAvatar.instance)
        {
            _feet = picked.transform.position - Vector3.up * 0.85f;
            _height = 1.7f;
            _width = 0.6f;
            _haveTarget = true;
            return true;
        }

        if (!CheatState.TargetFxEnemies.Value) return false;
        PlayerAvatar local = PlayerAvatar.instance;
        EnemyDirector director = EnemyDirector.instance;
        List<EnemyParent> enemies = director != null ? GameReflect.Get<List<EnemyParent>>(director, "enemiesSpawned") : null;
        if (local == null || enemies == null) return false;

        EnemyParent best = null;
        float bestD = 30f * 30f;
        foreach (EnemyParent e in enemies)
        {
            if (e == null || !GameReflect.Get<bool>(e, "Spawned")) continue; // despawned: not on the map
            float d = (e.transform.position - local.transform.position).sqrMagnitude;
            if (d < bestD) { bestD = d; best = e; }
        }
        if (best == null) return false;

        Bounds b = GlowFx.BoundsOf(best.gameObject);
        _feet = new Vector3(b.center.x, b.min.y, b.center.z);
        _height = Mathf.Max(0.5f, b.size.y);
        _width = Mathf.Max(0.3f, Mathf.Max(b.size.x, b.size.z));
        _haveTarget = true;
        return true;
    }

    private static bool Point(Camera cam, float x, float y, float z, out Vector2 c, out float ppu) =>
        GlowFx.Project(cam, _feet + new Vector3(x, y, z), out c, out ppu);

    // Rockstar "circle": half of a 2-degree ring (alternating 45-degree arcs) riding up and down the body.
    private static void DrawCircle(Camera cam, Color col, float f)
    {
        float radius = _width * 1.5f;
        float k = Mathf.Max(0.5f, 0.7f - 0.0f + 0.2f - 0.2f * f);
        float y = _height / 1.75f + _height / 2f * Mathf.Sin(_rot * 0.5f * Mathf.Deg2Rad);
        for (int a = 0; a < 360; a += 3)
        {
            if (a / 45 % 2 == 0) continue;
            double ang = (a + _rot) * Mathf.Deg2Rad;
            if (!Point(cam, (float)System.Math.Cos(ang) * radius * k, y, (float)System.Math.Sin(ang) * radius * k, out Vector2 c, out float ppu)) continue;
            CustomGui.Disc(c, Mathf.Max(1.5f, 0.4f * ppu), new Color(col.r, col.g, col.b, 0.05f * f), 8);
            CustomGui.Disc(c, Mathf.Max(1f, 0.15f * ppu), new Color(col.r, col.g, col.b, 0.75f * f), 6);
        }
    }

    // Rockstar "jello": a full ring whose bright head sweeps up and down, dragging a fading trail.
    private static void DrawJello(Camera cam, Color col, float f)
    {
        float radius = _width * 1.45f;
        float k = Mathf.Max(0.5f, 0.7f + 0.2f - 0.2f * f);
        for (int a = 0; a < 360; a += 4)
        {
            double ang = (a + _rot) * Mathf.Deg2Rad;
            float x = (float)System.Math.Cos(ang) * radius * k, z = (float)System.Math.Sin(ang) * radius * k;
            for (int j = 0; j < 15; j += 2)
            {
                float y = _height / 1.75f + _height / 2f * Mathf.Sin((_rot / 1.5f + j * 2f) * Mathf.Deg2Rad);
                if (!Point(cam, x, y, z, out Vector2 c, out float ppu)) continue;
                CustomGui.Disc(c, Mathf.Max(1.2f, 0.1f * ppu), new Color(col.r, col.g, col.b, col.a * f * (j / 15f) * 0.12f), 6);
            }
            float yHead = _height / 1.75f + _height / 2f * Mathf.Sin((_rot / 1.5f + 30f) * Mathf.Deg2Rad);
            if (Point(cam, x, yHead, z, out Vector2 hc, out float hp))
                CustomGui.Disc(hc, Mathf.Max(1.2f, 0.1f * hp), new Color(col.r, col.g, col.b, 0.3f * f), 6);
        }
    }

    // Rockstar "crystals": spikes on a ring around the target, each pointing at its centre, with a glow.
    private static void DrawCrystals(Camera cam, Color col, float f)
    {
        float radius = _width * 1.5f * (1.2f - 0.5f * f);
        Vector3 centre = _feet + Vector3.up * (_height * 0.5f);
        for (int a = 0; a < 360; a += 20)
        {
            double ang = (a + _rot * 0.3f) * Mathf.Deg2Rad;
            // Rockstar feeds the integer degrees straight into sin(), which gives the uneven spike heights.
            float y = 0.1f + _height * Mathf.Abs(Mathf.Sin(a));
            float x = (float)System.Math.Cos(ang) * radius, z = (float)System.Math.Sin(ang) * radius;
            if (!Point(cam, x, y, z, out Vector2 c, out float ppu)) continue;

            Vector3 tipWorld = _feet + new Vector3(x, y, z);
            Vector3 toward = (centre - tipWorld).normalized;
            Vector2 tip;
            float tp;
            if (!GlowFx.Project(cam, tipWorld + toward * 0.35f, out tip, out tp)) tip = c;

            Vector2 dir = tip - c;
            Vector2 perp = dir.sqrMagnitude > 0.01f ? new Vector2(-dir.y, dir.x).normalized * Mathf.Max(1.5f, 0.05f * ppu) : Vector2.zero;
            Color spike = new(col.r, col.g, col.b, f);
            CustomGui.Triangle(c + perp, c - perp, tip, spike);
            CustomGui.Disc(c, Mathf.Max(1.5f, 0.5f * ppu), new Color(col.r, col.g, col.b, 0.2f * f * 0.5f), 8);
            CustomGui.Disc(c, Mathf.Max(1f, 0.12f * ppu), new Color(col.r, col.g, col.b, 0.6f * f), 6);
        }
    }

    // Rockstar "souls": a stream of glow points winding around the target in bursts that grow and then skip ahead.
    private static void DrawSouls(Camera cam, Color col, float f)
    {
        float radius = _width * 1.5f;
        int growth = 0, skip = 0;
        for (int a = 0; a < 360; a += 3)
        {
            float core = 0.23f + 0.005f * growth, halo = 0.7f + 0.005f * growth;
            if (skip > 0) { skip -= 3; continue; }
            growth += 3;
            if (growth > 50) { skip = 100; growth = 0; continue; }

            float k = Mathf.Max(0.5f, 1.2f - 0.5f * f);
            double ang = (a + _rot) * Mathf.Deg2Rad;
            float y = _height / 1.5f + _height / 3f * Mathf.Sin((a / 2f + _rot / 5f) * Mathf.Deg2Rad);
            if (!Point(cam, (float)System.Math.Cos(ang) * radius * k, y, (float)System.Math.Sin(ang) * radius * k, out Vector2 c, out float ppu)) continue;
            CustomGui.Disc(c, Mathf.Max(1.5f, halo * 0.5f * ppu), new Color(col.r, col.g, col.b, 0.025f * f), 10);
            CustomGui.Disc(c, Mathf.Max(1f, core * 0.5f * ppu), new Color(col.r, col.g, col.b, 0.55f * f), 8);
        }
    }
}
