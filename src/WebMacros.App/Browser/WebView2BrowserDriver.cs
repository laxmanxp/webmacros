using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using WebMacros.Engine.Runtime;

namespace WebMacros.App.Browser;

/// <summary>
/// <see cref="IBrowserDriver"/> on top of WebView2. Tabs are separate WebView2 controls stacked in one Grid;
/// only the active one is visible. All members must be called on the UI thread.
/// </summary>
public sealed class WebView2BrowserDriver : IBrowserDriver
{
    private readonly Grid _host;
    private readonly string _userDataFolder;
    private CoreWebView2Environment? _env;
    private bool _blockImages;
    private string? _recorderScript;

    public WebView2BrowserDriver(Grid host, string userDataFolder)
    {
        _host = host;
        _userDataFolder = userDataFolder;
    }

    public ObservableCollection<BrowserTab> Tabs { get; } = new();
    public int CurrentTabIndex { get; private set; }
    public BrowserTab? ActiveTab => CurrentTabIndex >= 0 && CurrentTabIndex < Tabs.Count ? Tabs[CurrentTabIndex] : null;
    public int TabCount => Tabs.Count;
    public string CurrentUrl => ActiveTab is { IsInitialized: true } t ? t.Core.Source : "about:blank";
    public Func<BrowserDialog, DialogDecision?>? DialogHandler { get; set; }

    /// <summary>Raised when the active tab changes (index may be the same after a close).</summary>
    public event Action<BrowserTab>? ActiveTabChanged;
    /// <summary>A message posted by page script (window.chrome.webview.postMessage) — used by the recorder.</summary>
    public event Action<BrowserTab, string>? WebMessageReceived;
    public event Action<BrowserTab>? NavigationCompleted;

    public async Task InitializeAsync(string? startUrl)
    {
        Directory.CreateDirectory(_userDataFolder);
        _env = await CoreWebView2Environment.CreateAsync(null, _userDataFolder);
        await CreateTabAsync(startUrl ?? "about:blank", activate: true);
    }

    public async Task<BrowserTab> CreateTabAsync(string? url, bool activate)
    {
        if (_env is null) throw new InvalidOperationException("Browser not initialized");
        var view = new WebView2 { Visibility = Visibility.Visible };
        var tab = new BrowserTab(view);
        _host.Children.Add(view);
        // Initialization needs the control in the visual tree; keep it visible until ready.
        foreach (var other in Tabs) other.View.Visibility = Visibility.Hidden;
        await view.EnsureCoreWebView2Async(_env);
        tab.Attach();
        ConfigureCore(tab);
        Tabs.Add(tab);
        if (activate) Activate(Tabs.Count - 1);
        else Activate(CurrentTabIndex); // restore visibility of the active tab
        if (!string.IsNullOrEmpty(url) && url != "about:blank") tab.Core.Navigate(url);
        return tab;
    }

