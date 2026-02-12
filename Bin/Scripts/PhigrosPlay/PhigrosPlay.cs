using Godot;
using System;
using System.IO;
using PhigrosChart;
using AExtension;
using AudioLoader;

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

	    LineInit();
	    IsPlaying = true;
	    MusicPlayer.Play();
    }

    public void InfoInit()
    {
	    var path = @"F:\谱面\官方\Diamond_Eyes_chart\Chart_IN_#5794.json";
            // var path = @"F:\谱面\官方\Alice_in_a_xxxxxxxx_chart\Chart_IN_#2962.json";
            // var path = @"F:\谱面\官方\Aphasia_chart\Chart_IN_#2380.json";
            // var path = @"F:\谱面\官方\开心病_chart\Chart_IN_#2162.json";
            // var path = @"F:\谱面\官方\SpasmodicSP\SpasmodicSP.json";
	    // var path = @"F:\谱面\官方\今年も「雪降り、メリクリ」目指して頑張ります！！\今年も「雪降り、メリクリ」目指して頑張ります！！.json";
	    // var path = @"F:\谱面\官方\RetributionSP\Chart_IN.json";
	    // var path = @"F:\谱面\官方\Dlyrotz-SUPER ULTIMATE AMAZING AWESOME PRO MAX EXTRA REMIX-\IN.json";
	    Chart = ChartLoader.LoadChart(File.ReadAllText(path));
	    var path2 = @"F:\谱面\官方\Diamond_Eyes_chart\music_#5769.wav";
	    // var path2 = @"F:\谱面\官方\Alice_in_a_xxxxxxxx_chart\music_#4720.wav";
	    // var path2 = @"F:\谱面\官方\Aphasia_chart\music_#663.wav";
	    // var path2 = @"F:\谱面\官方\开心病_chart\music_#4959.wav";
	    // var path2 = @"F:\谱面\官方\SpasmodicSP\SpasmodicSP.ogg";
	    // var path2 = @"F:\谱面\官方\今年も「雪降り、メリクリ」目指して頑張ります！！\今年も「雪降り、メリクリ」目指して頑張ります！！.wav";
	    // var path2 = @"F:\谱面\官方\RetributionSP\music.wav";
	    // var path2 = @"F:\谱面\官方\Dlyrotz-SUPER ULTIMATE AMAZING AWESOME PRO MAX EXTRA REMIX-\music.ogg";
	    DirAccess.CopyAbsolute(path2, ProjectSettings.GlobalizePath("user://music.wav"));
	    DirAccess.Open("user://").ListDirBegin();

	    MusicPlayer.Stream = GodotAudioLoader.LoadAudioFile("user://music.wav");
    }

    public void OInit()
    {
	    Node2D sObjects = ResourceLoader.Load<PackedScene>("res://Bin/Scenes/PhigrosPlay/Objects.tscn").Instantiate<Node2D>();
	    JudgeLineO = sObjects.GetNode<JudgeLineNode>("JudgeLine");
	    // JudgeLineO.Visible = false;
	    JudgeLineO.Position = new Vector2(0, 0);
	    JudgeLineO.Rotation = 0;
	    HitEffectO = sObjects.GetNode<AnimatedSprite2D>("Animation");
	    HitEffectO.Position = new Vector2(0, 0);
	    HitEffectO.Rotation = 0;
	    TapO = sObjects.GetNode<NoteNode>("Tap");
	    TapO.Position = new Vector2(0, 0);
	    TapO.Rotation = 0;
	    DragO = sObjects.GetNode<NoteNode>("Drag");
	    DragO.Position = new Vector2(0, 0);
	    DragO.Rotation = 0;
	    FlickO = sObjects.GetNode<NoteNode>("Flick");
	    FlickO.Position = new Vector2(0, 0);
	    FlickO.Rotation = 0;
	    HoldO = sObjects.GetNode<NoteNode>("Hold");
	    HoldO.Position = new Vector2(0, 0);
	    HoldO.Rotation = 0;
    }

    public void LineInit()
    {
	    int lineId = 0;
	    foreach (var lineData in Chart.JudgeLines)
	    {
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
		    double audioTime = MusicPlayer.GetPlaybackPosition() + AudioServer.GetTimeSinceLastMix() - AudioServer.GetOutputLatency();
		    // 动态校准游戏时钟
		    if (Math.Abs(GameTime - audioTime) > 0.02)  // 阈值校准
		    {
			    GameTime = audioTime;  // 同步
		    }
		    else
		    {
			    GameTime += delta;  // 时间推进
		    }
	    }
	    else
	    {
		    GameTime += delta;  // 时间推进
	    }

	    ComboBox.Visible = ComboNum >= 3;
	    ComboLabel.Text = ComboNum.ToString();
    }
}
