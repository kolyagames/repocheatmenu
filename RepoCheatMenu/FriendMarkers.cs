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

// Rockstar's Friend Marker (FriendMarkerEspFeature, the "sims" type): a small green diamond floating over a
// friend's head, visible through walls. Rockstar's friend list does not exist here - in co-op every other
// living player is a teammate - so the marker goes over all of them. Its colour is Rockstar's friend green
// (52, 199, 89). The diamond bobs and spins slowly; the spin is faked by sliding the middle edge sideways, which
// is all a flat overlay needs to read as a rotating plumbob.
internal static class FriendMarkers
{
    private static readonly Color Green = new(52f / 255f, 199f / 255f, 89f / 255f);

    public static void Draw()
    {
        if (!CheatState.FriendMarkers.Value || !GameState.IsInGame() || EspOverlay.IsGameMenuOpen()) return;
        Camera cam = Camera.main;
        if (cam == null || CustomGui.ScreenWidth <= 0f || !GlowFx.HasRoom) return;

        GameDirector gd = GameDirector.instance;
        List<PlayerAvatar> list = gd != null ? GameReflect.Get<List<PlayerAvatar>>(gd, "PlayerList") : null;
        if (list == null) return;

        float t = Time.unscaledTime;
        foreach (PlayerAvatar p in list)
        {
            if (p == null || p == PlayerAvatar.instance || GameReflect.Get<bool>(p, "deadSet")) continue;
            Vector3 head = PlayerTargets.WorldPosition(p) + Vector3.up * (2.35f + Mathf.Sin(t * 2f + p.GetInstanceID()) * 0.05f);
            if (!GlowFx.Project(cam, head, out Vector2 c, out float px)) continue;

            float halfH = Mathf.Max(3f, 0.17f * px);
            float halfW = halfH * 0.62f;
            float spin = t * 1.6f + p.GetInstanceID() * 0.37f;
            float mid = halfW * 0.8f * Mathf.Sin(spin);

            Vector2 top = c + new Vector2(0f, halfH), bottom = c - new Vector2(0f, halfH);
            Vector2 left = c - new Vector2(halfW, 0f), right = c + new Vector2(halfW, 0f);
            Vector2 centre = c + new Vector2(mid, 0f);

            GlowFx.Glow(c, 0.28f, px, Green, 0.6f);
            bool frontRight = Mathf.Cos(spin) >= 0f;
            Color lit = Green, shade = new(Green.r * 0.62f, Green.g * 0.62f, Green.b * 0.62f, 1f);
            Color a = frontRight ? lit : shade, b = frontRight ? shade : lit;
            CustomGui.Triangle(top, left, centre, b);
            CustomGui.Triangle(bottom, left, centre, new Color(b.r * 0.85f, b.g * 0.85f, b.b * 0.85f, 1f));
            CustomGui.Triangle(top, right, centre, a);
            CustomGui.Triangle(bottom, right, centre, new Color(a.r * 0.85f, a.g * 0.85f, a.b * 0.85f, 1f));
        }
    }
}
