// ============================================================================
// RepoCheatMenu - вспомогательный мод для R.E.P.O.
// Copyright (c) 2026 Kolya. Все права защищены.
// Исходный код закрыт: без разрешения автора не копировать, не публиковать
// и не выдавать за свою работу. Подробности - LICENSE.txt.
// R.E.P.O. принадлежит semiwork; мод неофициальный и с ней не связан.
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace RepoCheatMenu;

// "Блоки" tab: the visual (Scratch-like) script editor. The script is a list of block rows; a container block
// (Повторить / Всегда / Если...) owns every row down to its "Конец", which the editor shows as indentation.
// Click a row's title to select it - a block added from the palette lands right after the selected row (or at
// the end), so selecting a container and adding blocks fills it. Moving, duplicating and deleting a container
// works on the whole container (deleting keeps its contents, one level up).
//
// The tab is built twice per frame (measure, then draw - see DrawContent), so every click that changes what
// the tab lays out (the block list, the saved list, the running state, the note line) is queued in `pending`
// and applied after the draw pass: both passes of a frame always see the same rows.
internal static partial class CustomOverlayUI
{
    private static int _blkSel = -1;
    private static string _confirmKey;
    private static float _confirmUntil;
    private static string _blkNote = "", _blkNoteNext;
    private static float _blkNoteUntil;

    private static List<Block> Blk => BlockScripts.Draft;

    private static void BlkNote(string text) => _blkNoteNext = text;

    // Two-click confirmation: the first click arms the button for 3 s and relabels it, the second one acts.
    private static bool ConfirmButton(string key, Rect r, string text, RockGui.Kind kind, float u, float font = 7f)
    {
        bool armed = _confirmKey == key && Time.unscaledTime < _confirmUntil;
        if (!RockGui.Button(key, r, armed ? "Точно?" : text, armed ? RockGui.Kind.Danger : kind, u, font)) return false;
        if (armed)
        {
            _confirmKey = null;
            return true;
        }
        _confirmKey = key;
        _confirmUntil = Time.unscaledTime + 3f;
        return false;
    }

