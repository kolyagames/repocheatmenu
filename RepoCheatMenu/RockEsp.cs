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

// Rockstar's Nametag ESP (NametagEspFeature) for R.E.P.O., drawn on the CustomGui overlay once per frame instead
// of the old tick-driven text labels, so it follows the target smoothly:
//   * player: 22-unit pill (12,12,12 at 92 % alpha, radius 5) = a 14-unit colour chip with radius 4 where
//     Rockstar draws the head, the name in the medium face, the health in red "[hp]";
//   * enemy / valuable / cart / extraction point: the plain variant - medium text on a black box at 39 % alpha;
//   * the whole tag is drawn at 0.6 scale and shrinks a little with distance, never below a readable size.
// Uses the same switches and range as the old ESP (EspPlayers / EspEnemies / EspValuables / EspCarts /
// EspExtractionPoints / EspMaxDistance); CheatState.EspStyle picks which renderer shows the labels (the chams
// silhouettes of Shader Mode are unaffected).
internal static class RockEsp
{
    private static readonly HudCtx Cx = new();
    private static readonly Color PillColor = new(12f / 255f, 12f / 255f, 12f / 255f, 235f / 255f);
    private static readonly Color BoxColor = new(0f, 0f, 0f, 100f / 255f);
    private static readonly Color HpRed = new(1f, 85f / 255f, 85f / 255f, 1f);

    public static bool Active =>
        CheatState.EspStyle.Value == "Rockstar"
        && (CheatState.EspPlayers.Value || CheatState.EspEnemies.Value || CheatState.EspValuables.Value
            || CheatState.EspCarts.Value || CheatState.EspExtractionPoints.Value || CheatState.EspTraps.Value);

    public static void Draw()
    {
        if (!Active || !GameState.IsInGame() || EspOverlay.IsGameMenuOpen()) return;
        Camera cam = Camera.main;
        if (cam == null || CustomGui.ScreenWidth <= 0f || !GlowFx.HasRoom) return;

        CustomGui.Style = CustomGui.TextStyle.Medium;
        float range = CheatState.EspMaxDistance.Value;

        if (CheatState.EspPlayers.Value) Players(cam, range);
        if (CheatState.EspEnemies.Value) Enemies(cam, range);
        if (CheatState.EspValuables.Value) Valuables(cam, range);
        if (CheatState.EspCarts.Value) Carts(cam, range);
        if (CheatState.EspExtractionPoints.Value) Extraction(cam, range);
        if (CheatState.EspTraps.Value) Traps(cam, range);

        CustomGui.Style = CustomGui.TextStyle.Regular;
    }

    // --- sources --------------------------------------------------------------------------------------------

    private static void Players(Camera cam, float range)
    {
        GameDirector gd = GameDirector.instance;
        List<PlayerAvatar> list = gd != null ? GameReflect.Get<List<PlayerAvatar>>(gd, "PlayerList") : null;
        if (list == null) return;

        foreach (PlayerAvatar p in list)
        {
            if (p == null || p == PlayerAvatar.instance) continue;
            bool dead = GameReflect.Get<bool>(p, "deadSet");
            Vector3 at = PlayerTargets.WorldPosition(p);
            float d = Vector3.Distance(cam.transform.position, at);
            if (d > range) continue;

            string name = PlayerTargets.DisplayName(p);
            string hp = "";
            if (dead)
            {
                hp = "†";
            }
            else
            {
                PlayerHealth ph = GameReflect.Get<PlayerHealth>(p, "playerHealth");
                if (ph != null) hp = GameReflect.Get<int>(ph, "health").ToString();
            }

            Color chip;
            try { chip = SemiFunc.PlayerGetColorMain(p); }
            catch { chip = Color.white; }
            if (dead) chip = Color.Lerp(chip, Color.gray, 0.65f);

            Pill(cam, at + Vector3.up * (dead ? 0.6f : 1.95f), d, name, hp.Length > 0 ? "[" + hp + "]" : "", chip, dead);
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
            Transform center = enemy != null ? GameReflect.Get<Transform>(enemy, "CenterTransform") : null;
            if (center == null) continue;

            float d = Vector3.Distance(cam.transform.position, center.position);
            if (d > range) continue;

            string name = GameReflect.Get<string>(parent, "enemyName");
            EnemyHealth health = GameReflect.Get<EnemyHealth>(enemy, "Health");
            string hp = health != null && !GameReflect.Get<bool>(health, "dead") ? "[" + GameReflect.Get<int>(health, "healthCurrent") + "]" : "";
            Tag(cam, center.position + Vector3.up * 1.1f, d, string.IsNullOrEmpty(name) ? "Враг" : name, hp, HpRed);
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
            PhysGrabObject po = GameReflect.Get<PhysGrabObject>(v, "physGrabObject");
            if (po == null) continue;
            Vector3 pos = GameReflect.Get<Vector3>(po, "midPoint");
            float d = Vector3.Distance(cam.transform.position, pos);
            if (d > range) continue;

            float value = GameReflect.Get<float>(v, "dollarValueCurrent");
            Tag(cam, pos + Vector3.up * 0.25f, d, "$" + value.ToString("0"), "", new Color(1f, 0.85f, 0.25f));
        }
    }

