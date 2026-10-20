// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace RepoCheatMenu;

// Rockstar's second menu mode, "Dropdown": five 115 x 243-unit panels side by side (one per category, centred on
// the screen, 10 units apart), each with a header (category icon on an accent glow, title, hairline) and a
// scrolling list of features. Left click switches a feature on or off (or runs it), right click opens its
// settings inside the same panel (back arrow, switch in the corner), middle click binds a key. The hovered
// feature's full description is printed above the panels.
//
// Rockstar's list is made of modules; this client's menu is made of rows. The rows are captured once per opening
// by running every tab builder with the row helpers in capture mode (see Capture in the helpers): a switch or a
// button that is not a "->" sub-row becomes a feature, the "->" rows after it become its settings, and any other
// stand-alone row (a slider, a selector) becomes a settings-only feature. The row helpers then draw the settings
// pages themselves, so every setting looks and behaves exactly as in the Modern window.
internal static partial class CustomOverlayUI
{
    private enum RowKind { Toggle, Button, Setting }

    private sealed class CapRow
    {
        public RowKind Kind;
        public string Label;
        public Action Draw;
        public ConfigEntry<bool> Toggle;
        public Action Action;
        public Action Flip;                 // how this toggle switches - includes a row's onChanged callback
    }

    private sealed class Feature
    {
        public string Name, Full;
        public CapRow Main;                 // the switch/button this feature is, or null
        public readonly List<CapRow> Settings = new();
        public Action Special;              // e.g. "open the script editor in the Modern window"
    }

    private static List<CapRow> _capture;
    private static readonly Dictionary<string, List<string>> DropCaches = new();

    private static void Capture(RowKind kind, string label, Action draw, ConfigEntry<bool> toggle, Action action, Action flip = null) =>
        _capture.Add(new CapRow { Kind = kind, Label = label, Draw = draw, Toggle = toggle, Action = action,
            Flip = flip ?? (toggle != null ? () => toggle.Value = !toggle.Value : null) });

    private static List<string> CacheFor(string key) => DropCaches.TryGetValue(key, out List<string> c) ? c : null;
    private static void StoreCache(string key, List<string> c) => DropCaches[key] = c;

    // --- state -----------------------------------------------------------------------------------------------

    private static bool _dropdown;
    private static List<Feature>[] _features;
    private static readonly int[] DdOpen = { -1, -1, -1, -1, -1 };
    private static readonly float[] DdScroll = new float[5];
    private static string _ddTooltip = "";
    private static Feature _hoverFeature;    // Rockstar-style preview panel source
    private static Rect _hoverRow;
    private static int _hoverPanel;

    private const float PanelUW = 115f, PanelUH = 243f, PanelGapU = 10f, DdHeaderU = 25f, DdRowU = 18f;

    // Called when the window opens: which layout, and (for the dropdown) the feature model.
    private static void ChooseLayout()
    {
        _dropdown = CheatState.MenuStyle.Value == MenuRouter.DropdownMode;
        if (_dropdown) BuildFeatureModel();
    }

    private static string ShortName(string label)
    {
        string s = label.Trim();
        if (s.StartsWith("->")) s = s.Substring(2).Trim();
        int cut = s.IndexOf(" (", StringComparison.Ordinal);
        if (cut > 3) s = s.Substring(0, cut);
        cut = s.IndexOf(" - ", StringComparison.Ordinal);
        if (cut > 3) s = s.Substring(0, cut);
        s = s.Trim();
        return s.Length > 30 ? s.Substring(0, 29) + "…" : s;
    }

    private static bool IsSubRow(string label) => label.TrimStart().StartsWith("->");

