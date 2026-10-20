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

// Rockstar's mini notifications (IconNotification): a 20-unit pill at the bottom centre, an icon at +5/+5 and
// the text from +20 on in the medium face, the feature name picked out in the notification colour. Squircle
// (radius 7, exponent 7) over a dark translucent fill, 2.5 s on screen, each one pops in from half size over
// 300 ms and the stack slides when one leaves. Everything the island used to announce (a feature switched on or
// off, lobby/enemy/quota alerts, the cosmetics unlock) can go through here instead - CheatState.NotifyStyle.
internal static class RockNotify
{
    private sealed class Note
    {
        public string Highlight, Rest;
        public Color Accent;
        public string Icon;
        public float Born;
        public readonly HudKit.Tween Alpha = new(300f, HudKit.InOut);
        public readonly HudKit.Tween Slide = new(300f, HudKit.Soft);
    }

    private const float DurationSeconds = 2.5f;
    private const float PillH = 20f, Pitch = 25f, FromBottom = 87f;
    private static readonly List<Note> Notes = new();
    private static readonly IconPool Icons = new();
    private static readonly HudCtx Cx = new();

    private static readonly Color Success = new(98f / 255f, 1f, 0f);
    private static readonly Color Error = new(1f, 40f / 255f, 40f / 255f);
    private static readonly Color Info = new(234f / 255f, 179f / 255f, 8f / 255f);

    public static void Push(string highlight, string rest, bool positive, bool isSwitch)
    {
        // A toggle flipped twice in a row replaces its own pill instead of stacking two.
        foreach (Note existing in Notes)
        {
            if (existing.Highlight == highlight && Time.unscaledTime - existing.Born < DurationSeconds)
            {
                existing.Born = Time.unscaledTime;
                existing.Rest = rest;
                existing.Accent = positive ? Success : Error;
                existing.Icon = positive ? "check" : "error";
                return;
            }
        }

        Notes.Add(new Note
        {
            Highlight = highlight,
            Rest = rest,
            Accent = isSwitch ? (positive ? Success : Error) : (positive ? Success : Info),
            Icon = positive ? "check" : (isSwitch ? "error" : "info"),
            Born = Time.unscaledTime,
        });
        if (Notes.Count > 6) Notes.RemoveAt(0);
    }

    public static void Draw()
    {
        Icons.Begin();
        try
        {
            if (Notes.Count == 0) return;

            float u = Mathf.Max(0.5f, CheatState.HudScale.Value);
            float sw = CustomGui.ScreenWidth, sh = CustomGui.ScreenHeight;
            if (sw <= 0f) return;

            int stack = 0;
            CustomGui.Style = CustomGui.TextStyle.Medium;
            for (int i = 0; i < Notes.Count; i++)
            {
                Note n = Notes[i];
                bool alive = Time.unscaledTime - n.Born < DurationSeconds;
                float a = n.Alpha.Set(alive);
                float slide = n.Slide.Set(stack * Pitch);
                if (!alive && a <= 0.002f) { Notes.RemoveAt(i--); continue; }

                string full = n.Highlight + n.Rest;
                float textW = Cx.Measure(full, 7f);
                float w = textW + 26f;
                // Pop in from half size around the pill's own centre; a card's horizontal centre is the screen's.
                float x = sw * 0.5f - w * u * 0.5f;
                // HudCtx measures y from the top of the screen.
                float yDown = sh - (FromBottom + slide + PillH) * u;
                float pop = Mathf.Clamp01(a);
                Cx.Begin(x, yDown, w, PillH, u, 0.5f + 0.5f * pop, pop);

                Cx.Squircle(0f, 0f, w, PillH, 7f, 7f, new Color(0f, 0f, 0f, 0.62f));

                Icons.Draw(n.Icon, Cx.Map(5f, 5f, 10f, 10f), new Color(n.Accent.r, n.Accent.g, n.Accent.b, pop));

                float tx = 20f;
                Color white = Color.white;
                float hw = Cx.Measure(n.Highlight, 7f);
                Cx.Text(tx, 0f, hw + 6f, PillH, n.Highlight, 7f, n.Accent, TextAnchor.MiddleLeft);
                if (n.Rest.Length > 0)
                    Cx.Text(tx + hw, 0f, textW - hw + 6f, PillH, n.Rest, 7f, white, TextAnchor.MiddleLeft);
                if (alive) stack++;
            }
            CustomGui.Style = CustomGui.TextStyle.Regular;
        }
        finally
        {
            Icons.End();
        }
    }
}
