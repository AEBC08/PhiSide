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

    // 首次布局：Size/Position 走 CallDeferred，保持原有的延后时序
    public void Init()
    {
        ApplyViewport(deferred: true);
    }

    public void ViewportUpdate()
    {
        ApplyViewport(deferred: false);
    }

    private void ApplyViewport(bool deferred)
    {
        if (RootNode is null)
        {
            GD.PrintErr("SafeAreaBG 的 RootNode 未绑定，跳过视口布局更新");
            return;
        }

        var viewportSize = GameViewport.GetVisibleRect().Size;
        if (viewportSize.Y < viewportSize.X && viewportSize.X / viewportSize.Y > VRatio)
        {
            int newWidth = (int)Math.Ceiling(viewportSize.Y * VRatio);
            if (deferred)
            {
                Callable.From(() => {
                    Size = new Vector2(newWidth, viewportSize.Y);
                    Position = new Vector2((viewportSize.X - newWidth) / 2, Position.Y);
                }).CallDeferred();
            }
            else
            {
                Size = new Vector2(newWidth, viewportSize.Y);
                Position = new Vector2((viewportSize.X - newWidth) / 2, Position.Y);
            }
            RootNode.ViewportV = new LengthVector(newWidth, viewportSize.Y);
        }
        else
        {
            if (deferred)
            {
                Callable.From(() => {
                    Size = new Vector2(viewportSize.X, viewportSize.Y);
                    Position = Vector2.Zero;
                }).CallDeferred();
            }
            else
            {
                Size = new Vector2(viewportSize.X, viewportSize.Y);
                Position = Vector2.Zero;
            }
            RootNode.ViewportV = new LengthVector(viewportSize.X, viewportSize.Y);
        }
    }
}
