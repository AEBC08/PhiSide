using Godot;
using System;
using PhigrosChart;
using AExtension;

public partial class PhigrosPlay : Node
{
    [Export] public Control PauseMenu;
    [Export] public Node2D ObjectsNode;

    [Export] public Label NameLabel;
    [Export] public Label LevelLabel;

    [Export] public TextureRect Background;

    [Export] public AudioStreamPlayer MusicPlayer;
    [Export] public AudioStreamPlayer TabAudio;
    [Export] public AudioStreamPlayer DragAudio;
    [Export] public AudioStreamPlayer FlickAudio;
    [Export] public AudioStreamPlayer HoldAudio;

    [Export] public VBoxContainer ComboBox;

    [Export] public ProgressBar PlayProgressBar;
    [Export] public Label ScoreLabel;
    [Export] public Label ComboLabel;

    [Export] public Node2D LinesNode;

    [Export] public Node2D EffectsNode;

    public LengthVector ViewportV;

    public JudgeLineNode JudgeLineO;
    public AnimatedSprite2D HitEffectO;
    public NoteNode TapO;
    public NoteNode DragO;
    public NoteNode FlickO;
    public NoteNode HoldO;

    public ChartLoader.ChartV3 Chart;
    public bool IsPlaying;
    public double GameTime;
    public int ComboNum;

    public override void _Ready()
    {
        ViewportV = new LengthVector(
            (float)ProjectSettings.GetSetting("display/window/size/viewport_width"),
            (float)ProjectSettings.GetSetting("display/window/size/viewport_height")
        );
        OInit();
        InfoInit();
        if (Chart is null)
        {
            // 谱面未加载成功时不再继续初始化，避免后续空引用
            return;
        }

        LineInit();
        IsPlaying = true;
        MusicPlayer.Play();
    }

    public void InfoInit()
    {
        // 谱面路径目前写死为测试谱面，后续可改为按用户选择加载
        using var file = Godot.FileAccess.Open("res://Assets/Test/1.json", Godot.FileAccess.ModeFlags.Read);
        if (file is null)
        {
            GD.PrintErr("谱面文件缺失或无法打开: res://Assets/Test/1.json，错误: ", Godot.FileAccess.GetOpenError());
            return;
        }

        try
        {
            Chart = ChartLoader.LoadChart(file.GetAsText());
        }
        catch (Exception e)
        {
            // 谱面内容损坏时不让异常冒到 Godot 主循环：记录原因并保持 Chart 为 null，
            // 由 _Ready 的判空分支终止初始化。
            GD.PrintErr("谱面解析失败: res://Assets/Test/1.json — ", e.Message);
            Chart = null;
        }
    }

    public void OInit()
    {
        Node2D sObjects = ResourceLoader.Load<PackedScene>("res://Bin/Scenes/PhigrosPlay/Objects.tscn").Instantiate<Node2D>();

        T Template<T>(string path) where T : Node2D
        {
            T node = sObjects.GetNode<T>(path);
            node.Position = new Vector2(0, 0);
            node.Rotation = 0;
            return node;
        }

        JudgeLineO = Template<JudgeLineNode>("JudgeLine");
        HitEffectO = Template<AnimatedSprite2D>("Animation");
        TapO = Template<NoteNode>("Tap");
        DragO = Template<NoteNode>("Drag");
        FlickO = Template<NoteNode>("Flick");
        HoldO = Template<NoteNode>("Hold");
    }

    public void LineInit()
    {
        int lineId = 0;
        foreach (var lineData in Chart.JudgeLines)
        {
            // 谱面里把 notesAbove / notesBelow 写成 null 时兜底，
            // 否则下面的 .Count 取用与后续 NoteInit 的 foreach 都会抛 NRE
            lineData.NotesAbove ??= new System.Collections.Generic.List<ChartLoader.NoteV3>();
            lineData.NotesBelow ??= new System.Collections.Generic.List<ChartLoader.NoteV3>();

            if (lineData.NotesAbove.Count == 0 && lineData.NotesBelow.Count == 0 && lineData.MoveEvents.Count <= 1 && lineData.RotateEvents.Count <= 1) {continue;}
            JudgeLineNode newLine = (JudgeLineNode)JudgeLineO.Duplicate();
            newLine.LineData = lineData;
            newLine.RootNode = this;
            newLine.GetNode<Label>("LineID").Text = lineId.ToString();
            LinesNode.AddChild(newLine);
            newLine.Init();
            lineId++;
        }
        GD.Print("LineNum: ", lineId);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!IsPlaying) return;
        // 获取音频基准时间（单位：秒）
        if (MusicPlayer.IsPlaying())
        {
            GameTime = MusicPlayer.GetPlaybackPosition() + AudioServer.GetTimeSinceLastMix() - AudioServer.GetOutputLatency();
        }
        else
        {
            GameTime += delta;  // 时间推进
        }

        ComboBox.Visible = ComboNum >= 3;
        ComboLabel.Text = ComboNum.ToString();
    }
}