    private static void BuildBlocksTab()
    {
        if (_capture != null || Searching || _countOnly) return;
        float u = _u;
        var pending = new List<Action>();
        List<Block> blk = Blk;
        if (_blkSel >= blk.Count) _blkSel = blk.Count - 1;

        // The measure pass is the first one of a frame: notes queued last frame become visible for both passes.
        if (_measuring && _blkNoteNext != null)
        {
            _blkNote = _blkNoteNext;
            _blkNoteNext = null;
            _blkNoteUntil = Time.unscaledTime + 6f;
        }

        SectionHeader("Блоки");
        LiveInfo(() => "Соберите поведение из блоков и нажмите «Запустить». Клик по названию блока выбирает его - " +
            "новые блоки встают после выбранного. Правки сами сохраняются в черновик, «Сохранить» кладёт скрипт в список ниже.");

        // --- toolbar: name + run / stop / save / new / delete -------------------------------------------------
        Rect bar = NextRow();
        if (RowVisible(bar))
        {
            float h = 12f * u, y = bar.y + (bar.height - h) * 0.5f;
            Rect nameBox = new(bar.x + 6f * u, y, 84f * u, h);
            Rect run = new(nameBox.xMax + 5f * u, y, 50f * u, h);
            Rect save = new(run.xMax + 4f * u, y, 48f * u, h);
            Rect fresh = new(save.xMax + 4f * u, y, 38f * u, h);
            Rect del = new(fresh.xMax + 4f * u, y, 42f * u, h);

            BlockScripts.DraftName = RockGui.TextBox("blk_name", nameBox, BlockScripts.DraftName, u);
            bool running = BlockScripts.Running;
            if (RockGui.Button("blk_run", run, running ? "Стоп" : "Запустить", running ? RockGui.Kind.Danger : RockGui.Kind.Primary, u))
                pending.Add(() =>
                {
                    if (BlockScripts.Running) BlockScripts.Stop();
                    else BlockScripts.Start(Blk, BlockScripts.DraftName);
                });
            if (RockGui.Button("blk_save", save, "Сохранить", RockGui.Kind.Normal, u))
                pending.Add(() =>
                {
                    string saved = BlockScripts.Save(BlockScripts.DraftName, Blk);
                    if (saved == null) BlkNote("Введите имя скрипта.");
                    else
                    {
                        BlockScripts.DraftName = saved;
                        BlkNote("Сохранено: «" + saved + "».");
                    }
                });
            if (ConfirmButton("blk_new", fresh, "Новый", RockGui.Kind.Normal, u))
                pending.Add(() =>
                {
                    BlockScripts.Draft = new List<Block>();
                    BlockScripts.DraftName = "Мой скрипт";
                    _blkSel = -1;
                });
            if (ConfirmButton("blk_del", del, "Удалить", RockGui.Kind.Danger, u))
                pending.Add(() =>
                {
                    string name = BlockScripts.DraftName.Trim();
                    BlkNote(BlockScripts.Delete(name)
                        ? "Удалён сохранённый скрипт «" + name + "» (в редакторе он остался)."
                        : "Скрипта «" + name + "» нет среди сохранённых.");
                });
        }

        // --- toolbar 2: clipboard / undo settings / forget lobbies ------------------------------------------
        Rect bar2 = NextRow();
        if (RowVisible(bar2))
        {
            float h = 11f * u, y = bar2.y + (bar2.height - h) * 0.5f;
            Rect copy = new(bar2.x + 6f * u, y, 54f * u, h);
            Rect paste = new(copy.xMax + 4f * u, y, 50f * u, h);
            Rect undo = new(paste.xMax + 4f * u, y, 88f * u, h);
            Rect forget = new(undo.xMax + 4f * u, y, 84f * u, h);

            if (RockGui.Button("blk_copy", copy, "Копировать", RockGui.Kind.Normal, u, 6.5f))
            {
                try
                {
                    GUIUtility.systemCopyBuffer = BlockScripts.Export(blk, BlockScripts.DraftName);
                    BlkNote("Скрипт скопирован в буфер обмена - его можно отправить другу, а он нажмёт «Вставить».");
                }
                catch (Exception e) { BlkNote("Не удалось скопировать: " + e.Message); }
            }
            if (blk.Count == 0 ? RockGui.Button("blk_paste", paste, "Вставить", RockGui.Kind.Normal, u, 6.5f)
                               : ConfirmButton("blk_paste", paste, "Вставить", RockGui.Kind.Normal, u, 6.5f))
            {
                string clip = null;
                try { clip = GUIUtility.systemCopyBuffer; } catch (Exception) { }
                List<Block> got = BlockScripts.Import(clip, out string importedName);
                if (got == null) BlkNote("В буфере обмена нет скрипта блоков.");
                else
                    pending.Add(() =>
                    {
                        BlockScripts.Draft = got;
                        if (importedName != null) BlockScripts.DraftName = importedName;
                        _blkSel = -1;
                        BlkNote("Вставлено блоков: " + got.Count + ". Нажмите «Сохранить», чтобы оставить его в списке.");
                    });
            }
            if (RockGui.Button("blk_undo", undo, "Откатить настройки (" + BlockScripts.SnapshotCount + ")", RockGui.Kind.Normal, u, 6.5f))
            {
                int n = BlockScripts.RestoreSnapshot();
                BlkNote(n > 0 ? "Возвращено настроек, изменённых скриптом: " + n + "." : "Скрипт ничего не менял.");
            }
            if (ConfirmButton("blk_forget", forget, "Забыть серверы (" + BlockScripts.VisitedCount + ")", RockGui.Kind.Normal, u, 6.5f))
            {
                BlockScripts.ForgetVisited();
                BlkNote("Список посещённых серверов очищен.");
            }
        }

        Button("Запуск / стоп скрипта из редактора (средний клик по строке - назначить клавишу)",
            BlockScripts.Running ? "Стоп" : "Запустить", BlockScripts.ToggleDraftRun);
        LiveInfo(BlockScripts.StatusLine);
        if (Time.unscaledTime < _blkNoteUntil && !string.IsNullOrEmpty(_blkNote))
            LiveInfo(() => _blkNote);

        // --- the script ----------------------------------------------------------------------------------------
        SectionHeader("Скрипт «" + (BlockScripts.DraftName ?? "").Trim() + "» · блоков: " + blk.Count);
        if (blk.Count == 0) LiveInfo(() => "Пусто. Добавьте блоки из палитры ниже.");

        int depth = 0;
        for (int i = 0; i < blk.Count; i++)
        {
            Block b = blk[i];
            BlockDef def = BlockDefs.Get(b.Kind);
            if (def == null) continue;
            if (b.Kind == BlockDefs.End && depth > 0) depth--;
            int drawDepth = b.Kind == BlockDefs.Else && depth > 0 ? depth - 1 : depth;

            Rect row = NextRow(16f);
            if (RowVisible(row)) DrawBlockRow(b, def, i, drawDepth, row, u, pending);
            if (def.Container) depth++;
        }

        // --- palette --------------------------------------------------------------------------------------------
        SectionHeader("Добавить блок" + (_blkSel >= 0 && _blkSel < blk.Count ? " (после выбранного)" : " (в конец)"));
        string lastCat = null;
        var rowBlocks = new List<BlockDef>();
        void FlushPalette()
        {
            if (rowBlocks.Count == 0) return;
            Rect r = NextRow(15f);
            if (RowVisible(r))
            {
                float gap = 3f * u, w = (r.width - 12f * u - gap * 2f) / 3f, h = 12f * u, y = r.y + (r.height - h) * 0.5f;
                for (int k = 0; k < rowBlocks.Count; k++)
                {
                    BlockDef d = rowBlocks[k];
                    Rect cell = new(r.x + 6f * u + k * (w + gap), y, w, h);
                    Color c = BlockDefs.CategoryColor(d.Category);
                    CustomGui.Rect(new Rect(cell.x, cell.y, 2f * u, cell.height), c, 1f * u);
                    if (RockGui.Button(("blk_add", d.Id), new Rect(cell.x + 2.5f * u, cell.y, cell.width - 2.5f * u, cell.height), d.Title, RockGui.Kind.Normal, u, 6f))
                    {
                        string id = d.Id;
                        pending.Add(() => AddBlock(id));
                    }
                }
            }
            rowBlocks.Clear();
        }
        foreach (BlockDef d in BlockDefs.All)
        {
            if (d.Id == BlockDefs.End) continue; // a container adds its own "Конец"
            if (lastCat != null && d.Category != lastCat) FlushPalette();
            lastCat = d.Category;
            rowBlocks.Add(d);
            if (rowBlocks.Count == 3) FlushPalette();
        }
        FlushPalette();

        // --- saved scripts --------------------------------------------------------------------------------------
        SectionHeader("Сохранённые скрипты");
        foreach (string name in new List<string>(BlockScripts.SavedNames))
        {
            Rect row = NextRow();
            if (!RowVisible(row)) continue;
            float h = 11f * u, y = row.y + (row.height - h) * 0.5f;
            bool auto = string.Equals(BlockScripts.AutoRunName?.Value, name, StringComparison.OrdinalIgnoreCase);
            bool runningThis = BlockScripts.Running && string.Equals(BlockScripts.RunningName, name, StringComparison.OrdinalIgnoreCase);
            Rect runBtn = new(row.xMax - 6f * u - 40f * u, y, 40f * u, h);
            Rect autoBtn = new(runBtn.x - 3f * u - 62f * u, y, 62f * u, h);
            Rect loadBtn = new(autoBtn.x - 3f * u - 44f * u, y, 44f * u, h);
            RockGui.Label(row, (runningThis ? "▶ " : "") + name, u, runningThis ? 1f : 0.85f, 160f * u);
            string captured = name;
            if (RockGui.Button(("blk_load", name), loadBtn, "Открыть", RockGui.Kind.Normal, u))
                pending.Add(() =>
                {
                    List<Block> loaded = BlockScripts.Load(captured);
                    if (loaded == null) return;
                    BlockScripts.Draft = loaded;
                    BlockScripts.DraftName = captured;
                    _blkSel = -1;
                });
            if (RockGui.Button(("blk_auto", name), autoBtn, auto ? "Автозапуск ✓" : "Автозапуск", auto ? RockGui.Kind.Primary : RockGui.Kind.Normal, u))
                BlockScripts.AutoRunName.Value = auto ? "" : captured;
            if (RockGui.Button(("blk_runs", name), runBtn, runningThis ? "Стоп" : "Старт", runningThis ? RockGui.Kind.Danger : RockGui.Kind.Primary, u))
                pending.Add(() =>
                {
                    if (runningThis) { BlockScripts.Stop(); return; }
                    List<Block> loaded = BlockScripts.Load(captured);
                    if (loaded != null) BlockScripts.Start(loaded, captured);
                });
            RockGui.Hairline(row, u);
        }
        LiveInfo(() => "Автозапуск: скрипт стартует один раз через ~25 с после появления главного меню игры.");

        SectionHeader("Журнал скрипта");
        LiveInfo(BlockScripts.HistoryText);

        foreach (Action a in pending) a();
    }

