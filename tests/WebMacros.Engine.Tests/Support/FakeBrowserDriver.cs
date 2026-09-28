using WebMacros.Engine.Runtime;

namespace WebMacros.Engine.Tests.Support;

/// <summary>
/// In-memory <see cref="IBrowserDriver"/>: every tab is a Jint engine with the test DOM. Pages are registered
/// as JS snippets (calling setPage(h(...))) keyed by URL, so engine-generated scripts really run against a DOM.
/// </summary>
public sealed class FakeBrowserDriver : IBrowserDriver
{
    private sealed class Tab
    {
        public JsDom Dom = null!;
        public string Url = "about:blank";
        public readonly Stack<string> History = new();
    }

    private readonly List<Tab> _tabs = new();
    private int _current;

    public Dictionary<string, string> Pages { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Calls { get; } = new();
    public List<string> Scripts { get; } = new();
    public byte[] ScreenshotBytes { get; set; } = { 0x89, 0x50, 0x4E, 0x47 };
    public bool ImagesBlocked { get; private set; }
    public BrowsingDataKinds? Cleared { get; private set; }
    public List<BrowserDialog> Dialogs { get; } = new();

    /// <summary>Called before each ExecuteScriptAsync with the 1-based call number (e.g. to add elements late).</summary>
    public Action<int, JsDom>? BeforeScript { get; set; }
    public Func<string, Exception?>? FailNavigation { get; set; }
    /// <summary>Fallback page script for URLs not in <see cref="Pages"/> (e.g. data: URLs).</summary>
    public Func<string, string?>? PageResolver { get; set; }

    public FakeBrowserDriver(string? initialPage = null)
    {
        _tabs.Add(new Tab { Dom = NewDom(initialPage) });
    }

    private JsDom NewDom(string? page)
    {
        var dom = new JsDom();
        dom.Engine.SetValue("__dialog", new Func<string, string, string, string?>(OnDialog));
        if (page is not null) dom.Execute(page);
        return dom;
    }

    private string? OnDialog(string kind, string message, string def)
    {
        var k = kind switch { "alert" => DialogKind.Alert, "confirm" => DialogKind.Confirm, _ => DialogKind.Prompt };
        var dialog = new BrowserDialog(k, message, def);
        Dialogs.Add(dialog);
        var d = DialogHandler?.Invoke(dialog);
        if (d is null || !d.Accept) return null;
        return k == DialogKind.Prompt ? d.Text ?? def : "ok";
    }

    private Tab Current => _tabs[_current];
    public JsDom Dom => Current.Dom;
    public string CurrentUrl => Current.Url;
    public int TabCount => _tabs.Count;
    public int CurrentTabIndex => _current;
    public Func<BrowserDialog, DialogDecision?>? DialogHandler { get; set; }

    public Task NavigateAsync(string url, TimeSpan timeout, CancellationToken ct)
    {
        Calls.Add("GOTO " + url);
        if (FailNavigation?.Invoke(url) is { } ex) throw ex;
        Current.History.Push(Current.Url);
        Current.Url = url;
        Current.Dom = NewDom(Pages.TryGetValue(url, out var p) ? p : PageResolver?.Invoke(url));
        return Task.CompletedTask;
    }

    public Task GoBackAsync(TimeSpan timeout, CancellationToken ct)
    {
        Calls.Add("BACK");
        if (Current.History.Count > 0)
        {
            var url = Current.History.Pop();
            Current.Url = url;
            Current.Dom = NewDom(Pages.TryGetValue(url, out var p) ? p : null);
        }
        return Task.CompletedTask;
    }

    public Task ReloadAsync(TimeSpan timeout, CancellationToken ct)
    {
        Calls.Add("REFRESH");
        Current.Dom = NewDom(Pages.TryGetValue(Current.Url, out var p) ? p : null);
        return Task.CompletedTask;
    }

    public Task WaitForPageLoadAsync(TimeSpan timeout, CancellationToken ct) => Task.CompletedTask;

    public Task<string> ExecuteScriptAsync(string script, CancellationToken ct)
    {
        Scripts.Add(script);
        BeforeScript?.Invoke(Scripts.Count, Current.Dom);
        return Task.FromResult(Current.Dom.EvaluateAsWebView2Json(script));
    }

    public Task OpenTabAsync(CancellationToken ct)
    {
        Calls.Add("TAB OPEN");
        _tabs.Add(new Tab { Dom = NewDom(null) });
        return Task.CompletedTask;
    }

    public Task SwitchTabAsync(int index, CancellationToken ct)
    {
        Calls.Add("TAB " + (index + 1));
        _current = index;
        return Task.CompletedTask;
    }

    public Task CloseTabAsync(CancellationToken ct)
    {
        Calls.Add("TAB CLOSE");
        _tabs.RemoveAt(_current);
        _current = Math.Max(0, _current - 1);
        return Task.CompletedTask;
    }

    public Task CloseOtherTabsAsync(CancellationToken ct)
    {
        Calls.Add("TAB CLOSEALLOTHERS");
        var keep = Current;
        _tabs.Clear();
        _tabs.Add(keep);
        _current = 0;
        return Task.CompletedTask;
    }

    public Task<byte[]> CaptureScreenshotAsync(bool fullPage, ImageFormat format, CancellationToken ct)
    {
        Calls.Add($"SCREENSHOT fullPage={fullPage} {format}");
        return Task.FromResult(ScreenshotBytes);
    }

    public Task ClearBrowsingDataAsync(BrowsingDataKinds kinds, CancellationToken ct)
    {
        Calls.Add("CLEAR " + kinds);
        Cleared = kinds;
        return Task.CompletedTask;
    }

    public Task SetImageBlockingAsync(bool enabled, CancellationToken ct)
    {
        Calls.Add("FILTER IMAGES " + enabled);
        ImagesBlocked = enabled;
        return Task.CompletedTask;
    }
}
