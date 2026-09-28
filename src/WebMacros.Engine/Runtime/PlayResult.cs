namespace WebMacros.Engine.Runtime;

public enum PlayStatus { Completed, Failed, Stopped }

public sealed record PlayResult
{
    public PlayStatus Status { get; init; }
    public int LoopsCompleted { get; init; }
    public int? ErrorLine { get; init; }
    public string? ErrorMessage { get; init; }
    /// <summary>The !EXTRACT value at the end of each completed loop (empty strings omitted).</summary>
    public IReadOnlyList<string> Extracts { get; init; } = Array.Empty<string>();
    public TimeSpan Elapsed { get; init; }
    public bool Success => Status == PlayStatus.Completed;

    public override string ToString() => Status switch
    {
        PlayStatus.Completed => $"Completed {LoopsCompleted} loop(s) in {Elapsed.TotalSeconds:0.00}s",
        PlayStatus.Stopped => $"Stopped by user after {LoopsCompleted} loop(s)",
        _ => $"Error on line {ErrorLine}: {ErrorMessage}",
    };
}

/// <summary>Folders and tuning knobs for the interpreter.</summary>
public sealed record InterpreterOptions
{
    /// <summary>Folder of the running macro (relative datasource paths fall back to it).</summary>
    public string MacroFolder { get; init; } = Environment.CurrentDirectory;
    public string DataSourceFolder { get; init; } = Environment.CurrentDirectory;
    public string DownloadFolder { get; init; } = Environment.CurrentDirectory;
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(250);
    public double DefaultTimeoutStepSeconds { get; init; } = 6;
    public double DefaultTimeoutPageSeconds { get; init; } = 60;
    public string Version { get; init; } = "1.0.0";
}

/// <summary>Per-run settings, used by the scripting API (iimSet values for the next iimPlay).</summary>
public sealed record MacroRunOptions
{
    /// <summary>Variables applied (in order, with SET semantics) before the first line runs.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Variables { get; init; } = Array.Empty<KeyValuePair<string, string>>();
    /// <summary>Overrides the default of !EXTRACT_TEST_POPUP for this run (the macro can still SET it).</summary>
    public bool? ShowExtractPopup { get; init; }
}
