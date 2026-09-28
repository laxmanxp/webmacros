using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebMacros.Engine.Recording;

public sealed record AttrCandidate(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("pos")] int Pos);

/// <summary>One user action captured by the recorder script.</summary>
public sealed record RecordedAction
{
    /// <summary>click | type | select | check | submit</summary>
    [JsonPropertyName("action")] public string Action { get; init; } = "";
    /// <summary>iMacros type string, e.g. INPUT:TEXT, A, BUTTON:SUBMIT.</summary>
    [JsonPropertyName("type")] public string Type { get; init; } = "";
    [JsonPropertyName("candidates")] public IReadOnlyList<AttrCandidate> Candidates { get; init; } = Array.Empty<AttrCandidate>();
    [JsonPropertyName("value")] public string? Value { get; init; }
    [JsonPropertyName("values")] public IReadOnlyList<string>? Values { get; init; }
    [JsonPropertyName("checked")] public bool? Checked { get; init; }
    [JsonPropertyName("password")] public bool Password { get; init; }

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static RecordedAction? Parse(string json)
    {
        try { return JsonSerializer.Deserialize<RecordedAction>(json, Options); }
        catch (JsonException) { return null; }
    }
}
