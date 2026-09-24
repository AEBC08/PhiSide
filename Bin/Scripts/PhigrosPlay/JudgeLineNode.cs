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

    public double T1Time;
    public double BeatTime;

    public double GameTime;
    public double GameTTime;

    public int LastSpeedEventsIndex;
    public double SFloorPosition;

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
        GameTTime = RootNode.GameTime / T1Time;
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
        newNote.Position = new Vector2(noteData.PositionX * RootNode.ViewportV.OneX,
            (float)-(noteData.FloorPosition * (noteData.FloorPosition - LineFloorPosition) * RootNode.ViewportV.OneY));
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
        GameTime = RootNode.GameTime;
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

    public void UpdateSpeedEvents()
    {
        if (LineData.SpeedEvents.Count == 0) return;
        while (SpeedEventsIndex < LineData.SpeedEvents.Count - 1 && LineData.SpeedEvents[SpeedEventsIndex].EndTime <= GameTTime)
        {
            SpeedEventsIndex++;
        }
        var speedEvent = LineData.SpeedEvents[SpeedEventsIndex];
        if (SpeedEventsIndex != 0 && LastSpeedEventsIndex != SpeedEventsIndex)
        {
            var lastSpeedEvent = LineData.SpeedEvents[SpeedEventsIndex - 1];
            SFloorPosition = SFloorPosition + lastSpeedEvent.Value * (speedEvent.StartTime - lastSpeedEvent.StartTime) * T1Time;
            LastSpeedEventsIndex = SpeedEventsIndex;
        }
        LineFloorPosition = SFloorPosition + speedEvent.Value * (GameTTime - speedEvent.StartTime) * T1Time;
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

    private void UpdateHoldSide(List<NoteNode> holds, float ySign)
    {
        for (int i = holds.Count - 1; i >= 0; i--)
        {
            var note = holds[i];
            if (!IsInstanceValid(note)) continue;

            // 头部位置（屏幕 Y，世界向上→屏幕向上取负）
            note.Position = new Vector2(
                note.NoteData.PositionX * RootNode.ViewportV.OneX,
                ySign * (float)((note.NoteData.FloorPosition - LineFloorPosition) * RootNode.ViewportV.OneY)
            );

            // 正确计算 Hold 条长度（尾部屏幕 Y - 头部屏幕 Y 的差值）
            float headScreenY = note.Position.Y;
            float tailScreenY = ySign * (float)(note.NoteData.Speed * (note.NoteData.Time + note.NoteData.HoldTime - GameTTime) * T1Time * RootNode.ViewportV.OneY);
            float lengthPx = headScreenY - tailScreenY;
            if (lengthPx < 0) lengthPx = 0;

            // 贴图为空或高度为 0 时不做除法，避免 NRE / 除零
            float textureHeight = note.Texture is null ? 0f : note.Texture.GetSize().Y;
            note.Scale = new Vector2(note.Scale.X, lengthPx / Math.Max(1f, textureHeight));
            NoteProgress(note);
        }
    }

    public void NoteProgress(NoteNode note)
    {
        if (GameTTime >= note.NoteData.Time)
        {
            note.AutoPlay();
        }
    }
}
