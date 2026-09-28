using System.Text.Json.Serialization;

namespace WebMacros.Engine.Scripting;

/// <summary>Frame selection sent to the finder runtime: F=n (0 = top document) or NAME=.</summary>
public sealed record FrameSpec(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("name")] string? Name)
{
    public static FrameSpec ByIndex(int index) => new(index, null);
    public static FrameSpec ByName(string name) => new(0, name);
    public bool IsTop => Index == 0 && Name is null;
}

public sealed record TypeSpec(
    [property: JsonPropertyName("tag")] string Tag,
    [property: JsonPropertyName("sub")] string? Sub);

public sealed record AttrCondition(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("value")] string Value);

/// <summary>JSON description of an element operation, consumed by <c>__wm.run(spec)</c>.</summary>
public sealed record ElementSpec
{
    [JsonPropertyName("kind")] public string Kind { get; init; } = "tag";
    [JsonPropertyName("pos")] public int Pos { get; init; } = 1;
    [JsonPropertyName("relative")] public bool Relative { get; init; }
    [JsonPropertyName("type")] public TypeSpec? Type { get; init; }
    [JsonPropertyName("attrs")] public IReadOnlyList<AttrCondition>? Attrs { get; init; }
    [JsonPropertyName("form")] public IReadOnlyList<AttrCondition>? Form { get; init; }
    [JsonPropertyName("xpath")] public string? XPath { get; init; }
    [JsonPropertyName("selector")] public string? Selector { get; init; }
    [JsonPropertyName("content")] public string? Content { get; init; }
    [JsonPropertyName("pressEnter")] public bool PressEnter { get; init; }
    [JsonPropertyName("extract")] public string? Extract { get; init; }
    [JsonPropertyName("frame")] public FrameSpec? Frame { get; init; }
    [JsonPropertyName("eventType")] public string? EventType { get; init; }
    [JsonPropertyName("key")] public int? Key { get; init; }
    [JsonPropertyName("chars")] public string? Chars { get; init; }
    [JsonPropertyName("x")] public double? X { get; init; }
    [JsonPropertyName("y")] public double? Y { get; init; }
}

/// <summary>Result object returned (as JSON) by the finder runtime.</summary>
public sealed record ElementResult
{
    [JsonPropertyName("ok")] public bool Ok { get; init; }
    [JsonPropertyName("found")] public bool Found { get; init; }
    [JsonPropertyName("retry")] public bool Retry { get; init; }
    [JsonPropertyName("error")] public string? Error { get; init; }
    [JsonPropertyName("value")] public string? Value { get; init; }
    [JsonPropertyName("tag")] public string? Tag { get; init; }
}

/// <summary>Result of an EVAL() script.</summary>
public sealed record EvalResult
{
    [JsonPropertyName("ok")] public bool Ok { get; init; }
    [JsonPropertyName("value")] public string? Value { get; init; }
    [JsonPropertyName("error")] public string? Error { get; init; }
    [JsonPropertyName("macroError")] public bool MacroError { get; init; }
}