    private static void DrawBlockRow(Block b, BlockDef def, int i, int depth, Rect row, float u, List<Action> pending)
    {
        bool active = BlockScripts.Running && ReferenceEquals(BlockScripts.CurrentBlock, b);
        bool selected = _blkSel == i;
        bool structural = b.Kind == BlockDefs.End || b.Kind == BlockDefs.Else;
        Color cat = BlockDefs.CategoryColor(def.Category);

        float indent = Mathf.Min(depth, 6) * 8f * u;
        Rect body = new(row.x + 2f * u + indent, row.y + 1f * u, row.width - 4f * u - indent, row.height - 2f * u);
        float alpha = active ? 0.34f : selected ? 0.22f : structural ? 0.07f : 0.12f;
        CustomGui.Rect(body, new Color(cat.r, cat.g, cat.b, alpha), 3f * u);
        CustomGui.Rect(new Rect(body.x, body.y, 2.5f * u, body.height), active ? Color.white : cat, 1f * u);

        // right side: duplicate / up / down / delete ("Конец" goes with its container, so it has none)
        float bw = 9f * u, bh = 10f * u, by = body.y + (body.height - bh) * 0.5f;
        Rect xBtn = new(body.xMax - bw - 2f * u, by, bw, bh);
        Rect dnBtn = new(xBtn.x - bw - 2f * u, by, bw, bh);
        Rect upBtn = new(dnBtn.x - bw - 2f * u, by, bw, bh);
        Rect dupBtn = new(upBtn.x - bw - 2f * u, by, bw, bh);
        int idx = i;
        float paramsRight = body.xMax - 3f * u;
        if (b.Kind != BlockDefs.End)
        {
            paramsRight = dupBtn.x - 3f * u;
            if (RockGui.Button(("blk_dup", i), dupBtn, "⧉", RockGui.Kind.Normal, u, 6.5f)) pending.Add(() => DuplicateRange(idx));
            if (RockGui.Button(("blk_up", i), upBtn, "↑", RockGui.Kind.Normal, u, 6.5f)) pending.Add(() => MoveRange(idx, -1));
            if (RockGui.Button(("blk_dn", i), dnBtn, "↓", RockGui.Kind.Normal, u, 6.5f)) pending.Add(() => MoveRange(idx, +1));
            if (RockGui.Button(("blk_x", i), xBtn, "✕", RockGui.Kind.Danger, u, 6.5f)) pending.Add(() => DeleteAt(idx));
        }

        string titleText = def.Title;
        if (b.Kind == BlockDefs.End)
        {
            string opener = OpenerTitle(i);
            if (opener != null) titleText = "Конец «" + opener + "»";
        }
        float titleW = TitleWidth(b.Kind) * u;
        Rect title = new(body.x + 5f * u, body.y, titleW, body.height);
        CustomGui.TextClipped(title, titleText, RockGui.Font(7.5f, u),
            RockGui.White(active || selected ? 1f : structural ? 0.6f : 0.85f), TextAnchor.MiddleLeft);
        if (CustomGui.IsHovered(title) && UiInput.Down(0)) _blkSel = i;

        DrawBlockParams(b, i, title.xMax + 2f * u, paramsRight, by, bh, u);
    }