    private static void BuildFeatureModel()
    {
        _features = new List<Feature>[Groups.Length];
        for (int g = 0; g < Groups.Length; g++)
        {
            var list = new List<Feature>();
            foreach (string tab in Groups[g].Tabs)
            {
                if (tab == "Блоки")
                {
                    list.Add(new Feature
                    {
                        Name = "Блочные скрипты",
                        Full = "Визуальный редактор поведения бота из блоков (как Scratch): открывается в окне Modern",
                        Special = () => { _dropdown = false; SelectTab(TabIndex("Блоки")); _openT0 = Time.unscaledTime; },
                    });
                    continue;
                }

                if (tab == "Скрипты")
                {
                    list.Add(new Feature
                    {
                        Name = "Редактор скриптов",
                        Full = "Свои C#-скрипты: редактор открывается в окне Modern (кнопка внизу откроет его)",
                        Special = () => { _dropdown = false; SelectTab(TabIndex("Скрипты")); _openT0 = Time.unscaledTime; },
                    });
                    continue;
                }

                List<CapRow> rows;
                _capture = new List<CapRow>();
                try { Tabs[TabIndex(tab)].Build?.Invoke(); }
                catch (Exception e) { Plugin.Log.LogWarning("Dropdown: capturing " + tab + " failed: " + e.Message); }
                finally { rows = _capture; _capture = null; }

                Feature current = null;
                foreach (CapRow row in rows)
                {
                    if (IsSubRow(row.Label) && current != null)
                    {
                        current.Settings.Add(row);
                        continue;
                    }
                    if (row.Kind != RowKind.Setting)
                    {
                        current = new Feature { Name = ShortName(row.Label), Full = row.Label.Trim(), Main = row };
                        list.Add(current);
                    }
                    else
                    {
                        current = null;
                        var solo = new Feature { Name = ShortName(row.Label), Full = row.Label.Trim() };
                        solo.Settings.Add(row);
                        list.Add(solo);
                    }
                }
            }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            _features[g] = list;
            DdOpen[g] = -1;
            DdScroll[g] = 0f;
        }
    }

    // --- drawing ---------------------------------------------------------------------------------------------

    private static Rect DdPanel(int i) =>
        new(_win.x + i * (PanelUW + PanelGapU) * _u, _win.y, PanelUW * _u, PanelUH * _u);

    private static Rect DdRect(int i, float ux, float uy, float uw, float uh)
    {
        Rect p = DdPanel(i);
        return new Rect(p.x + ux * _u, p.yMax - (uy + uh) * _u, uw * _u, uh * _u);
    }

    private static void ComputeDropdownWindow()
    {
        float totalU = Groups.Length * PanelUW + (Groups.Length - 1) * PanelGapU;
        float sw = CustomGui.ScreenWidth, sh = CustomGui.ScreenHeight;
        float u = Mathf.Max(0.5f, CheatState.HudScale.Value);
        if (sw > 40f) u = Mathf.Min(u, (sw - 16f) / totalU);
        if (sh > 40f) u = Mathf.Min(u, (sh - 90f) / PanelUH);
        _u = Mathf.Max(0.5f, u);
        _win = new Rect((sw - totalU * _u) * 0.5f, (sh - PanelUH * _u) * 0.5f, totalU * _u, PanelUH * _u);
    }

    private static void DrawDropdown()
    {
        _ddTooltip = "";
        _hoverFeature = null;
        float wheel = UiInput.Wheel();
        CustomGui.Style = CustomGui.TextStyle.Regular;

        for (int g = 0; g < Groups.Length; g++)
            DrawPanel(g, ref wheel);

        DrawPreviewCard();
    }

    private static void DrawPanel(int g, ref float wheel)
    {
        float u = _u;
        Rect panel = DdPanel(g);
        CustomGui.Rect(panel, HudKit.Panel(0.94f), 11f * u);
        CustomGui.RoundedRing(panel, 11f * u, Mathf.Max(1f, 0.5f * u), RockGui.Border);

        // Header: category icon on a glowing dot, title, hairline.
        Rect dot = DdRect(g, 8f, 8f, 9f, 9f);
        CustomGui.Disc(dot.center, 9f * u, new Color(RockGui.Accent.r, RockGui.Accent.g, RockGui.Accent.b, 0.12f), 14);
        CustomGui.Disc(dot.center, 6.5f * u, new Color(RockGui.Accent.r, RockGui.Accent.g, RockGui.Accent.b, 0.16f), 14);
        MenuIcons.Draw(Groups[g].Icon, dot, RockGui.Accent);
        CustomGui.Style = CustomGui.TextStyle.Medium;
        CustomGui.Text(DdRect(g, 21f, 8f, 90f, 11f), Groups[g].Name, RockGui.Font(8f, u), RockGui.White(), TextAnchor.MiddleLeft);
        CustomGui.Style = CustomGui.TextStyle.Regular;
        CustomGui.Rect(DdRect(g, 1f, 24f, PanelUW - 2f, 1f), RockGui.Border);

        Rect list = DdRect(g, 0f, DdHeaderU, PanelUW, PanelUH - DdHeaderU - 1f);
        bool hoverList = CustomGui.IsHovered(list);

        List<Feature> features = _features != null && g < _features.Length ? _features[g] : null;
        if (features == null) return;

        if (DdOpen[g] >= 0 && DdOpen[g] < features.Count)
            DrawSettingsPage(g, features[DdOpen[g]], list, hoverList, ref wheel);
        else
            DrawFeatureList(g, features, list, hoverList, ref wheel);
    }

