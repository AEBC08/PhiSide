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

    public float ColorMinAlpha = 0.05f;

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

    // 音符可见性阈值：文档「Y(t) > 2H 时变得不可见」，其中 2H = 3.3333336 Y
    private const float MaxVisibleY = 3.3333336f;
    // 文档 getMaxVisiblePos 中的魔数
    private const float VisibleMagic = 11718.75f;

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

    public override void _PhysicsProcess(double delta)
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
            note.Visible = IsNoteVisible(note.NoteData, false);

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
            bool judged = GameTTime >= noteData.Time;

            if (!judged)
            {
                note.Position = new Vector2(
                    noteData.PositionX * RootNode.ViewportV.OneX,
                    ySign * (float)((noteData.FloorPosition - LineFloorPosition) * RootNode.ViewportV.OneY));

                double lengthY = noteData.Speed * noteData.HoldTime * T1Time;
                note.UpdateHoldVisual((float)(lengthY * RootNode.ViewportV.OneY), false);
                note.Visible = IsNoteVisible(noteData, true);
            }
            else
            {
                // 头部钉在判定线上，剩余部分近端贴住判定线
                note.Position = new Vector2(noteData.PositionX * RootNode.ViewportV.OneX, 0f);

                double tailY = noteData.Speed * ((double)noteData.Time + noteData.HoldTime - GameTTime) * T1Time;
                note.UpdateHoldVisual((float)(Math.Max(tailY, 0d) * RootNode.ViewportV.OneY), true);
                note.Visible = true;
            }

            NoteProgress(note);
        }
    }

    // 文档「音符渲染细节」：
    //   1. 长度（speed 或 holdTime 取整后）为 0 的 Hold 不渲染；
    //   2. currentFloorPosition < -0.001 且未打击时不渲染；
    //   3. 判定线实时位置超过 getMaxVisiblePos(音符 floorPosition) 时不渲染；
    //   4. Y(t) > 2H（即 3.3333336 Y）时不渲染，其中非 Hold 为 η·PN(t)、Hold 头部为 PN(t)。
    private bool IsNoteVisible(ChartLoader.NoteV3 noteData, bool isHold, bool headHit = false)
    {
        if (isHold && (noteData.Speed <= 0f || noteData.HoldTime <= 0))
        {
            // 长度为 0 的 Hold 不渲染；判定与连击仍照常处理
            return false;
        }

        if (headHit)
        {
            // 头部已判定并钉在判定线上：此时 currentFloorPosition 必然变成了负数，
            // 再按判定线位置剔除会把整条 Hold 提前隐藏
            return true;
        }

        double currentFloorPosition = noteData.FloorPosition - LineFloorPosition;
        if (currentFloorPosition < -0.001) return false;

        float y = (float)(isHold ? currentFloorPosition : noteData.Speed * currentFloorPosition);
        if (y > MaxVisibleY) return false;

        if ((float)LineFloorPosition > GetMaxVisiblePos(noteData.FloorPosition)) return false;

        return true;
    }

    // 逐字复刻文档给出的 getMaxVisiblePos：
    // 当判定线实时位置超过返回值时，该音符不会被渲染。
    private static float GetMaxVisiblePos(float x)
    {
        float n = x;  // C# 的 float 即 float32，等价于文档中的 Math.fround
        if (!float.IsFinite(n))
        {
            // 文档在此抛异常；退化为「不可见」，避免异常冒到 Godot 主循环
            return float.NegativeInfinity;
        }

        float prime = n >= VisibleMagic
            ? MathF.Pow(2f, MathF.Floor(1f + MathF.Log2(n / VisibleMagic)))
            : 1f;

        double a = (double)n / prime + 0.001;
        float r = (float)a;
        if ((double)r <= a) return r * prime;

        // 文档在此把 a 的 float32 位模式向零方向移动 1 ULP 后再乘 prime
        float aDown = r > 0f || float.IsNegative(r) ? MathF.BitDecrement(r) : MathF.BitIncrement(r);
        return aDown * prime;
    }

    public void NoteProgress(NoteNode note)
    {
        if (GameTTime >= note.NoteData.Time)
        {
            note.AutoPlay();
        }
    }
}