    // Title of the container a "Конец" at endIndex closes, or null when it closes nothing.
    private static string OpenerTitle(int endIndex)
    {
        List<Block> blk = Blk;
        int depth = 0;
        for (int k = endIndex - 1; k >= 0; k--)
        {
            string kind = blk[k].Kind;
            if (kind == BlockDefs.End) depth++;
            else if (BlockDefs.IsContainer(kind))
            {
                if (depth == 0) return BlockDefs.Get(kind)?.Title;
                depth--;
            }
        }
        return null;
    }

    // A container row owns everything down to its matching "Конец"; any other row is a range of one.
    private static int RangeEnd(int i)
    {
        List<Block> blk = Blk;
        if (i < 0 || i >= blk.Count || !BlockDefs.IsContainer(blk[i].Kind)) return i;
        int depth = 0;
        for (int k = i; k < blk.Count; k++)
        {
            if (BlockDefs.IsContainer(blk[k].Kind)) depth++;
            else if (blk[k].Kind == BlockDefs.End && --depth == 0) return k;
        }
        return blk.Count - 1; // never closed: it runs to the end of the script
    }

    // Moves the row (with its whole container range) one row up or down - into or out of a neighbouring
    // container exactly like stepping past one row at a time.
    private static void MoveRange(int i, int dir)
    {
        List<Block> blk = Blk;
        if (i < 0 || i >= blk.Count) return;
        int e = RangeEnd(i);
        if (dir < 0)
        {
            if (i == 0) return;
            Block above = blk[i - 1];
            blk.RemoveAt(i - 1);
            blk.Insert(e, above);
            _blkSel = i - 1;
        }
        else
        {
            if (e >= blk.Count - 1) return;
            Block below = blk[e + 1];
            blk.RemoveAt(e + 1);
            blk.Insert(i, below);
            _blkSel = i + 1;
        }
    }

