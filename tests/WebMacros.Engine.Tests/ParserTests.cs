using WebMacros.Engine.Syntax;

namespace WebMacros.Engine.Tests;

public class ParserTests
{
    private static T Parse<T>(string line) where T : MacroCommand
    {
        var cmd = MacroParser.ParseLine(line, 7);
        var t = Assert.IsType<T>(cmd);
        Assert.Equal(7, t.LineNumber);
        Assert.Equal(line.Trim(), t.Text);
        return t;
    }

    [Fact] public void Version() => Assert.Equal("7500718", Parse<VersionCommand>("VERSION BUILD=7500718").Build);

    [Theory]
    [InlineData("TAB OPEN", TabAction.Open, null)]
    [InlineData("TAB CLOSE", TabAction.Close, null)]
    [InlineData("TAB CLOSEALLOTHERS", TabAction.CloseAllOthers, null)]
    [InlineData("TAB T=2", TabAction.Switch, "2")]
    [InlineData("TAB T={{n}}", TabAction.Switch, "{{n}}")]
    public void Tab(string line, TabAction action, string? index)
    {
        var t = Parse<TabCommand>(line);
        Assert.Equal(action, t.Action);
        Assert.Equal(index, t.Index);
    }

    [Fact] public void Url() => Assert.Equal("https://example.com/?q=1", Parse<UrlCommand>("URL GOTO=https://example.com/?q=1").Goto);
    [Fact] public void Back() => Parse<BackCommand>("BACK");
    [Fact] public void Refresh() => Parse<RefreshCommand>("REFRESH");
    [Fact] public void Wait() => Assert.Equal("1.5", Parse<WaitCommand>("WAIT SECONDS=1.5").Seconds);

    [Fact]
    public void SetPlainAndQuoted()
    {
        var s = Parse<SetCommand>("SET !VAR1 hello");
        Assert.Equal(("!VAR1", "hello", false), (s.Variable, s.Value, s.IsEval));
        Assert.Equal("a b", Parse<SetCommand>("SET name \"a b\"").Value);
        Assert.Equal("a=b", Parse<SetCommand>("SET x a=b").Value);
    }

    [Fact]
    public void SetEval()
    {
        var s = Parse<SetCommand>("SET !VAR2 EVAL(\"var s = \\\"{{!VAR1}}\\\"; s.length;\")");
        Assert.True(s.IsEval);
        Assert.Equal("var s = \"{{!VAR1}}\"; s.length;", s.EvalScript);
    }

    [Fact]
    public void QuotedEvalTextIsNotEval() => Assert.False(Parse<SetCommand>("SET x \"EVAL(1)\"").IsEval);

    [Fact]
    public void SetBuiltIns()
    {
        Assert.Equal("NULL", Parse<SetCommand>("SET !EXTRACT NULL").Value);
        Parse<SetCommand>("SET !TIMEOUT_STEP 3");
        Parse<SetCommand>("SET !TIMEOUT_PAGE 30");
        Parse<SetCommand>("SET !ERRORIGNORE YES");
        Parse<SetCommand>("SET !LOOP 2");
        Parse<SetCommand>("SET !DATASOURCE data.csv");
        Parse<SetCommand>("SET !DATASOURCE_LINE {{!LOOP}}");
        Parse<SetCommand>("SET !DATASOURCE_COLUMNS 3");
        Parse<SetCommand>("SET !EXTRACT_TEST_POPUP NO");
    }

    [Fact]
    public void Add()
    {
        var a = Parse<AddCommand>("ADD !EXTRACT {{!VAR1}}");
        Assert.Equal(("!EXTRACT", "{{!VAR1}}"), (a.Variable, a.Value));
    }

    [Fact]
    public void TagFull()
    {
        var t = Parse<TagCommand>("TAG POS=R2 TYPE=INPUT:TEXT FORM=NAME:f ATTR=NAME:q&&CLASS:big CONTENT=\"hi there\"");
        Assert.Equal("R2", t.Pos);
        Assert.Equal("INPUT:TEXT", t.Type);
        Assert.Equal("NAME:q&&CLASS:big", t.Attr);
        Assert.Equal("NAME:f", t.Form);
        Assert.Equal("hi there", t.Content);
        Assert.Null(t.Extract);
    }

    [Fact]
    public void TagExtractXpathSelector()
    {
        Assert.Equal("HREF", Parse<TagCommand>("TAG POS=1 TYPE=A ATTR=TXT:* EXTRACT=HREF").Extract);
        Assert.Equal("//div[@id='x']", Parse<TagCommand>("TAG XPATH=\"//div[@id='x']\" EXTRACT=TXT").XPath);
        Assert.Equal("div.item > a", Parse<TagCommand>("TAG SELECTOR=\"div.item > a\"").Selector);
    }

