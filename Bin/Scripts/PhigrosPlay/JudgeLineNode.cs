using Godot;
using System;
using System.Collections.Generic;
using PhigrosChart;

public partial class JudgeLineNode : Node2D
{
    public ChartLoader.JudgeLineV3 LineData;

    public Sprite2D LineSprite;
    public Node2D NotesNode;

    public PhigrosPlay RootNode;

    public float ColorMinAlpha = 0.00f;

    // 1 T = 1.875 / BPM 秒（文档「谱面格式」：1T 为 1/32 拍）
    public double T1Time;
    // Hold 打击特效间隔：与 BPM 成反比（BPM 30 → 1s，60 → 0.5s），与实际打击时刻无关
    public double BeatTime;

    // 谱面时间（已扣除 offset），单位秒
    public double GameTime;
    // 谱面时间换算成 T 数，事件与音符的时间轴都以此为单位
    public double GameTTime;

    // 速度事件累加出的判定线 floorPosition 前缀和
    public double SFloorPosition;

    // 判定线实时 floorPosition，单位 Y
    public double LineFloorPosition;

    public int SpeedEventsIndex;
    public int DisappearEventsIndex;
    public int MoveEventsIndex;
    public int RotateEventIndex;

    public void Init()
    {
        LineSprite = GetNode<Sprite2D>("JudgeLine");
        NotesNode = GetNode<Node2D>("Notes");
        // BPM 非有限值或非正数（含 NaN / Infinity）时按 1 处理，
        // 避免 T1Time/BeatTime 变成 0 或 Infinity 后让位置计算整体变成 NaN
        float bpm = float.IsFinite(LineData.Bpm) && LineData.Bpm > 0 ? LineData.Bpm : 1f;
        if (bpm != LineData.Bpm)
        {
            GD.PrintErr($"判定线 BPM 非法（{LineData.Bpm}），已按 1 处理");
        }
        T1Time = 1.875 / bpm;
        BeatTime = 30 / bpm;  // Hold 动画间隔
        NoteInit();
    }

    public readonly List<NoteNode> NotesAboveList = [];
    public readonly List<NoteNode> NotesBelowList = [];
    public readonly List<NoteNode> HoldsAboveList = [];
    public readonly List<NoteNode> HoldsBelowList = [];

    public void NoteInit()
    {
        // 谱面时间 = 音乐时间 - offset：offset 为正时谱面整体延后 |offset| 秒开始
        GameTTime = RootNode.ChartTime / T1Time;
        UpdateSpeedEvents();
        InitNoteSide(LineData.NotesAbove, NotesAboveList, HoldsAboveList, true);
        InitNoteSide(LineData.NotesBelow, NotesBelowList, HoldsBelowList, false);
    }

    // 上半场（isAbove = true）/ 下半场（isAbove = false）的区别只在 Y 取负、旋转 180 度以及入队的列表
    private void InitNoteSide(List<ChartLoader.NoteV3> noteDataList, List<NoteNode> notesList, List<NoteNode> holdsList, bool isAbove)
    {
        foreach (var noteData in noteDataList)
        {
            NoteNode newNote = AddNote(noteData);
            if (newNote is null) continue;
            newNote.IsAbove = isAbove;
            if (!isAbove)
            {
                newNote.Position = -newNote.Position;
                newNote.RotationDegrees = 180;
            }
            if (newNote.IsHold)
            {
                holdsList.Add(newNote);
            }
            else
            {
                notesList.Add(newNote);
            }
        }
    }

    public NoteNode AddNote(ChartLoader.NoteV3 noteData)
    {
        switch (noteData.Type)
        {
            case 1:
            {
                return CreateNote(noteData, RootNode.TapO, RootNode.TabAudio);
            }
            case 2:
            {
                return CreateNote(noteData, RootNode.DragO, RootNode.DragAudio);
            }
            case 3:
            {
                var newNote = CreateNote(noteData, RootNode.HoldO, RootNode.HoldAudio);
                newNote.OnNoteDestroyed = (note, isAbove) => RemoveFromList(note, isAbove, HoldsAboveList, HoldsBelowList);
                newNote.IsHold = true;
                newNote.HoldEndTime = noteData.Time + noteData.HoldTime;
                // Hold 需要把整张贴图拆成头 / 身 / 尾三段，拉伸时端帽保持原比例
                newNote.SetupHoldSlices();
                return newNote;
            }
            case 4:
            {
                return CreateNote(noteData, RootNode.FlickO, RootNode.FlickAudio);
            }
        }
        return null;
    }

