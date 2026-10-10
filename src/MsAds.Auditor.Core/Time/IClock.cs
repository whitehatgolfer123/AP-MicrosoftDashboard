namespace MsAds.Auditor.Core.Time;

/// <summary>Abstracts "now" so period resolution is deterministic under test.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>A clock fixed at a supplied instant, for tests and reproducible reports.</summary>
public sealed class FixedClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; } = utcNow;
}
