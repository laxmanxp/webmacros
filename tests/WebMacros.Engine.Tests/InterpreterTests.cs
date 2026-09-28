using WebMacros.Engine.Runtime;
using WebMacros.Engine.Tests.Support;

namespace WebMacros.Engine.Tests;

public sealed class InterpreterTests : IDisposable
{
    private const string FormPage = """
setPage(h('html', null, h('head', null, h('title', null, 'Form')), h('body', null,
  h('form', { id: 'f' },
    h('input', { type: 'text', name: 'user', id: 'user' }),
    h('input', { type: 'text', name: 'city', id: 'city' }),
    h('select', { name: 'lang', id: 'lang' }, h('option', { value: 'en' }, 'English'), h('option', { value: 'de' }, 'German')),
    h('input', { type: 'checkbox', name: 'news', id: 'news' }),
    h('button', { id: 'send' }, 'Send')),
  h('ul', null, h('li', { 'class': 'item' }, 'Apple'), h('li', { 'class': 'item' }, 'Banana "B"'), h('li', { 'class': 'item' }, 'Cherry')),
  h('a', { href: '/next', id: 'next' }, 'Next page'))));
""";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wm-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeBrowserDriver _driver = new();
    private readonly RecordingHost _host = new();
    private readonly FakeClock _clock = new();