    [Theory]
    [InlineData("TAG POS=1 ATTR=NAME:q", "requires TYPE")]
    [InlineData("TAG TYPE=A XPATH=//a", "cannot be combined")]
    [InlineData("TAG XPATH=//a SELECTOR=a", "either XPATH")]
    [InlineData("TAG TYPE=A CONTENT=x EXTRACT=TXT", "mutually exclusive")]
    [InlineData("TAG POS=abc TYPE=A", "invalid POS")]
    [InlineData("TAG POS=0 TYPE=A", "invalid POS")]
    [InlineData("TAG TYPE=A FOO=1", "unknown parameter FOO")]
    [InlineData("TAG TYPE=A TYPE=B", "duplicate")]
    public void TagErrors(string line, string message)
    {
        var ex = Assert.Throws<MacroSyntaxException>(() => MacroParser.ParseLine(line, 3));
        Assert.Contains(message, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, ex.LineNumber);
    }

    [Fact]
    public void Event()
    {
        var e = Parse<EventCommand>("EVENT TYPE=KEYPRESS SELECTOR=\"#q\" CHARS=\"abc\"");
        Assert.Equal(("KEYPRESS", "#q", "abc"), (e.Type, e.Selector, e.Chars));
        Assert.Equal("13", Parse<EventCommand>("EVENT TYPE=KEYPRESS XPATH=//input KEY=13").Key);
        Assert.Equal("MOUSEOVER", Parse<EventCommand>("EVENTS TYPE=mouseover SELECTOR=a").Type);
        Assert.Throws<MacroSyntaxException>(() => MacroParser.ParseLine("EVENT TYPE=EXPLODE SELECTOR=a"));
        Assert.Throws<MacroSyntaxException>(() => MacroParser.ParseLine("EVENT TYPE=KEYPRESS KEY=x"));
    }

    [Fact]
    public void Click()
    {
        var c = Parse<ClickCommand>("CLICK X=10 Y=20");
        Assert.Equal(("10", "20"), (c.X, c.Y));
        Assert.Throws<MacroSyntaxException>(() => MacroParser.ParseLine("CLICK X=10"));
    }

    [Fact]
    public void Frame()
    {
        Assert.Equal("2", Parse<FrameCommand>("FRAME F=2").Index);
        Assert.Equal("main", Parse<FrameCommand>("FRAME NAME=main").FrameName);
        Assert.Throws<MacroSyntaxException>(() => MacroParser.ParseLine("FRAME"));
        Assert.Throws<MacroSyntaxException>(() => MacroParser.ParseLine("FRAME F=1 NAME=x"));
    }

    [Fact]
    public void Prompt()
    {
        var p = Parse<PromptCommand>("PROMPT \"Your name?\" !VAR1 Bob");
        Assert.Equal(("Your name?", "!VAR1", "Bob"), (p.Message, p.Variable, p.Default));
        var m = Parse<PromptCommand>("PROMPT Hello<SP>there");
        Assert.Null(m.Variable);
    }

    [Fact] public void Pause() => Parse<PauseCommand>("PAUSE");

    [Theory]
    [InlineData("SAVEAS TYPE=EXTRACT FOLDER=* FILE=out.csv", "EXTRACT", "*", "out.csv")]
    [InlineData("SAVEAS TYPE=htm FOLDER=c:\\temp FILE=*", "HTM", "c:\\temp", "*")]
    [InlineData("SAVEAS TYPE=CPL", "CPL", null, null)]
    [InlineData("SAVEAS TYPE=PNG FILE=+_x", "PNG", null, "+_x")]
    public void SaveAs(string line, string type, string? folder, string? file)
    {
        var s = Parse<SaveAsCommand>(line);
        Assert.Equal((type, folder, file), (s.Type, s.Folder, s.File));
    }

    [Fact]
    public void Screenshot()
    {
        var s = Parse<ScreenshotCommand>("SCREENSHOT TYPE=PAGE FOLDER=* FILE=shot.png");
        Assert.Equal(("PAGE", "*", "shot.png"), (s.Type, s.Folder, s.File));
        Assert.Throws<MacroSyntaxException>(() => MacroParser.ParseLine("SCREENSHOT TYPE=VIDEO"));
    }

    [Fact]
    public void OnDialog()
    {
        var d = Parse<OnDialogCommand>("ONDIALOG POS=1 BUTTON=OK CONTENT=hi");
        Assert.Equal(("1", "OK", "hi"), (d.Pos, d.Button, d.Content));
        Assert.Equal("1", Parse<OnDialogCommand>("ONDIALOG BUTTON=CANCEL").Pos);
        Assert.Throws<MacroSyntaxException>(() => MacroParser.ParseLine("ONDIALOG POS=1 BUTTON=MAYBE"));
    }

