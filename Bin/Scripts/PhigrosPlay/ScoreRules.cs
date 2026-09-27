using System;
using System.Numerics;

// Phigros 计分规则（通行规则，非本工程文档记载）：
//   总分 = 900000 × (Σ 判定权重 / 音符总数) + 100000 × (最大连击 / 音符总数)
//   Acc  = Σ 判定权重 / 音符总数
//   判定权重：Perfect = 1、Good = 0.65、Bad = 0、Miss = 0
//   全部 Perfect 且连击不断时：总分恰好 1000000、Acc 恰好 100%
//
// 参考文档（谱面格式 / 参数计算 / 指标参数）：
//   https://docs.lchzh.net/learning/phigros
//   https://docs.lchzh.net/learning/phigros/calc
//   https://docs.lchzh.net/learning/phigros/metrics
// 注意：上述文档只记载了右上角分数的显示逻辑（本文件 GetScoreText 逐条复刻其实现
//       与三个实测样例），并未给出 900000 / 100000 的分项构成。
//
// 本文件不引用 Godot，便于脱离引擎比对数值与显示逻辑。
//
// ── 判定等级 ───────────────────────────────────────────────────────────────
// 自动演奏（autoplay）只会产生 Perfect，因此必定满分；
// Good / Bad / Miss 预留给后续手动演奏。
public enum JudgeGrade
{
    Miss = 0,
    Bad = 1,
    Good = 2,
    Perfect = 3,
}

public static class ScoreRules
{
    public const double MaxScore = 1000000d;
    public const double NoteScorePart = 900000d;
    public const double ComboScorePart = 100000d;

    public static double WeightOf(JudgeGrade grade)
    {
        switch (grade)
        {
            case JudgeGrade.Perfect: return 1d;
            case JudgeGrade.Good: return 0.65d;
            default: return 0d;   // Bad / Miss 不计权重
        }
    }

    // 音符部分按权重占比、连击部分按最大连击占比；两者之和即总分。
    // 入参越界（负数 / 超过音符数 / 非有限）时夹回合法范围，保证结果始终有限。
    public static double ComputeScore(double weightSum, int maxCombo, int totalNotes)
    {
        if (totalNotes <= 0) return 0d;

        double n = totalNotes;
        double weight = double.IsFinite(weightSum) ? Math.Clamp(weightSum, 0d, n) : 0d;
        double combo = Math.Clamp((double)maxCombo, 0d, n);

        double score = NoteScorePart * (weight / n) + ComboScorePart * (combo / n);
        return double.IsFinite(score) ? Math.Clamp(score, 0d, MaxScore) : 0d;
    }

    public static double ComputeAcc(double weightSum, int totalNotes)
    {
        if (totalNotes <= 0) return 0d;
        double weight = double.IsFinite(weightSum) ? Math.Clamp(weightSum, 0d, totalNotes) : 0d;
        return weight / totalNotes;
    }

    // 文档「游戏画面 / 右上角分数显示规律」的 JavaScript 实现逐条复刻：
    //   function getScoreText(score) {
    //     score = isFinite((score += 0.5)) ? score | 0 : 1 << 31;
    //     if (score >= 1e6) return '1000000';
    //     return '0' + (score / 1e5).toFixed(5).replace('.', '');
    //   }
    // 实测样例：7812.5 → "0007813"；-33333.3 → "0-033332"；∞ → "0-2147483648"。
    public static string GetScoreText(double score)
    {
        double shifted = score + 0.5d;
        int truncated = double.IsFinite(shifted) ? ToInt32(shifted) : 1 << 31;

        if (truncated >= 1000000) return "1000000";

        return "0" + Fixed5(truncated / 100000d).Replace(".", "");
    }

    // 复刻 JS 位运算 ToInt32：向零截断，再对 2^32 取模并映射回 int32 范围
    private static int ToInt32(double value)
    {
        if (!double.IsFinite(value)) return 0;

        double modulo = Math.Truncate(value) % 4294967296d;   // 与 JS 一样取被除数符号
        if (modulo < 0d) modulo += 4294967296d;
        if (modulo >= 2147483648d) modulo -= 4294967296d;
        return (int)modulo;
    }

    // 复刻 JS Number.prototype.toFixed(5)：按 double 的精确十进制展开保留 5 位小数；
    // 恰好落在半格时取较大的那个值（JS 规范如此），符号只取 value < 0（-0 不带负号）。
    private static string Fixed5(double value)
    {
        long bits = BitConverter.DoubleToInt64Bits(value);
        int biasedExponent = (int)((bits >> 52) & 0x7FF);
        long mantissaBits = bits & 0xFFFFFFFFFFFFFL;
        int exponent;
        if (biasedExponent == 0)
        {
            exponent = -1074;   // 非规格化数（含 0）
        }
        else
        {
            mantissaBits |= 1L << 52;
            exponent = biasedExponent - 1075;
        }

        // 先算 value × 10^5，再按 2 的幂缩放成整数（需要时四舍五入到整数）
        BigInteger scaled = new BigInteger(mantissaBits) * 100000;
        if (exponent >= 0)
        {
            scaled <<= exponent;
        }
        else
        {
            BigInteger divisor = BigInteger.One << -exponent;
            BigInteger quotient = BigInteger.DivRem(scaled, divisor, out BigInteger remainder);
            BigInteger twice = BigInteger.Abs(remainder) * 2;
            if (twice > divisor) quotient += scaled.Sign < 0 ? -1 : 1;   // 超过半格：远离零
            else if (twice == divisor) quotient += 1;                    // 恰好半格：取较大值
            scaled = quotient;
        }

        BigInteger magnitude = BigInteger.Abs(scaled);
        return (value < 0d ? "-" : "")
            + (magnitude / 100000).ToString()
            + "."
            + (magnitude % 100000).ToString("D5");
    }
}