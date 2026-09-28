using System.Text.Json;
using WebMacros.Engine.Recording;
using WebMacros.Engine.Runtime;
using WebMacros.Engine.Syntax;
using WebMacros.Engine.Tests.Support;

namespace WebMacros.Engine.Tests;

public class RecorderTests
{
    private static RecordedAction Action(string action, string type, params (string Key, string Value, int Pos)[] c) => new()
    {
        Action = action, Type = type, Candidates = c.Select(x => new AttrCandidate(x.Key, x.Value, x.Pos)).ToArray(),
    };

    [Fact]
    public void PrefersIdThenNameThenText()
    {
        var a = Action("click", "A", ("CLASS", "btn", 2), ("TXT", "Log in", 1), ("ID", "login", 1), ("*", "*", 7));
        Assert.Equal("TAG POS=1 TYPE=A ATTR=ID:login", MacroRecorder.ToLine(a));
        var b = Action("click", "A", ("CLASS", "btn", 2), ("TXT", "Log in", 1), ("*", "*", 7));
        Assert.Equal("TAG POS=1 TYPE=A ATTR=TXT:Log<SP>in", MacroRecorder.ToLine(b));
        var c = Action("click", "DIV", ("*", "*", 4));
        Assert.Equal("TAG POS=4 TYPE=DIV ATTR=*", MacroRecorder.ToLine(c));
    }

    [Fact]
    public void GeneratesContentLines()
    {
        Assert.Equal("TAG POS=1 TYPE=INPUT:TEXT ATTR=NAME:q CONTENT=hello<SP>world",
            MacroRecorder.ToLine(Action("type", "INPUT:TEXT", ("NAME", "q", 1)) with { Value = "hello world" }));
        Assert.Equal("TAG POS=1 TYPE=SELECT ATTR=ID:s CONTENT=%a:%b",
            MacroRecorder.ToLine(Action("select", "SELECT", ("ID", "s", 1)) with { Values = new[] { "a", "b" } }));
        Assert.Equal("TAG POS=2 TYPE=INPUT:CHECKBOX ATTR=NAME:t CONTENT=NO",
            MacroRecorder.ToLine(Action("check", "INPUT:CHECKBOX", ("NAME", "t", 2)) with { Checked = false }));
        Assert.Equal("TAG POS=1 TYPE=TEXTAREA ATTR=NAME:c CONTENT=a<BR>b<TAB>c",
            MacroRecorder.ToLine(Action("type", "TEXTAREA", ("NAME", "c", 1)) with { Value = "a\r\nb\tc" }));
        Assert.Equal("TAG POS=1 TYPE=INPUT:TEXT ATTR=NAME:q CONTENT=x<ENTER>",
            MacroRecorder.ToLine(Action("submit", "INPUT:TEXT", ("NAME", "q", 1)) with { Value = "x" }));
        Assert.Null(MacroRecorder.ToLine(Action("hover", "A")));
    }

    [Fact]
    public void RecorderAccumulatesAndDeduplicates()
    {
        var r = new MacroRecorder();
        var added = new List<string>();
        r.LineAdded += added.Add;
        r.Start("https://a.test/");
        r.AddNavigation("https://a.test/");
        r.Add(Action("type", "INPUT:TEXT", ("NAME", "q", 1)) with { Value = "h" });
        r.Add(Action("type", "INPUT:TEXT", ("NAME", "q", 1)) with { Value = "hello" });
        r.Add(Action("submit", "INPUT:TEXT", ("NAME", "q", 1)) with { Value = "hello" });
        r.Add(Action("type", "INPUT:PASSWORD", ("NAME", "pw", 1)) with { Value = "secret", Password = true });
        r.AddBack();
        r.AddTabOpen();
        r.AddTabSwitch(2);
        var lines = r.Lines;
        Assert.StartsWith("VERSION BUILD=", lines[0]);
        Assert.Equal("TAB T=1", lines[2]);
        Assert.Equal("URL GOTO=https://a.test/", lines[3]);
        Assert.Equal("TAG POS=1 TYPE=INPUT:TEXT ATTR=NAME:q CONTENT=hello<ENTER>", lines[4]);
        Assert.StartsWith("' Note", lines[5]);
        Assert.Equal(new[] { "BACK", "TAB OPEN", "TAB T=2" }, lines.Skip(7));
        Assert.Equal(10, lines.Count);

        // Everything the recorder produces must parse.
        var parsed = MacroParser.Parse(r.GetMacroText());
        Assert.Equal(8, parsed.Commands.Count);
    }