    private static void DuplicateRange(int i)
    {
        List<Block> blk = Blk;
        if (i < 0 || i >= blk.Count) return;
        int e = RangeEnd(i);
        var copy = new List<Block>();
        for (int k = i; k <= e; k++) copy.Add(blk[k].Clone());
        blk.InsertRange(e + 1, copy);
        _blkSel = e + 1;
    }

    // Deleting a container keeps what is inside it (one level up) and removes its "Конец" and its own "Иначе"
    // with it - a leftover "Иначе" would otherwise attach itself to the next container out.
    private static void DeleteAt(int i)
    {
        List<Block> blk = Blk;
        if (i < 0 || i >= blk.Count) return;
        int e = RangeEnd(i);
        if (e != i)
        {
            var remove = new List<int>();
            if (blk[e].Kind == BlockDefs.End) remove.Add(e);
            int depth = 0;
            for (int k = i + 1; k < e; k++)
            {
                string kind = blk[k].Kind;
                if (BlockDefs.IsContainer(kind)) depth++;
                else if (kind == BlockDefs.End) depth--;
                else if (kind == BlockDefs.Else && depth == 0) remove.Add(k);
            }
            remove.Sort();
            for (int r = remove.Count - 1; r >= 0; r--) blk.RemoveAt(remove[r]);
        }
        blk.RemoveAt(i);
        if (_blkSel >= blk.Count) _blkSel = blk.Count - 1;
    }

    private static void AddBlock(string id)
    {
        List<Block> blk = Blk;
        int at = _blkSel >= 0 && _blkSel < blk.Count ? _blkSel + 1 : blk.Count;
        blk.Insert(at, BlockDefs.Create(id));
        if (BlockDefs.IsContainer(id)) blk.Insert(at + 1, BlockDefs.Create(BlockDefs.End));
        _blkSel = at;
    }

