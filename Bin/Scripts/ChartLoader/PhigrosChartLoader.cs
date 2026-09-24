using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhigrosChart
{
    public static class ChartLoader
    {
        #region 标准化数据结构（formatVersion=3）
        public class ChartV3
        {
            [JsonPropertyName("formatVersion")]
            public int FormatVersion { get; set; } = 3;

            [JsonPropertyName("offset")]
            public float Offset { get; set; }

            [JsonPropertyName("judgeLineList")]
            public List<JudgeLineV3> JudgeLines { get; set; } = new();
        }

        public class JudgeLineV3
        {
            [JsonPropertyName("bpm")]
            public float Bpm { get; set; }

            [JsonPropertyName("notesAbove")]
            public List<NoteV3> NotesAbove { get; set; } = new();

            [JsonPropertyName("notesBelow")]
            public List<NoteV3> NotesBelow { get; set; } = new();

            [JsonPropertyName("speedEvents")]
            public List<SpeedEventV3> SpeedEvents { get; set; } = new();

            [JsonPropertyName("judgeLineMoveEvents")]
            public List<MoveEventV3> MoveEvents { get; set; } = new();

            [JsonPropertyName("judgeLineRotateEvents")]
            public List<RotateEventV3> RotateEvents { get; set; } = new();

            [JsonPropertyName("judgeLineDisappearEvents")]
            public List<DisappearEventV3> DisappearEvents { get; set; } = new();
        }

        public class NoteV3
        {
            [JsonPropertyName("type")]
            public int Type { get; set; }

            [JsonPropertyName("time")]
            public int Time { get; set; }

            [JsonPropertyName("positionX")]
            public float PositionX { get; set; }

            [JsonPropertyName("holdTime")]
            public int HoldTime { get; set; }

            [JsonPropertyName("speed")]
            public float Speed { get; set; }

            [JsonPropertyName("floorPosition")]
            public float FloorPosition { get; set; }
        }

        public class SpeedEventV3 : IEventV3
        {
            [JsonPropertyName("startTime")]
            public int StartTime { get; set; }

            [JsonPropertyName("endTime")]
            public int EndTime { get; set; }

            [JsonPropertyName("value")]
            public float Value { get; set; }
        }

        public class MoveEventV3 : IEventV3
        {
            [JsonPropertyName("startTime")]
            public int StartTime { get; set; }

            [JsonPropertyName("endTime")]
            public int EndTime { get; set; }

            [JsonPropertyName("start")]
            public float X1 { get; set; }

            [JsonPropertyName("end")]
            public float X2 { get; set; }

            [JsonPropertyName("start2")]
            public float Y1 { get; set; }

            [JsonPropertyName("end2")]
            public float Y2 { get; set; }
        }

        public class RotateEventV3 : IEventV3
        {
            [JsonPropertyName("startTime")]
            public int StartTime { get; set; }

            [JsonPropertyName("endTime")]
            public int EndTime { get; set; }

            [JsonPropertyName("start")]
            public float Angle1 { get; set; }

            [JsonPropertyName("end")]
            public float Angle2 { get; set; }
        }

        public class DisappearEventV3 : IEventV3
        {
            [JsonPropertyName("startTime")]
            public int StartTime { get; set; }

            [JsonPropertyName("endTime")]
            public int EndTime { get; set; }

            [JsonPropertyName("start")]
            public float Alpha1 { get; set; }

            [JsonPropertyName("end")]
            public float Alpha2 { get; set; }
        }

        private interface IEventV3
        {
            int StartTime { get; set; }
            int EndTime { get; set; }
        }
        #endregion

        #region 核心加载逻辑增强
        public static ChartV3 LoadChart(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new JsonException("谱面 JSON 内容为空，无法解析");

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                throw new JsonException($"谱面 JSON 根节点应为对象，实际为 {root.ValueKind}");

            var formatVersion = RequireProperty(root, "formatVersion", "root").GetInt32();
            var chart = formatVersion switch
            {
                1 => ConvertV1(root),
                3 => ConvertV3(root),
                _ => ConvertOtherFormat(root)
            };

            chart.JudgeLines ??= new List<JudgeLineV3>();

            foreach (var line in chart.JudgeLines)
                NormalizeJudgeLine(line);

            return chart;
        }

        private static ChartV3 ConvertOtherFormat(JsonElement root)
        {
            const float centerRange = 5f;

            var chart = new ChartV3
            {
                Offset = RequireProperty(root, "offset", "root").GetSingle(),
                JudgeLines = new List<JudgeLineV3>()
            };

            int lineIndex = 0;
            foreach (var lineElem in RequireProperty(root, "judgeLineList", "root").EnumerateArray())
            {
                string lineContext = $"judgeLineList[{lineIndex}]";
                var judgeLine = new JudgeLineV3
                {
                    Bpm = RequireProperty(lineElem, "bpm", lineContext).GetSingle(),
                    SpeedEvents = ParseSpeedEvents(RequireProperty(lineElem, "speedEvents", lineContext), $"{lineContext}.speedEvents"),
                    MoveEvents = ParseOtherMoveEvents(RequireProperty(lineElem, "judgeLineMoveEvents", lineContext), centerRange, $"{lineContext}.judgeLineMoveEvents"),
                    RotateEvents = ParseRotateEvents(RequireProperty(lineElem, "judgeLineRotateEvents", lineContext), $"{lineContext}.judgeLineRotateEvents"),
                    DisappearEvents = ParseDisappearEvents(RequireProperty(lineElem, "judgeLineDisappearEvents", lineContext), $"{lineContext}.judgeLineDisappearEvents"),
                    NotesAbove = ParseOtherNotes(RequireProperty(lineElem, "notesAbove", lineContext), centerRange, $"{lineContext}.notesAbove"),
                    NotesBelow = ParseOtherNotes(RequireProperty(lineElem, "notesBelow", lineContext), centerRange, $"{lineContext}.notesBelow")
                };

                chart.JudgeLines.Add(judgeLine);
                lineIndex++;
            }
            return chart;
        }
        #endregion

        #region 事件列表规范化
        private const int MinStart = -999999;
        private const int MaxEnd = 1000000000;

        private static void NormalizeJudgeLine(JudgeLineV3 line)
        {
            line.SpeedEvents ??= new List<SpeedEventV3>();
            line.MoveEvents ??= new List<MoveEventV3>();
            line.RotateEvents ??= new List<RotateEventV3>();
            line.DisappearEvents ??= new List<DisappearEventV3>();

            NormalizeEventList(line.SpeedEvents, minStartTime: 0);
            NormalizeEventList(line.MoveEvents, MinStart);
            NormalizeEventList(line.RotateEvents, MinStart);
            NormalizeEventList(line.DisappearEvents, MinStart);
        }

        private static void NormalizeEventList<T>(List<T> events, int minStartTime)
            where T : class, IEventV3, new()
        {
            if (events.Count == 0)
                return;

            // 按 StartTime 排序
            events.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

            // 修补第一个事件的 StartTime
            events[0].StartTime = minStartTime;

            // 填补相邻 gap: 前一个 EndTime 不等于下一个 StartTime 时拉齐
            for (int i = 0; i < events.Count - 1; i++)
            {
                if (events[i].EndTime != events[i + 1].StartTime)
                    events[i].EndTime = events[i + 1].StartTime;
            }

            // 最后一个事件的 EndTime 扩展到足够大
            events[^1].EndTime = MaxEnd;
        }
        #endregion

        #region 其他版本数据解析
        private static List<MoveEventV3> ParseOtherMoveEvents(JsonElement events, float centerRange, string context)
        {
            var list = new List<MoveEventV3>();
            int index = 0;
            foreach (var e in events.EnumerateArray())
            {
                string itemContext = $"{context}[{index}]";
                list.Add(new MoveEventV3
                {
                    StartTime = ParseIntFromJson(RequireProperty(e, "startTime", itemContext), $"{itemContext}.startTime"),
                    EndTime = ParseIntFromJson(RequireProperty(e, "endTime", itemContext), $"{itemContext}.endTime"),
                    X1 = ConvertToV3Coord(RequireProperty(e, "start", itemContext).GetSingle(), centerRange),
                    Y1 = ConvertToV3Coord(RequireProperty(e, "start2", itemContext).GetSingle(), centerRange),
                    X2 = ConvertToV3Coord(RequireProperty(e, "end", itemContext).GetSingle(), centerRange),
                    Y2 = ConvertToV3Coord(RequireProperty(e, "end2", itemContext).GetSingle(), centerRange)
                });
                index++;
            }
            return list;
        }

        private static float ConvertToV3Coord(float original, float centerRange)
        {
            return (original + centerRange) / (2 * centerRange);
        }

        private static List<NoteV3> ParseOtherNotes(JsonElement notes, float centerRange, string context)
        {
            var list = new List<NoteV3>();
            int index = 0;
            foreach (var n in notes.EnumerateArray())
            {
                string itemContext = $"{context}[{index}]";
                list.Add(new NoteV3
                {
                    Type = ParseIntFromJson(RequireProperty(n, "type", itemContext), $"{itemContext}.type"),
                    Time = ParseIntFromJson(RequireProperty(n, "time", itemContext), $"{itemContext}.time"),
                    PositionX = ConvertToV3Coord(RequireProperty(n, "positionX", itemContext).GetSingle(), centerRange),
                    HoldTime = ParseIntFromJson(RequireProperty(n, "holdTime", itemContext), $"{itemContext}.holdTime"),
                    Speed = RequireProperty(n, "speed", itemContext).GetSingle(),
                    FloorPosition = ConvertToV3Coord(RequireProperty(n, "floorPosition", itemContext).GetSingle(), centerRange)
                });
                index++;
            }
            return list;
        }
        #endregion

        #region 数据解析
        private static ChartV3 ConvertV1(JsonElement root)
        {
            var chart = new ChartV3
            {
                Offset = RequireProperty(root, "offset", "root").GetSingle(),
                JudgeLines = new List<JudgeLineV3>()
            };

            int lineIndex = 0;
            foreach (var lineElem in RequireProperty(root, "judgeLineList", "root").EnumerateArray())
            {
                string lineContext = $"judgeLineList[{lineIndex}]";
                chart.JudgeLines.Add(new JudgeLineV3
                {
                    Bpm = RequireProperty(lineElem, "bpm", lineContext).GetSingle(),
                    SpeedEvents = ParseSpeedEvents(RequireProperty(lineElem, "speedEvents", lineContext), $"{lineContext}.speedEvents"),
                    MoveEvents = ParseMoveEventsV1(RequireProperty(lineElem, "judgeLineMoveEvents", lineContext), $"{lineContext}.judgeLineMoveEvents"),
                    RotateEvents = ParseRotateEvents(RequireProperty(lineElem, "judgeLineRotateEvents", lineContext), $"{lineContext}.judgeLineRotateEvents"),
                    DisappearEvents = ParseDisappearEvents(RequireProperty(lineElem, "judgeLineDisappearEvents", lineContext), $"{lineContext}.judgeLineDisappearEvents"),
                    NotesAbove = ParseNotes(RequireProperty(lineElem, "notesAbove", lineContext), $"{lineContext}.notesAbove"),
                    NotesBelow = ParseNotes(RequireProperty(lineElem, "notesBelow", lineContext), $"{lineContext}.notesBelow")
                });
                lineIndex++;
            }
            return chart;
        }

        private static ChartV3 ConvertV3(JsonElement root)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowReadingFromString |
                                 JsonNumberHandling.AllowNamedFloatingPointLiterals,
                Converters = { new Int32Converter() },
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            var chart = JsonSerializer.Deserialize<ChartV3>(root.GetRawText(), options);
            if (chart is null)
                throw new JsonException("formatVersion=3 的谱面反序列化结果为 null");

            return chart;
        }
        #endregion

        #region 增强的整型处理
        private static List<NoteV3> ParseNotes(JsonElement notes, string context)
        {
            var list = new List<NoteV3>();
            int index = 0;
            foreach (var n in notes.EnumerateArray())
            {
                string itemContext = $"{context}[{index}]";
                list.Add(new NoteV3
                {
                    Type = ParseIntFromJson(RequireProperty(n, "type", itemContext), $"{itemContext}.type"),
                    Time = ParseIntFromJson(RequireProperty(n, "time", itemContext), $"{itemContext}.time"),
                    PositionX = RequireProperty(n, "positionX", itemContext).GetSingle(),
                    HoldTime = ParseIntFromJson(RequireProperty(n, "holdTime", itemContext), $"{itemContext}.holdTime"),
                    Speed = RequireProperty(n, "speed", itemContext).GetSingle(),
                    FloorPosition = RequireProperty(n, "floorPosition", itemContext).GetSingle()
                });
                index++;
            }
            return list;
        }

        // 取属性，字段缺失时抛出带字段名与上下文的明确异常
        private static JsonElement RequireProperty(JsonElement element, string name, string context)
        {
            if (element.ValueKind != JsonValueKind.Object)
                throw new JsonException($"期望 JSON 对象，实际为 {element.ValueKind}（位置: {context}）");

            if (!element.TryGetProperty(name, out var value))
                throw new JsonException($"谱面缺少字段 \"{name}\"（位置: {context}）");

            return value;
        }

        private static int ParseIntFromJson(JsonElement element, string context)
        {
            if (element.ValueKind == JsonValueKind.Number)
            {
                if (element.TryGetInt32(out int intValue)) return intValue;
                return RoundToInt32(element.GetDouble());
            }

            if (element.ValueKind == JsonValueKind.String)
                return ParseInt32String(element.GetString(), context);

            throw new JsonException($"无法转换为整数：字段 {context} 的 JSON 类型为 {element.ValueKind}");
        }

        // 小数拍（如 192.5）四舍五入取整，不再抛异常
        private static int RoundToInt32(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new JsonException($"无法转换为整数：值为 {value}");

            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        private static int ParseInt32String(string text, string context)
        {
            if (text is null)
                throw new JsonException($"无法转换为整数：字段 {context} 的值为 null");

            if (int.TryParse(text, out int intValue)) return intValue;

            if (double.TryParse(text, out double doubleValue)) return RoundToInt32(doubleValue);

            throw new JsonException($"无法转换为整数：字段 {context} 的值 \"{text}\" 不是数字");
        }

        private class Int32Converter : JsonConverter<int>
        {
            public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.Number)
                {
                    if (reader.TryGetInt32(out int intValue)) return intValue;
                    return RoundToInt32(reader.GetDouble());
                }

                if (reader.TokenType == JsonTokenType.String)
                    return ParseInt32String(reader.GetString(), "整数属性");

                throw new JsonException($"无法转换为整数：JSON token 类型为 {reader.TokenType}");
            }

            public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
                => writer.WriteNumberValue(value);
        }
        #endregion

        #region 其他数据解析方法
        private static List<MoveEventV3> ParseMoveEventsV1(JsonElement events, string context)
        {
            var list = new List<MoveEventV3>();
            int index = 0;
            foreach (var e in events.EnumerateArray())
            {
                string itemContext = $"{context}[{index}]";
                var start = ParseIntFromJson(RequireProperty(e, "start", itemContext), $"{itemContext}.start");
                var end = ParseIntFromJson(RequireProperty(e, "end", itemContext), $"{itemContext}.end");

                list.Add(new MoveEventV3
                {
                    StartTime = ParseIntFromJson(RequireProperty(e, "startTime", itemContext), $"{itemContext}.startTime"),
                    EndTime = ParseIntFromJson(RequireProperty(e, "endTime", itemContext), $"{itemContext}.endTime"),
                    X1 = (start % 1000f) / 880f,
                    Y1 = (start / 1000f) / 520f,
                    X2 = (end % 1000f) / 880f,
                    Y2 = (end / 1000f) / 520f
                });
                index++;
            }
            return list;
        }

        private static List<SpeedEventV3> ParseSpeedEvents(JsonElement events, string context)
        {
            var list = new List<SpeedEventV3>();
            int index = 0;
            foreach (var e in events.EnumerateArray())
            {
                string itemContext = $"{context}[{index}]";
                list.Add(new SpeedEventV3
                {
                    StartTime = ParseIntFromJson(RequireProperty(e, "startTime", itemContext), $"{itemContext}.startTime"),
                    EndTime = ParseIntFromJson(RequireProperty(e, "endTime", itemContext), $"{itemContext}.endTime"),
                    Value = RequireProperty(e, "value", itemContext).GetSingle()
                });
                index++;
            }
            return list;
        }

        private static List<RotateEventV3> ParseRotateEvents(JsonElement events, string context)
        {
            var list = new List<RotateEventV3>();
            int index = 0;
            foreach (var e in events.EnumerateArray())
            {
                string itemContext = $"{context}[{index}]";
                list.Add(new RotateEventV3
                {
                    StartTime = ParseIntFromJson(RequireProperty(e, "startTime", itemContext), $"{itemContext}.startTime"),
                    EndTime = ParseIntFromJson(RequireProperty(e, "endTime", itemContext), $"{itemContext}.endTime"),
                    Angle1 = RequireProperty(e, "start", itemContext).GetSingle(),
                    Angle2 = RequireProperty(e, "end", itemContext).GetSingle()
                });
                index++;
            }
            return list;
        }

        private static List<DisappearEventV3> ParseDisappearEvents(JsonElement events, string context)
        {
            var list = new List<DisappearEventV3>();
            int index = 0;
            foreach (var e in events.EnumerateArray())
            {
                string itemContext = $"{context}[{index}]";
                list.Add(new DisappearEventV3
                {
                    StartTime = ParseIntFromJson(RequireProperty(e, "startTime", itemContext), $"{itemContext}.startTime"),
                    EndTime = ParseIntFromJson(RequireProperty(e, "endTime", itemContext), $"{itemContext}.endTime"),
                    Alpha1 = RequireProperty(e, "start", itemContext).GetSingle(),
                    Alpha2 = RequireProperty(e, "end", itemContext).GetSingle()
                });
                index++;
            }
            return list;
        }
        #endregion
    }
}
