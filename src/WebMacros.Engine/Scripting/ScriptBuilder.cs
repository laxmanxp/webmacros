using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebMacros.Engine.Scripting;

/// <summary>
/// Builds the JavaScript snippets the engine sends through <see cref="Runtime.IBrowserDriver.ExecuteScriptAsync"/>.
/// Every script evaluates to a JSON <em>string</em> (via JSON.stringify) so results are identical in any JS host.
/// </summary>
public static class ScriptBuilder
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    public static string SerializeSpec(ElementSpec spec) => JsonSerializer.Serialize(spec, JsonOptions);

    /// <summary>Script that runs an element operation described by <paramref name="spec"/>.</summary>
    public static string BuildElementScript(ElementSpec spec) =>
        "(function(){\n" + FinderRuntime.Source + "\nvar __spec = " + SerializeSpec(spec) + ";\nreturn JSON.stringify(__wm.run(__spec));\n})()";

    /// <summary>Script for <c>EVAL("...")</c>: evaluates the code, supports <c>MacroError("msg")</c>.</summary>
    public static string BuildEvalScript(string code) =>
        "(function(){\n" +
        "  function MacroError(m){ var e = new Error(String(m)); e.__macroError = true; throw e; }\n" +
        "  try {\n" +
        "    var __v = eval(" + JsonSerializer.Serialize(code) + ");\n" +
        "    return JSON.stringify({ ok: true, value: (__v === undefined || __v === null) ? '' : String(__v) });\n" +
        "  } catch (e) {\n" +
        "    return JSON.stringify({ ok: false, macroError: !!(e && e.__macroError), error: String(e && e.message ? e.message : e) });\n" +
        "  }\n" +
        "})()";

    /// <summary>Script returning the full HTML of the (top) document.</summary>
    public const string PageHtmlScript = "JSON.stringify({ ok: true, value: document.documentElement ? document.documentElement.outerHTML : '' })";
    public const string PageTextScript = "JSON.stringify({ ok: true, value: document.body ? (document.body.innerText || document.body.textContent || '') : '' })";
    public const string PageTitleScript = "JSON.stringify({ ok: true, value: document.title || '' })";

    /// <summary>
    /// Decodes the raw value returned by a WebView2-style ExecuteScriptAsync (the JSON encoding of the script's
    /// completion value). Our scripts return JSON strings, so the raw value is a JSON string literal containing JSON.
    /// </summary>
    public static string? DecodeScriptResult(string? raw)
    {
        if (raw is null) return null;
        var t = raw.Trim();
        if (t.Length == 0 || t == "null" || t == "undefined") return null;
        if (t[0] == '"')
        {
            try { return JsonSerializer.Deserialize<string>(t); }
            catch (JsonException) { return t; }
        }
        return t;
    }

    public static T? ParseResult<T>(string? raw) where T : class
    {
        var json = DecodeScriptResult(raw);
        if (json is null) return null;
        try { return JsonSerializer.Deserialize<T>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }
}
