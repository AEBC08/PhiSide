using Godot;

// ReSharper disable CheckNamespace
public partial class FpsLabel : Label
{
    public int FrameCount;

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta)
    {
        FrameCount++;
        if (FrameCount >= 30)
        {
            Text = $"FPS: {1 / delta : 0.000}";
            FrameCount = 0;
        }
    }
}
