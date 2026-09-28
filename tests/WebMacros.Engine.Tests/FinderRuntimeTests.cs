using WebMacros.Engine.Scripting;
using WebMacros.Engine.Tests.Support;

namespace WebMacros.Engine.Tests;

/// <summary>Runs the generated JavaScript against a small DOM in Jint to verify matching semantics.</summary>
public class FinderRuntimeTests
{
    private const string Page = """
setPage(h('html', null,
  h('head', null, h('title', null, 'Test page')),
  h('body', null,
    h('form', { name: 'search', id: 'f1' },
      h('input', { type: 'text', name: 'q', id: 'q', 'class': 'box big' }),
      h('input', { name: 'untyped' }),
      h('input', { type: 'checkbox', name: 'agree', id: 'agree' }),
      h('input', { type: 'radio', name: 'size', value: 'small', id: 'rs' }),
      h('input', { type: 'radio', name: 'size', value: 'large', id: 'rl' }),
      h('select', { name: 'color', id: 'color' },
        h('option', { value: 'r' }, 'Red'), h('option', { value: 'g' }, 'Green'), h('option', { value: 'b' }, 'Deep  Blue')),
      h('textarea', { name: 'notes', id: 'notes' }),
      h('input', { type: 'submit', value: 'Go now', id: 'go' })),
    h('form', { name: 'other', id: 'f2' }, h('input', { type: 'text', name: 'q', id: 'q2' })),
    h('div', { 'class': 'results' },
      h('a', { href: '/one', 'class': 'result', title: 'First' }, 'Result one'),
      h('span', { id: 'mid' }, 'Price: 10'),
      h('a', { href: 'https://other.test/two', 'class': 'result' }, '  Result\n two '),
      h('a', { href: '/three', 'class': 'result featured' }, 'Result three'),
      h('img', { src: '/logo.png', alt: 'Logo' })),
    h('p', { id: 'pt', 'data-pt': '100,200' }, 'Clickable'),
    h('div', { id: 'ce', contenteditable: 'true' }, 'edit me')
  )));
""";

    private static (JsDom Dom, ElementResult Result) Run(ElementSpec spec, string page = Page)
    {
        var dom = new JsDom(page);
        var json = dom.EvalString(ScriptBuilder.BuildElementScript(spec));
        var res = System.Text.Json.JsonSerializer.Deserialize<ElementResult>(json, ScriptBuilder.JsonOptions)!;
        return (dom, res);
    }

    private static ElementResult Tag(string? pos, string? type, string? attr, string? content = null, string? extract = null, string? form = null,
        string? xpath = null, string? selector = null) =>
        Run(TagSpecBuilder.BuildTag(pos, type, attr, form, xpath, selector, content, extract, null)).Result;

    private static string Extract(string? pos, string? type, string? attr, string what) =>
        Assert.IsType<string>(Tag(pos, type, attr, extract: what).Value);

    [Fact]
    public void MatchesByNameAndType()
    {
        var r = Tag("1", "INPUT:TEXT", "NAME:q", extract: "ID");
        Assert.True(r.Ok);
        Assert.Equal("q", r.Value);
        Assert.Equal("INPUT:TEXT", r.Tag);
    }

