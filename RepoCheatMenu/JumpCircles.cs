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

// Rockstar's Jump Circles (JumpCircleEspFeature) for R.E.P.O.: a ring on the floor where a player pushed off.
// Rockstar spawns it on the tick where a grounded player starts rising, at the last grounded spot; it grows to
// the full radius over 800 ms along an ease-out cubic (alpha follows the same curve), then fades for 1500 ms
// while still swelling by 18 % and thinning to half. At most 12 rings are drawn at a time.
// The game has no isOnGround for remote players, so a jump is "was within 1 m/s of level, now rising faster than
// 2.5 m/s" (standing up from a crouch is slower) and the floor is a ray down from the player. The ring is a
// 40-segment band drawn on the CustomGui overlay: a faint wide halo under a brighter core, each segment
// projected from the floor plane.
internal static class JumpCircles
{
    private sealed class Ring
    {
        public Vector3 Centre;
        public float Born;
    }

    private sealed class Track
    {
        public float Y;
        public float Time;
        public float LastVy;
        public Vector3 GroundPoint;
        public bool HaveGround;
    }

    private const float ExpandSeconds = 0.8f;
    private const float FadeSeconds = 1.5f;
    private const int Segments = 40;
    private const int MaxRings = 12;

    private static readonly List<Ring> Rings = new();
    private static readonly Dictionary<int, Track> Tracks = new();
    private static readonly List<int> Gone = new();
    private static readonly HashSet<int> Alive = new();
    private static readonly Vector2[] Core = new Vector2[Segments * 2];
    private static readonly Vector2[] Halo = new Vector2[Segments * 2];
    private static readonly bool[] Ok = new bool[Segments];

    public static void Draw()
    {
        if (!CheatState.JumpCircles.Value || !GameState.IsInGame() || EspOverlay.IsGameMenuOpen())
        {
            if (Rings.Count > 0) Rings.Clear();
            if (Tracks.Count > 0) Tracks.Clear();
            return;
        }

        Detect();

        Camera cam = Camera.main;
        if (cam == null || CustomGui.ScreenWidth <= 0f || Rings.Count == 0) return;

        float now = Time.unscaledTime;
        float total = ExpandSeconds + FadeSeconds;
        Rings.RemoveAll(r => now - r.Born > total);
        Color fx = GlowFx.FxColor();
        float maxRadius = Mathf.Clamp(CheatState.JumpCircleRadius.Value / 100f, 0.5f, 2.5f);

        int drawn = 0;
        foreach (Ring r in Rings)
        {
            if (drawn >= MaxRings || !GlowFx.HasRoom) break;
            float age = now - r.Born;
            float radius, alpha;
            if (age < ExpandSeconds)
            {
                float k = age / ExpandSeconds;
                float e = 1f - (1f - k) * (1f - k) * (1f - k);
                radius = maxRadius * e;
                alpha = e;
            }
            else
            {
                float k = Mathf.Clamp01((age - ExpandSeconds) / FadeSeconds);
                radius = maxRadius * (1f + k * 0.18f);
                alpha = (1f - k) * (1f - k);
            }
            if (radius <= 0.01f || alpha <= 0.01f) continue;

            float thin = 1f - 0.5f * Mathf.Clamp01(age / total);
            float w = 0.09f * thin;
            if (DrawBand(cam, r.Centre, radius - w * 3f, radius + w * 3f, Halo, fx, alpha * 0.18f)) drawn++;
            DrawBand(cam, r.Centre, radius - w, radius + w, Core, fx, Mathf.Min(1f, alpha * 0.9f));
        }
    }

    private static bool DrawBand(Camera cam, Vector3 centre, float inner, float outer, Vector2[] buf, Color c, float alpha)
    {
        bool[] ok = Ok;
        for (int i = 0; i < Segments; i++)
        {
            float a = i * Mathf.PI * 2f / Segments;
            Vector3 dir = new(Mathf.Cos(a), 0f, Mathf.Sin(a));
            bool p1 = GlowFx.Project(cam, centre + dir * inner, out buf[i * 2], out _);
            bool p2 = GlowFx.Project(cam, centre + dir * outer, out buf[i * 2 + 1], out _);
            ok[i] = p1 && p2;
        }

        Color col = new(c.r, c.g, c.b, alpha);
        bool any = false;
        for (int i = 0; i < Segments; i++)
        {
            int j = (i + 1) % Segments;
            if (!ok[i] || !ok[j]) continue;
            Vector2 a0 = buf[i * 2], a1 = buf[i * 2 + 1], b0 = buf[j * 2], b1 = buf[j * 2 + 1];
            CustomGui.Triangle(a0, a1, b1, col);
            CustomGui.Triangle(a0, b1, b0, col);
            any = true;
        }
        return any;
    }

    // --- jump detection ----------------------------------------------------------------------------------------

    private static void Detect()
    {
        GameDirector gd = GameDirector.instance;
        List<PlayerAvatar> list = gd != null ? GameReflect.Get<List<PlayerAvatar>>(gd, "PlayerList") : null;
        if (list == null) return;

        float now = Time.unscaledTime;
        Alive.Clear();
        foreach (PlayerAvatar p in list)
        {
            if (p == null || GameReflect.Get<bool>(p, "deadSet")) continue;
            int id = p.GetInstanceID();
            Alive.Add(id);

            Vector3 pos = p.transform.position;
            if (!Tracks.TryGetValue(id, out Track t))
            {
                Tracks[id] = new Track { Y = pos.y, Time = now };
                continue;
            }

            float dt = now - t.Time;
            if (dt < 0.016f) continue; // keep a stable window for the velocity
            float vy = (pos.y - t.Y) / dt;

            bool wasLevel = Mathf.Abs(t.LastVy) < 1f;
            if (wasLevel && vy > 2.5f && t.HaveGround && Rings.Count < 64)
                Rings.Add(new Ring { Centre = t.GroundPoint + Vector3.up * 0.03f, Born = now });

            if (Mathf.Abs(vy) < 1f)
            {
                t.HaveGround = Physics.Raycast(pos + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 3f,
                    ~0, QueryTriggerInteraction.Ignore);
                if (t.HaveGround) t.GroundPoint = hit.point;
            }

            t.LastVy = vy;
            t.Y = pos.y;
            t.Time = now;
        }

        Gone.Clear();
        foreach (int id in Tracks.Keys)
            if (!Alive.Contains(id)) Gone.Add(id);
        foreach (int id in Gone) Tracks.Remove(id);
    }
}