    [Fact]
    public void ParsesRecorderJson()
    {
        var a = RecordedAction.Parse("{\"action\":\"click\",\"type\":\"A\",\"candidates\":[{\"key\":\"TXT\",\"value\":\"Go\",\"pos\":2}]}")!;
        Assert.Equal("TAG POS=2 TYPE=A ATTR=TXT:Go", MacroRecorder.ToLine(a));
        Assert.Null(RecordedAction.Parse("{oops"));
    }

    /// <summary>The recorder script and the playback script must agree on POS: record in Jint, then play back.</summary>
    [Fact]
    public async Task RecordedLinesPlayBackOnTheSameDom()
    {
        const string page = """
setPage(h('html', null, h('body', null,
  h('a', { 'class': 'x' }, 'Same'), h('a', { 'class': 'x' }, 'Same'), h('a', { 'class': 'x', id: 'third' }, 'Same'),
  h('input', { type: 'text', name: 'q' }), h('input', { type: 'text', name: 'q' }),
  h('button', null, 'Click me'))));
""";
        var dom = new JsDom(page);
        var posted = new List<string>();
        dom.Engine.SetValue("__post", new Action<string>(posted.Add));
        dom.Execute("window.chrome = { webview: { postMessage: function (m) { __post(m); } } }; window.top = window;");
        // Our test DOM has no document-level listeners: install them on the root element instead.
        dom.Execute("document.addEventListener = function (t, f, c) { document.documentElement.addEventListener(t, f, c); }; document.removeEventListener = function () {};");
        dom.Execute(RecorderScript.Build());
        dom.Execute("var links = document.getElementsByTagName('a'); links[1].click();");
        dom.Execute("var inputs = document.getElementsByTagName('input'); inputs[1].value = 'typed'; inputs[1].dispatchEvent(new Event('change', { bubbles: true }));");
        dom.Execute("document.getElementsByTagName('button')[0].click();");

        var rec = new MacroRecorder();
        foreach (var json in posted) rec.Add(RecordedAction.Parse(json)!);
        Assert.Equal(new[]
        {
            "TAG POS=2 TYPE=A ATTR=TXT:Same",
            "TAG POS=2 TYPE=INPUT:TEXT ATTR=NAME:q CONTENT=typed",
            "TAG POS=1 TYPE=BUTTON:SUBMIT ATTR=TXT:Click<SP>me",
        }, rec.Lines);

        var driver = new FakeBrowserDriver(page);
        var result = await new MacroInterpreter(driver, clock: new FakeClock()).PlayAsync(string.Join("\n", rec.Lines));
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("typed", driver.Dom.EvalString("document.getElementsByTagName('input')[1].value"));
        Assert.Contains("click", driver.Dom.EvalString("document.getElementsByTagName('a')[1].events.join(',')"));
        Assert.DoesNotContain("click", driver.Dom.EvalString("document.getElementsByTagName('a')[0].events.join(',')"));
    }

    [Fact]
    public void RecorderScriptSkipsIframesAndIsIdempotent()
    {
        var s = RecorderScript.Build();
        Assert.Contains("window.top !== window", s);
        Assert.Contains("__wmRecorderInstalled", s);
        Assert.Contains("var __wm = (function", s);
    }
}