    // Width (in units) reserved for a block's title before its parameters start.
    private static float TitleWidth(string kind) => kind switch
    {
        BlockDefs.JoinNew => 80f,
        BlockDefs.WaitInRoom => 90f,
        BlockDefs.WaitLevel => 84f,
        BlockDefs.WaitPlayers => 90f,
        BlockDefs.WaitScene => 62f,
        BlockDefs.WaitDead => 92f,
        BlockDefs.WaitRandom => 66f,
        BlockDefs.Leave => 84f,
        BlockDefs.ClearVisited => 102f,
        BlockDefs.Restore => 118f,
        BlockDefs.IfOk => 70f,
        BlockDefs.IfScene => 56f,
        BlockDefs.IfMoney => 56f,
        BlockDefs.IfHp => 56f,
        BlockDefs.IfVar => 70f,
        BlockDefs.IfChance => 40f,
        BlockDefs.SetVar => 50f,
        BlockDefs.SetFeature => 52f,
        BlockDefs.SetValue => 72f,
        BlockDefs.SpawnItem => 74f,
        BlockDefs.SpawnValuable => 80f,
        BlockDefs.PickPlayer => 60f,
        BlockDefs.CallScript => 66f,
        BlockDefs.Notify => 48f,
        BlockDefs.Log => 62f,
        BlockDefs.Stop => 90f,
        BlockDefs.Break => 70f,
        BlockDefs.End => 120f,
        _ => 40f,
    };

    private static readonly string[] FeatureModes = { "Включить", "Выключить", "Переключить" };
    private static readonly string[] IfRoomModes = { "я в комнате", "я не в комнате" };
    private static readonly string[] IfOkModes = { "удался", "не удался" };
    private static readonly string[] IsNotModes = { "является", "не является" };
    private static readonly string[] CmpModes = { "≥", "<" };
    private static readonly string[] VarOps = { "=", "+", "−", "случ. 0..N" };

