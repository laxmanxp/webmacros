using WebMacros.Engine.Runtime;

namespace WebMacros.Engine.Tests.Support;

public sealed class RecordingHost : IMacroHost
{
    public List<(int Line, string Text, int Loop)> Lines { get; } = new();
    public List<(LogLevel Level, string Message)> Logs { get; } = new();
    public List<string> Extracts { get; } = new();
    public List<string> ExtractPopups { get; } = new();
    public List<string> Messages { get; } = new();
    public Queue<string?> PromptAnswers { get; } = new();
    public List<string> Prompts { get; } = new();
    public int Pauses { get; private set; }
    public Queue<bool> ConfirmAnswers { get; } = new();
    public List<string> Confirms { get; } = new();
    public List<string> Statuses { get; } = new();
    public Action<int>? OnLineHook { get; set; }

    public void OnLine(int lineNumber, string text, int loop)
    {
        Lines.Add((lineNumber, text, loop));
        OnLineHook?.Invoke(lineNumber);
    }

    public void Log(LogLevel level, string message) => Logs.Add((level, message));
    public void OnExtract(string value) => Extracts.Add(value);

    public Task<string?> PromptAsync(string message, string? defaultValue, CancellationToken ct)
    {
        Prompts.Add(message);
        return Task.FromResult(PromptAnswers.Count > 0 ? PromptAnswers.Dequeue() : defaultValue);
    }

    public Task ShowMessageAsync(string message, CancellationToken ct) { Messages.Add(message); return Task.CompletedTask; }
    public Task PauseAsync(CancellationToken ct) { Pauses++; return Task.CompletedTask; }
    public Task ShowExtractAsync(string extract, CancellationToken ct) { ExtractPopups.Add(extract); return Task.CompletedTask; }

    public Task<bool> ConfirmAsync(string message, CancellationToken ct)
    {
        Confirms.Add(message);
        return Task.FromResult(ConfirmAnswers.Count == 0 || ConfirmAnswers.Dequeue());
    }

    public void ShowStatus(string message) => Statuses.Add(message);

    public IEnumerable<string> LogsAt(LogLevel level) => Logs.Where(l => l.Level == level).Select(l => l.Message);

    public IEnumerable<string> Warnings => Logs.Where(l => l.Level == LogLevel.Warning).Select(l => l.Message);
}
