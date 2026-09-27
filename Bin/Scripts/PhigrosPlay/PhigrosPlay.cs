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

    // ── 计分（Phigros 通行 1000000 分制，规则与显示格式见 ScoreRules.cs）──────
    // autoplay 命中一律按 Perfect 结算，因此打完全部音符必然恰好 1000000 分 / 100% Acc。
    public int TotalNotes;
    public int JudgedNotes;
    public int MaxCombo;
    public double ScoreWeightSum;
    public double Score;
    public double Acc;

    private readonly List<AnimatedSprite2D> _hitEffectIdle = [];
    private readonly List<AnimatedSprite2D> _hitEffectActive = [];

    private double _musicLength;
    private bool _musicStarted;

    // ── 暂停菜单 ─────────────────────────────────────────────────────────
    // 1 秒内连续两次按下 Pause 才打开菜单；菜单按钮与倒计时由代码接线。
    private const double PauseDoubleClickSeconds = 1d;
    private const double ResumeCountdownSeconds = 3d;
    private Button _pauseButton;
    private Control _pauseButtons;
    private Label _pauseCountdown;
    private ColorRect _pauseBackground;
    private Tween _menuTween;
    private float _menuBackgroundAlpha = 1f;
    private ulong _lastPausePressMsec;
    private bool _pausePressPending;

    public override void _Ready()
    {
        ViewportV = new LengthVector(
            (float)ProjectSettings.GetSetting("display/window/size/viewport_width"),
            (float)ProjectSettings.GetSetting("display/window/size/viewport_height")
        );
        OInit();
        PauseInit();
        ManualInit();
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
        MusicPlayer?.Play();
        IsPlaying = true;
    }

    public void InfoInit()
    {
        // 谱面路径目前写死为测试谱面，后续可改为按用户选择加载
        using var file = FileAccess.Open(@"res://Assets/Test/1.json", FileAccess.ModeFlags.Read);
        // MusicPlayer.Stream = AudioLoader.GodotAudioLoader.LoadAudioFile(@"F:\谱面\官方\Alice_in_a_xxxxxxxx_chart\music_#4720.wav");
        if (file is null)
        {
            GD.PrintErr("谱面文件缺失或无法打开，错误: ", FileAccess.GetOpenError());
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
            GD.PrintErr("谱面解析失败: ", e.Message);
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
        // 重新统计音符总数并清空计分状态，重复初始化时不会把旧数据累加进来
        TotalNotes = 0;
        JudgedNotes = 0;
        MaxCombo = 0;
        ComboNum = 0;
        ScoreWeightSum = 0d;
        Score = 0d;
        Acc = 0d;

        int lineId = 0;
        foreach (var lineData in Chart.JudgeLines)
        {
            // 谱面里把 notesAbove / notesBelow 写成 null 时兜底，
            // 否则下面的 .Count 取用与后续 NoteInit 的 foreach 都会抛 NRE
            lineData.NotesAbove ??= [];
            lineData.NotesBelow ??= [];

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

    public override void _Process(double delta)
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

        UpdateManualPlay(delta);
        UpdateHud();
    }

    // 音符判定完成时由 NoteNode 调用：累计权重、更新连击与总分。
    // grade 默认为 Perfect（autoplay 只会命中），Good / Bad / Miss 预留给手动演奏。
    // 音符判定完成时由 NoteNode 调用：累计权重、更新连击与总分。
    // 连击只由 Perfect / Good 累积，Bad / Miss 会清零（Phigros 官方规则）。
    public void OnNoteJudged(JudgeGrade grade = JudgeGrade.Perfect)
    {
        JudgedNotes++;

        if (grade is JudgeGrade.Perfect or JudgeGrade.Good)
        {
            ComboNum++;
            if (ComboNum > MaxCombo) MaxCombo = ComboNum;
        }
        else
        {
            ComboNum = 0;
        }

        ScoreWeightSum += ScoreRules.WeightOf(grade);
        RecomputeScore();
    }

    private void UpdateHud()
    {
        PlayProgressBar?.Value = Math.Clamp(GameTime, 0d, PlayProgressBar.MaxValue);

        // 与文档实测一致：不到 7 位补前导零（"0007813"），达到 1000000 恒显示 "1000000"
        ScoreLabel?.Text = ScoreRules.GetScoreText(Score);

        ComboBox.Visible = ComboNum >= 3;
        ComboLabel.Text = ComboNum.ToString();
    }

    // ── 暂停菜单 ─────────────────────────────────────────────────────────
    // 接线：Pause 按钮「1 秒内双击」打开菜单；Quit / Restart / Continue 全部由代码响应。
    // 暂停走 GetTree().Paused，依赖场景里已设好的进程模式分工：
    //   Darkening        = Always   （UI 与暂停菜单在暂停时仍可交互）
    //   Darkening/Objects= Pausable （判定线、音符、特效全部冻结）
    public void PauseInit()
    {
        if (PauseMenu is null)
        {
            GD.PrintErr("暂停菜单未在场景中指定，跳过初始化");
            return;
        }

        // 必须显式设为 Always：暂停期间进程模式仍是 Inherit / Pausable 的按钮不响应输入，
        // 挂在它上面的补间也不会推进（倒计时就走不到恢复那一步）。
        PauseMenu.ProcessMode = ProcessModeEnum.Always;

        _pauseBackground = PauseMenu.GetNodeOrNull<ColorRect>("Background");
        _pauseButtons = PauseMenu.GetNodeOrNull<Control>("Buttons");
        _pauseCountdown = PauseMenu.GetNodeOrNull<Label>("Countdown");
        _menuBackgroundAlpha = _pauseBackground?.Modulate.A ?? 1f;

        Button quit = PauseMenu.GetNodeOrNull<Button>("Buttons/Quit");
        if (quit is not null) quit.Pressed += OnQuitPressed;
        Button restart = PauseMenu.GetNodeOrNull<Button>("Buttons/Restart");
        if (restart is not null) restart.Pressed += OnRestartPressed;
        Button resume = PauseMenu.GetNodeOrNull<Button>("Buttons/Continue");
        if (resume is not null) resume.Pressed += OnContinuePressed;

        _pauseButton = GetNodeOrNull<Button>("Darkening/UI/Pause");
        if (_pauseButton is not null) _pauseButton.Pressed += OnPausePressed;

        PauseMenu.Visible = false;
        if (_pauseCountdown is not null) _pauseCountdown.Visible = false;
    }

    // 1 秒内双击 Pause 才打开菜单：第一次只记下时刻，第二次仍落在窗口内才生效
    private void OnPausePressed()
    {
        if (PauseMenu is null || PauseMenu.Visible) return;

        ulong now = Time.GetTicksMsec();
        if (_pausePressPending && now - _lastPausePressMsec <= (ulong)(PauseDoubleClickSeconds * 1000d))
        {
            _pausePressPending = false;
            OpenPauseMenu();
            return;
        }

        _pausePressPending = true;
        _lastPausePressMsec = now;
    }

    public void OpenPauseMenu()
    {
        KillMenuTween();

        // 再次打开菜单时把背景不透明度改回来，并恢复按钮、收起倒计时
        if (_pauseBackground is not null)
        {
            _pauseBackground.Modulate = new Color(_pauseBackground.Modulate, _menuBackgroundAlpha);
        }
        if (_pauseButtons is not null) _pauseButtons.Visible = true;
        if (_pauseCountdown is not null) _pauseCountdown.Visible = false;

        PauseMenu.Visible = true;
        GetTree().Paused = true;
    }

    // Continue：先藏起按钮进入倒计时，3 秒后解除暂停；期间背景同时缓动到透明
    private void OnContinuePressed()
    {
        _pauseButtons?.Visible = false;
        SetCountdownText("3");
        _pauseCountdown?.Visible = true;

        KillMenuTween();

        // 补间挂在 PauseMenu 上（它已是 Always），并显式声明「暂停时也推进」，
        // 否则暂停期间补间会停住，倒计时永远走不到恢复那一步。
        _menuTween = PauseMenu.CreateTween();
        _menuTween.SetPauseMode(Tween.TweenPauseMode.Process);
        _menuTween.SetParallel();

        if (_pauseBackground is not null)
        {
            // 暂停菜单背景逐渐缓动到透明，能看见下面的游戏画面
            _menuTween.TweenProperty(_pauseBackground, "modulate:a", 0f, ResumeCountdownSeconds)
                .SetTrans(Tween.TransitionType.Cubic)
                .SetEase(Tween.EaseType.InOut);
        }

        _menuTween.TweenCallback(Callable.From(() => SetCountdownText("2"))).SetDelay(1d);
        _menuTween.TweenCallback(Callable.From(() => SetCountdownText("1"))).SetDelay(2d);
        _menuTween.TweenCallback(Callable.From(ResumeGame)).SetDelay(ResumeCountdownSeconds);
    }

    // 倒计时结束：解除暂停并收起菜单（背景不透明度留到下次打开时恢复）
    public void ResumeGame()
    {
        GetTree().Paused = false;
        PauseMenu?.Visible = false;
        _pauseCountdown?.Visible = false;
        SetCountdownText("3");
    }

    // Restart：重新加载本场景，谱面、音乐、计分与连击全部回到开头
    private void OnRestartPressed()
    {
        KillMenuTween();
        // 暂停状态属于场景树，重载前必须解除，否则新场景一进来就停在暂停态
        GetTree().Paused = false;
        Error err = GetTree().ReloadCurrentScene();
        if (err != Error.Ok) GD.PrintErr($"重新开始失败：{err}");
    }

    private void OnQuitPressed()
    {
        GetTree().Quit();
    }

    private void SetCountdownText(string text)
    {
        if (_pauseCountdown is not null && IsInstanceValid(_pauseCountdown)) _pauseCountdown.Text = text;
    }

    private void KillMenuTween()
    {
        if (_menuTween is not null && _menuTween.IsValid()) _menuTween.Kill();
        _menuTween = null;
    }
}