    [Fact] public void InputWithoutTypeIsText() => Assert.Equal("untyped", Extract("2", "INPUT:TEXT", "*", "NAME"));
    [Fact] public void PosSelectsNthMatch() => Assert.Equal("q2", Extract("2", "INPUT:TEXT", "NAME:q", "ID"));
    [Fact] public void TxtIsWhitespaceNormalized() => Assert.Equal("https://other.test/two", Extract("1", "A", "TXT:Result two", "HREF"));
    [Fact] public void WildcardMatches() => Assert.Equal("Result three", Extract("3", "A", "TXT:Result*", "TXT"));
    [Fact] public void WildcardIsAnchored() => Assert.False(Tag("1", "A", "TXT:esult*", extract: "TXT").Ok);
    [Fact] public void ClassMatchesSingleToken() => Assert.Equal("Result three", Extract("1", "A", "CLASS:featured", "TXT"));
    [Fact] public void ClassMatchesFullString() => Assert.Equal("Result three", Extract("1", "A", "CLASS:result<SP>featured".Replace("<SP>", " "), "TXT"));
    [Fact] public void HrefMatchesRelativeOrAbsolute()
    {
        Assert.Equal("Result one", Extract("1", "A", "HREF:/one", "TXT"));
        Assert.Equal("Result one", Extract("1", "A", "HREF:https://example.test/one", "TXT"));
    }
    [Fact] public void AndCombos() => Assert.Equal("rl", Extract("1", "INPUT:RADIO", "NAME:size&&VALUE:large", "ID"));
    [Fact] public void AnyAttributeKey() => Assert.Equal("Result one", Extract("1", "A", "TITLE:First", "TXT"));
    [Fact] public void AttrStarRequiresPresence() => Assert.Equal("Result one", Extract("1", "A", "TITLE:*", "TXT"));
    [Fact] public void TypeStarMatchesAnyTag() => Assert.Equal("Price: 10", Extract("1", "*", "ID:mid", "TXT"));
    [Fact] public void SubmitButtonTxtIsValue() => Assert.Equal("go", Extract("1", "INPUT:SUBMIT", "TXT:Go<SP>now".Replace("<SP>", " "), "ID"));
    [Fact] public void FormRestriction() => Assert.Equal("q2", Tag("1", "INPUT:TEXT", "NAME:q", extract: "ID", form: "NAME:other").Value);

    [Fact]
    public void ExtractKinds()
    {
        Assert.Equal("First", Extract("1", "A", "*", "TITLE"));
        Assert.Equal("Logo", Extract("1", "IMG", "*", "ALT"));
        Assert.Equal("Test page", Extract("1", "TITLE", "*", "TXT"));
        Assert.Equal("<a href=\"/one\" class=\"result\" title=\"First\">Result one</a>", Extract("1", "A", "*", "HTM"));
        Assert.Equal("#EANF#", Extract("1", "A", "*", "ALT"));
        Assert.Equal("Red", Extract("1", "SELECT", "*", "TXT"));
        Assert.Equal("Red[OPTION]Green[OPTION]Deep Blue", Extract("1", "SELECT", "*", "TXTALL"));
    }

    [Fact]
    public void NotFoundIsRetryable()
    {
        var r = Tag("1", "A", "TXT:Nope");
        Assert.False(r.Ok);
        Assert.True(r.Retry);
        Assert.Contains("not found", r.Error);
    }

    [Fact]
    public void TooFewMatchesReportsCount()
    {
        var r = Tag("9", "A", "CLASS:result");
        Assert.Contains("only 3", r.Error);
    }

    [Fact]
    public void ClickDispatchesMouseEventsAndClick()
    {
        var (dom, r) = Run(TagSpecBuilder.BuildTag("1", "A", "TXT:Result one", null, null, null, null, null, null));
        Assert.True(r.Ok);
        Assert.Equal("https://example.test/one", dom.EvalString("window.__clickedHref"));
        Assert.Equal("mouseover:A,mousedown:A,mouseup:A,click:A", dom.EvalString("__log.join(',')"));
    }

    [Fact]
    public void ContentFillsTextWithEvents()
    {
        var (dom, r) = Run(TagSpecBuilder.BuildTag("1", "INPUT:TEXT", "ID:q", null, null, null, "hello world", null, null));
        Assert.True(r.Ok);
        Assert.Equal("hello world", dom.EvalString("byId('q').value"));
        Assert.Equal("focus,value=hello world,input,change", dom.EvalString("byId('q').events.join(',')"));
    }

    [Fact]
    public void ContentWithEnterSubmitsForm()
    {
        var (dom, _) = Run(TagSpecBuilder.BuildTag("1", "INPUT:TEXT", "ID:q", null, null, null, "abc<ENTER>", null, null));
        Assert.Equal("abc", dom.EvalString("byId('q').value"));
        Assert.Equal("true", dom.EvalString("String(byId('f1').submitted)"));
        Assert.Contains("keydown", dom.EvalString("byId('q').events.join(',')"));
    }

