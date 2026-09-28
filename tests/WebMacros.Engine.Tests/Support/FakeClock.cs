using WebMacros.Engine.Runtime;

namespace WebMacros.Engine.Tests.Support;

/// <summary>Clock whose Delay advances time instantly.</summary>
public sealed class FakeClock : IEngineClock
{
    public DateTimeOffset Now { get; private set; } = new(2026, 9, 28, 16, 45, 7, TimeSpan.FromHours(5.5));
    public TimeSpan TotalDelayed { get; private set; }
    public List<TimeSpan> Delays { get; } = new();

    public Task Delay(TimeSpan duration, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Delays.Add(duration);
        TotalDelayed += duration;
        Now += duration;
        return Task.CompletedTask;
    }

    public void Advance(TimeSpan t) => Now += t;
}
