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

                LineNode.RootNode.ComboNum++;
                QueueFree();
            }
        }
        else
        {
            OnNoteDestroyed?.Invoke(this, IsAbove);
            EffectAudio?.Play();
            SpawnHitEffect();
            LineNode.RootNode.ComboNum++;
            QueueFree();
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