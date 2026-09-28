using WebMacros.Engine.Runtime;
using WebMacros.Engine.Samples;
using WebMacros.Engine.Tests.Support;

namespace WebMacros.Engine.Tests;

/// <summary>Plays the seeded sample macros against local mock versions of the real pages.</summary>
public sealed class SampleMacroPlaybackTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wm-samples-" + Guid.NewGuid().ToString("N"));
    private readonly FakeBrowserDriver _driver = new();
    private readonly RecordingHost _host = new();

    public SampleMacroPlaybackTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private MacroInterpreter Interp() => new(_driver, _host, new InterpreterOptions
    {
        MacroFolder = _dir, DataSourceFolder = _dir, DownloadFolder = Path.Combine(_dir, "Downloads"),
    }, new FakeClock());

    [Fact]
    public async Task DuckDuckGoSample()
    {
        _driver.Pages["https://html.duckduckgo.com/html/"] = """
setPage(h('html', null, h('body', null,
  h('form', { id: 'search_form' }, h('input', { type: 'text', name: 'q' }), h('input', { type: 'submit', value: 'S' })),
  h('div', null,
    h('a', { 'class': 'result__a', href: '/1' }, 'First result'),
    h('a', { 'class': 'result__a', href: '/2' }, 'Second result'),
    h('a', { 'class': 'result__a', href: '/3' }, 'Third result'),
    h('a', { 'class': 'result__a', href: '/4' }, 'Fourth result')))));
""";
        var r = await Interp().PlayAsync(SampleMacros.DuckDuckGo.Content);
        Assert.True(r.Success, r.ErrorMessage);
        Assert.Equal("webview2 browser automation", _driver.Dom.EvalString("document.getElementsByTagName('input')[0].value"));
        Assert.Equal("true", _driver.Dom.EvalString("String(byId('search_form').submitted)"));
        var csv = await File.ReadAllTextAsync(Path.Combine(_dir, "Downloads", "ddg-results.csv"));
        Assert.Equal("\"First result\",\"Second result\",\"Third result\",\"Fourth result\",\"#EANF#\"", csv.Trim());
    }

    [Fact]
    public async Task FormFromCsvSample()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, SampleMacros.CustomersCsv.FileName), SampleMacros.CustomersCsv.Content);
        _driver.Pages["https://httpbin.org/forms/post"] = """
setPage(h('html', null, h('body', null,
  h('form', { id: 'order' },
    h('input', { name: 'custname' }), h('input', { type: 'tel', name: 'custtel' }), h('input', { type: 'email', name: 'custemail' }),
    h('input', { type: 'radio', name: 'size', value: 'small' }), h('input', { type: 'radio', name: 'size', value: 'medium' }), h('input', { type: 'radio', name: 'size', value: 'large' }),
    h('input', { type: 'checkbox', name: 'topping', value: 'bacon' }), h('input', { type: 'checkbox', name: 'topping', value: 'cheese' }),
    h('input', { type: 'checkbox', name: 'topping', value: 'mushroom' }),
    h('input', { type: 'time', name: 'delivery' }), h('textarea', { name: 'comments' }), h('button', null, 'Submit order')),
  h('pre', null, '{ "form": "posted" }'))));
""";
        var r = await Interp().PlayAsync(SampleMacros.FormFromCsv.Content, loops: 4);
        Assert.True(r.Success, r.ErrorMessage);
        Assert.Equal(3, r.LoopsCompleted);
        var lines = await File.ReadAllLinesAsync(Path.Combine(_dir, "Downloads", "httpbin-orders.csv"));
        Assert.Equal(new[]
        {
            "\"Alice Smith\",\"{ \"\"form\"\": \"\"posted\"\" }\"",
            "\"Bob Jones\",\"{ \"\"form\"\": \"\"posted\"\" }\"",
            "\"Carol White\",\"{ \"\"form\"\": \"\"posted\"\" }\"",
        }, lines);
        // Last loop's values are in the form.
        Assert.Equal("carol@example.com", _driver.Dom.EvalString("document.getElementsByTagName('input')[2].value"));
        Assert.Equal("No onions", _driver.Dom.EvalString("document.getElementsByTagName('textarea')[0].value"));
        Assert.Equal("true", _driver.Dom.EvalString("String(document.getElementsByTagName('input')[5].checked)"));
    }

    [Fact]
    public async Task LoopPagesSample()
    {
        _driver.Pages["https://example.com/"] = "setPage(h('html', null, h('head', null, h('title', null, 'Example Domain')), h('body')));";
        _driver.Pages["https://httpbin.org/html"] = "setPage(h('html', null, h('head'), h('body', null, h('h1', null, 'Herman Melville'))));";
        _driver.Pages["https://www.wikipedia.org/"] = "setPage(h('html', null, h('head', null, h('title', null, 'Wikipedia')), h('body')));";
        var r = await Interp().PlayAsync(SampleMacros.LoopPages.Content, loops: 3);
        Assert.True(r.Success, r.ErrorMessage);
        Assert.Equal(new[] { "GOTO https://example.com/", "GOTO https://httpbin.org/html", "GOTO https://www.wikipedia.org/" }, _driver.Calls);
        var lines = await File.ReadAllLinesAsync(Path.Combine(_dir, "Downloads", "visited-pages.csv"));
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("\"https://example.com/\",\"Example Domain\",\"0.000\",\"2026-09-28 16:45:07\"", lines[0]);
        Assert.StartsWith("\"https://httpbin.org/html\",\"#EANF#\"", lines[1]); // no <title>: ignored error
    }

    [Fact]
    public async Task DialogsDemoSample()
    {
        _driver.Pages["https://example.com/"] = "setPage(h('html', null, h('body')));";
        var r = await Interp().PlayAsync(SampleMacros.DialogsDemo.Content);
        Assert.True(r.Success, r.ErrorMessage);
        Assert.Equal(new[] { "WORLD has 5 letters[EXTRACT]alert handled[EXTRACT]confirm: CANCEL[EXTRACT]prompt: WebMacros[EXTRACT]sum=20" }, r.Extracts);
        Assert.Single(_host.ExtractPopups);
        Assert.Equal(3, _driver.Dialogs.Count);
    }
}
