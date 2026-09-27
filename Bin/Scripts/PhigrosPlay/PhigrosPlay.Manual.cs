using Godot;
using System;
using System.Collections.Generic;
using PhigrosChart;

// ── 手动演奏（鼠标 / 触摸）───────────────────────────────────────────────
// 规则取自 Phigros 官方机制（Phigros Wiki「判定」一节，搬运自萌娘百科，CC BY-NC-SA 3.0）：
//   https://killkpa.miraheze.org/wiki/Phigros
//
//   1) 判定区域 = 与判定线垂直、向两边无限延展的矩形。
//      ⇒ 点击点只要落在音符「沿判定线方向」的宽度带内就能判定，
//        沿判定线法线方向（相对判定线的整条 y 轴）放哪里都算命中。
//        判定线会平移 / 旋转，所以比较前先把点击点换算到判定线的本地坐标。
//        这就是 Phigros 的「垂直判断」。
//
//   2) 判定窗口（音符时间与当前谱面时间之差，单位秒）：
//        Perfect  ±0.080s          100% 判定分（金牌）
//        Good     ±0.080 ~ 0.160s   65% 判定分（蓝牌）
//        Bad      ±0.160 ~ 0.180s    0% 判定分，连击归零，音符变暗
//        Miss     始终没击中         0% 判定分，连击归零
//
//   3) 特例：Hold 没有 Bad 判定，提前松手即断（中途换手长按不影响）；
//      Drag / Flick 没有 Good / Bad 判定：Drag 要在与判定线重合后判定区内有手指，
//      Flick 是 ±0.08s 对称窗口、判定区内滑动即可（与 Tap / Hold 一致的窗口宽度）。
//
//   4) 单次点击只判定一个 Tap / Hold，优先「仍可判定中最早出现」的那个。
public partial class PhigrosPlay
{
    // Inspector 里勾选即可切换自动演奏 / 手动演奏；Mode 标签随之显示 Autoplay / Combo。
    [Export] public bool ManualPlay = true;

    public const double PerfectSeconds = 0.080;
    public const double GoodSeconds = 0.160;
    public const double BadSeconds = 0.180;

    // Drag / Flick：与判定线重合（late = 0）之后的判定容差；没抓住就是 Miss
    public const double SlideHitSeconds = 0.100;

    // 判定区半宽的下限（占视口宽度比例），避免音符贴图太窄时鼠标打不中
    public const float BandMinHalfWidthScale = 0.045f;

    // 滑动认定的时间窗（Flick 用）：按住移动后这段时间内都算「正在滑动」
    private const ulong SwipeGraceMsec = 150ul;


    private readonly List<ManualPointer> _pointers = [];
    private readonly List<NoteNode> _scratch = [];

    // 一个指针（鼠标 id 固定 0，触摸用 touch 的 index）
    private sealed class ManualPointer
    {
        public int Id;
        public Vector2 Position;
        public bool Pressed;
        public ulong SwipeUntilMsec;   // 最近一次滑动的时间窗
    }

    public float BandMinHalfWidth => ViewportV.W * BandMinHalfWidthScale;

    public void ManualInit()
    {
        _pointers.Clear();
        _pointers.Add(new ManualPointer { Id = 0 });
        RefreshModeLabel();
    }

    // Mode 标签：手动演奏显示 Combo，自动演奏显示 Autoplay
    public void RefreshModeLabel()
    {
        Label mode = GetNodeOrNull<Label>("Darkening/UI/ComboBox/Mode");
        if (mode is not null) mode.Text = ManualPlay ? "Combo" : "Autoplay";
    }

    // 音符到点还没被命中时算漏掉的时刻（Hold 无 Bad、Drag / Flick 无 Good / Bad）
    public double MissSecondsOf(NoteNode note)
    {
        return note.NoteData.Type switch
        {
            3 => GoodSeconds,          // Hold
            2 => SlideHitSeconds,      // Drag：过线后 0.10s 内要接住
            4 => PerfectSeconds,       // Flick：±0.08s 对称窗口，过线后 0.08s 算漏
            _ => BadSeconds,           // Tap
        };
    }

