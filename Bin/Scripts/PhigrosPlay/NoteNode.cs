using Godot;
using System;
using PhigrosChart;

public partial class NoteNode : Sprite2D
{
	public ChartLoader.NoteV3 NoteData;
	public Action<NoteNode, bool> OnNoteDestroyed;
	public bool IsAbove;
	public AudioStreamPlayer EffectAudio;
	public JudgeLineNode LineNode;
	public bool IsHold;
	public double HoldEndTime;
	public double BeatCount;

	public void AutoPlay()
	{
		if (IsHold)
		{
			if (BeatCount == 0) EffectAudio.Play();
			var nowBeat = LineNode.GameTime / LineNode.BeatTime;
			if (nowBeat - BeatCount >= 1)
			{
				BeatCount = nowBeat;
                AnimatedSprite2D newHitEffect2 = (AnimatedSprite2D)LineNode.RootNode.HitEffectO.Duplicate();
                newHitEffect2.Position = LineNode.GlobalTransform * new Vector2(Position.X, 0);
                LineNode.RootNode.EffectsNode.AddChild(newHitEffect2);
			}
			if (LineNode.GameTTime >= HoldEndTime)
			{
				OnNoteDestroyed?.Invoke(this, IsAbove);
				
				LineNode.RootNode.ComboNum++;
				QueueFree();
			}
		}
		else
		{
			OnNoteDestroyed?.Invoke(this, IsAbove);
			EffectAudio.Play();
			AnimatedSprite2D newHitEffect = (AnimatedSprite2D)LineNode.RootNode.HitEffectO.Duplicate();
			newHitEffect.Position = LineNode.GlobalTransform * new Vector2(Position.X, 0);
			LineNode.RootNode.EffectsNode.AddChild(newHitEffect);
			LineNode.RootNode.ComboNum++;
			QueueFree();
		}
	}
}