    [Fact]
    public void Clear()
    {
        Assert.Null(Parse<ClearCommand>("CLEAR").What);
        Assert.Equal("COOKIES", Parse<ClearCommand>("CLEAR COOKIES").What);
    }

    [Fact]
    public void Filter()
    {
        var f = Parse<FilterCommand>("FILTER TYPE=IMAGES STATUS=ON");
        Assert.Equal(("IMAGES", "ON"), (f.Type, f.Status));
        Assert.Throws<MacroSyntaxException>(() => MacroParser.ParseLine("FILTER TYPE=IMAGES STATUS=MAYBE"));
    }

    [Fact]
    public void Stopwatch()
    {
        Assert.Equal(StopwatchAction.Toggle, Parse<StopwatchCommand>("STOPWATCH ID=a").Action);
        Assert.Equal(StopwatchAction.Start, Parse<StopwatchCommand>("STOPWATCH START ID=a").Action);
        Assert.Equal(StopwatchAction.Stop, Parse<StopwatchCommand>("STOPWATCH STOP ID=a").Action);
        Assert.Equal(StopwatchAction.Label, Parse<StopwatchCommand>("STOPWATCH LABEL=x").Action);
        Assert.Throws<MacroSyntaxException>(() => MacroParser.ParseLine("STOPWATCH"));
    }

    [Fact]
    public void SearchAndFileDelete()
    {
        Assert.Equal("REGEXP:id=(\\d+)", Parse<SearchCommand>("SEARCH SOURCE=REGEXP:id=(\\d+) EXTRACT=$1").Source);
        Assert.Equal("a.csv", Parse<FileDeleteCommand>("FILEDELETE NAME=a.csv").FileName);
        Assert.Throws<MacroSyntaxException>(() => MacroParser.ParseLine("SEARCH SOURCE=foo"));
    }

    [Theory]
    [InlineData("SIZE X=800 Y=600")]
    [InlineData("ONDOWNLOAD FOLDER=* FILE=*")]
    [InlineData("ONLOGIN USER=a PASSWORD=b")]
    [InlineData("WINCLICK X=1 Y=2")]
    [InlineData("DS CMD=CLICK X=1 Y=2")]
    public void UnsupportedCommandsParse(string line)
    {
        var u = Assert.IsType<UnsupportedCommand>(MacroParser.ParseLine(line));
        Assert.Equal(line.Split(' ')[0], u.CommandName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("' a comment")]
    [InlineData("   ' indented comment")]
    [InlineData("// also a comment")]
    public void CommentsAndBlankLinesAreSkipped(string line) => Assert.Null(MacroParser.ParseLine(line));

    [Theory]
    [InlineData("FOO BAR", "Unknown command")]
    [InlineData("URL", "requires GOTO")]
    [InlineData("WAIT SECONDS=abc", "must be a number")]
    [InlineData("SET !VAR1", "exactly two")]
    [InlineData("SET !VAR1 a b", "exactly two")]
    [InlineData("SET 1abc x", "invalid variable name")]
    [InlineData("BACK now", "takes no parameters")]
    [InlineData("TAB", "TAB requires")]
    [InlineData("SAVEAS TYPE=DOCX", "must be one of")]
    public void Errors(string line, string message)
    {
        var ex = Assert.Throws<MacroSyntaxException>(() => MacroParser.ParseLine(line, 12));
        Assert.Contains(message, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("Line 12:", ex.Message);
    }

    [Fact]
    public void WholeMacroKeepsLineNumbers()
    {
        var m = MacroParser.Parse("VERSION BUILD=1\r\n' comment\r\n\r\nURL GOTO=https://a.test\nWAIT SECONDS=1");
        Assert.Equal(3, m.Commands.Count);
        Assert.Equal(new[] { 1, 4, 5 }, m.Commands.Select(c => c.LineNumber));
        Assert.Equal(5, m.SourceLines.Count);
    }

    [Fact]
    public void ValidateCollectsAllErrors()
    {
        var errors = MacroParser.Validate("FOO\nURL GOTO=x\nBAR\nWAIT SECONDS=zz");
        Assert.Equal(new[] { 1, 3, 4 }, errors.Select(e => e.LineNumber));
    }

    [Fact]
    public void VariablesSkipLiteralValidation()
    {
        Parse<WaitCommand>("WAIT SECONDS={{delay}}");
        Parse<TagCommand>("TAG POS={{n}} TYPE=A ATTR=*");
    }
}