    public override void _Input(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Left:
            {
                bool rising = mb.Pressed && !IsPointerPressed(0);
                SetPointer(0, mb.Position, mb.Pressed);
                if (rising) TryJudgeAt(mb.Position);
                break;
            }
            case InputEventMouseMotion mm:
            {
                Vector2 fromMove = PointerOf(0).Position;
                bool dragging = (mm.ButtonMask & MouseButtonMask.Left) != 0;
                SetPointer(0, mm.Position, null);
                if (dragging)
                {
                    // 按住鼠标移动 = 滑动：线段扫过的 Drag / Flick 算命中
                    MarkSwipe(0);
                    SweepJudge(0, fromMove, mm.Position);
                }
                break;
            }
            case InputEventScreenTouch st:
            {
                bool rising = st.Pressed && !IsPointerPressed(st.Index);
                SetPointer(st.Index, st.Position, st.Pressed);
                if (rising) TryJudgeAt(st.Position);
                break;
            }
            case InputEventScreenDrag sd:
            {
                Vector2 fromDrag = PointerOf(sd.Index).Position;
                SetPointer(sd.Index, sd.Position, true);   // 拖动必然是按住状态
                MarkSwipe(sd.Index);
                SweepJudge(sd.Index, fromDrag, sd.Position);
                break;
            }
        }
    }

    // 每帧处理：Drag 的「判定区内按住」、Flick 的「判定区内滑动」、Hold 的「必须一直按住」
    public void UpdateManualPlay(double delta)
    {
        if (!ManualPlay || !IsPlaying || Chart is null) return;

        foreach (JudgeLineNode line in EnumerateLines())
        {
            _scratch.Clear();
            CollectNotes(line, _scratch);

            foreach (NoteNode note in _scratch)
            {
                if (!IsInstanceValid(note)) continue;

                if (note.IsHold)
                {
                    // 头还没命中：等按下（见 TryJudgeAt）或到点判漏
                    if (!note.HoldHeadHit) continue;
                    // 已经松手断过的不再重复处理
                    if (note.JudgedGrade == JudgeGrade.Miss) continue;

                    // 头命中后必须一直有指针按在判定区内，松手即断
                    if (!IsPressedInBand(line, note))
                    {
                        note.BreakHold();
                        continue;
                    }

                    // 判定期间的连击特效：与 AutoPlay 同一套节奏（每 BeatTime 一次）
                    note.UpdateHoldBeatEffect();
                    continue;
                }

                if (note.Judged) continue;

                // Drag / Flick：只有 Perfect / Miss，且只在与判定线重合之后才判（不提前）
                int type = note.NoteData.Type;
                if (type is not (2 or 4)) continue;
                if (!IsGradeable(note, line)) continue;

                bool caught = type == 2
                    ? IsPressedInBand(line, note)    // Drag：判定区内有手指
                    : IsSwipingInBand(line, note);   // Flick：手指在判定区内滑动
                if (caught) note.ManualHit(GradeOf(note, LateSeconds(note, line)));
            }
        }
    }

    // 按下：判定一个音符（官方：优先仍可判定中最早出现的 Tap / Hold）
    private void TryJudgeAt(Vector2 position)
    {
        if (!ManualPlay || !IsPlaying || Chart is null) return;
        if (IsOverUi(position)) return;

        NoteNode best = null;
        JudgeLineNode bestLine = null;
        double bestTime = double.PositiveInfinity;

        foreach (JudgeLineNode line in EnumerateLines())
        {
            foreach (NoteNode note in EnumerateLineNotes(line))
            {
                if (!IsInstanceValid(note) || note.Judged) continue;
                if (note.NoteData.Type is not (1 or 3)) continue;   // Drag / Flick 由每帧检查处理
                if (!IsGradeable(note, line)) continue;             // 还在判定窗口外
                if (!IsInBand(line, note, position)) continue;      // 不在判定区域内
                if (note.NoteData.Time >= bestTime) continue;       // 优先最早出现的音符

                best = note;
                bestLine = line;
                bestTime = note.NoteData.Time;
            }
        }

        if (best is null) return;
        best.ManualHit(GradeOf(best, LateSeconds(best, bestLine)));
    }

    // 判定区域：只比较沿判定线方向的偏移，法线方向（整条 y 轴）不限制
    private static bool IsInBand(JudgeLineNode line, NoteNode note, Vector2 globalPosition)
    {
        Vector2 local = line.ToLocal(globalPosition);
        return Math.Abs(local.X - note.Position.X) <= note.BandHalfWidth;
    }

    // 音符时间以本判定线的 T 为单位（1T = 1.875 / BPM 秒）；正数表示音符已越过判定线
    private double LateSeconds(NoteNode note, JudgeLineNode line)
    {
        return ChartTime - note.NoteData.Time * line.T1Time;
    }

    // 是否处在可判定窗口内（Drag 只在过线之后可判；Flick 与 Tap / Hold 一样是对称窗口）
    private bool IsGradeable(NoteNode note, JudgeLineNode line)
    {
        double late = LateSeconds(note, line);
        return note.NoteData.Type switch
        {
            2 => late >= 0d && late <= SlideHitSeconds,   // Drag：与判定线重合之后
            4 => Math.Abs(late) <= PerfectSeconds,        // Flick：±0.08s 对称窗口
            3 => Math.Abs(late) <= GoodSeconds,           // Hold：没有 Bad 判定
            _ => Math.Abs(late) <= BadSeconds,            // Tap
        };
    }

    private static JudgeGrade GradeOf(NoteNode note, double lateSeconds)
    {
        // Drag / Flick 没有 Good / Bad 判定
        if (note.NoteData.Type is 2 or 4) return JudgeGrade.Perfect;

        double a = Math.Abs(lateSeconds);
        if (a <= PerfectSeconds) return JudgeGrade.Perfect;
        if (a <= GoodSeconds) return JudgeGrade.Good;
        return JudgeGrade.Bad;
    }

    private IEnumerable<JudgeLineNode> EnumerateLines()
    {
        if (LinesNode is null) yield break;
        foreach (Node child in LinesNode.GetChildren())
        {
            if (child is JudgeLineNode line) yield return line;
        }
    }

    private static IEnumerable<NoteNode> EnumerateLineNotes(JudgeLineNode line)
    {
        foreach (NoteNode note in line.NotesAboveList) yield return note;
        foreach (NoteNode note in line.NotesBelowList) yield return note;
        foreach (NoteNode note in line.HoldsAboveList) yield return note;
        foreach (NoteNode note in line.HoldsBelowList) yield return note;
    }

    private static void CollectNotes(JudgeLineNode line, List<NoteNode> into)
    {
        into.AddRange(line.NotesAboveList);
        into.AddRange(line.NotesBelowList);
        into.AddRange(line.HoldsAboveList);
        into.AddRange(line.HoldsBelowList);
    }

    private ManualPointer PointerOf(int id)
    {
        ManualPointer pointer = _pointers.Find(p => p.Id == id);
        if (pointer is null)
        {
            pointer = new ManualPointer { Id = id };
            _pointers.Add(pointer);
        }
        return pointer;
    }

    private bool IsPointerPressed(int id) => PointerOf(id).Pressed;

    private void SetPointer(int id, Vector2 position, bool? pressed)
    {
        ManualPointer pointer = PointerOf(id);
        pointer.Position = position;
        if (pressed.HasValue) pointer.Pressed = pressed.Value;
    }

    // 按住状态下的移动算一次滑动（Flick 用）
    private void MarkSwipe(int id)
    {
        ManualPointer pointer = PointerOf(id);
        if (!pointer.Pressed) return;
        pointer.SwipeUntilMsec = Time.GetTicksMsec() + SwipeGraceMsec;
    }

    private bool IsPressedInBand(JudgeLineNode line, NoteNode note)
    {
        foreach (ManualPointer pointer in _pointers)
        {
            if (pointer.Pressed && IsInBand(line, note, pointer.Position)) return true;
        }
        return false;
    }

    private bool IsSwipingInBand(JudgeLineNode line, NoteNode note)
    {
        ulong now = Time.GetTicksMsec();
        foreach (ManualPointer pointer in _pointers)
        {
            if (!pointer.Pressed || pointer.SwipeUntilMsec < now) continue;
            if (IsInBand(line, note, pointer.Position)) return true;
        }
        return false;
    }


    // 滑动线段扫过判定带的 Drag / Flick 算命中（官方：Drag 只需判定区内有手指，Flick 需要滑动）。
    // Godot 默认会合并一帧内的多次鼠标移动，快速横滑是一段位移，只看端点会跳过中间的判定带，
    // 所以这里按「线段与判定带是否相交」判定。Tap / Hold 不吃滑动，必须真的按一下。
    private void SweepJudge(int id, Vector2 globalFrom, Vector2 globalTo)
    {
        if (!ManualPlay || !IsPlaying || Chart is null) return;
        if (!PointerOf(id).Pressed) return;
        if (IsOverUi(globalTo)) return;

        foreach (JudgeLineNode line in EnumerateLines())
        {
            _scratch.Clear();
            CollectNotes(line, _scratch);

            foreach (NoteNode note in _scratch)
            {
                if (!IsInstanceValid(note) || note.Judged) continue;
                // Tap / Hold 不吃滑动判定：必须由按下事件判定（见 TryJudgeAt）
                if (note.NoteData.Type is not (2 or 4)) continue;
                if (!IsGradeable(note, line)) continue;
                if (!SegmentInBand(line, note, globalFrom, globalTo)) continue;

                note.ManualHit(GradeOf(note, LateSeconds(note, line)));
            }
        }
    }

    // 判定区域只在沿判定线方向有宽度：把线段两端换算到判定线本地坐标后做区间相交
    private static bool SegmentInBand(JudgeLineNode line, NoteNode note, Vector2 globalFrom, Vector2 globalTo)
    {
        float a = line.ToLocal(globalFrom).X;
        float b = line.ToLocal(globalTo).X;
        float half = note.BandHalfWidth;
        return MathF.Max(a, b) >= note.Position.X - half && MathF.Min(a, b) <= note.Position.X + half;
    }
    // 点 UI（Pause 按钮、暂停菜单）时不判定音符
    private bool IsOverUi(Vector2 position)
    {
        if (PauseMenu is not null && PauseMenu.Visible) return true;
        return _pauseButton is not null && _pauseButton.GetGlobalRect().HasPoint(position);
    }

    // 判定改写（Hold 中途松手：Perfect / Good → Miss）。判定数不重复计，只退权重并清连击。
    public void OnNoteRegraded(JudgeGrade previous)
    {
        ScoreWeightSum -= ScoreRules.WeightOf(previous);
        ComboNum = 0;
        RecomputeScore();
    }

    public void RecomputeScore()
    {
        // 分母固定用谱面音符总数；总数尚未统计完时退化为已判定数，避免分母偏小
        int total = TotalNotes > 0 ? TotalNotes : JudgedNotes;
        Score = ScoreRules.ComputeScore(ScoreWeightSum, MaxCombo, total);
        Acc = ScoreRules.ComputeAcc(ScoreWeightSum, total);
    }
}