    private static void Carts(Camera cam, float range)
    {
        foreach (PhysGrabCart cart in CartHack.GetCarts())
        {
            if (cart == null) continue;
            float d = Vector3.Distance(cam.transform.position, cart.transform.position);
            if (d > range) continue;
            Tag(cam, cart.transform.position + Vector3.up * 1.0f, d, "Тележка", "", Color.cyan);
        }
    }

    // Rockstar's Trap ESP: every trap of the level with its state. The scene is searched once a second,
    // not every frame.
    private static Trap[] _traps = System.Array.Empty<Trap>();
    private static float _nextTrapScan;

    private static void Traps(Camera cam, float range)
    {
        if (Time.unscaledTime >= _nextTrapScan)
        {
            _nextTrapScan = Time.unscaledTime + 1f;
            _traps = Object.FindObjectsByType<Trap>(FindObjectsSortMode.None);
        }

        foreach (Trap trap in _traps)
        {
            if (trap == null) continue;
            float d = Vector3.Distance(cam.transform.position, trap.transform.position);
            if (d > range) continue;

            bool spent = trap.trapTriggered;
            string state = spent ? "[сработала]" : "[готова]";
            Tag(cam, trap.transform.position + Vector3.up * 0.8f, d, "Ловушка", state,
                spent ? new Color(0.65f, 0.65f, 0.7f) : new Color(1f, 0.55f, 0.2f));
        }
    }

    private static void Extraction(Camera cam, float range)
    {
        RoundDirector dir = RoundDirector.instance;
        List<GameObject> points = dir != null ? GameReflect.Get<List<GameObject>>(dir, "extractionPointList") : null;
        if (points == null) return;

        foreach (GameObject go in points)
        {
            if (go == null) continue;
            float d = Vector3.Distance(cam.transform.position, go.transform.position);
            if (d > range) continue;
            ExtractionPoint point = go.GetComponent<ExtractionPoint>();
            string state = point != null ? GameReflect.Get<ExtractionPoint.State>(point, "currentState").ToString() : "?";
            Tag(cam, go.transform.position + Vector3.up * 2f, d, "Эвакуация", "[" + state + "]", RockGui.Accent);
        }
    }

    // --- drawing ---------------------------------------------------------------------------------------------

    private static float ScaleFor(float distance) => 0.6f * Mathf.Clamp(1f - distance / 40f, 0.65f, 1f);

    private static bool Anchor(Camera cam, Vector3 world, out Vector2 c)
    {
        c = default;
        return GlowFx.Project(cam, world, out c, out _);
    }

    // The player pill.
    private static void Pill(Camera cam, Vector3 world, float distance, string name, string hp, Color chip, bool dead)
    {
        if (!Anchor(cam, world, out Vector2 c)) return;

        float u = Mathf.Max(0.5f, CheatState.HudScale.Value);
        string nameText = name + (hp.Length > 0 ? " " : "");
        float nameW = Cx.Measure(nameText, 11f, u);
        float hpW = hp.Length > 0 ? Cx.Measure(hp, 11f, u) : 0f;
        float w = 22f + nameW + hpW + 4f;
        const float h = 22f;

        Place(c, w, h, u, ScaleFor(distance), dead ? 0.7f : 1f);
        Cx.Rect(0f, 0f, w, h, PillColor, 5f);
        Cx.Rect(4f, 4f, 14f, 14f, chip, 4f);
        Cx.Text(22f, 0f, nameW + 4f, h, nameText, 11f, Color.white, TextAnchor.MiddleLeft);
        if (hpW > 0f)
            Cx.Text(22f + nameW, 0f, hpW + 4f, h, hp, 11f, HpRed, TextAnchor.MiddleLeft);
    }

    // The plain tag: text on a black box.
    private static void Tag(Camera cam, Vector3 world, float distance, string name, string extra, Color extraColor)
    {
        if (!Anchor(cam, world, out Vector2 c)) return;

        float u = Mathf.Max(0.5f, CheatState.HudScale.Value);
        string nameText = name + (extra.Length > 0 ? " " : "");
        float nameW = Cx.Measure(nameText, 11f, u);
        float extraW = extra.Length > 0 ? Cx.Measure(extra, 11f, u) : 0f;
        float w = nameW + extraW + 6f;
        const float h = 15f;

        Place(c, w, h, u, ScaleFor(distance), 1f);
        Cx.Rect(0f, 0f, w, h, BoxColor, 1f);
        Cx.Text(3f, 0f, nameW + 4f, h, nameText, 11f, Color.white, TextAnchor.MiddleLeft);
        if (extraW > 0f)
            Cx.Text(3f + nameW, 0f, extraW + 4f, h, extra, 11f, extraColor, TextAnchor.MiddleLeft);
    }

    // Centre the (w x h)-unit card on the projected point and scale it around that centre.
    private static void Place(Vector2 centre, float w, float h, float u, float scale, float alpha)
    {
        float sh = CustomGui.ScreenHeight;
        float x = centre.x - w * u * 0.5f;
        float yDown = (sh - centre.y) - h * u * 0.5f;
        Cx.Begin(x, yDown, w, h, u, scale, alpha);
    }
}
