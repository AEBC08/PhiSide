using Godot;
using System;
using System.Collections.Generic;
using PhigrosChart;
using AExtension;

public partial class PhigrosPlay : Node
{
    // 打击特效对象池上限：池内实例常驻，用完归还，
    // 避免每次打击都 Duplicate + AddChild + 新建 GPU 粒子缓冲。
    public const int MaxHitEffects = 48;

    // 文档（实测数据）：音乐进度到达 (音乐时长 - 0.22049) 秒时，
    // 谱面和进度条停止移动，音乐停止播放。
    public const double MusicEndOffset = 0.22049;

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

    // 音乐时间（秒），进度条以它为准
    public double GameTime;

    // 谱面时间（秒）= 音乐时间 - 谱面 offset。
    // 文档：offset 为非负数时音乐立即开始，谱面延迟 |offset| 秒开始。
    public double ChartTime => GameTime - (Chart?.Offset ?? 0f);

    public int ComboNum;

    private readonly List<AnimatedSprite2D> _hitEffectIdle = [];
    private readonly List<AnimatedSprite2D> _hitEffectActive = [];

    private double _musicLength;
    private bool _musicStarted;

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

        InitHitEffectPool();
        LineInit();
        _musicLength = MusicPlayer?.Stream?.GetLength() ?? 0d;
        PlayProgressBar.MaxValue = _musicLength > 0 ? _musicLength : 1;
        PlayProgressBar.Value = 0;
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

    // 打击特效对象池：提前建好固定数量并常驻，打击时只做取用/归还
    private void InitHitEffectPool()
    {
        if (HitEffectO is null || EffectsNode is null) return;

        for (int i = 0; i < MaxHitEffects; i++)
        {
            var effect = (AnimatedSprite2D)HitEffectO.Duplicate();

            // 模板的 AnimationPlayer 只负责在 0.5s / 1.0s 时 queue_free 以及置 Particle.emitting，
            // 与池化复用冲突（节点会被销毁），因此改用代码驱动。
            if (effect.GetNodeOrNull<AnimationPlayer>("AnimationPlayer") is { } animPlayer)
            {
                effect.RemoveChild(animPlayer);
                animPlayer.Free();
            }

            effect.Autoplay = "";
            effect.Visible = false;
            effect.AnimationFinished += () => ReleaseHitEffect(effect);
            EffectsNode.AddChild(effect);
            _hitEffectIdle.Add(effect);
        }
    }

    // 取一个空闲特效；若全部在用则抢占最早的一个（保证节点数量恒定，不会随打击次数增长）
    public AnimatedSprite2D AcquireHitEffect()
    {
        AnimatedSprite2D effect = null;

        while (_hitEffectIdle.Count > 0)
        {
            int last = _hitEffectIdle.Count - 1;
            effect = _hitEffectIdle[last];
            _hitEffectIdle.RemoveAt(last);
            if (IsInstanceValid(effect)) break;
            effect = null;
        }

        if (effect is null)
        {
            if (_hitEffectActive.Count == 0) return null;
            effect = _hitEffectActive[0];
            _hitEffectActive.RemoveAt(0);
        }

        _hitEffectActive.Add(effect);
        return effect;
    }

    // 播放特效：重置动画与粒子，位置由调用方设置
    public void PlayHitEffect(AnimatedSprite2D effect)
    {
        if (effect is null || !IsInstanceValid(effect)) return;

        effect.Visible = true;
        effect.Stop();
        effect.Frame = 0;
        effect.FrameProgress = 0f;
        effect.Play("default");

        if (effect.GetNodeOrNull<GpuParticles2D>("Particle") is { } particle)
        {
            particle.Emitting = true;
            particle.Restart();
        }
    }

    public void ReleaseHitEffect(AnimatedSprite2D effect)
    {
        if (effect is null || !IsInstanceValid(effect)) return;
        // 已被抢占（或本来就空闲）时直接忽略，避免重复归还
        if (!_hitEffectActive.Remove(effect)) return;

        effect.Visible = false;
        if (effect.GetNodeOrNull<GpuParticles2D>("Particle") is { } particle)
        {
            particle.Emitting = false;
        }

        _hitEffectIdle.Add(effect);
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

        // 音乐结束时谱面与进度条停止移动（文档：音乐进度到达 音乐时长 - 0.22049 秒）
        double stopAt = _musicLength > 0 ? _musicLength - MusicEndOffset : double.PositiveInfinity;

        if (MusicPlayer.IsPlaying())
        {
            _musicStarted = true;
            // 获取音频基准时间（单位：秒）
            GameTime = MusicPlayer.GetPlaybackPosition() + AudioServer.GetTimeSinceLastMix() - AudioServer.GetOutputLatency();

            if (GameTime >= stopAt)
            {
                GameTime = stopAt;
                MusicPlayer.Stop();
                IsPlaying = false;
            }
        }
        else if (_musicLength > 0)
        {
            // 音乐已停止（正常结束或提前结束）：冻结时间，不再自行推进
            _musicStarted = true;
        }
        else
        {
            GameTime += delta;  // 没有音乐时自由推进
        }

        UpdateHud();
    }

    private void UpdateHud()
    {
        if (PlayProgressBar is not null)
        {
            double max = _musicLength > 0 ? _musicLength : Math.Max(GameTime, 1d);
            PlayProgressBar.MaxValue = max;
            PlayProgressBar.Value = Math.Clamp(GameTime, 0d, max);
        }

        ComboBox.Visible = ComboNum >= 3;
        ComboLabel.Text = ComboNum.ToString();
    }
}