    public NoteNode CreateNote(ChartLoader.NoteV3 noteData, NoteNode noteO, AudioStreamPlayer effectAudio)
    {
        NoteNode newNote = (NoteNode)noteO.Duplicate();
        newNote.NoteData = noteData;
        newNote.OnNoteDestroyed = (note, isAbove) => RemoveFromList(note, isAbove, NotesAboveList, NotesBelowList);
        newNote.EffectAudio = effectAudio;
        newNote.LineNode = this;
        // 初始位置与每帧公式保持同一表达式形状：
        // Hold 头部 Y(t) = PN(t)，其它音符 Y(t) = η·PN(t)（η 即音符自身的 speed）
        double currentFloorPosition = noteData.FloorPosition - LineFloorPosition;
        float offsetY = noteData.Type == 3
            ? (float)(currentFloorPosition * RootNode.ViewportV.OneY)
            : (float)(noteData.Speed * currentFloorPosition * RootNode.ViewportV.OneY);
        newNote.Position = new Vector2(noteData.PositionX * RootNode.ViewportV.OneX, -offsetY);
        NotesNode.AddChild(newNote);
        // 只有真正建出节点的音符（Type 1..4）才计入总数，未知类型不参与计分
        RootNode.TotalNotes++;
        return newNote;
    }

    public void RemoveFromList(NoteNode note, bool isAbove, List<NoteNode> aboveList, List<NoteNode> belowList)
    {
        if (isAbove)
        {
            aboveList.Remove(note);
        }
        else
        {
            belowList.Remove(note);
        }
    }

    public override void _Process(double delta)
    {
        if (!RootNode.IsPlaying) return;
        GameTime = RootNode.ChartTime;
        GameTTime = GameTime / T1Time;
        UpdateSpeedEvents();
        UpdateDisappearEvent();
        UpdateMoveEvent();
        UpdateRotateEvent();
        UpdateNotes();
    }

    public double CalculateProgress(int startTime, int endTime)
    {
        if (endTime == startTime) return 0; // 防止除以零
        return Math.Clamp((GameTTime - startTime) / (endTime - startTime), 0, 1);
    }

    // 文档「判定线实时参数」：
    //   v_k 为第 k 个速度事件的 value，p_k 为它的 floorPosition
    //   p_k = p_{k-1} + v_{k-1}·(t_k - t_{k-1})·(1.875/BPM)
    //   PJ(t) = p_k + v_k·(t - t_k)·(1.875/BPM)
    // 一帧内可能跨过多个速度事件，必须逐段累加，否则判定线位置会漂移。
    public void UpdateSpeedEvents()
    {
        if (LineData.SpeedEvents.Count == 0) return;

        while (SpeedEventsIndex < LineData.SpeedEvents.Count - 1 &&
               LineData.SpeedEvents[SpeedEventsIndex].EndTime <= GameTTime)
        {
            var speedEvent = LineData.SpeedEvents[SpeedEventsIndex];
            var nextEvent = LineData.SpeedEvents[SpeedEventsIndex + 1];
            SFloorPosition += speedEvent.Value * (nextEvent.StartTime - speedEvent.StartTime) * T1Time;
            SpeedEventsIndex++;
        }

        var currentEvent = LineData.SpeedEvents[SpeedEventsIndex];
        LineFloorPosition = SFloorPosition + currentEvent.Value * (GameTTime - currentEvent.StartTime) * T1Time;
    }

    public void UpdateDisappearEvent()
    {
        if (LineData.DisappearEvents.Count == 0) return;
        while (DisappearEventsIndex < LineData.DisappearEvents.Count - 1 && LineData.DisappearEvents[DisappearEventsIndex].EndTime <= GameTTime)
        {
            DisappearEventsIndex++;
        }
        var disappearEvent = LineData.DisappearEvents[DisappearEventsIndex];
        double progress = CalculateProgress(disappearEvent.StartTime, disappearEvent.EndTime);
        LineSprite.Modulate = new Color(LineSprite.Modulate, (float)Math.Clamp(progress * (disappearEvent.Alpha2 - disappearEvent.Alpha1) + disappearEvent.Alpha1, ColorMinAlpha, 1));
    }

    public void UpdateMoveEvent()
    {
        if (LineData.MoveEvents.Count == 0) return;
        while (MoveEventsIndex < LineData.MoveEvents.Count - 1 && LineData.MoveEvents[MoveEventsIndex].EndTime <= GameTTime)
        {
            MoveEventsIndex++;
        }
        var moveEvent = LineData.MoveEvents[MoveEventsIndex];
        double progress = CalculateProgress(moveEvent.StartTime, moveEvent.EndTime);
        Position = new Vector2((float)(RootNode.ViewportV.W * (progress * (moveEvent.X2 - moveEvent.X1) + moveEvent.X1)),
            (float)(RootNode.ViewportV.H * (1 - (progress * (moveEvent.Y2 - moveEvent.Y1) + moveEvent.Y1))));
    }

