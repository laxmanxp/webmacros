namespace WebMacros.Engine.Runtime;

public enum DialogKind { Alert, Confirm, Prompt, BeforeUnload }

/// <summary>A JavaScript dialog (alert/confirm/prompt/beforeunload) raised by the page.</summary>
public sealed record BrowserDialog(DialogKind Kind, string Message, string? DefaultText);

/// <summary>How to answer a dialog. <see cref="Accept"/> = OK/YES; <see cref="Text"/> is the prompt() result.</summary>
public sealed record DialogDecision(bool Accept, string? Text);

[Flags]
public enum BrowsingDataKinds { None = 0, Cookies = 1, Cache = 2, All = Cookies | Cache | 4 }

public enum ImageFormat { Png, Jpeg }

/// <summary>
/// Everything the engine needs from a browser. The WPF app implements this on top of WebView2;
/// tests implement it with an in-memory DOM. All tab indexes are 0-based.
/// </summary>
public interface IBrowserDriver
{
    string CurrentUrl { get; }
    int TabCount { get; }
    int CurrentTabIndex { get; }

    /// <summary>Navigates the current tab and waits until the page finished loading.</summary>
    Task NavigateAsync(string url, TimeSpan timeout, CancellationToken ct);
    Task GoBackAsync(TimeSpan timeout, CancellationToken ct);
    Task ReloadAsync(TimeSpan timeout, CancellationToken ct);

    /// <summary>Called after actions that may trigger navigation (clicks, key presses). Waits for any pending load.</summary>
    Task WaitForPageLoadAsync(TimeSpan timeout, CancellationToken ct);

    /// <summary>
    /// Runs a script in the current tab's top document and returns the JSON encoding of its completion value
    /// (same contract as CoreWebView2.ExecuteScriptAsync).
    /// </summary>
    Task<string> ExecuteScriptAsync(string script, CancellationToken ct);

    /// <summary>Opens a new tab without switching to it (like iMacros TAB OPEN).</summary>
    Task OpenTabAsync(CancellationToken ct);
    Task SwitchTabAsync(int index, CancellationToken ct);
    Task CloseTabAsync(CancellationToken ct);
    Task CloseOtherTabsAsync(CancellationToken ct);

    Task<byte[]> CaptureScreenshotAsync(bool fullPage, ImageFormat format, CancellationToken ct);
    Task ClearBrowsingDataAsync(BrowsingDataKinds kinds, CancellationToken ct);
    Task SetImageBlockingAsync(bool enabled, CancellationToken ct);

    /// <summary>
    /// Set by the engine while a macro runs. The driver calls it synchronously for each JS dialog; a null result
    /// means "not handled by the macro" (the driver then shows the dialog to the user).
    /// </summary>
    Func<BrowserDialog, DialogDecision?>? DialogHandler { get; set; }
}
