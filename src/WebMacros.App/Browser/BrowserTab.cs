using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace WebMacros.App.Browser;

/// <summary>Outcome of a top-level navigation.</summary>
public sealed record NavigationResult(bool Success, CoreWebView2WebErrorStatus Status, int HttpStatusCode);

/// <summary>One browser tab: a WebView2 control plus page-load tracking used by the driver.</summary>
public sealed class BrowserTab : INotifyPropertyChanged
{
    private TaskCompletionSource<NavigationResult>? _nav;
    private ulong _currentNavigationId;
    private bool _navigationStartedSinceExpect;
    private string _title = "New tab";
    private string _url = "about:blank";

    public BrowserTab(WebView2 view) => View = view;

    public WebView2 View { get; }
    public CoreWebView2 Core => View.CoreWebView2 ?? throw new InvalidOperationException("Browser tab is not initialized yet");
    public bool IsInitialized => View.CoreWebView2 is not null;
    public bool IsLoading { get; private set; }
    public string? RecorderScriptId { get; set; }

    public string Title
    {
        get => _title;
        private set { _title = value; OnPropertyChanged(); OnPropertyChanged(nameof(Header)); }
    }

    public string Url
    {
        get => _url;
        private set { _url = value; OnPropertyChanged(); }
    }

    public string Header => Title.Length > 28 ? Title[..28] + "…" : Title;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Hooks the load-tracking events. Call once after EnsureCoreWebView2Async.</summary>
    public void Attach()
    {
        var core = Core;
        core.NavigationStarting += (_, e) =>
        {
            IsLoading = true;
            _currentNavigationId = e.NavigationId;
            _navigationStartedSinceExpect = true;
            if (_nav is null || _nav.Task.IsCompleted)
                _nav = new TaskCompletionSource<NavigationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        };
        core.NavigationCompleted += (_, e) =>
        {
            if (e.NavigationId != _currentNavigationId) return; // a superseded navigation
            IsLoading = false;
            _nav?.TrySetResult(new NavigationResult(e.IsSuccess, e.WebErrorStatus, e.HttpStatusCode));
        };
        core.SourceChanged += (_, e) =>
        {
            Url = core.Source;
            // Same-document (#fragment / history.pushState) navigations raise no NavigationStarting/Completed.
            if (!e.IsNewDocument && _nav is { Task.IsCompleted: false } && !_navigationStartedSinceExpect)
            {
                IsLoading = false;
                _nav.TrySetResult(new NavigationResult(true, CoreWebView2WebErrorStatus.Unknown, 200));
            }
        };
        core.DocumentTitleChanged += (_, _) => Title = string.IsNullOrWhiteSpace(core.DocumentTitle) ? core.Source : core.DocumentTitle;
    }

    /// <summary>Prepares to await a navigation we are about to start ourselves.</summary>
    public Task<NavigationResult> ExpectNavigation()
    {
        _nav = new TaskCompletionSource<NavigationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _navigationStartedSinceExpect = false;
        IsLoading = true;
        return _nav.Task;
    }

    /// <summary>Clears the loading state when starting a navigation failed synchronously.</summary>
    public void ResetLoading()
    {
        IsLoading = false;
        _nav?.TrySetResult(new NavigationResult(false, CoreWebView2WebErrorStatus.Unknown, 0));
    }

    /// <summary>The pending navigation task, or a completed one if the page is idle.</summary>
    public Task<NavigationResult> CurrentNavigation =>
        IsLoading && _nav is not null ? _nav.Task : Task.FromResult(new NavigationResult(true, CoreWebView2WebErrorStatus.Unknown, 200));
}
