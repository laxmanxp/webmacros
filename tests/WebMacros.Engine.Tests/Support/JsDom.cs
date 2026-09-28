using System.Reflection;
using System.Text.Json;
using Jint;
using Jint.Native;

namespace WebMacros.Engine.Tests.Support;

/// <summary>A Jint engine preloaded with the tiny test DOM (Support/TestDom.js).</summary>
public sealed class JsDom
{
    private static readonly string DomSource = LoadDom();
    public Jint.Engine Engine { get; }

    public JsDom(string? pageScript = null)
    {
        Engine = new Jint.Engine(o => o.TimeoutInterval(TimeSpan.FromSeconds(10)));
        Engine.Execute(DomSource);
        if (pageScript is not null) Engine.Execute(pageScript);
    }

    private static string LoadDom()
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().Single(n => n.EndsWith("TestDom.js", StringComparison.Ordinal));
        using var s = asm.GetManifestResourceStream(name)!;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public void Execute(string script) => Engine.Execute(script);

    public JsValue Evaluate(string script) => Engine.Evaluate(script);

    public string EvalString(string script) => Engine.Evaluate(script).ToString();

    /// <summary>Returns the value as WebView2's ExecuteScriptAsync would (JSON encoding).</summary>
    public string EvaluateAsWebView2Json(string script)
    {
        var v = Engine.Evaluate(script);
        if (v.IsString()) return JsonSerializer.Serialize(v.AsString());
        if (v.IsNull() || v.IsUndefined()) return "null";
        if (v.IsBoolean()) return v.AsBoolean() ? "true" : "false";
        if (v.IsNumber()) return v.AsNumber().ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Engine.Evaluate("JSON.stringify").AsFunctionInstance().Call(v).ToString();
    }
}
