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

        public class SpeedEventV3
        {
            [JsonPropertyName("startTime")]
            public int StartTime { get; set; }

            [JsonPropertyName("endTime")]
            public int EndTime { get; set; }

            [JsonPropertyName("value")]
            public float Value { get; set; }
        }

        public class MoveEventV3
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

        public class RotateEventV3
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

        public class DisappearEventV3
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
        #endregion

        #region 核心加载逻辑增强
        public static ChartV3 LoadChart(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            var formatVersion = root.GetProperty("formatVersion").GetInt32();
            return formatVersion switch
            {
                1 => ConvertV1(root),
                3 => ConvertV3(root),
                _ => ConvertOtherFormat(root, formatVersion) // 新增其他格式处理
            };
        }

        private static ChartV3 ConvertOtherFormat(JsonElement root, int originVersion)
        {
            const float centerRange = 5f; // 中心坐标系范围 ±5单位

            var chart = new ChartV3
            {
                Offset = root.GetProperty("offset").GetSingle(),
                JudgeLines = new List<JudgeLineV3>()
            };

            foreach (var lineElem in root.GetProperty("judgeLineList").EnumerateArray())
            {
                var judgeLine = new JudgeLineV3
                {
                    Bpm = lineElem.GetProperty("bpm").GetSingle(),
                    SpeedEvents = ParseSpeedEvents(lineElem.GetProperty("speedEvents")),
                    MoveEvents = ParseOtherMoveEvents(lineElem.GetProperty("judgeLineMoveEvents"), centerRange),
                    RotateEvents = ParseOtherRotateEvents(lineElem.GetProperty("judgeLineRotateEvents")),
                    DisappearEvents = ParseDisappearEvents(lineElem.GetProperty("judgeLineDisappearEvents")),
                    NotesAbove = ParseOtherNotes(lineElem.GetProperty("notesAbove"), centerRange),
                    NotesBelow = ParseOtherNotes(lineElem.GetProperty("notesBelow"), centerRange)
                };

                chart.JudgeLines.Add(judgeLine);
            }
            return chart;
        }
        #endregion

        #region 其他版本数据解析
        private static List<MoveEventV3> ParseOtherMoveEvents(JsonElement events, float centerRange)
        {
            var list = new List<MoveEventV3>();
            foreach (var e in events.EnumerateArray())
            {
                list.Add(new MoveEventV3
                {
                    StartTime = ParseIntFromJson(e.GetProperty("startTime")),
                    EndTime = ParseIntFromJson(e.GetProperty("endTime")),
                    // 坐标系转换公式：v3 = (原坐标 + centerRange) / (2 * centerRange)
                    X1 = ConvertToV3Coord(e.GetProperty("start").GetSingle(), centerRange),
                    Y1 = ConvertToV3Coord(e.GetProperty("start2").GetSingle(), centerRange),
                    X2 = ConvertToV3Coord(e.GetProperty("end").GetSingle(), centerRange),
                    Y2 = ConvertToV3Coord(e.GetProperty("end2").GetSingle(), centerRange)
                });
            }
            return list;
        }

        private static float ConvertToV3Coord(float original, float centerRange)
        {
            // 将中心坐标系转换为左下角原点
            return (original + centerRange) / (2 * centerRange);
        }

        private static List<NoteV3> ParseOtherNotes(JsonElement notes, float centerRange)
        {
            var list = new List<NoteV3>();
            foreach (var n in notes.EnumerateArray())
            {
                list.Add(new NoteV3
                {
                    Type = ParseIntFromJson(n.GetProperty("type")),
                    Time = ParseIntFromJson(n.GetProperty("time")),
                    PositionX = ConvertToV3Coord(n.GetProperty("positionX").GetSingle(), centerRange),
                    HoldTime = ParseIntFromJson(n.GetProperty("holdTime")),
                    Speed = n.GetProperty("speed").GetSingle(),
                    FloorPosition = ConvertToV3Coord(n.GetProperty("floorPosition").GetSingle(), centerRange)
                });
            }
            return list;
        }

        private static List<RotateEventV3> ParseOtherRotateEvents(JsonElement events)
        {
            var list = new List<RotateEventV3>();
            foreach (var e in events.EnumerateArray())
            {
                // 旋转角度直接保留原始值
                list.Add(new RotateEventV3
                {
                    StartTime = ParseIntFromJson(e.GetProperty("startTime")),
                    EndTime = ParseIntFromJson(e.GetProperty("endTime")),
                    Angle1 = e.GetProperty("start").GetSingle(),
                    Angle2 = e.GetProperty("end").GetSingle()
                });
            }
            return list;
        }
        #endregion

        #region 数据解析
        private static ChartV3 ConvertV1(JsonElement root)
        {
            var chart = new ChartV3
            {
                Offset = root.GetProperty("offset").GetSingle(),
                JudgeLines = new List<JudgeLineV3>()
            };

            foreach (var lineElem in root.GetProperty("judgeLineList").EnumerateArray())
            {
                chart.JudgeLines.Add(new JudgeLineV3
                {
                    Bpm = lineElem.GetProperty("bpm").GetSingle(),
                    SpeedEvents = ParseSpeedEvents(lineElem.GetProperty("speedEvents")),
                    MoveEvents = ParseMoveEventsV1(lineElem.GetProperty("judgeLineMoveEvents")),
                    RotateEvents = ParseRotateEvents(lineElem.GetProperty("judgeLineRotateEvents")),
                    DisappearEvents = ParseDisappearEvents(lineElem.GetProperty("judgeLineDisappearEvents")),
                    NotesAbove = ParseNotes(lineElem.GetProperty("notesAbove")),
                    NotesBelow = ParseNotes(lineElem.GetProperty("notesBelow"))
                });
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

            return JsonSerializer.Deserialize<ChartV3>(root.GetRawText(), options);
        }
        #endregion

        #region 增强的整型处理
        private static List<NoteV3> ParseNotes(JsonElement notes)
        {
            var list = new List<NoteV3>();
            foreach (var n in notes.EnumerateArray())
            {
                list.Add(new NoteV3
                {
                    Type = ParseIntFromJson(n.GetProperty("type")),
                    Time = ParseIntFromJson(n.GetProperty("time")),
                    PositionX = n.GetProperty("positionX").GetSingle(),
                    HoldTime = ParseIntFromJson(n.GetProperty("holdTime")),
                    Speed = n.GetProperty("speed").GetSingle(),
                    FloorPosition = n.GetProperty("floorPosition").GetSingle()
                });
            }
            return list;
        }

        private static int ParseIntFromJson(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number)
            {
                if (element.TryGetInt32(out int intValue)) return intValue;
                
                var floatValue = element.GetDouble();
                if (Math.Abs(floatValue % 1) > 0.0001)
                    throw new JsonException($"Invalid integer value: {floatValue}");
                return (int)floatValue;
            }

            if (element.ValueKind == JsonValueKind.String)
                return int.Parse(element.GetString()!);

            throw new JsonException($"Unexpected JSON type {element.ValueKind} for integer");
        }

        private class Int32Converter : JsonConverter<int>
        {
            public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.Number)
                {
                    if (reader.TryGetInt32(out int intValue)) return intValue;
                    
                    var floatValue = reader.GetDouble();
                    if (Math.Abs(floatValue % 1) > 0.0001)
                        throw new JsonException($"Invalid integer value: {floatValue}");
                    return (int)floatValue;
                }

                if (reader.TokenType == JsonTokenType.String)
                    return int.Parse(reader.GetString()!);

                throw new JsonException($"Unexpected token {reader.TokenType}");
            }

            public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
                => writer.WriteNumberValue(value);
        }
        #endregion

        #region 其他数据解析方法
        private static List<MoveEventV3> ParseMoveEventsV1(JsonElement events)
        {
            var list = new List<MoveEventV3>();
            foreach (var e in events.EnumerateArray())
            {
                var start = ParseIntFromJson(e.GetProperty("start"));
                var end = ParseIntFromJson(e.GetProperty("end"));

                list.Add(new MoveEventV3
                {
                    StartTime = ParseIntFromJson(e.GetProperty("startTime")),
                    EndTime = ParseIntFromJson(e.GetProperty("endTime")),
                    X1 = (start % 1000f) / 880f,
                    Y1 = (start / 1000f) / 520f,
                    X2 = (end % 1000f) / 880f,
                    Y2 = (end / 1000f) / 520f
                });
            }
            return list;
        }

        private static List<SpeedEventV3> ParseSpeedEvents(JsonElement events)
        {
            var list = new List<SpeedEventV3>();
            foreach (var e in events.EnumerateArray())
            {
                list.Add(new SpeedEventV3
                {
                    StartTime = ParseIntFromJson(e.GetProperty("startTime")),
                    EndTime = ParseIntFromJson(e.GetProperty("endTime")),
                    Value = e.GetProperty("value").GetSingle()
                });
            }
            return list;
        }

        private static List<RotateEventV3> ParseRotateEvents(JsonElement events)
        {
            var list = new List<RotateEventV3>();
            foreach (var e in events.EnumerateArray())
            {
                list.Add(new RotateEventV3
                {
                    StartTime = ParseIntFromJson(e.GetProperty("startTime")),
                    EndTime = ParseIntFromJson(e.GetProperty("endTime")),
                    Angle1 = e.GetProperty("start").GetSingle(),
                    Angle2 = e.GetProperty("end").GetSingle()
                });
            }
            return list;
        }

        private static List<DisappearEventV3> ParseDisappearEvents(JsonElement events)
        {
            var list = new List<DisappearEventV3>();
            foreach (var e in events.EnumerateArray())
            {
                list.Add(new DisappearEventV3
                {
                    StartTime = ParseIntFromJson(e.GetProperty("startTime")),
                    EndTime = ParseIntFromJson(e.GetProperty("endTime")),
                    Alpha1 = e.GetProperty("start").GetSingle(),
                    Alpha2 = e.GetProperty("end").GetSingle()
                });
            }
            return list;
        }
        #endregion
    }
}