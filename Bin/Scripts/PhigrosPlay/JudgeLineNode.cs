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

	public bool IsPlaying;
	public double GameTime;
	public double GameTTime;
	
	public int LastSpeedEventsIndex;
	public double SFloorPosition;
	public double LastSFloorPosition;

	public double LineFloorPosition;

	public int SpeedEventsIndex;
	public int DisappearEventsIndex;
	public int MoveEventsIndex;
	public int RotateEventIndex;

	public void Init()
	{
		LineSprite = GetNode<Sprite2D>("JudgeLine");
		NotesNode = GetNode<Node2D>("Notes");
		T1Time = 1.875 / LineData.Bpm;
		BeatTime = 30 / LineData.Bpm;  // Hold 动画间隔
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
		foreach (var noteData in LineData.NotesAbove)
		{
			NoteNode newNote = AddNote(noteData);
			if (newNote is null) continue;
			newNote.IsAbove = true;
			if (newNote.IsHold)
			{
				HoldsAboveList.Add(newNote);
			}
			else
			{
				NotesAboveList.Add(newNote);
			}
		}
		foreach (var noteData in LineData.NotesBelow)
		{
			NoteNode newNote = AddNote(noteData);
			if (newNote is null) continue;
			newNote.Position = -newNote.Position;
			newNote.RotationDegrees = 180;
			if (newNote.IsHold)
			{
				HoldsBelowList.Add(newNote);
			}
			else
			{
				NotesBelowList.Add(newNote);
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
				newNote.OnNoteDestroyed = RemoveHoldFromList;
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
		newNote.OnNoteDestroyed = RemoveNoteFromList;
		newNote.EffectAudio = effectAudio;
		newNote.LineNode = this;
		newNote.Position = new Vector2(noteData.PositionX * RootNode.ViewportV.OneX,
			(float)-(noteData.FloorPosition * (noteData.FloorPosition - LineFloorPosition) * RootNode.ViewportV.OneY));
		NotesNode.AddChild(newNote);
		return newNote;
	}
	
	public void RemoveNoteFromList(NoteNode note, bool isAbove)
	{
		if (isAbove)
		{
			NotesAboveList.Remove(note);
		}
		else
		{
			NotesBelowList.Remove(note);
		}
	}
	
	public void RemoveHoldFromList(NoteNode note, bool isAbove)
    {
    	if (isAbove)
    	{
    		HoldsAboveList.Remove(note);
    	}
    	else
    	{
    		HoldsBelowList.Remove(note);
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
		while (SpeedEventsIndex < LineData.SpeedEvents.Count - 1 && LineData.SpeedEvents[SpeedEventsIndex].EndTime <= GameTTime)
		{
			SpeedEventsIndex++;
		}
		var speedEvent = LineData.SpeedEvents[SpeedEventsIndex];
		if (SpeedEventsIndex != 0 && LastSpeedEventsIndex != SpeedEventsIndex)
		{
			var lastSpeedEvent = LineData.SpeedEvents[SpeedEventsIndex - 1];
			SFloorPosition = LastSFloorPosition + lastSpeedEvent.Value * (speedEvent.StartTime - lastSpeedEvent.StartTime) * T1Time;
			LastSFloorPosition = SFloorPosition;
			LastSpeedEventsIndex = SpeedEventsIndex;
		}
		LineFloorPosition = SFloorPosition + speedEvent.Value * (GameTTime - speedEvent.StartTime) * T1Time;
	}

	public void UpdateDisappearEvent()
	{
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
		for (int i = NotesAboveList.Count - 1; i >= 0; i--)
		{
			var note = NotesAboveList[i];
			if (!IsInstanceValid(note)) continue;
			note.Position = new Vector2(note.NoteData.PositionX * RootNode.ViewportV.OneX,
				(float)-(note.NoteData.Speed * (note.NoteData.FloorPosition - LineFloorPosition) * RootNode.ViewportV.OneY));

			NoteProgress(note);
		}
		for (int i = NotesBelowList.Count - 1; i >= 0; i--)
		{
			var note = NotesBelowList[i];
			if (!IsInstanceValid(note)) continue;
			note.Position = new Vector2(note.NoteData.PositionX * RootNode.ViewportV.OneX,
				(float)(note.NoteData.Speed * (note.NoteData.FloorPosition - LineFloorPosition) * RootNode.ViewportV.OneY));

			NoteProgress(note);
		}
		UpdateHoldNotes();
	}

	public void UpdateHoldNotes()
	{
		for (int i = HoldsAboveList.Count - 1; i >= 0; i--)
		{
			var note = HoldsAboveList[i];
			if (!IsInstanceValid(note)) continue;

			note.Position = new Vector2(note.NoteData.PositionX * RootNode.ViewportV.OneX,
            				(float)-((note.NoteData.FloorPosition - LineFloorPosition) * RootNode.ViewportV.OneY));

			/*double holdTall;
			if (GameTTime <= note.NoteData.Time)
			{
				holdTall = note.NoteData.FloorPosition - LineFloorPosition + note.NoteData.Speed * note.NoteData.HoldTime * T1Time;
			}
			else
			{
				holdTall = note.NoteData.Speed * (note.NoteData.Time + note.NoteData.HoldTime - GameTTime) * T1Time;
			}
			note.Scale = new Vector2(note.Scale.X, (float)((holdTall - note.Position.Y) / note.Texture.GetSize().Y));*/
			
			HoldProgress(note);
		}
		for (int i = HoldsBelowList.Count - 1; i >= 0; i--)
		{
			var note = HoldsBelowList[i];
			if (!IsInstanceValid(note)) continue;
			
			note.Position = new Vector2(note.NoteData.PositionX * RootNode.ViewportV.OneX,
				(float)((note.NoteData.FloorPosition - LineFloorPosition) * RootNode.ViewportV.OneY));

			/*double holdTall;
			if (GameTTime <= note.NoteData.Time)
			{
				holdTall = (note.NoteData.FloorPosition - LineFloorPosition + note.NoteData.Speed * note.NoteData.HoldTime * T1Time) * RootNode.ViewportV.OneY;
			}
			else
			{
				holdTall = (note.NoteData.Speed * (note.NoteData.Time + note.NoteData.HoldTime - GameTTime) * T1Time) * RootNode.ViewportV.OneY;
			}

			var textureY = note.Texture.GetSize().Y;
			note.Scale = new Vector2(note.Scale.X, (float)((holdTall - note.Position.Y) / textureY));*/
			// note.Offset = new Vector2(0, -(textureY / 2));
			
			HoldProgress(note);
		}
	}

	public void NoteProgress(NoteNode note)
	{
		if (GameTTime >= note.NoteData.Time)
		{
			note.AutoPlay();
		}
	}
	
	public void HoldProgress(NoteNode note)
    {
    	if (GameTTime >= note.NoteData.Time)
    	{
    		note.AutoPlay();
    	}
    }
}
