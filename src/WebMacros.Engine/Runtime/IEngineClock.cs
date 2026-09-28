namespace WebMacros.Engine.Runtime;

/// <summary>Time source for WAIT, retries and STOPWATCH (replaceable in tests).</summary>
public interface IEngineClock
{
    DateTimeOffset Now { get; }
    Task Delay(TimeSpan duration, CancellationToken ct);
}

public sealed class SystemClock : IEngineClock
{
    public static readonly SystemClock Instance = new();
    public DateTimeOffset Now => DateTimeOffset.Now;
    public Task Delay(TimeSpan duration, CancellationToken ct) =>
        duration <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(duration, ct);
}