    private static void DrawFeatureList(int g, List<Feature> features, Rect list, bool hoverList, ref float wheel)
    {
        float u = _u;
        float contentH = (features.Count * DdRowU + 4f) * u;
        float overflow = Mathf.Max(0f, contentH - list.height);
        if (hoverList && wheel != 0f)
        {
            DdScroll[g] = Mathf.Clamp(DdScroll[g] - wheel * 12f * u, 0f, overflow);
            wheel = 0f;
        }
        DdScroll[g] = Mathf.Clamp(DdScroll[g], 0f, overflow);

        for (int k = 0; k < features.Count; k++)
        {
            Feature f = features[k];
            float top = list.yMax - (2f * u + k * DdRowU * u) + DdScroll[g];
            Rect row = new(list.x, top - DdRowU * u, list.width, DdRowU * u);
            if (row.yMin < list.yMin - 0.5f || row.yMax > list.yMax + 0.5f) continue;

            bool hov = CustomGui.IsHovered(row);
            bool enabled = f.Main != null && f.Main.Toggle != null && f.Main.Toggle.Value;
            float hv = RockGui.HoverAmount(("ddrow", g, k), hov);
            float on = RockGui.OnAmount(("ddon", g, k), enabled);

            if (hv > 0.01f) CustomGui.Rect(new Rect(row.x + 3f * u, row.y + 1f * u, row.width - 6f * u, row.height - 2f * u), RockGui.White(0.04f * hv), 4f * u);

            Keybind kb = f.Main?.Toggle != null ? KeybindManager.Find(f.Main.Toggle) : f.Main?.Action != null ? KeybindManager.Find(f.Main.Action) : null;
            bool capturing = kb != null && ReferenceEquals(KeybindManager.Capturing, kb);
            string label = capturing ? "Клавиша: ..." : f.Name;

            Color c = Color.Lerp(RockGui.White(), RockGui.Accent, 0.5f * on);
            c.a = 0.75f + 0.25f * on;
            CustomGui.TextClipped(new Rect(row.x + 9f * u, row.y, row.width - 28f * u, row.height), label, RockGui.Font(8f, u), c, TextAnchor.MiddleLeft);
            if (on > 0.02f)
                MenuIcons.Draw("check", new Rect(row.xMax - 9f * u - 6f * u, row.center.y - 3f * u, 6f * u, 6f * u), RockGui.White(on));
            else if (f.Settings.Count > 0 || (f.Main == null && f.Special == null))
                CustomGui.Text(new Rect(row.xMax - 18f * u, row.y, 10f * u, row.height), "›", RockGui.Font(8f, u), RockGui.White(0.25f + 0.2f * hv), TextAnchor.MiddleCenter);

            if (!hov) continue;
            _ddTooltip = f.Full;
            _hoverFeature = f;
            _hoverRow = row;
            _hoverPanel = g;
            if (UiInput.Down(0))
            {
                if (f.Special != null) f.Special();
                else if (f.Main?.Toggle != null) f.Main.Flip();
                else if (f.Main?.Action != null) f.Main.Action();
                else OpenFeature(g, k);
            }
            else if (UiInput.Down(1) && (f.Settings.Count > 0 || f.Main == null) && f.Special == null)
            {
                OpenFeature(g, k);
            }
            else if (UiInput.Down(2) && kb != null && KeybindManager.Capturing == null)
            {
                KeybindManager.StartCapture(kb);
            }
        }

        DrawPanelScrollbar(list, overflow, DdScroll[g]);
    }

    private static void OpenFeature(int g, int index)
    {
        DdOpen[g] = index;
        DdScroll[g] = 0f;
    }