    public void UpdateRotateEvent()
    {
        if (LineData.RotateEvents.Count == 0) return;
        while (RotateEventIndex < LineData.RotateEvents.Count - 1 && LineData.RotateEvents[RotateEventIndex].EndTime <= GameTTime)
        {
            RotateEventIndex++;
        }
        var rotateEvent = LineData.RotateEvents[RotateEventIndex];
        double progress = CalculateProgress(rotateEvent.StartTime, rotateEvent.EndTime);
        RotationDegrees = (float)-(progress * (rotateEvent.Angle2 - rotateEvent.Angle1) + rotateEvent.Angle1);
    }

    public void UpdateNotes()
    {
        UpdateNoteSide(NotesAboveList, -1f);
        UpdateNoteSide(NotesBelowList, 1f);
        UpdateHoldNotes();
    }

    private void UpdateNoteSide(List<NoteNode> notes, float ySign)
    {
        for (int i = notes.Count - 1; i >= 0; i--)
        {
            var note = notes[i];
            if (!IsInstanceValid(note)) continue;
            note.Position = new Vector2(note.NoteData.PositionX * RootNode.ViewportV.OneX,
                ySign * (float)(note.NoteData.Speed * (note.NoteData.FloorPosition - LineFloorPosition) * RootNode.ViewportV.OneY));
            // 可见性交给场景父节点的 clip_contents 裁剪，代码不再逐音符剔除；
            // 位置计算与判定照常进行（判定与可见性无关）。

            NoteProgress(note);
        }
    }

    public void UpdateHoldNotes()
    {
        // 判定线上方的 Hold 音符（屏幕 Y 取负）
        UpdateHoldSide(HoldsAboveList, -1f);

        // 判定线下方的 Hold 音符（屏幕 Y 取正）
        UpdateHoldSide(HoldsBelowList, 1f);
    }

    // 文档「音符的实时参数」：
    //   未判定时（t ≤ tN）头 Y(t) = PN(t)，尾 YT(t) = PN(t) + η·tH·(1.875/BPM)
    //     → 头贴判定线移动，长度恒为 d = η·tH·(1.875/BPM)
    //   已判定时（tN < t ≤ tN+tH）尾 YT(t) = η·(tN+tH-t)·(1.875/BPM)
    //     → 头钉在判定线上（并隐藏头部图形），尾向判定线收缩
    private void UpdateHoldSide(List<NoteNode> holds, float ySign)
    {
        for (int i = holds.Count - 1; i >= 0; i--)
        {
            var note = holds[i];
            if (!IsInstanceValid(note)) continue;

            var noteData = note.NoteData;
            // 一到判定线就进入「正常拉伸」（头部钉在判定线上，剩余部分收缩），与是否命中无关：
            // 漏掉的 Hold 同样这样拉伸，只是要等到漏掉时刻才变灰。
            bool judged = note.Judged || GameTTime >= noteData.Time;

            if (!judged)
            {
                note.Position = new Vector2(
                    noteData.PositionX * RootNode.ViewportV.OneX,
                    ySign * (float)((noteData.FloorPosition - LineFloorPosition) * RootNode.ViewportV.OneY));

                double lengthY = noteData.Speed * noteData.HoldTime * T1Time;
                note.UpdateHoldVisual((float)(lengthY * RootNode.ViewportV.OneY), false);
                // 可见性交给场景父节点的 clip_contents 裁剪，这里不再逐音符剔除
            }
            else
            {
                // 头部钉在判定线上，剩余部分近端贴住判定线
                note.Position = new Vector2(noteData.PositionX * RootNode.ViewportV.OneX, 0f);

                double tailY = noteData.Speed * ((double)noteData.Time + noteData.HoldTime - GameTTime) * T1Time;
                note.UpdateHoldVisual((float)(Math.Max(tailY, 0d) * RootNode.ViewportV.OneY), true);
            }

            NoteProgress(note);
        }
    }

    // 自动演奏：音符到点即命中。
    // 手动演奏：到点还没被命中就算漏掉（各类型判定窗口见 PhigrosPlay.Manual.cs）；
    //           Hold 过完结束时刻收尾（没命中的先按 Miss 结算，免得短 Hold 永远不收尾）。
    public void NoteProgress(NoteNode note)
    {
        if (!RootNode.ManualPlay)
        {
            if (GameTTime >= note.NoteData.Time)
            {
                note.AutoPlay();
            }
            return;
        }

        if (note.IsHold && GameTTime >= note.HoldEndTime)
        {
            if (!note.Judged) note.ManualMiss();
            note.FinishHold();
            return;
        }

        if (note.Judged) return;

        // 音符时间以本判定线的 T 为单位（1T = 1.875 / BPM 秒）
        double late = (GameTTime - note.NoteData.Time) * T1Time;
        if (late > RootNode.MissSecondsOf(note))
        {
            note.ManualMiss();
        }
    }
}