    private static void DrawBlockParams(Block b, int i, float x, float right, float y, float h, float u)
    {
        switch (b.Kind)
        {
            case BlockDefs.Wait:
                Stepper(("blk_n", i), x, y, h, u, ref b.N, 0f, 3600f, b.N >= 20f ? 5f : 1f, "с");
                break;
            case BlockDefs.WaitRandom:
                x = Stepper(("blk_n", i), x, y, h, u, ref b.N, 0f, 3600f, 1f, "от");
                Stepper(("blk_n2", i), x, y, h, u, ref b.N2, 0f, 3600f, 1f, "до");
                break;
            case BlockDefs.JoinNew:
                x = Stepper(("blk_n", i), x, y, h, u, ref b.N, 5f, 300f, 5f, "ждать, с");
                Stepper(("blk_n2", i), x, y, h, u, ref b.N2, 0f, 10f, 1f, "игроков от");
                break;
            case BlockDefs.WaitInRoom:
            case BlockDefs.WaitLevel:
                Stepper(("blk_n", i), x, y, h, u, ref b.N, 5f, 600f, 5f, "макс, с");
                break;
            case BlockDefs.WaitPlayers:
                x = Stepper(("blk_n", i), x, y, h, u, ref b.N, 1f, 10f, 1f, "от");
                Stepper(("blk_n2", i), x, y, h, u, ref b.N2, 5f, 600f, 5f, "макс, с");
                break;
            case BlockDefs.WaitScene:
                x = CycleChip(("blk_m", i), x, y, h, u, BlockScripts.SceneNames, ref b.Mode);
                Stepper(("blk_n", i), x, y, h, u, ref b.N, 5f, 900f, 5f, "макс, с");
                break;
            case BlockDefs.WaitDead:
                Stepper(("blk_n", i), x, y, h, u, ref b.N, 5f, 3600f, 5f, "макс, с");
                break;
            case BlockDefs.Repeat:
                Stepper(("blk_n", i), x, y, h, u, ref b.N, 1f, 1000f, b.N >= 20f ? 5f : 1f, "раз");
                break;
            case BlockDefs.IfInRoom:
                CycleChip(("blk_m", i), x, y, h, u, IfRoomModes, ref b.Mode);
                break;
            case BlockDefs.IfOk:
                CycleChip(("blk_m", i), x, y, h, u, IfOkModes, ref b.Mode);
                break;
            case BlockDefs.IfChance:
                Stepper(("blk_n", i), x, y, h, u, ref b.N, 0f, 100f, 5f, "%");
                break;
            case BlockDefs.IfScene:
                x = InvertChip(("blk_inv", i), x, y, h, u, IsNotModes, b);
                CycleChip(("blk_m", i), x, y, h, u, BlockScripts.SceneNames, ref b.Mode);
                break;
            case BlockDefs.IfMoney:
                x = CycleChip(("blk_m", i), x, y, h, u, BlockScripts.MoneySources, ref b.Mode);
                x = InvertChip(("blk_inv", i), x, y, h, u, CmpModes, b);
                Stepper(("blk_n", i), x, y, h, u, ref b.N, 0f, 1000000000f, b.N < 100f ? 10f : b.N < 1000f ? 50f : 500f, "");
                break;
            case BlockDefs.IfHp:
                x = InvertChip(("blk_inv", i), x, y, h, u, CmpModes, b);
                Stepper(("blk_n", i), x, y, h, u, ref b.N, 0f, 500f, 5f, "");
                break;
            case BlockDefs.IfVar:
                b.Text = RockGui.TextBox("blk_t" + i, new Rect(x, y, 46f * u, h), b.Text, u);
                x += 49f * u;
                x = InvertChip(("blk_inv", i), x, y, h, u, CmpModes, b);
                Stepper(("blk_n", i), x, y, h, u, ref b.N, -1000000f, 1000000f, Mathf.Abs(b.N) < 10f ? 1f : Mathf.Abs(b.N) < 100f ? 5f : 50f, "");
                break;
            case BlockDefs.SetVar:
                b.Text = RockGui.TextBox("blk_t" + i, new Rect(x, y, 46f * u, h), b.Text, u);
                x += 49f * u;
                x = CycleChip(("blk_m", i), x, y, h, u, VarOps, ref b.Mode);
                Stepper(("blk_n", i), x, y, h, u, ref b.N, -1000000f, 1000000f, Mathf.Abs(b.N) < 10f ? 1f : Mathf.Abs(b.N) < 100f ? 5f : 50f, "");
                break;
            case BlockDefs.Notify:
            case BlockDefs.Log:
                b.Text = RockGui.TextBox("blk_t" + i, new Rect(x, y, Mathf.Max(30f * u, right - x), h), b.Text, u);
                break;
            case BlockDefs.SetFeature:
                x = CycleChip(("blk_m", i), x, y, h, u, FeatureModes, ref b.Mode);
                NameField(b, i, x, right, y, h, u, BlockScripts.FeatureNames);
                break;
            case BlockDefs.SetValue:
                x = Stepper(("blk_n", i), x, y, h, u, ref b.N, -100000f, 100000f, Mathf.Abs(b.N) < 10f ? 0.1f : Mathf.Abs(b.N) < 100f ? 1f : 10f, "=");
                NameField(b, i, x, right, y, h, u, BlockScripts.NumberNames);
                break;
            case BlockDefs.Teleport:
                x = CycleChip(("blk_m", i), x, y, h, u, BlockScripts.TeleportDestinations, ref b.Mode);
                if (b.Mode == 6 || b.Mode == 7) Stepper(("blk_n", i), x, y, h, u, ref b.N, 1f, 200f, b.N >= 20f ? 5f : 1f, "м");
                break;
            case BlockDefs.PickPlayer:
                CycleChip(("blk_m", i), x, y, h, u, BlockScripts.PlayerPicks, ref b.Mode);
                break;
            case BlockDefs.SpawnItem:
            case BlockDefs.SpawnValuable:
            {
                x = Stepper(("blk_n", i), x, y, h, u, ref b.N, 1f, 100f, b.N >= 10f ? 5f : 1f, "раз");
                x = Stepper(("blk_n2", i), x, y, h, u, ref b.N2, 0.05f, 5f, 0.05f, "пауза");
                List<string> names = null;
                try { names = b.Kind == BlockDefs.SpawnItem ? ItemSpawnerHack.ScanItemNames() : EconomyHack.ScanValuableNames(); }
                catch (Exception) { }
                NameField(b, i, x, right, y, h, u, names);
                break;
            }
            case BlockDefs.CallScript:
                NameField(b, i, x, right, y, h, u, new List<string>(BlockScripts.SavedNames));
                break;
        }
    }

