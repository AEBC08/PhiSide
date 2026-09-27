using Godot;
using System;
using PhigrosChart;

public partial class NoteNode : Sprite2D
{
    // Hold 贴图（989×2000）的上下两端各 50px 是圆角端帽。
    // 拉伸 Hold 时端帽必须保持原比例，只拉伸中段，否则端帽会被拉成奇怪的形状。
    public const float HoldCapPixels = 50f;

    public ChartLoader.NoteV3 NoteData;
    public Action<NoteNode, bool> OnNoteDestroyed;
    public bool IsAbove;
    public AudioStreamPlayer EffectAudio;
    public JudgeLineNode LineNode;
    public bool IsHold;
    public double HoldEndTime;
    public double BeatCount;

    // ── Hold 的 9-slice 分片 ───────────────────────────────────────────────
    // 本地坐标约定：Node2D 原点（y = 0）是贴图底边 / 判定线一侧（头端），
    // -y 方向是 Hold 条延伸的方向（判定后上半场为屏幕上方、下半场由父节点旋转 180° 处理）。
    private Sprite2D _holdHead;
    private Sprite2D _holdBody;
    private Sprite2D _holdTail;
    private bool _holdSlicesReady;
    private float _holdBaseScale = 1f;
    private float _holdTexWidth;
    private float _holdTexHeight;
    private float _holdCapRows;
    private float _holdBodyRows;
    private bool _holdHeadHidden = true;
    private bool _holdHeadHit;

    // 为 Hold 构建头 / 身 / 尾三个分片，并让本节点不再绘制整张贴图。
    // 头与尾是固定尺寸的端帽，中段按需要拉伸；判定后只需要隐藏头部分片。
    public void SetupHoldSlices()
    {
        if (_holdSlicesReady) return;
        _holdSlicesReady = true;

        Texture2D texture = Texture;
        if (texture is null)
        {
            GD.PrintErr("Hold 音符缺少贴图，无法建立分片结构");
            return;
        }

        _holdTexWidth = texture.GetWidth();
        _holdTexHeight = texture.GetHeight();
        _holdCapRows = Mathf.Min(HoldCapPixels, _holdTexHeight * 0.5f);
        _holdBodyRows = Mathf.Max(_holdTexHeight - _holdCapRows * 2f, 0f);
        _holdBaseScale = Mathf.Abs(Scale.Y) > 0f ? Mathf.Abs(Scale.Y) : 1f;

        // 头 / 身 / 尾改由子节点分片绘制，自身不再直接绘制整张贴图
        Texture = null;

        _holdHead = AddSlice("HoldHead", texture,
            new Rect2(0f, _holdTexHeight - _holdCapRows, _holdTexWidth, _holdCapRows));
        _holdTail = AddSlice("HoldTail", texture,
            new Rect2(0f, 0f, _holdTexWidth, _holdCapRows));
        if (_holdBodyRows > 0f)
        {
            _holdBody = AddSlice("HoldBody", texture,
                new Rect2(0f, _holdCapRows, _holdTexWidth, _holdBodyRows));
        }

        UpdateHoldVisual(0f, true);
    }

    private Sprite2D AddSlice(string name, Texture2D texture, Rect2 region)
    {
        var slice = new Sprite2D
        {
            Name = name,
            // FilterClip：把采样限制在本分片内，避免线性过滤把相邻分片的像素混进来
            Texture = new AtlasTexture { Atlas = texture, Region = region, FilterClip = true },
            Centered = false,   // 分片以左上角为锚点，便于把头/尾分别钉在两端
            Position = Vector2.Zero,
            Scale = Vector2.One,
        };
        AddChild(slice);
        return slice;
    }

    // 更新 Hold 的显示长度。
    // lengthPx：期望的屏幕像素长度（不含任何缩放换算，由判定线逻辑按文档公式给出）。
    // headHidden：已判定（头部图形消失，剩余部分近端贴住判定线）。
    public void UpdateHoldVisual(float lengthPx, bool headHidden)
    {
        if (!_holdSlicesReady) return;

        _holdHeadHidden = headHidden;

        // 分片是子节点，会跟随本节点缩放，因此先换算回本地的贴图像素尺度
        float lengthLocal = Mathf.Max(lengthPx, 0f) / _holdBaseScale;
        if (lengthLocal <= 0.001f)
        {
            HideSlices();
            return;
        }

        // 短 Hold 时压缩端帽，避免两端端帽互相重叠（不会拉坏形状，只是等比缩小）
        float capLocal = Mathf.Min(_holdCapRows, lengthLocal * 0.5f);
        float capScale = _holdCapRows > 0f ? capLocal / _holdCapRows : 0f;
        float x = -_holdTexWidth * 0.5f;

        if (_holdHead is not null)
        {
            _holdHead.Visible = !headHidden;
            _holdHead.Position = new Vector2(x, -capLocal);
            _holdHead.Scale = new Vector2(1f, capScale);
        }

        // 尾端帽：位于远离判定线的一端，紧贴中段末端
        if (_holdTail is not null)
        {
            _holdTail.Visible = true;
            _holdTail.Position = new Vector2(x, -lengthLocal);
            _holdTail.Scale = new Vector2(1f, capScale);
        }

        // 中段：夹在尾端帽内侧与头部之间；判定后头部消失，中段直接延伸到判定线
        if (_holdBody is not null)
        {
            float bodyLocal = lengthLocal - capLocal - (headHidden ? 0f : capLocal);
            _holdBody.Visible = bodyLocal > 0.001f;
            if (_holdBody.Visible)
            {
                _holdBody.Position = new Vector2(x, -lengthLocal + capLocal);
                _holdBody.Scale = new Vector2(1f, bodyLocal / _holdBodyRows);
            }
        }
    }

