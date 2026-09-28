using System.Windows;
using WebMacros.Engine.Runtime;

namespace WebMacros.App;

/// <summary>Routes interpreter callbacks to the main window (the interpreter runs on the UI thread).</summary>
public sealed class WpfMacroHost : IMacroHost
{
    private readonly MainWindow _window;
    private TaskCompletionSource? _pause;

    public WpfMacroHost(MainWindow window) => _window = window;

    public bool IsPaused => _pause is { Task.IsCompleted: false };

    public void OnLine(int lineNumber, string text, int loop) => _window.ShowCurrentLine(lineNumber, text, loop);

    public void Log(LogLevel level, string message) => _window.AddLog(level, message);

    public void OnExtract(string value) { /* logged via Log(LogLevel.Extract) */ }

    public Task<string?> PromptAsync(string message, string? defaultValue, CancellationToken ct) =>
        Task.FromResult(InputDialog.Ask(_window, "WebMacros – PROMPT", message, defaultValue));

    public Task ShowMessageAsync(string message, CancellationToken ct)
    {
        MessageBox.Show(_window, message, "WebMacros", MessageBoxButton.OK, MessageBoxImage.Information);
        return Task.CompletedTask;
    }

    public async Task PauseAsync(CancellationToken ct)
    {
        _pause = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _window.SetPaused(true);
        try
        {
            await using var reg = ct.Register(() => _pause.TrySetCanceled(ct));
            await _pause.Task;
        }
        finally
        {
            _window.SetPaused(false);
        }
    }

    public void Resume() => _pause?.TrySetResult();

    public Task ShowExtractAsync(string extract, CancellationToken ct)
    {
        var text = extract.Replace(MacroState.ExtractSeparator, Environment.NewLine);
        MessageBox.Show(_window, text, "Extracted data (!EXTRACT_TEST_POPUP)", MessageBoxButton.OK, MessageBoxImage.Information);
        return Task.CompletedTask;
    }
}
