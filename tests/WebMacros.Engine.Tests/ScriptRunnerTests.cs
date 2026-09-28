using WebMacros.Engine.Runtime;
using WebMacros.Engine.Samples;
using WebMacros.Engine.Scripts;
using WebMacros.Engine.Tests.Support;

namespace WebMacros.Engine.Tests;

public sealed class ScriptRunnerTests : IDisposable
{
    private const string ListPage = """
setPage(h('html', null, h('head', null, h('title', null, 'List')), h('body', null,
  h('input', { type: 'text', name: 'q', id: 'q' }),
  h('ul', null, h('li', null, 'Apple'), h('li', null, 'Banana'), h('li', null, 'Cherry')))));
""";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wm-scripts-" + Guid.NewGuid().ToString("N"));
    private readonly FakeBrowserDriver _driver = new();
    private readonly RecordingHost _host = new();

    public ScriptRunnerTests()
    {
        Directory.CreateDirectory(_dir);
        _driver.Pages["https://list.test/"] = ListPage;
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private ScriptRunner Runner(out MacroInterpreter interp)
    {
        var options = new InterpreterOptions { MacroFolder = _dir, DataSourceFolder = _dir, DownloadFolder = Path.Combine(_dir, "Downloads") };
        interp = new MacroInterpreter(_driver, _host, options, new FakeClock());
        return new ScriptRunner(interp, _driver, _host, options);
    }

    private Task<ScriptResult> Run(string code, CancellationToken ct = default) => Runner(out _).RunAsync(code, ct);

    private IEnumerable<string> Console => _host.LogsAt(LogLevel.Info).Where(m => m.StartsWith("> ", StringComparison.Ordinal)).Select(m => m[2..]);

    // console.log output is prefixed so tests can tell it apart from engine log lines.
    private const string P = "function out(v) { console.log('> ' + v); }\n";

    [Fact]
    public async Task WhileLoopWithIimSetAndIimPlay()
    {
        var r = await Run(P + """
var i = 1, total = 0;
while (i <= 3) {
  iimSet("n", i * 10);
  var ret = iimPlay("CODE:SET !EXTRACT {{n}}\nADD !EXTRACT {{!LOOP}}");
  out(ret + ":" + iimGetLastExtract());
  total += Number(iimGetLastExtract(1));
  i++;
}
out("total=" + total);
""");
        Assert.True(r.Success, r.ErrorMessage);
        Assert.Equal(3, r.MacrosPlayed);
        Assert.Equal(new[] { "1:10[EXTRACT]1", "1:20[EXTRACT]1", "1:30[EXTRACT]1", "total=60" }, Console);
        Assert.Empty(_host.ExtractPopups); // no extract dialog in scripting mode
    }

    [Fact]
    public async Task IimSetValuesApplyToNextPlayOnly()
    {
        var r = await Run(P + """
iimSet("who", "Ann");
iimSet("-var_city", "Paris");    // iMacros 6 syntax
iimSet("!VAR1", 5);
out(iimPlay("CODE:SET !EXTRACT {{who}}<SP>{{city}}<SP>{{!VAR1}}"));
out(iimGetLastExtract(1));
out(iimPlay("CODE:SET !EXTRACT {{who}}"));
out(iimGetLastError());
out(iimGetLastErrorCode());
""");
        Assert.True(r.Success, r.ErrorMessage);
        Assert.Equal("1", Console.ElementAt(0));
        Assert.Equal("Ann Paris 5", Console.ElementAt(1));
        Assert.Equal("-1", Console.ElementAt(2));
        Assert.Contains("{{who}} is not defined", Console.ElementAt(3));
        Assert.Equal("-1", Console.ElementAt(4));
    }

    [Fact]
    public async Task BadIimSetBuiltInFailsThePlay()
    {
        var r = await Run(P + "iimSet('!TIMEOUT_STEP', 'soon'); out(iimPlay('CODE:SET a 1')); out(iimGetLastError());");
        Assert.True(r.Success);
        Assert.Equal("-1", Console.First());
        Assert.Contains("iimSet(\"!TIMEOUT_STEP\")", Console.Last());
    }

    [Fact]
    public async Task ExtractFromPageByIndex()
    {
        var r = await Run(P + """
iimPlay("CODE:URL GOTO=https://list.test/\nTAG POS=1 TYPE=LI ATTR=* EXTRACT=TXT\nTAG POS=3 TYPE=LI ATTR=* EXTRACT=TXT");
out(iimGetLastExtract(1));
out(iimGetExtract(2));
out(iimGetLastExtract(3));
out(iimGetLastExtract());
""");
        Assert.True(r.Success, r.ErrorMessage);
        Assert.Equal(new[] { "Apple", "Cherry", "#nodata#", "Apple[EXTRACT]Cherry" }, Console);
    }

    [Fact]
    public async Task PlaysMacroFilesByName()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "hello.iim"), "SET !EXTRACT hello");
        Directory.CreateDirectory(Path.Combine(_dir, "sub"));
        await File.WriteAllTextAsync(Path.Combine(_dir, "sub", "two.iim"), "SET !EXTRACT two");
        var r = await Run(P + """
out(iimPlay("hello") + iimGetLastExtract());
out(iimPlay("hello.iim") + iimGetLastExtract());
out(iimPlay("sub/two") + iimGetLastExtract());
out(iimPlay("missing"));
out(iimGetLastError());
out(iimGetLastExtract());
""");
        Assert.True(r.Success, r.ErrorMessage);
        Assert.Equal(new[] { "1hello", "1hello", "1two", "-3", "macro not found: missing", "" }, Console);
    }

    [Fact]
    public async Task MacroErrorsReturnCodesAndScriptContinues()
    {
        var r = await Run(P + """
var ret = iimPlay("CODE:SET !TIMEOUT_STEP 0\nURL GOTO=https://list.test/\nTAG POS=1 TYPE=DIV ATTR=ID:nope");
out(ret);
out(iimGetLastError());
out(iimPlay("CODE:FOO BAR"));
out(iimGetErrorText());
if (iimPlay("CODE:SET ok 1") == 1) out("recovered:" + iimGetLastError() + ".");
""");
        Assert.True(r.Success, r.ErrorMessage);
        var c = Console.ToArray();
        Assert.Equal("-1", c[0]);
        Assert.StartsWith("CODE line 3: TAG: Element not found", c[1]);
        Assert.Equal("-2", c[2]);
        Assert.Contains("Unknown command", c[3]);
        Assert.Equal("recovered:.", c[4]);
    }

    [Fact]
    public async Task UncaughtJavaScriptErrorReportsScriptLine()
    {
        var r = await Run("var a = 1;\nvar b = 2;\nthrow new Error('bad input');\nconsole.log('never');");
        Assert.Equal(PlayStatus.Failed, r.Status);
        Assert.Equal(3, r.ErrorLine);
        Assert.Equal("Error: bad input", r.ErrorMessage);
        Assert.DoesNotContain(_host.Logs, l => l.Message == "never");

        var r2 = await Run("var x = 1;\nundefinedFunction();");
        Assert.Equal(2, r2.ErrorLine);
        Assert.StartsWith("ReferenceError", r2.ErrorMessage);
    }

    [Fact]
    public async Task SyntaxErrorIsReportedWithLine()
    {
        var r = await Run("var a = 1;\nwhile (a < {\n}");
        Assert.Equal(PlayStatus.Failed, r.Status);
        Assert.Equal(3, r.ErrorLine); // the parser notices the missing ')' at the '}' on line 3
        Assert.Single(ScriptRunner.Validate("var a = 1;\nwhile (a < {\n}"));
        Assert.Equal(3, ScriptRunner.Validate("var a = 1;\nwhile (a < {\n}")[0].Line);
        Assert.Empty(ScriptRunner.Validate("var a = 1; while (a < 3) { a++; }"));
    }

    [Fact]
    public async Task AbortStopsInfiniteLoop()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var r = await Run("var i = 0; while (true) { i++; }", cts.Token);
        Assert.Equal(PlayStatus.Stopped, r.Status);
    }

    [Fact]
    public async Task AbortDuringIimPlayStopsMacroAndScript()
    {
        using var cts = new CancellationTokenSource();
        _host.OnLineHook = line => { if (line == 2) cts.Cancel(); };
        var r = await Run(P + """
try {
  iimPlay("CODE:SET a 1\nSET b 2\nSET c 3");
} catch (e) {
  out("caught");
}
out("after");
""", cts.Token);
        Assert.Equal(PlayStatus.Stopped, r.Status);
        Assert.Empty(Console); // the abort cannot be caught by the script
        Assert.Equal(1, r.MacrosPlayed);
    }

    [Fact]
    public async Task StopBeforeStartRunsNothing()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var r = await Run(P + "out('x'); iimPlay('CODE:SET a 1');", cts.Token);
        Assert.Equal(PlayStatus.Stopped, r.Status);
        Assert.Empty(Console);
        Assert.Equal(0, r.MacrosPlayed);
    }

    [Fact]
    public async Task IimExitEndsScriptAndCannotBeCaught()
    {
        var r = await Run(P + "out(1); try { iimExit(); } catch (e) { out('caught'); } out(2);");
        Assert.True(r.Success);
        Assert.Equal(new[] { "1" }, Console);
    }

    [Fact]
    public async Task DialogsMapToHost()
    {
        _host.PromptAnswers.Enqueue("Zed");
        _host.PromptAnswers.Enqueue(null);
        _host.ConfirmAnswers.Enqueue(false);
        var r = await Run(P + """
alert("Hello");
alert({ a: 1 });
out(prompt("Name?", "Bob"));
out(prompt("Again?") === null);
out(confirm("Sure?"));
out(confirm("Really?"));
iimDisplay("Working...");
console.warn("careful", 42);
console.error("broken");
""");
        Assert.True(r.Success, r.ErrorMessage);
        Assert.Equal(new[] { "Hello", "{\"a\":1}" }, _host.Messages);
        Assert.Equal(new[] { "Name?", "Again?" }, _host.Prompts);
        Assert.Equal(new[] { "Sure?", "Really?" }, _host.Confirms);
        Assert.Equal(new[] { "Zed", "true", "false", "true" }, Console);
        Assert.Equal(new[] { "Working..." }, _host.Statuses);
        Assert.Contains("careful 42", _host.Warnings);
        Assert.Contains("broken", _host.LogsAt(LogLevel.Error));
    }

    [Fact]
    public async Task IimEvalRunsInCurrentPage()
    {
        var r = await Run(P + """
iimPlay("CODE:URL GOTO=https://list.test/");
out(iimEval("document.title"));
out(iimEval("document.getElementsByTagName('li').length * 2"));
try { iimEval("throw new Error('page boom')"); } catch (e) { out(e.message); }
""");
        Assert.True(r.Success, r.ErrorMessage);
        Assert.Equal(new[] { "List", "6", "iimEval: page boom" }, Console);
    }

    [Fact]
    public async Task ReadCsvAndTextFiles()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "d.csv"), "a,b\n\"x, y\",2\n");
        await File.WriteAllTextAsync(Path.Combine(_dir, "s.csv"), "1;2;3\n");
        await File.WriteAllTextAsync(Path.Combine(_dir, "t.txt"), "line1\nline2");
        var r = await Run(P + """
var rows = readCsv("d.csv");
out(rows.length + " " + rows[1][0] + " " + rows[1][1]);
out(readCsv("s.csv", ";")[0].join("+"));
out(readTextFile("t.txt").split("\n").length);
try { readCsv("nope.csv"); } catch (e) { out(e.message); }
""");
        Assert.True(r.Success, r.ErrorMessage);
        Assert.Equal(new[] { "2 x, y 2", "1+2+3", "2", "readCsv: file not found: nope.csv" }, Console);
    }

    [Fact]
    public async Task ScriptRunsOnCallerSynchronizationContext()
    {
        var ctx = new CountingContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(ctx);
        Task<ScriptResult> task;
        try { task = Runner(out _).RunAsync("console.log('x'); iimPlay('CODE:SET a 1');"); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        var r = await task;
        Assert.True(r.Success, r.ErrorMessage);
        Assert.True(ctx.Posts >= 2);
    }

    private sealed class CountingContext : SynchronizationContext
    {
        public int Posts;
        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref Posts);
            ThreadPool.QueueUserWorkItem(_ => d(state));
        }
    }

    [Fact]
    public async Task SampleScriptFillsRowsFromCsv()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, SampleMacros.CustomersCsv.FileName), SampleMacros.CustomersCsv.Content);
        _driver.PageResolver = url => !url.StartsWith("data:text/html", StringComparison.Ordinal) ? null : """
setPage(h('html', null, h('head', null, h('title', null, 'Rows demo')), h('body', null,
  h('table', null, h('thead', null, h('tr', null, h('th', null, '#'))), h('tbody', { id: 'rows' })),
  h('button', { type: 'button', id: 'add' }, 'Add Row'),
  h('p', { id: 'status' }, '0 rows'))));
byId('add').addEventListener('click', function () {
  var tb = byId('rows'), n = tb.childNodes.length;
  if (n >= 5) { byId('status').textContent = 'Maximum rows reached'; return; }
  tb.appendChild(h('tr', null, h('td', null, String(n + 1)),
    h('td', null, h('input', { name: 'name' })), h('td', null, h('input', { name: 'email' })),
    h('td', null, h('input', { type: 'checkbox', name: 'priority' }))));
  byId('status').textContent = (n + 1) + ' rows';
});
""";
        var r = await Runner(out _).RunAsync(SampleMacros.AddRowsScript.Content);
        Assert.True(r.Success, r.ErrorMessage);
        string Js(string s) => _driver.Dom.EvalString(s);
        Assert.Equal("3", Js("byId('rows').childNodes.length"));
        Assert.Equal("Alice Smith|Bob Jones|Carol White",
            Js("document.getElementsByTagName('input').filter(function (e) { return e.getAttribute('name') === 'name'; }).map(function (e) { return e.value; }).join('|')"));
        Assert.Equal("carol@example.com", Js("document.getElementsByTagName('input')[7].value"));
        Assert.Equal("false,false,true",
            Js("document.getElementsByTagName('input').filter(function (e) { return e.getAttribute('name') === 'priority'; }).map(function (e) { return e.checked; }).join(',')"));
        Assert.Contains("Carol White marked as priority", _host.LogsAt(LogLevel.Info));
        Assert.Equal("Done: the table has 3 row(s)", _host.Statuses.Last());
        Assert.StartsWith("data:text/html;charset=utf-8,", _driver.CurrentUrl);
        Assert.Empty(ScriptRunner.Validate(SampleMacros.AddRowsScript.Content));
    }

    [Fact]
    public async Task SampleScriptStopsWhenPageRefusesRows()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, SampleMacros.CustomersCsv.FileName), SampleMacros.CustomersCsv.Content);
        _driver.PageResolver = _ => """
setPage(h('html', null, h('body', null, h('table', null, h('tbody', { id: 'rows' })),
  h('button', { type: 'button', id: 'add' }, 'Add Row'), h('p', { id: 'status' }, '0 rows'))));
byId('add').addEventListener('click', function () { byId('status').textContent = 'Maximum rows reached'; });
""";
        var r = await Runner(out _).RunAsync(SampleMacros.AddRowsScript.Content);
        Assert.True(r.Success, r.ErrorMessage);
        Assert.Contains(_host.Statuses, s => s.Contains("does not accept more rows"));
        Assert.Equal(2, r.MacrosPlayed); // open page + one Add Row attempt
    }

    [Fact]
    public async Task RunnerRejectsConcurrentRuns()
    {
        var runner = Runner(out _);
        using var cts = new CancellationTokenSource();
        var first = runner.RunAsync("while (true) {}", cts.Token);
        Assert.Throws<InvalidOperationException>(() => { _ = runner.RunAsync("1"); });
        cts.Cancel();
        Assert.Equal(PlayStatus.Stopped, (await first).Status);
        Assert.False(runner.IsRunning);
    }
}