    private void HideSlices()
    {
        if (_holdHead is not null) _holdHead.Visible = false;
        if (_holdBody is not null) _holdBody.Visible = false;
        if (_holdTail is not null) _holdTail.Visible = false;
    }

    public bool HoldHeadHidden => _holdHeadHidden;

    public void AutoPlay()
    {
        if (IsHold)
        {
            if (!_holdHeadHit)
            {
                // 首次判定：播放 Hold 音效并产生头部打击特效
                // （用标志位而不是 BeatCount 判断，避免判定到第一次间隔特效之间音效被反复重播）
                _holdHeadHit = true;
                EffectAudio?.Play();
                SpawnHitEffect();
            }

            var nowBeat = LineNode.GameTime / LineNode.BeatTime;
            if (nowBeat - BeatCount >= 1)
            {
                BeatCount = nowBeat;
                SpawnHitEffect();
            }
            if (LineNode.GameTTime >= HoldEndTime)
            {
                OnNoteDestroyed?.Invoke(this, IsAbove);

                // autoplay 必定命中：按 Perfect 结算（累计权重并更新连击 / 总分）
                LineNode.RootNode.OnNoteJudged();
                QueueFree();
            }
        }
        else
        {
            OnNoteDestroyed?.Invoke(this, IsAbove);
            EffectAudio?.Play();
            SpawnHitEffect();
            // autoplay 必定命中：按 Perfect 结算（累计权重并更新连击 / 总分）
            LineNode.RootNode.OnNoteJudged();
            QueueFree();
        }
    }


    // ── 手动演奏接口（判定规则见 PhigrosPlay.Manual.cs）────────────────────
    // 漏掉 / 松手后的偏暗程度：只是略微变暗变灰，别压太狠（1 = 原色，0.45 会看着像褪色/透明）
    private const float MissTint = 0.75f;

    public bool Judged;                 // 已结算：命中或漏掉
    public JudgeGrade JudgedGrade;      // 已结算的等级（Hold 中途松手会改写为 Miss）
    public bool HoldHeadHit => _holdHeadHit;

    // 判定区半宽：音符沿判定线方向的半宽。
    // Hold 拆片后自身贴图为空，改用拆片时记下的原贴图宽。
    public float BandHalfWidth
    {
        get
        {
            float width = _holdSlicesReady && _holdTexWidth > 0f ? _holdTexWidth : GetRect().Size.X;
            float half = Mathf.Abs(width * Scale.X) * 0.5f;
            float min = LineNode?.RootNode?.BandMinHalfWidth ?? 0f;
            return Mathf.Max(half, min);
        }
    }

    // 命中：音效 + 打击特效 + 按 grade 结算。非 Hold 立即销毁；Hold 只标记头已命中。
    public void ManualHit(JudgeGrade grade)
    {
        if (Judged) return;

        if (IsHold)
        {
            if (_holdHeadHit) return;
            _holdHeadHit = true;
        }
        else
        {
            OnNoteDestroyed?.Invoke(this, IsAbove);
        }

        Judged = true;
        JudgedGrade = grade;
        EffectAudio?.Play();
        SpawnHitEffect();
        LineNode.RootNode.OnNoteJudged(grade);

        if (!IsHold) QueueFree();
    }

    // Hold 中途松手：变暗，并把已结算的判定改写为 Miss（官方：Hold 提前松开就断了）
    public void BreakHold()
    {
        if (!IsHold || !_holdHeadHit || JudgedGrade == JudgeGrade.Miss) return;

        JudgeGrade previous = JudgedGrade;
        JudgedGrade = JudgeGrade.Miss;
        Modulate = new Color(MissTint, MissTint, MissTint, Modulate.A);
        LineNode.RootNode.OnNoteRegraded(previous);
    }

    // 漏掉：变暗 + 按 Miss 结算。非 Hold 从列表摘除并销毁；Hold 继续显示到结束时刻。
    public void ManualMiss()
    {
        if (Judged) return;

        Judged = true;
        JudgedGrade = JudgeGrade.Miss;
        Modulate = new Color(MissTint, MissTint, MissTint, Modulate.A);
        LineNode.RootNode.OnNoteJudged(JudgeGrade.Miss);

        if (!IsHold)
        {
            OnNoteDestroyed?.Invoke(this, IsAbove);
            QueueFree();
        }
    }

    // Hold 走完（含中途松手的）：从列表摘除并销毁
    public void FinishHold()
    {
        OnNoteDestroyed?.Invoke(this, IsAbove);
        QueueFree();
    }

    // Hold 判定期间的连击特效：与 AutoPlay 同一套节奏（每 BeatTime 秒一次）
    public void UpdateHoldBeatEffect()
    {
        double nowBeat = LineNode.GameTime / LineNode.BeatTime;
        if (nowBeat - BeatCount >= 1d)
        {
            BeatCount = nowBeat;
            SpawnHitEffect();
        }
    }

    // 打击特效从对象池取用，而不是每次击打都 Duplicate + AddChild
    private void SpawnHitEffect()
    {
        PhigrosPlay root = LineNode?.RootNode;
        if (root is null) return;

        AnimatedSprite2D hitEffect = root.AcquireHitEffect();
        if (hitEffect is null) return;

        hitEffect.Position = root.EffectsNode.ToLocal(LineNode.ToGlobal(new Vector2(Position.X, 0)));
        root.PlayHitEffect(hitEffect);
    }
}
