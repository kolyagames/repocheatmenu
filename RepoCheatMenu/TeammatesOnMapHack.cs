// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace RepoCheatMenu;

// The in-game map (Tab) shows only yourself plus "custom" markers - a dead player's head uses one. This puts
// the same kind of marker on every other living player: a MapCustom on a child object of their avatar, with
// the head's own sprite in the player's colour. MapCustomEntity follows its Parent while it is active and
// removes itself otherwise (death, leaving), so a marker whose entity is gone is simply re-added later.
// Local only - nobody else's map changes. Most useful for a ghost observer.
internal static class TeammatesOnMapHack
{
    private const string MarkerName = "RepoCheatMenu_MapMarker";
    private static readonly Dictionary<PlayerAvatar, MapCustom> Markers = new();
    private static readonly List<PlayerAvatar> Gone = new();
    private static float _next;

    public static void Apply()
    {
        if (!CheatState.TeammatesOnMap.Value)
        {
            if (Markers.Count > 0) Clear();
            return;
        }
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 1f;

        if (Map.Instance == null || LevelGenerator.Instance == null || !LevelGenerator.Instance.Generated) return;
        GameDirector director = GameDirector.instance;
        List<PlayerAvatar> players = director != null ? GameReflect.Get<List<PlayerAvatar>>(director, "PlayerList") : null;
        if (players == null) return;

        Gone.Clear();
        foreach (PlayerAvatar p in Markers.Keys)
            if (p == null || Markers[p] == null) Gone.Add(p);
        foreach (PlayerAvatar p in Gone) Markers.Remove(p);

        foreach (PlayerAvatar p in players)
        {
            if (p == null || p == PlayerAvatar.instance) continue;
            if (GameReflect.Get<bool>(p, "isDisabled"))
            {
                // Dead: the game marks the head itself; drop ours so it does not sit on the death spot.
                if (Markers.TryGetValue(p, out MapCustom deadMarker) && deadMarker != null)
                {
                    MapCustomEntity entity = GameReflect.Get<MapCustomEntity>(deadMarker, "mapCustomEntity");
                    if (entity != null) Object.Destroy(entity.gameObject);
                }
                continue;
            }

            if (!Markers.TryGetValue(p, out MapCustom marker))
            {
                marker = Create(p);
                if (marker == null) continue;
                Markers[p] = marker;
            }
            if (GameReflect.Get<MapCustomEntity>(marker, "mapCustomEntity") == null)
                marker.Add();
        }
    }

    private static MapCustom Create(PlayerAvatar p)
    {
        PlayerDeathHead head = GameReflect.Get<PlayerDeathHead>(p, "playerDeathHead");
        Sprite sprite = head != null && head.mapCustom != null ? head.mapCustom.sprite : null;
        if (sprite == null) return null;

        var go = new GameObject(MarkerName);
        go.transform.SetParent(p.transform, false);
        MapCustom marker = go.AddComponent<MapCustom>();
        marker.autoAdd = false;
        marker.sprite = sprite;
        try { marker.color = SemiFunc.PlayerGetColorMain(p); }
        catch (System.Exception) { marker.color = Color.green; }
        return marker;
    }

    private static void Clear()
    {
        foreach (MapCustom marker in Markers.Values)
        {
            if (marker == null) continue;
            MapCustomEntity entity = GameReflect.Get<MapCustomEntity>(marker, "mapCustomEntity");
            if (entity != null) Object.Destroy(entity.gameObject);
            Object.Destroy(marker.gameObject);
        }
        Markers.Clear();
    }
}
