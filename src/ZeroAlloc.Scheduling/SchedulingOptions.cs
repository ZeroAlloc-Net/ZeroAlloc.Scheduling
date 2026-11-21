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

    /// <summary>
    /// How long a claimed job may stay Running before another poll may claim it again. Default: 15 minutes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A run that stops without recording an outcome, because the host stopped or crashed while the job
    /// ran, leaves its job Running. Once the job's start lies further back than this timeout, the next
    /// poll claims it again and runs it as a new attempt. When that attempt is past the job's maximum,
    /// the worker dead-letters the job instead of running it.
    /// </para>
    /// <para>
    /// A run that takes longer than this timeout is claimed again while it still runs, so the job runs
    /// twice. Set the timeout above the longest run you expect. The EF Core and ORM stores ignore the
    /// outcome of the run that lost its claim, and so does the Redis store when another host took the
    /// job over; the in-memory store records both outcomes.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or negative.</exception>
    public TimeSpan ClaimTimeout
    {
        get => _claimTimeout;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            _claimTimeout = value;
        }
    }

    private TimeSpan _claimTimeout = TimeSpan.FromMinutes(15);
}