    // Rockstar-style preview card for the currently hovered feature: a glass panel attached to the
    // side of the hovered dropdown panel showing the feature's full name, keybind, current state and
    // description (from the ConfigEntry's own prose when there is one, the row label otherwise). This
    // is the "привью" that Rockstar's ModuleSettingsPanel shows next to a module card - a static panel
    // next to the list that explains what the hovered item is before you touch it.
    private static void DrawPreviewCard()
    {
        Feature f = _hoverFeature;
        if (f == null)
        {
            if (_ddTooltip.Length > 0)
                CustomGui.TextClipped(new Rect(_win.x, _win.yMax + 6f * _u, _win.width, 14f * _u), _ddTooltip,
                    RockGui.Font(10f, _u), RockGui.White(0.6f), TextAnchor.MiddleCenter);
            return;
        }

        float u = _u;
        const float CardUW = 150f, CardUH = 118f;
        float sw = CustomGui.ScreenWidth;
        Rect panel = DdPanel(_hoverPanel);
        float rightEdge = panel.xMax + (PanelGapU * u);
        float leftEdge = panel.x - (PanelGapU * u) - CardUW * u;
        // Prefer putting the card on the right of the hovered panel; if it would overflow, flip to the left.
        float cardX = rightEdge + CardUW * u <= sw - 4f * u ? rightEdge
                      : (leftEdge >= 4f * u ? leftEdge : panel.xMax - CardUW * u);
        float cardY = Mathf.Clamp(_hoverRow.y - 4f * u, _win.y, _win.yMax - CardUH * u);
        Rect card = new(cardX, cardY, CardUW * u, CardUH * u);

        CustomGui.Rect(card, RockGui.Panel6, 6f * u);
        CustomGui.RoundedRing(card, 6f * u, 1f * u, RockGui.White(0.08f));

        // Title bar: accent chip + name
        Rect title = new(card.x + 7f * u, card.y + 6f * u, card.width - 14f * u, 11f * u);
        CustomGui.Rect(new Rect(title.x, title.y + 2f * u, 2f * u, 7f * u), RockGui.Accent, 1f * u);
        CustomGui.Style = CustomGui.TextStyle.Medium;
        CustomGui.TextClipped(new Rect(title.x + 5f * u, title.y, title.width - 5f * u, title.height),
            f.Name, RockGui.Font(10f, u), RockGui.White(0.95f), TextAnchor.MiddleLeft);
        CustomGui.Style = CustomGui.TextStyle.Regular;

        // State pill (ON/OFF for toggles, VALUE for sliders/cycles)
        Rect subRow = new(card.x + 7f * u, title.yMax + 2f * u, card.width - 14f * u, 9f * u);
        BepInEx.Configuration.ConfigEntry<bool> toggle = f.Main?.Toggle;
        string state = null;
        Color stateCol = RockGui.White(0.6f);
        if (toggle != null)
        {
            state = toggle.Value ? "ВКЛ" : "ВЫКЛ";
            stateCol = toggle.Value ? RockGui.Accent : RockGui.White(0.5f);
        }
        else if (f.Settings.Count == 1 && f.Settings[0].Toggle != null)
        {
            state = f.Settings[0].Toggle.Value ? "ВКЛ" : "ВЫКЛ";
        }

        // Keybind line
        var kb = toggle != null ? KeybindManager.Find(toggle) : f.Main?.Action != null ? KeybindManager.Find(f.Main.Action) : null;
        string key = kb != null && kb.Vk.Value != 0 ? "клавиша: " + Win32.VkName(kb.Vk.Value) : "клавиша: не задана";
        CustomGui.TextClipped(subRow, key, RockGui.Font(7f, u), RockGui.White(0.5f), TextAnchor.MiddleLeft);
        if (state != null)
            CustomGui.TextClipped(subRow, state, RockGui.Font(7f, u), stateCol, TextAnchor.MiddleRight);

        // Hairline
        CustomGui.Rect(new Rect(card.x + 7f * u, subRow.yMax + 3f * u, card.width - 14f * u, 0.5f * u), RockGui.White(0.08f));

        // Description: config description if available, otherwise the Full label.
        string desc = PreviewDescription(f);
        Rect body = new(card.x + 7f * u, subRow.yMax + 6f * u, card.width - 14f * u, card.yMax - subRow.yMax - 12f * u);
        CustomGui.TextClipped(body, desc, RockGui.Font(7.5f, u), RockGui.White(0.78f), TextAnchor.UpperLeft);
    }

    private static string PreviewDescription(Feature f)
    {
        var toggle = f.Main?.Toggle;
        string prose = toggle?.Description?.Description;
        if (string.IsNullOrEmpty(prose))
        {
            foreach (CapRow r in f.Settings)
            {
                prose = r.Toggle?.Description?.Description;
                if (!string.IsNullOrEmpty(prose)) break;
            }
        }
        return string.IsNullOrEmpty(prose) ? f.Full : prose;
    }

