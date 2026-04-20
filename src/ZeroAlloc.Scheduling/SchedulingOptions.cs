namespace ZeroAlloc.Scheduling;

/// <summary>Configuration for the scheduling background worker.</summary>
public sealed class SchedulingOptions
{
    /// <summary>Delay between polling cycles. Default: 5 seconds.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Jobs claimed per polling cycle. Default: 20.</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>Base delay for exponential retry back-off. Default: 2 seconds.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Default max attempts when not specified on [Job]. Default: 3.</summary>
    public int DefaultMaxAttempts { get; set; } = 3;
}