    [Theory]
    [InlineData("%g", "g")]
    [InlineData("$Green", "g")]
    [InlineData("$Deep*", "b")]
    [InlineData("#3", "b")]
    [InlineData("Green", "g")]
    [InlineData("r", "r")]
    public void SelectContent(string content, string expectedValue)
    {
        var (dom, r) = Run(TagSpecBuilder.BuildTag("1", "SELECT", "NAME:color", null, null, null, content, null, null));
        Assert.True(r.Ok, r.Error);
        Assert.Equal(expectedValue, dom.EvalString("byId('color').value"));
    }

    [Fact]
    public void SelectMissingOptionIsFatal()
    {
        var r = Tag("1", "SELECT", "NAME:color", content: "%zzz");
        Assert.False(r.Ok);
        Assert.False(r.Retry);
        Assert.Contains("not found in SELECT", r.Error);
    }

    [Fact]
    public void CheckboxYesNo()
    {
        var dom = new JsDom(Page);
        string Run(string content) =>
            dom.EvalString(ScriptBuilder.BuildElementScript(TagSpecBuilder.BuildTag("1", "INPUT:CHECKBOX", "ID:agree", null, null, null, content, null, null)));
        Run("YES");
        Assert.Equal("true", dom.EvalString("String(byId('agree').checked)"));
        Run("YES"); // unchanged, no toggle
        Assert.Equal("true", dom.EvalString("String(byId('agree').checked)"));
        Run("NO");
        Assert.Equal("false", dom.EvalString("String(byId('agree').checked)"));
    }

    [Fact]
    public void TextareaAndContentEditable()
    {
        var (dom, _) = Run(TagSpecBuilder.BuildTag("1", "TEXTAREA", "NAME:notes", null, null, null, "line1\nline2", null, null));
        Assert.Equal("line1\nline2", dom.EvalString("byId('notes').value"));
        var (dom2, r2) = Run(TagSpecBuilder.BuildTag("1", "DIV", "ID:ce", null, null, null, "new text", null, null));
        Assert.True(r2.Ok, r2.Error);
        Assert.Equal("new text", dom2.EvalString("byId('ce').textContent"));
    }

    [Fact]
    public void ContentOnUnsupportedElementIsFatal()
    {
        var r = Tag("1", "SPAN", "ID:mid", content: "x");
        Assert.False(r.Ok);
        Assert.False(r.Retry);
    }

    [Fact]
    public void RelativePositionUsesAnchor()
    {
        var dom = new JsDom(Page);
        string Exec(ElementSpec s) => dom.EvalString(ScriptBuilder.BuildElementScript(s));
        Exec(TagSpecBuilder.BuildTag("1", "SPAN", "ID:mid", null, null, null, null, "TXT", null));
        var next = Exec(TagSpecBuilder.BuildTag("R1", "A", "*", null, null, null, null, "TXT", null));
        Assert.Contains("Result two", next);
        // The found element becomes the new anchor.
        var next2 = Exec(TagSpecBuilder.BuildTag("R1", "A", "*", null, null, null, null, "TXT", null));
        Assert.Contains("Result three", next2);
        var prev = Exec(TagSpecBuilder.BuildTag("R-2", "A", "*", null, null, null, null, "TXT", null));
        Assert.Contains("Result one", prev);
    }

    [Fact]
    public void RelativeWithoutAnchorIsFatal()
    {
        var r = Tag("R1", "A", "*", extract: "TXT");
        Assert.False(r.Ok);
        Assert.False(r.Retry);
        Assert.Contains("anchor", r.Error);
    }

    [Fact]
    public void XPathAndSelector()
    {
        Assert.Equal("Price: 10", Tag(null, null, null, extract: "TXT", xpath: "//span[@id='mid']").Value);
        Assert.Equal("Result three", Tag("3", null, null, extract: "TXT", selector: "a.result").Value);
        var bad = Tag(null, null, null, extract: "TXT", selector: "a!!b");
        Assert.False(bad.Retry);
        Assert.Contains("Invalid CSS selector", bad.Error);
    }