    public InterpreterTests()
    {
        Directory.CreateDirectory(_dir);
        _driver.Pages["https://form.test/"] = FormPage;
        _driver.Pages["https://empty.test/"] = "setPage(h('html', null, h('body')));";
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private MacroInterpreter NewInterpreter() => new(_driver, _host, new InterpreterOptions
    {
        MacroFolder = _dir, DataSourceFolder = _dir, DownloadFolder = Path.Combine(_dir, "Downloads"),
    }, _clock);

    private async Task<(PlayResult Result, MacroInterpreter Interp)> Play(string macro, int loops = 1)
    {
        var interp = NewInterpreter();
        var result = await interp.PlayAsync(macro, loops);
        return (result, interp);
    }

    private static string M(params string[] lines) => string.Join("\n", lines);

    // ---- variables ---------------------------------------------------------------------------------------

    [Fact]
    public async Task SetAddAndSubstitution()
    {
        var (r, i) = await Play(M(
            "SET !VAR1 5",
            "ADD !VAR1 10",
            "SET name Bob",
            "ADD name <SP>Smith",
            "SET greeting \"Hi {{name}}, total={{!VAR1}}\"",
            "SET !VAR2 a<TAB>b<BR>c"));
        Assert.True(r.Success, r.ErrorMessage);
        Assert.Equal("15", i.State.Get("!VAR1"));
        Assert.Equal("Bob Smith", i.State.Get("name"));
        Assert.Equal("Hi Bob Smith, total=15", i.State.Get("greeting"));
        Assert.Equal("a\tb\nc", i.State.Get("!VAR2"));
    }

    [Fact]
    public async Task AddToUndefinedUserVariableStartsFromZero()
    {
        var (r, i) = await Play("ADD counter 2\nADD counter 3.5");
        Assert.True(r.Success);
        Assert.Equal("5.5", i.State.Get("counter"));
    }

    [Fact]
    public async Task UndefinedVariableFailsWithLineNumber()
    {
        var (r, _) = await Play("SET !VAR1 ok\nURL GOTO={{missing}}");
        Assert.Equal(PlayStatus.Failed, r.Status);
        Assert.Equal(2, r.ErrorLine);
        Assert.Contains("{{missing}} is not defined", r.ErrorMessage);
    }

    [Fact]
    public async Task UnknownBuiltInFails()
    {
        var (r, _) = await Play("SET !BOGUS 1");
        Assert.Contains("Unknown built-in", r.ErrorMessage);
    }

    [Fact]
    public async Task IgnoredBuiltInWarns()
    {
        var (r, _) = await Play("SET !REPLAYSPEED FAST");
        Assert.True(r.Success);
        Assert.Contains(_host.Warnings, w => w.Contains("no effect"));
    }

    [Fact]
    public async Task NowFormat()
    {
        var (r, i) = await Play("SET t {{!NOW:yyyymmdd_hhnnss}}\nSET d {{!NOW:dd.mm.yy<SP>dow}}");
        Assert.True(r.Success);
        Assert.Equal("20260928_164507", i.State.Get("t"));
        Assert.Equal("28.09.26 2", i.State.Get("d")); // Monday = 2
    }

    [Fact]
    public async Task ParseErrorReportedBeforeRunning()
    {
        var (r, _) = await Play("URL GOTO=https://form.test/\nFOO");
        Assert.Equal(PlayStatus.Failed, r.Status);
        Assert.Equal(2, r.ErrorLine);
        Assert.Empty(_driver.Calls);
    }

    // ---- loops -------------------------------------------------------------------------------------------

    [Fact]
    public async Task LoopRunsNTimesWithLoopCounter()
    {
        var (r, _) = await Play("ADD !EXTRACT loop{{!LOOP}}", loops: 3);
        Assert.True(r.Success);
        Assert.Equal(3, r.LoopsCompleted);
        Assert.Equal(new[] { "loop1", "loop2", "loop3" }, r.Extracts);
        Assert.Equal(3, _host.ExtractPopups.Count);
        Assert.Equal(new[] { 1, 2, 3 }, _host.Lines.Select(l => l.Loop));
    }

    [Fact]
    public async Task SetLoopChangesStartOnlyInFirstLoop()
    {
        var (r, _) = await Play("SET !LOOP 3\nADD !EXTRACT {{!LOOP}}", loops: 5);
        Assert.True(r.Success);
        Assert.Equal(new[] { "3", "4", "5" }, r.Extracts);
        Assert.Contains(_host.Warnings, w => w.Contains("only applied in the first loop"));
    }

    [Fact]
    public async Task ExtractPopupCanBeDisabled()
    {
        var (r, _) = await Play("SET !EXTRACT_TEST_POPUP NO\nADD !EXTRACT x", loops: 2);
        Assert.True(r.Success);
        Assert.Empty(_host.ExtractPopups);
        Assert.Equal(2, r.Extracts.Count);
    }

    // ---- datasource --------------------------------------------------------------------------------------

    [Fact]
    public async Task DatasourceReadsRowPerLoop()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "people.csv"), "name,city\nAnn,\"Paris, FR\"\nBen,Rome\n");
        var (r, _) = await Play(M(
            "SET !EXTRACT_TEST_POPUP NO",
            "SET !LOOP 2",
            "SET !DATASOURCE people.csv",
            "SET !DATASOURCE_LINE {{!LOOP}}",
            "ADD !EXTRACT {{!COL1}}",
            "ADD !EXTRACT {{!COL2}}",
            "ADD !EXTRACT {{!DATASOURCE_COLUMNS}}"), loops: 3);
        Assert.True(r.Success, r.ErrorMessage);
        Assert.Equal(new[] { "Ann[EXTRACT]Paris, FR[EXTRACT]2", "Ben[EXTRACT]Rome[EXTRACT]2" }, r.Extracts);
    }

    [Fact]
    public async Task DatasourceEndIsAnError()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "one.csv"), "only\n");
        var (r, _) = await Play("SET !DATASOURCE one.csv\nSET !DATASOURCE_LINE {{!LOOP}}\nSET x {{!COL1}}", loops: 2);
        Assert.Equal(PlayStatus.Failed, r.Status);
        Assert.Equal(1, r.LoopsCompleted);
        Assert.Equal(3, r.ErrorLine);
        Assert.Contains("End of datasource", r.ErrorMessage);
    }

    [Fact]
    public async Task DatasourceMissingFileAndColumn()
    {
        var (r1, _) = await Play("SET !DATASOURCE nope.csv");
        Assert.Contains("not found", r1.ErrorMessage);
        await File.WriteAllTextAsync(Path.Combine(_dir, "d.csv"), "a;b\n");
        var (r2, i2) = await Play("SET !DATASOURCE_DELIMITER ;\nSET !DATASOURCE d.csv\nSET x {{!COL2}}\nSET y {{!COL3}}");
        Assert.Equal(4, r2.ErrorLine);
        Assert.Equal("b", i2.State.Get("x"));
        var (r3, _) = await Play("SET x {{!COL1}}");
        Assert.Contains("no datasource", r3.ErrorMessage);
    }

    // ---- TAG / extract -----------------------------------------------------------------------------------

    [Fact]
    public async Task FillFormAndExtract()
    {
        var (r, _) = await Play(M(
            "SET !EXTRACT_TEST_POPUP NO",
            "SET who \"Jane Doe\"",
            "URL GOTO=https://form.test/",
            "TAG POS=1 TYPE=INPUT:TEXT ATTR=NAME:user CONTENT={{who}}",
            "TAG POS=1 TYPE=INPUT:TEXT ATTR=NAME:city CONTENT=New<SP>York",
            "TAG POS=1 TYPE=SELECT ATTR=NAME:lang CONTENT=$German",
            "TAG POS=1 TYPE=INPUT:CHECKBOX ATTR=NAME:news CONTENT=YES",
            "TAG POS=2 TYPE=LI ATTR=CLASS:item EXTRACT=TXT",
            "TAG POS=1 TYPE=A ATTR=TXT:Next* EXTRACT=HREF",
            "TAG POS=1 TYPE=BUTTON ATTR=TXT:Send"));
        Assert.True(r.Success, r.ErrorMessage);
        Assert.Equal("Jane Doe", _driver.Dom.EvalString("byId('user').value"));
        Assert.Equal("New York", _driver.Dom.EvalString("byId('city').value"));
        Assert.Equal("de", _driver.Dom.EvalString("byId('lang').value"));
        Assert.Equal("true", _driver.Dom.EvalString("String(byId('news').checked)"));
        Assert.Equal("true", _driver.Dom.EvalString("String(byId('f').submitted)"));
        Assert.Equal(new[] { "Banana \"B\"[EXTRACT]https://example.test/next" }, r.Extracts);
        Assert.Equal(new[] { "Banana \"B\"", "https://example.test/next" }, _host.Extracts);
    }

    [Fact]
    public async Task ExtractNullClearsAndSetReplaces()
    {
        var (r, i) = await Play(M(
            "SET !EXTRACT_TEST_POPUP NO",
            "ADD !EXTRACT a",
            "ADD !EXTRACT b",
            "SET !VAR1 {{!EXTRACT}}",
            "SET !EXTRACT NULL",
            "ADD !EXTRACT c",
            "SET !VAR2 {{!EXTRACT}}",
            "SET !EXTRACT replaced"));
        Assert.True(r.Success);
        Assert.Equal("a[EXTRACT]b", i.State.Get("!VAR1"));
        Assert.Equal("c", i.State.Get("!VAR2"));
        Assert.Equal(new[] { "replaced" }, r.Extracts);
    }

    [Fact]
    public async Task TagRetriesUntilElementAppears()
    {
        _driver.BeforeScript = (n, dom) =>
        {
            if (n == 3) dom.Execute("document.body.appendChild(h('div', { id: 'late' }, 'Loaded late'));");
        };
        var (r, i) = await Play("URL GOTO=https://empty.test/\nTAG POS=1 TYPE=DIV ATTR=ID:late EXTRACT=TXT");
        Assert.True(r.Success, r.ErrorMessage);
        Assert.Equal("Loaded late", i.State.ExtractValues.Single());
        Assert.Equal(3, _driver.Scripts.Count);
        Assert.Equal(2, _clock.Delays.Count);
    }

    [Fact]
    public async Task TagTimesOutAfterTimeoutStep()
    {
        var (r, _) = await Play("SET !TIMEOUT_STEP 2\nURL GOTO=https://empty.test/\nTAG POS=1 TYPE=DIV ATTR=ID:never");
        Assert.Equal(PlayStatus.Failed, r.Status);
        Assert.Equal(3, r.ErrorLine);
        Assert.Contains("Element not found", r.ErrorMessage);
        Assert.Contains("!TIMEOUT_STEP", r.ErrorMessage);
        Assert.InRange(_clock.TotalDelayed.TotalSeconds, 2, 2.3);
    }

    [Fact]
    public async Task DefaultTimeoutStepIsSixSeconds()
    {
        var (r, _) = await Play("URL GOTO=https://empty.test/\nTAG POS=1 TYPE=DIV ATTR=ID:never");
        Assert.Equal(PlayStatus.Failed, r.Status);
        Assert.InRange(_clock.TotalDelayed.TotalSeconds, 6, 6.3);
    }

    [Fact]
    public async Task FatalTagErrorsDoNotRetry()
    {
        var (r, _) = await Play("URL GOTO=https://form.test/\nTAG POS=1 TYPE=SELECT ATTR=NAME:lang CONTENT=%xx");
        Assert.Equal(PlayStatus.Failed, r.Status);
        Assert.Contains("not found in SELECT", r.ErrorMessage);
        Assert.Empty(_clock.Delays);
    }

    [Fact]
    public async Task ErrorIgnoreContinuesAndExtractsEanf()
    {
        var (r, _) = await Play(M(
            "SET !EXTRACT_TEST_POPUP NO",
            "SET !ERRORIGNORE YES",
            "SET !TIMEOUT_STEP 0",
            "URL GOTO=https://form.test/",
            "TAG POS=1 TYPE=DIV ATTR=ID:none",
            "TAG POS=9 TYPE=LI ATTR=* EXTRACT=TXT",
            "TAG POS=1 TYPE=LI ATTR=* EXTRACT=TXT"));
        Assert.True(r.Success, r.ErrorMessage);
        Assert.Equal(new[] { "#EANF#[EXTRACT]Apple" }, r.Extracts);
        Assert.Equal(2, _host.Warnings.Count(w => w.Contains("Element not found")));
    }

    [Fact]
    public async Task ErrorIgnoreCanBeSwitchedOff()
    {
        var (r, _) = await Play("SET !ERRORIGNORE YES\nSET !TIMEOUT_STEP 0\nTAG POS=1 TYPE=DIV ATTR=ID:none\nSET !ERRORIGNORE NO\nTAG POS=1 TYPE=DIV ATTR=ID:none");
        Assert.Equal(5, r.ErrorLine);
    }

    [Fact]
    public async Task ErrorLoopSkipsToNextLoop()
    {
        var (r, _) = await Play(M(
            "SET !EXTRACT_TEST_POPUP NO",
            "SET !ERRORLOOP YES",
            "SET !TIMEOUT_STEP 0",
            "ADD !EXTRACT {{!LOOP}}",
            "TAG POS=1 TYPE=DIV ATTR=ID:none",
            "ADD !EXTRACT never"), loops: 2);
        Assert.True(r.Success);
        Assert.Equal(new[] { "1", "2" }, r.Extracts);
    }

    [Fact]
    public async Task XPathSelectorAndEvent()
    {
        var (r, i) = await Play(M(
            "SET !EXTRACT_TEST_POPUP NO",
            "URL GOTO=https://form.test/",
            "TAG XPATH=\"//a[@id='next']\" EXTRACT=TXT",
            "TAG SELECTOR=\"li.item\" EXTRACT=TXT",
            "EVENT TYPE=KEYPRESS SELECTOR=\"#city\" CHARS=\"Oslo\"",
            "EVENT TYPE=CLICK XPATH=\"//a[@id='next']\""));
        Assert.True(r.Success, r.ErrorMessage);
        Assert.Equal(new[] { "Next page", "Apple" }, i.State.ExtractValues);
        Assert.Equal("Oslo", _driver.Dom.EvalString("byId('city').value"));
        Assert.Equal("https://example.test/next", _driver.Dom.EvalString("window.__clickedHref"));
    }

    [Fact]
    public async Task ClickAtCoordinates()
    {
        _driver.Pages["https://pt.test/"] = "setPage(h('html', null, h('body', null, h('p', { id: 'p', 'data-pt': '5,6' }, 'x'))));";
        var (r, _) = await Play("URL GOTO=https://pt.test/\nCLICK X=5 Y=6\nCLICK X=1 Y=1");
        Assert.Equal(3, r.ErrorLine);
        Assert.Contains("click", _driver.Dom.EvalString("byId('p').events.join(',')"));
    }

    [Fact]
    public async Task FrameSelection()
    {
        _driver.Pages["https://frames.test/"] = FormPage + "addFrame('side', h('html', null, h('body', null, h('i', { id: 'fi' }, 'framed'))));";
        var (r, i) = await Play(M(
            "SET !EXTRACT_TEST_POPUP NO",
            "SET !TIMEOUT_STEP 0",
            "URL GOTO=https://frames.test/",
            "FRAME F=1",
            "TAG POS=1 TYPE=I ATTR=* EXTRACT=TXT",
            "FRAME NAME=side",
            "TAG POS=1 TYPE=I ATTR=* EXTRACT=TXT",
            "FRAME F=0",
            "TAG POS=1 TYPE=A ATTR=ID:next EXTRACT=TXT",
            "FRAME F=5"));
        Assert.Equal(10, r.ErrorLine);
        Assert.Equal(new[] { "framed", "framed", "Next page" }, i.State.ExtractValues);
    }

    // ---- EVAL / dialogs / prompt -------------------------------------------------------------------------

    [Fact]
    public async Task EvalComputesValues()
    {
        var (r, i) = await Play("SET !VAR1 abc\nSET !VAR2 EVAL(\"'{{!VAR1}}'.toUpperCase() + (2 * 21);\")");
        Assert.True(r.Success, r.ErrorMessage);
        Assert.Equal("ABC42", i.State.Get("!VAR2"));
    }

    [Fact]
    public async Task EvalErrorsRespectErrorIgnoreButMacroErrorAlwaysStops()
    {
        var (r1, _) = await Play("SET !ERRORIGNORE YES\nSET x EVAL(\"nope(\")\nSET y EVAL(\"MacroError('bad data')\")\nSET z 1");
        Assert.Equal(PlayStatus.Failed, r1.Status);
        Assert.Equal(3, r1.ErrorLine);
        Assert.Contains("MacroError: bad data", r1.ErrorMessage);
        Assert.Contains(_host.Warnings, w => w.Contains("EVAL error"));
    }

    [Fact]
    public async Task OnDialogAnswersAlertConfirmPrompt()
    {
        var (r, i) = await Play(M(
            "ONDIALOG POS=1 BUTTON=OK",
            "SET a EVAL(\"alert('hi'); 'done';\")",
            "ONDIALOG POS=1 BUTTON=CANCEL",
            "SET c EVAL(\"confirm('sure?') ? 'yes' : 'no';\")",
            "ONDIALOG POS=1 BUTTON=OK CONTENT=typed<SP>text",
            "SET p EVAL(\"prompt('name?', 'x');\")",
            "ONDIALOG POS=1 BUTTON=OK",
            "ONDIALOG POS=2 BUTTON=CANCEL",
            "SET two EVAL(\"(confirm('1') ? 'A' : 'a') + (confirm('2') ? 'B' : 'b');\")",
            "SET none EVAL(\"confirm('unhandled') ? 'Y' : 'N';\")"));
        Assert.True(r.Success, r.ErrorMessage);
        Assert.Equal("done", i.State.Get("a"));
        Assert.Equal("no", i.State.Get("c"));
        Assert.Equal("typed text", i.State.Get("p"));
        Assert.Equal("Ab", i.State.Get("two"));
        Assert.Equal("N", i.State.Get("none"));
        Assert.Equal(6, _driver.Dialogs.Count);
        Assert.Null(_driver.DialogHandler); // detached after run
    }

    [Fact]
    public async Task PromptStoresValueAndMessageOnlyPrompt()
    {
        _host.PromptAnswers.Enqueue("Zed");
        var (r, i) = await Play("PROMPT \"Your name?\" !VAR1 Bob\nPROMPT Hello<SP>{{!VAR1}}\nPROMPT \"Age?\" age 42");
        Assert.True(r.Success);
        Assert.Equal("Zed", i.State.Get("!VAR1"));
        Assert.Equal("42", i.State.Get("age"));
        Assert.Equal(new[] { "Hello Zed" }, _host.Messages);
        Assert.Equal(new[] { "Your name?", "Age?" }, _host.Prompts);
    }

    [Fact]
    public async Task CancelledPromptStopsMacro()
    {
        _host.PromptAnswers.Enqueue(null);
        var (r, _) = await Play("PROMPT q !VAR1\nSET x 1");
        Assert.Equal(PlayStatus.Stopped, r.Status);
    }

    [Fact]
    public async Task PauseCallsHost()
    {
        var (r, _) = await Play("PAUSE");
        Assert.True(r.Success);
        Assert.Equal(1, _host.Pauses);
    }

    // ---- navigation / tabs / browser ---------------------------------------------------------------------

    [Fact]
    public async Task NavigationCommands()
    {
        var (r, _) = await Play("URL GOTO=form.test/\nURL GOTO=https://empty.test/\nBACK\nREFRESH\nWAIT SECONDS=1.5");
        Assert.True(r.Success, r.ErrorMessage);
        Assert.Equal(new[] { "GOTO https://form.test/", "GOTO https://empty.test/", "BACK", "REFRESH" }, _driver.Calls);
        Assert.Equal("https://form.test/", _driver.CurrentUrl);
        Assert.Equal(TimeSpan.FromSeconds(1.5), _clock.Delays.Single());
    }

    [Theory]
    [InlineData("example.com", "https://example.com")]
    [InlineData("http://a.test/x", "http://a.test/x")]
    [InlineData("about:blank", "about:blank")]
    [InlineData("localhost:8080/app", "https://localhost:8080/app")]
    [InlineData("file:///c:/x.html", "file:///c:/x.html")]
    public void NormalizesUrls(string input, string expected) => Assert.Equal(expected, MacroInterpreter.NormalizeUrl(input));

    [Fact]
    public async Task NavigationTimeoutIsAnError()
    {
        _driver.FailNavigation = _ => new TimeoutException("Page load timeout (60s)");
        var (r, _) = await Play("URL GOTO=https://slow.test/");
        Assert.Equal(1, r.ErrorLine);
        Assert.Contains("timeout", r.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Tabs()
    {
        var (r, _) = await Play("TAB OPEN\nTAB T=2\nURL GOTO=https://form.test/\nTAB CLOSE\nTAB OPEN\nTAB CLOSEALLOTHERS\nTAB T=3");
        Assert.Equal(7, r.ErrorLine);
        Assert.Contains("does not exist", r.ErrorMessage);
        Assert.Equal(new[] { "TAB OPEN", "TAB 2", "GOTO https://form.test/", "TAB CLOSE", "TAB OPEN", "TAB CLOSEALLOTHERS" }, _driver.Calls);
    }

    [Fact]
    public async Task CannotCloseLastTab()
    {
        var (r, _) = await Play("TAB CLOSE");
        Assert.Contains("last tab", r.ErrorMessage);
    }

    [Fact]
    public async Task ClearAndFilter()
    {
        var (r, _) = await Play("CLEAR\nFILTER TYPE=IMAGES STATUS=ON\nFILTER TYPE=POPUPS STATUS=ON\nCLEAR COOKIES");
        Assert.True(r.Success);
        Assert.True(_driver.ImagesBlocked);
        Assert.Equal(new[] { "CLEAR All", "FILTER IMAGES True", "CLEAR Cookies" }, _driver.Calls);
        Assert.Contains(_host.Warnings, w => w.Contains("POPUPS"));
    }

    [Fact]
    public async Task UnsupportedCommandIsSkippedWithWarning()
    {
        var (r, _) = await Play("SIZE X=800 Y=600\nSET x 1");
        Assert.True(r.Success);
        Assert.Contains(_host.Warnings, w => w.Contains("SIZE is not implemented"));
    }

    // ---- files -------------------------------------------------------------------------------------------

    [Fact]
    public async Task SaveAsExtractWritesCsvRowsAndClears()
    {
        var (r, i) = await Play(M(
            "SET !EXTRACT_TEST_POPUP NO",
            "URL GOTO=https://form.test/",
            "TAG POS=1 TYPE=LI ATTR=* EXTRACT=TXT",
            "TAG POS=2 TYPE=LI ATTR=* EXTRACT=TXT",
            "SAVEAS TYPE=EXTRACT FOLDER=* FILE=out.csv",
            "ADD !EXTRACT second<SP>row",
            "SAVEAS TYPE=EXTRACT FOLDER=* FILE=out.csv",
            "SAVEAS TYPE=EXTRACT FOLDER=sub FILE=*"));
        Assert.True(r.Success, r.ErrorMessage);
        var lines = await File.ReadAllLinesAsync(Path.Combine(_dir, "Downloads", "out.csv"));
        Assert.Equal(new[] { "\"Apple\",\"Banana \"\"B\"\"\"", "\"second row\"" }, lines);
        Assert.Empty(i.State.ExtractValues);
        Assert.False(File.Exists(Path.Combine(_dir, "Downloads", "sub", "extract.csv"))); // nothing left to write
        Assert.Contains(_host.Warnings, w => w.Contains("nothing extracted"));
    }

    [Fact]
    public async Task SaveAsHtmlTextAndPng()
    {
        var target = Path.Combine(_dir, "pages");
        var (r, _) = await Play(M(
            "URL GOTO=https://form.test/",
            $"SAVEAS TYPE=HTM FOLDER={target} FILE=*",
            $"SAVEAS TYPE=TXT FOLDER={target} FILE=page",
            $"SAVEAS TYPE=PNG FOLDER={target} FILE=+_shot",
            $"SCREENSHOT TYPE=PAGE FOLDER={target} FILE=full.png",
            $"SCREENSHOT TYPE=BROWSER FOLDER={target} FILE=view"));
        Assert.True(r.Success, r.ErrorMessage);
        Assert.StartsWith("<html>", await File.ReadAllTextAsync(Path.Combine(target, "Form.htm")));
        Assert.Contains("Apple", await File.ReadAllTextAsync(Path.Combine(target, "page.txt")));
        Assert.Equal(_driver.ScreenshotBytes, await File.ReadAllBytesAsync(Path.Combine(target, "Form_shot.png")));
        Assert.True(File.Exists(Path.Combine(target, "full.png")));
        Assert.True(File.Exists(Path.Combine(target, "view.png")));
        Assert.Contains("SCREENSHOT fullPage=True Png", _driver.Calls);
        Assert.Contains("SCREENSHOT fullPage=False Png", _driver.Calls);
    }

    [Fact]
    public async Task FileDelete()
    {
        var f = Path.Combine(_dir, "del.txt");
        await File.WriteAllTextAsync(f, "x");
        var (r, _) = await Play($"FILEDELETE NAME={f}\nFILEDELETE NAME={f}");
        Assert.Equal(2, r.ErrorLine);
        Assert.False(File.Exists(f));
    }

    // ---- misc --------------------------------------------------------------------------------------------

    [Fact]
    public async Task StopwatchMeasuresWithClock()
    {
        var (r, i) = await Play("STOPWATCH ID=a\nWAIT SECONDS=2.25\nSTOPWATCH ID=a\nSET t {{!STOPWATCHTIME}}\nSTOPWATCH START ID=b\nWAIT SECONDS=1\nSTOPWATCH STOP ID=b\nSTOPWATCH LABEL=end\nSTOPWATCH STOP ID=zz");
        Assert.Equal(9, r.ErrorLine);
        Assert.Equal("2.250", i.State.Get("t"));
        Assert.Contains(_host.Logs, l => l.Message.Contains("Stopwatch b: 1.000s"));
        Assert.Contains(_host.Logs, l => l.Message.Contains("label end: 3.250s"));
    }

    [Fact]
    public async Task SearchExtractsFromSource()
    {
        var (r, i) = await Play("SET !EXTRACT_TEST_POPUP NO\nURL GOTO=https://form.test/\nSEARCH SOURCE=REGEXP:id=\"(n\\w+)\" EXTRACT=$1\nSEARCH SOURCE=TXT:cherry IGNORE_CASE=YES\nSEARCH SOURCE=TXT:zzz");
        Assert.Equal(5, r.ErrorLine);
        Assert.Equal("news", i.State.ExtractValues.Single());
    }

    [Fact]
    public async Task ReportsCurrentLineToHost()
    {
        await Play("' comment\nSET a 1\n\nSET b 2");
        Assert.Equal(new[] { 2, 4 }, _host.Lines.Select(l => l.Line));
        Assert.Equal("SET b 2", _host.Lines[1].Text);
    }

    [Fact]
    public async Task CancellationStopsMacro()
    {
        using var cts = new CancellationTokenSource();
        _host.OnLineHook = line => { if (line == 2) cts.Cancel(); };
        var interp = NewInterpreter();
        var r = await interp.PlayAsync("SET a 1\nSET b 2\nSET c 3", 1, cts.Token);
        Assert.Equal(PlayStatus.Stopped, r.Status);
        Assert.False(interp.State.IsDefined("c"));
        Assert.False(interp.IsRunning);
    }

    [Fact]
    public async Task UrlCurrentVariable()
    {
        var (_, i) = await Play("URL GOTO=https://form.test/\nSET u {{!URLCURRENT}}");
        Assert.Equal("https://form.test/", i.State.Get("u"));
    }

    [Fact]
    public async Task TimeoutVariablesValidate()
    {
        var (r, _) = await Play("SET !TIMEOUT_STEP abc");
        Assert.Contains("non-negative number", r.ErrorMessage);
        var (r2, _) = await Play("SET !ERRORIGNORE MAYBE");
        Assert.Contains("YES or NO", r2.ErrorMessage);
    }
}
