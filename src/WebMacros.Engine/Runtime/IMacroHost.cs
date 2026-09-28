namespace WebMacros.Engine.Runtime;

public enum LogLevel { Info, Extract, Warning, Error }

/// <summary>UI services used by the interpreter (log pane, dialogs, current-line indicator).</summary>
public interface IMacroHost
{
    /// <summary>Called before a line executes. <paramref name="lineNumber"/> is 1-based.</summary>
    void OnLine(int lineNumber, string text, int loop);
    void Log(LogLevel level, string message);
    void OnExtract(string value);

    /// <summary>Shows an input box. Returns null when the user cancels (stops the macro).</summary>
    Task<string?> PromptAsync(string message, string? defaultValue, CancellationToken ct);
    Task ShowMessageAsync(string message, CancellationToken ct);

    /// <summary>Completes when the user presses Resume.</summary>
    Task PauseAsync(CancellationToken ct);

    /// <summary>Shows the extracted data at the end of a loop when !EXTRACT_TEST_POPUP is YES.</summary>
    Task ShowExtractAsync(string extract, CancellationToken ct);
}

/// <summary>A host that ignores UI requests (prompts return the default value).</summary>
public class NullMacroHost : IMacroHost
{
    public virtual void OnLine(int lineNumber, string text, int loop) { }
    public virtual void Log(LogLevel level, string message) { }
    public virtual void OnExtract(string value) { }
    public virtual Task<string?> PromptAsync(string message, string? defaultValue, CancellationToken ct) => Task.FromResult<string?>(defaultValue ?? "");
    public virtual Task ShowMessageAsync(string message, CancellationToken ct) => Task.CompletedTask;
    public virtual Task PauseAsync(CancellationToken ct) => Task.CompletedTask;
    public virtual Task ShowExtractAsync(string extract, CancellationToken ct) => Task.CompletedTask;
}