    [Fact]
    public void FramesByIndexAndName()
    {
        const string framed = Page + "addFrame('inner', h('html', null, h('body', null, h('b', { id: 'fb' }, 'in frame'))));";
        var byIndex = Run(TagSpecBuilder.BuildTag("1", "B", "*", null, null, null, null, "TXT", FrameSpec.ByIndex(1)), framed).Result;
        Assert.Equal("in frame", byIndex.Value);
        var byName = Run(TagSpecBuilder.BuildTag("1", "B", "*", null, null, null, null, "TXT", FrameSpec.ByName("inn*")), framed).Result;
        Assert.Equal("in frame", byName.Value);
        var missing = Run(TagSpecBuilder.BuildFrameCheck(FrameSpec.ByIndex(3)), framed).Result;
        Assert.False(missing.Ok);
        Assert.True(missing.Retry);
        Assert.True(Run(TagSpecBuilder.BuildFrameCheck(FrameSpec.ByIndex(1)), framed).Result.Ok);
    }

    [Fact]
    public void EventKeypressCharsTypesIntoInput()
    {
        var (dom, r) = Run(TagSpecBuilder.BuildEvent("KEYPRESS", "#q", null, null, "ab", null));
        Assert.True(r.Ok, r.Error);
        Assert.Equal("ab", dom.EvalString("byId('q').value"));
        Assert.Contains("keydown,keypress,value=a,input,keyup", dom.EvalString("byId('q').events.join(',')"));
    }

    [Fact]
    public void EventKey13SubmitsForm()
    {
        var (dom, _) = Run(TagSpecBuilder.BuildEvent("KEYPRESS", null, "//input[@id='q']", "13", null, null));
        Assert.Equal("true", dom.EvalString("String(byId('f1').submitted)"));
    }

    [Fact]
    public void EventMouseoverAndClick()
    {
        var (dom, _) = Run(TagSpecBuilder.BuildEvent("MOUSEOVER", "#mid", null, null, null, null));
        Assert.Equal("mouseover,mouseenter", dom.EvalString("byId('mid').events.join(',')"));
        var (dom2, _) = Run(TagSpecBuilder.BuildEvent("CLICK", "#mid", null, null, null, null));
        Assert.Contains("click", dom2.EvalString("byId('mid').events.join(',')"));
    }

    [Fact]
    public void EventTargetNotFoundRetries()
    {
        var r = Run(TagSpecBuilder.BuildEvent("CLICK", "#nope", null, null, null, null)).Result;
        Assert.False(r.Ok);
        Assert.True(r.Retry);
    }

    [Fact]
    public void ClickAtPoint()
    {
        var (dom, r) = Run(TagSpecBuilder.BuildPoint(100, 200, null, null));
        Assert.True(r.Ok);
        Assert.Contains("click", dom.EvalString("byId('pt').events.join(',')"));
        Assert.False(Run(TagSpecBuilder.BuildPoint(1, 1, null, null)).Result.Ok);
    }

    [Fact]
    public void EvalScriptReturnsValueAndErrors()
    {
        var dom = new JsDom(Page);
        EvalResult Eval(string code) => System.Text.Json.JsonSerializer.Deserialize<EvalResult>(dom.EvalString(ScriptBuilder.BuildEvalScript(code)), ScriptBuilder.JsonOptions)!;
        Assert.Equal("6", Eval("1+2+3").Value);
        Assert.Equal("Test page", Eval("document.title").Value);
        Assert.Equal("", Eval("undefined").Value);
        var err = Eval("throw new Error('boom')");
        Assert.False(err.Ok);
        Assert.Equal("boom", err.Error);
        Assert.False(err.MacroError);
        var me = Eval("MacroError('stop here')");
        Assert.True(me.MacroError);
        Assert.Equal("stop here", me.Error);
        Assert.False(Eval("this is not js").Ok);
    }

    [Fact]
    public void RuntimeHelpersForRecorder()
    {
        var dom = new JsDom(Page);
        dom.Execute(FinderRuntime.Source);
        Assert.Equal("INPUT:TEXT", dom.EvalString("__wm.typeString(byId('q'))"));
        Assert.Equal("BUTTON:SUBMIT", dom.EvalString("__wm.typeString(h('button'))"));
        Assert.Equal("true", dom.EvalString("String(__wm.matchStr('a*c', 'abbbc'))"));
        Assert.Equal("false", dom.EvalString("String(__wm.matchStr('a.c', 'abc'))"));
        Assert.Equal("true", dom.EvalString("String(__wm.matchStr('(x)', '(x)'))"));
    }
}
