using Godot;
using System;
using AExtension;

public partial class SafeAreaBG : ColorRect
{
    [Export] public PhigrosPlay RootNode;

    public Viewport GameViewport;

    public float VRatio = 16f / 9f;

    public override void _Ready()
    {
        GameViewport = GetViewport();
        GameViewport.Connect("size_changed", Callable.From(ViewportUpdate));
        Callable.From(Init).CallDeferred();
    }

    public void Init()
    {
        var viewportSize = GameViewport.GetVisibleRect().Size;
        if (viewportSize.Y < viewportSize.X && viewportSize.X / viewportSize.Y > VRatio)
        {
            int newWidth = (int)Math.Ceiling(viewportSize.Y * VRatio);
            Callable.From(() => {
                Size = new Vector2(newWidth, viewportSize.Y);
                Position = new Vector2((viewportSize.X - newWidth) / 2, Position.Y);
            }).CallDeferred();
            RootNode.ViewportV = new LengthVector(newWidth, viewportSize.Y);
        }
        else
        {
            Callable.From(() => {
                Size = new Vector2(viewportSize.X, viewportSize.Y);
                Position = Vector2.Zero;
            }).CallDeferred();
            RootNode.ViewportV = new LengthVector(viewportSize.X, viewportSize.Y);
        }
    }
    
    public void ViewportUpdate()
    {
        var viewportSize = GameViewport.GetVisibleRect().Size;
        if (viewportSize.Y < viewportSize.X && viewportSize.X / viewportSize.Y > VRatio)
        {
            int newWidth = (int)Math.Ceiling(viewportSize.Y * VRatio);
            Size = new Vector2(newWidth, viewportSize.Y); 
            Position = new Vector2((viewportSize.X - newWidth) / 2, Position.Y);
            RootNode.ViewportV = new LengthVector(newWidth, viewportSize.Y);
        }
        else
        {
            Size = new Vector2(viewportSize.X, viewportSize.Y);
            Position = Vector2.Zero;
            RootNode.ViewportV = new LengthVector(viewportSize.X, viewportSize.Y);
        }
    }
}