    private void ConfigureCore(BrowserTab tab)
    {
        var core = tab.Core;
        core.Settings.AreDefaultScriptDialogsEnabled = false;
        core.Settings.IsStatusBarEnabled = true;
        core.ScriptDialogOpening += (_, e) => OnScriptDialog(e);
        core.WebMessageReceived += (_, e) =>
        {
            string? msg = null;
            try { msg = e.TryGetWebMessageAsString(); } catch (ArgumentException) { }
            if (msg is not null) WebMessageReceived?.Invoke(tab, msg);
        };
        core.NavigationCompleted += (_, _) => NavigationCompleted?.Invoke(tab);
        core.NewWindowRequested += async (_, e) =>
        {
            // Open popups / target=_blank links as new tabs (like iMacros).
            e.Handled = true;
            await CreateTabAsync(e.Uri, activate: true);
        };
        core.WebResourceRequested += (_, e) =>
        {
            if (_blockImages && e.ResourceContext == CoreWebView2WebResourceContext.Image && _env is not null)
                e.Response = _env.CreateWebResourceResponse(null, 403, "Blocked by WebMacros FILTER", "");
        };
        if (_blockImages) core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Image);
        if (_recorderScript is not null) _ = AddRecorderAsync(tab);
    }

    private void OnScriptDialog(CoreWebView2ScriptDialogOpeningEventArgs e)
    {
        var kind = e.Kind switch
        {
            CoreWebView2ScriptDialogKind.Alert => DialogKind.Alert,
            CoreWebView2ScriptDialogKind.Confirm => DialogKind.Confirm,
            CoreWebView2ScriptDialogKind.Prompt => DialogKind.Prompt,
            _ => DialogKind.BeforeUnload,
        };
        var decision = DialogHandler?.Invoke(new BrowserDialog(kind, e.Message, e.DefaultText));
        if (decision is not null)
        {
            if (decision.Accept)
            {
                if (kind == DialogKind.Prompt) e.ResultText = decision.Text ?? e.DefaultText;
                e.Accept();
            }
            return;
        }

        // Not handled by a macro: show our own dialog (default dialogs are disabled so the event fires).
        var deferral = e.GetDeferral();
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                var owner = Application.Current.MainWindow;
                switch (kind)
                {
                    case DialogKind.Alert:
                        MessageBox.Show(owner, e.Message, "Message from web page", MessageBoxButton.OK, MessageBoxImage.Information);
                        e.Accept();
                        break;
                    case DialogKind.Prompt:
                        var text = InputDialog.Ask(owner, "Web page prompt", e.Message, e.DefaultText);
                        if (text is not null) { e.ResultText = text; e.Accept(); }
                        break;
                    default:
                        var message = kind == DialogKind.BeforeUnload ? "Leave this page? Changes you made may not be saved." : e.Message;
                        if (MessageBox.Show(owner, message, "Web page", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
                            e.Accept();
                        break;
                }
            }
            finally
            {
                deferral.Complete();
            }
        });
    }

    public void Activate(int index)
    {
        if (Tabs.Count == 0) return;
        index = Math.Clamp(index, 0, Tabs.Count - 1);
        CurrentTabIndex = index;
        for (var i = 0; i < Tabs.Count; i++) Tabs[i].View.Visibility = i == index ? Visibility.Visible : Visibility.Hidden;
        ActiveTabChanged?.Invoke(Tabs[index]);
    }

    private BrowserTab Active => ActiveTab ?? throw new InvalidOperationException("No browser tab");

    private static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout, CancellationToken ct, string what)
    {
        var delay = Task.Delay(timeout, ct);
        var done = await Task.WhenAny(task, delay);
        if (done == task) return await task;
        ct.ThrowIfCancellationRequested();
        throw new TimeoutException($"{what} did not finish within {timeout.TotalSeconds:0.#}s (!TIMEOUT_PAGE)");
    }

    private async Task NavigateCoreAsync(BrowserTab tab, Action start, string what, TimeSpan timeout, CancellationToken ct)
    {
        var wait = tab.ExpectNavigation();
        try { start(); }
        catch { tab.ResetLoading(); throw; }
        var result = await WithTimeout(wait, timeout, ct, what);
        CheckResult(result, what);
        // Follow client-side redirects that start right after load.
        await WaitForPageLoadAsync(timeout, ct);
    }

    private static void CheckResult(NavigationResult r, string what)
    {
        if (r.Success) return;
        if (r.Status is CoreWebView2WebErrorStatus.OperationCanceled or CoreWebView2WebErrorStatus.ConnectionAborted) return; // superseded / download
        if (r.HttpStatusCode >= 400) return; // an HTTP error page is still a loaded page (iMacros behaves the same)
        throw new MacroRuntimeException($"{what} failed: {r.Status}");
    }

    public Task NavigateAsync(string url, TimeSpan timeout, CancellationToken ct)
    {
        var tab = Active;
        return NavigateCoreAsync(tab, () => tab.Core.Navigate(url), $"Loading {url}", timeout, ct);
    }

    public Task GoBackAsync(TimeSpan timeout, CancellationToken ct)
    {
        var tab = Active;
        if (!tab.Core.CanGoBack) throw new MacroRuntimeException("BACK: there is no previous page");
        return NavigateCoreAsync(tab, () => tab.Core.GoBack(), "BACK", timeout, ct);
    }

    public Task ReloadAsync(TimeSpan timeout, CancellationToken ct)
    {
        var tab = Active;
        return NavigateCoreAsync(tab, () => tab.Core.Reload(), "REFRESH", timeout, ct);
    }

    public async Task WaitForPageLoadAsync(TimeSpan timeout, CancellationToken ct)
    {
        var tab = Active;
        // Give a click-triggered navigation a moment to start.
        await Task.Delay(150, ct);
        var deadline = DateTime.UtcNow + timeout;
        while (tab.IsLoading)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) throw new TimeoutException($"Page did not finish loading within {timeout.TotalSeconds:0.#}s (!TIMEOUT_PAGE)");
            var r = await WithTimeout(tab.CurrentNavigation, remaining, ct, "Page load");
            CheckResult(r, "Page load");
            await Task.Delay(50, ct);
        }
    }

    public async Task<string> ExecuteScriptAsync(string script, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await Active.Core.ExecuteScriptAsync(script);
    }

    public async Task OpenTabAsync(CancellationToken ct) => await CreateTabAsync(null, activate: false);

    public Task SwitchTabAsync(int index, CancellationToken ct)
    {
        if (index < 0 || index >= Tabs.Count) throw new MacroRuntimeException($"Tab {index + 1} does not exist");
        Activate(index);
        return Task.CompletedTask;
    }

    public Task CloseTabAsync(CancellationToken ct)
    {
        CloseTab(CurrentTabIndex);
        return Task.CompletedTask;
    }

    public void CloseTab(int index)
    {
        if (Tabs.Count <= 1 || index < 0 || index >= Tabs.Count) return;
        var tab = Tabs[index];
        Tabs.RemoveAt(index);
        _host.Children.Remove(tab.View);
        tab.View.Dispose();
        var newIndex = CurrentTabIndex;
        if (index < CurrentTabIndex) newIndex--;
        else if (index == CurrentTabIndex) newIndex = Math.Max(0, index - 1);
        Activate(Math.Clamp(newIndex, 0, Tabs.Count - 1));
    }

    public Task CloseOtherTabsAsync(CancellationToken ct)
    {
        var keep = Active;
        foreach (var t in Tabs.Where(t => t != keep).ToList())
        {
            Tabs.Remove(t);
            _host.Children.Remove(t.View);
            t.View.Dispose();
        }
        Activate(0);
        return Task.CompletedTask;
    }

    public async Task<byte[]> CaptureScreenshotAsync(bool fullPage, ImageFormat format, CancellationToken ct)
    {
        var core = Active.Core;
        if (!fullPage)
        {
            using var ms = new MemoryStream();
            await core.CapturePreviewAsync(format == ImageFormat.Png ? CoreWebView2CapturePreviewImageFormat.Png : CoreWebView2CapturePreviewImageFormat.Jpeg, ms);
            return ms.ToArray();
        }

        // Full page via the DevTools protocol.
        var metricsJson = await core.CallDevToolsProtocolMethodAsync("Page.getLayoutMetrics", "{}");
        double width = 1280, height = 800;
        using (var metrics = JsonDocument.Parse(metricsJson))
        {
            var root = metrics.RootElement;
            if (root.TryGetProperty("cssContentSize", out var size) || root.TryGetProperty("contentSize", out size))
            {
                width = size.GetProperty("width").GetDouble();
                height = size.GetProperty("height").GetDouble();
            }
        }
        height = Math.Min(height, 16384);
        var param = JsonSerializer.Serialize(new
        {
            format = format == ImageFormat.Png ? "png" : "jpeg",
            captureBeyondViewport = true,
            clip = new { x = 0, y = 0, width = Math.Ceiling(width), height = Math.Ceiling(height), scale = 1 },
        });
        var resultJson = await core.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", param);
        using var result = JsonDocument.Parse(resultJson);
        return Convert.FromBase64String(result.RootElement.GetProperty("data").GetString() ?? "");
    }

    public async Task ClearBrowsingDataAsync(BrowsingDataKinds kinds, CancellationToken ct)
    {
        var k = kinds switch
        {
            BrowsingDataKinds.Cookies => CoreWebView2BrowsingDataKinds.Cookies,
            BrowsingDataKinds.Cache => CoreWebView2BrowsingDataKinds.DiskCache | CoreWebView2BrowsingDataKinds.CacheStorage,
            _ => CoreWebView2BrowsingDataKinds.AllSite | CoreWebView2BrowsingDataKinds.DiskCache,
        };
        await Active.Core.Profile.ClearBrowsingDataAsync(k);
    }

    public Task SetImageBlockingAsync(bool enabled, CancellationToken ct)
    {
        if (_blockImages == enabled) return Task.CompletedTask;
        _blockImages = enabled;
        foreach (var t in Tabs.Where(t => t.IsInitialized))
        {
            if (enabled) t.Core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Image);
            else t.Core.RemoveWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Image);
        }
        return Task.CompletedTask;
    }

    // ---- recorder support ----------------------------------------------------------------------------------

    public async Task StartRecordingAsync(string script)
    {
        _recorderScript = script;
        foreach (var t in Tabs.Where(t => t.IsInitialized)) await AddRecorderAsync(t);
    }

    private async Task AddRecorderAsync(BrowserTab tab)
    {
        if (_recorderScript is null || tab.RecorderScriptId is not null) return;
        tab.RecorderScriptId = await tab.Core.AddScriptToExecuteOnDocumentCreatedAsync(_recorderScript);
        await tab.Core.ExecuteScriptAsync(_recorderScript); // also attach to the page that is already loaded
    }

    public async Task StopRecordingAsync(string stopScript)
    {
        _recorderScript = null;
        foreach (var t in Tabs.Where(t => t.IsInitialized))
        {
            if (t.RecorderScriptId is not null)
            {
                t.Core.RemoveScriptToExecuteOnDocumentCreated(t.RecorderScriptId);
                t.RecorderScriptId = null;
            }
            try { await t.Core.ExecuteScriptAsync(stopScript); } catch (Exception) { /* page may be gone */ }
        }
    }
}