    private static void DrawPanelScrollbar(Rect list, float overflow, float scroll)
    {
        if (overflow <= 1f) return;
        float u = _u;
        Rect track = new(list.xMax - 5f * u, list.y + 2f * u, 2.5f * u, list.height - 4f * u);
        float thumbH = Mathf.Max(10f * u, track.height * list.height / (list.height + overflow));
        float t = Mathf.Clamp01(scroll / overflow);
        CustomGui.Rect(new Rect(track.x, track.yMax - thumbH - (track.height - thumbH) * t, track.width, thumbH), RockGui.White(0.3f), u);
    }

    // A feature's settings, inside its panel: [back] name ........ [switch], then its rows.
    private static void DrawSettingsPage(int g, Feature f, Rect list, bool hoverList, ref float wheel)
    {
        float u = _u;
        Rect head = new(list.x, list.yMax - DdRowU * u, list.width, DdRowU * u);
        Rect back = new(head.x + 3f * u, head.y, DdRowU * u, DdRowU * u);
        bool backHover = CustomGui.IsHovered(back);
        float bh = RockGui.HoverAmount(("ddback", g), backHover);
        MenuIcons.Draw("back", new Rect(back.center.x - 3f * u, back.center.y - 3f * u, 6f * u, 6f * u), RockGui.White(0.7f + 0.3f * bh));

        bool hasSwitch = f.Main?.Toggle != null;
        float switchW = hasSwitch ? 13f * u + 9f * u : 0f;
        CustomGui.Style = CustomGui.TextStyle.Medium;
        CustomGui.TextClipped(new Rect(back.xMax - 5f * u, head.y, head.width - back.width - switchW - 4f * u, head.height), f.Name,
            RockGui.Font(8f, u), RockGui.White(0.95f), TextAnchor.MiddleLeft);
        CustomGui.Style = CustomGui.TextStyle.Regular;

        if (hasSwitch)
        {
            ConfigEntry<bool> entry = f.Main.Toggle;
            Rect track = new(head.xMax - 9f * u - 13f * u, head.center.y - 4f * u, 13f * u, 8f * u);
            float on = RockGui.OnAmount(("ddsw", g), entry.Value);
            CustomGui.Rect(track, Color.Lerp(new Color(0.14f, 0.125f, 0.17f, 1f), RockGui.Accent, on), 3f * u);
            CustomGui.Rect(new Rect(track.x + 0.5f * u + 5f * on * u, track.y + 0.5f * u, 7f * u, 7f * u), RockGui.White(0.75f + 0.25f * on), 3.5f * u);
            if (CustomGui.IsHovered(track) && UiInput.Down(0)) f.Main.Flip();
        }
        if ((backHover && UiInput.Down(0)) || (CustomGui.IsHovered(head) && UiInput.Down(1)))
        {
            DdOpen[g] = -1;
            DdScroll[g] = 0f;
            return;
        }
        CustomGui.Rect(new Rect(list.x + 1f * u, head.y - 0.5f * u, list.width - 2f * u, u), RockGui.Border);

        // The settings rows, drawn by the real row helpers into a clip rect below the header.
        Rect rows = new(list.x + 5f * u, list.y, list.width - 10f * u, list.height - DdRowU * u - 1f * u);
        if (f.Settings.Count == 0)
        {
            CustomGui.Text(rows, "Нет настроек", RockGui.Font(7f, u), RockGui.White(0.45f), TextAnchor.MiddleCenter);
            return;
        }

        var saveClip = _contentClip;
        bool saveMeasuring = _measuring;
        try
        {
            _contentClip = rows;
            _cardOpen = true; // no card backgrounds here: the panel is the card

            _measuring = true;
            _rowCursorY = rows.yMax;
            foreach (CapRow r in f.Settings) r.Draw();
            float contentH = rows.yMax - _rowCursorY + 4f * u;
            float overflow = Mathf.Max(0f, contentH - rows.height);

            if (hoverList && wheel != 0f)
            {
                DdScroll[g] = Mathf.Clamp(DdScroll[g] - wheel * 12f * u, 0f, overflow);
                wheel = 0f;
            }
            DdScroll[g] = Mathf.Clamp(DdScroll[g], 0f, overflow);

            _measuring = false;
            _rowCursorY = rows.yMax + DdScroll[g];
            foreach (CapRow r in f.Settings) r.Draw();
            DrawPanelScrollbar(rows, overflow, DdScroll[g]);
        }
        finally
        {
            _contentClip = saveClip;
            _measuring = saveMeasuring;
            _cardOpen = false;
        }
    }
}
