using System.Text.Json;
using WebMacros.Engine.Scripting;
using WebMacros.Engine.Syntax;

namespace WebMacros.Engine.Tests;

public class TagSpecBuilderTests
{
    [Theory]
    [InlineData("1", 1, false)]
    [InlineData("12", 12, false)]
    [InlineData("R1", 1, true)]
    [InlineData("r3", 3, true)]
    [InlineData("R-2", -2, true)]
    public void ParsesPos(string pos, int value, bool relative)
    {
        Assert.True(TagSpecBuilder.TryParsePos(pos, out var v, out var r));
        Assert.Equal((value, relative), (v, r));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("R0")]
    [InlineData("x")]
    public void RejectsBadPos(string pos) => Assert.False(TagSpecBuilder.TryParsePos(pos, out _, out _));

    [Theory]
    [InlineData("INPUT:TEXT", "INPUT", "TEXT")]
    [InlineData("a", "A", null)]
    [InlineData("*", "*", null)]
    [InlineData("INPUT:*", "INPUT", "*")]
    [InlineData("button:submit", "BUTTON", "SUBMIT")]
    public void ParsesType(string type, string tag, string? sub)
    {
        var t = TagSpecBuilder.ParseType(type);
        Assert.Equal((tag, sub), (t.Tag, t.Sub));
    }

    [Fact]
    public void ParsesAttrCombosAndKeepsColonsInValue()
    {
        var a = TagSpecBuilder.ParseAttr("NAME:q&&href:https://x.test/a*&&data-id:5");
        Assert.Equal(3, a.Count);
        Assert.Equal(new AttrCondition("NAME", "q"), a[0]);
        Assert.Equal(new AttrCondition("HREF", "https://x.test/a*"), a[1]);
        Assert.Equal(new AttrCondition("DATA-ID", "5"), a[2]);
        Assert.Empty(TagSpecBuilder.ParseAttr("*"));
        Assert.Empty(TagSpecBuilder.ParseAttr(null));
        Assert.Throws<MacroSyntaxException>(() => TagSpecBuilder.ParseAttr("justtext"));
    }

    [Fact]
    public void BuildTagSpecWithContentEnterAndFrame()
    {
        var spec = TagSpecBuilder.BuildTag("2", "INPUT:TEXT", "NAME:q", null, null, null, "hello<ENTER>", null, FrameSpec.ByIndex(1));
        Assert.Equal(2, spec.Pos);
        Assert.False(spec.Relative);
        Assert.Equal("hello", spec.Content);
        Assert.True(spec.PressEnter);
        Assert.Equal(1, spec.Frame!.Index);
        Assert.Equal("tag", spec.Kind);
    }

    [Fact]
    public void TopFrameIsOmitted()
    {
        var spec = TagSpecBuilder.BuildTag(null, "A", null, null, null, null, null, "href", FrameSpec.ByIndex(0));
        Assert.Null(spec.Frame);
        Assert.Equal("HREF", spec.Extract);
        Assert.Equal(1, spec.Pos);
    }

    [Fact]
    public void RelativeNotAllowedWithXPath() =>
        Assert.Throws<MacroSyntaxException>(() => TagSpecBuilder.BuildTag("R1", null, null, null, "//a", null, null, null, null));

    [Fact]
    public void InvalidExtractRejected() =>
        Assert.Throws<MacroSyntaxException>(() => TagSpecBuilder.BuildTag("1", "A", null, null, null, null, null, "T X", null));

    [Fact]
    public void SerializedSpecIsCompactJson()
    {
        var spec = TagSpecBuilder.BuildTag("R-1", "A", "TXT:Next*", "ID:f", null, null, null, null, null);
        var json = ScriptBuilder.SerializeSpec(spec);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("tag", root.GetProperty("kind").GetString());
        Assert.Equal(-1, root.GetProperty("pos").GetInt32());
        Assert.True(root.GetProperty("relative").GetBoolean());
        Assert.Equal("A", root.GetProperty("type").GetProperty("tag").GetString());
        Assert.Equal("TXT", root.GetProperty("attrs")[0].GetProperty("key").GetString());
        Assert.Equal("Next*", root.GetProperty("attrs")[0].GetProperty("value").GetString());
        Assert.Equal("ID", root.GetProperty("form")[0].GetProperty("key").GetString());
        Assert.False(root.TryGetProperty("xpath", out _));
        Assert.False(root.TryGetProperty("content", out _));
    }

    [Fact]
    public void ElementScriptEmbedsRuntimeAndSpec()
    {
        var spec = TagSpecBuilder.BuildTag("1", "A", "TXT:</script><b>", null, null, null, null, null, null);
        var script = ScriptBuilder.BuildElementScript(spec);
        Assert.StartsWith("(function(){", script);
        Assert.Contains("var __wm = (function", script);
        Assert.Contains("__wm.run(__spec)", script);
        Assert.DoesNotContain("</script>", script); // JSON encoder escapes < and >
    }

    [Fact]
    public void EventAndPointSpecs()
    {
        var e = TagSpecBuilder.BuildEvent("keypress", "#q", null, "13", null, null);
        Assert.Equal(("event", "KEYPRESS", 13), (e.Kind, e.EventType, e.Key!.Value));
        var p = TagSpecBuilder.BuildPoint(10, 20.5, null, FrameSpec.ByName("f"));
        Assert.Equal(("point", 10.0, 20.5, "f"), (p.Kind, p.X!.Value, p.Y!.Value, p.Frame!.Name));
        Assert.Throws<MacroSyntaxException>(() => TagSpecBuilder.BuildEvent("KEYPRESS", null, null, "x", null, null));
    }

    [Theory]
    [InlineData("\"{\\\"ok\\\":true}\"", "{\"ok\":true}")]
    [InlineData("null", null)]
    [InlineData("", null)]
    [InlineData("42", "42")]
    public void DecodesWebView2Results(string raw, string? expected) => Assert.Equal(expected, ScriptBuilder.DecodeScriptResult(raw));

    [Fact]
    public void ParseResultToleratesGarbage()
    {
        Assert.Null(ScriptBuilder.ParseResult<ElementResult>("\"not json\""));
        var r = ScriptBuilder.ParseResult<ElementResult>("\"{\\\"ok\\\":true,\\\"value\\\":\\\"x\\\"}\"");
        Assert.True(r!.Ok);
        Assert.Equal("x", r.Value);
    }
}
