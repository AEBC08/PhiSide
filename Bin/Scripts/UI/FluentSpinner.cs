using Godot;
using System;
using System.Collections.Generic;

public partial class FluentSpinner : Label
{
    [Export] public int SpinnerVersion = 11;
    public List<string> SpinnerFrames = new();

    public override void _Ready()
    {
        GetSpinnerFrame();
    }

    public void GetSpinnerFrame()
    {
        int start, end;
        if (SpinnerVersion == 10)
        {
            start = 57426;
            end = 57547;
        }
        else
        {
            start = 57600;
            end = 57718;
        }
        for (int i = start; i <= end; i++)
        {
            try
            {
                SpinnerFrames.Add(char.ConvertFromUtf32(i));
            }
            catch (Exception e)
            {
                GD.PushError($"Error generating spinner frame {i}: {e.Message}");
            }
        }
    }

    public int FrameIndex;
    public override void _Process(double delta)
    {
        if (SpinnerFrames.Count == 0) return;
        if (FrameIndex >= SpinnerFrames.Count)
        {
            FrameIndex = 0;
        }
        Text = SpinnerFrames[FrameIndex];
        FrameIndex++;
    }
}