    // Text box + "⇄" (cycle through the known names). Leaving the box completes a partial name to the full
    // one it matches, so saved scripts always store exact names.
    private static void NameField(Block b, int i, float x, float right, float y, float h, float u, List<string> names)
    {
        string id = "blk_t" + i;
        Rect browse = new(right - 10f * u, y, 10f * u, h);
        Rect box = new(x, y, Mathf.Max(30f * u, browse.x - 2f * u - x), h);
        b.Text = RockGui.TextBox(id, box, b.Text, u);
        if (RockGui.Button(("blk_next", i), browse, "⇄", RockGui.Kind.Normal, u, 6.5f))
            b.Text = BlockScripts.NextName(names, b.Text);
        if (!CustomGui.IsFocused(id) && !string.IsNullOrWhiteSpace(b.Text))
        {
            string resolved = BlockScripts.ResolveName(names, b.Text);
            if (resolved != null && !string.Equals(resolved, b.Text, StringComparison.Ordinal)) b.Text = resolved;
        }
    }

    // "label [-] value [+]" - returns the x where the next control may start.
    private static float Stepper(object key, float x, float y, float h, float u, ref float value, float min, float max, float step, string label)
    {
        int font = RockGui.Font(6.5f, u);
        if (!string.IsNullOrEmpty(label))
        {
            float lw = CustomGui.MeasureText(label, font) + 3f * u;
            CustomGui.Text(new Rect(x, y, lw, h), label, font, RockGui.White(0.6f), TextAnchor.MiddleLeft);
            x += lw;
        }
        string shown = value.ToString("0.##");
        float bw = 8f * u, vw = Mathf.Max(17f * u, CustomGui.MeasureText(shown, RockGui.Font(7f, u)) + 4f * u);
        if (RockGui.Button((key, "-"), new Rect(x, y, bw, h), "−", RockGui.Kind.Normal, u, 7f)) value = Mathf.Max(min, value - step);
        CustomGui.Text(new Rect(x + bw, y, vw, h), shown, RockGui.Font(7f, u), RockGui.White(), TextAnchor.MiddleCenter);
        if (RockGui.Button((key, "+"), new Rect(x + bw + vw, y, bw, h), "+", RockGui.Kind.Normal, u, 7f)) value = Mathf.Min(max, value + step);
        value = (float)Math.Round(value, 2); // repeated +0.05 steps must not drift to 0.30000001
        return x + bw * 2f + vw + 4f * u;
    }

    private static float CycleChip(object key, float x, float y, float h, float u, string[] options, ref int mode)
    {
        mode = Mathf.Clamp(mode, 0, options.Length - 1);
        string text = options[mode];
        float w = CustomGui.MeasureText(text, RockGui.Font(6.5f, u)) + 10f * u;
        if (RockGui.Button(key, new Rect(x, y, w, h), text, RockGui.Kind.Primary, u, 6.5f)) mode = (mode + 1) % options.Length;
        return x + w + 3f * u;
    }

    // Two-way chip stored in N2 (0 / 1) - "является / не является", "≥ / <".
    private static float InvertChip(object key, float x, float y, float h, float u, string[] options, Block b)
    {
        int inv = b.N2 >= 0.5f ? 1 : 0;
        x = CycleChip(key, x, y, h, u, options, ref inv);
        b.N2 = inv;
        return x;
    }
}
