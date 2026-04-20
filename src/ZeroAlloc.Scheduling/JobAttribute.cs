namespace ZeroAlloc.Scheduling;

/// <summary>
/// Marks a class as a background job. The source generator emits a typed executor,
/// DI registration extension, and (for recurring jobs) a startup hosted service.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class JobAttribute : Attribute
{
    /// <summary>Maximum dispatch attempts before dead-lettering. 0 = use SchedulingOptions.DefaultMaxAttempts.</summary>
    public int MaxAttempts { get; set; }

    /// <summary>Predefined recurrence interval. Mutually exclusive with <see cref="Cron"/>.</summary>
    public Every Every { get; set; } = (Every)(-1); // sentinel: not set

    /// <summary>Cron expression for recurrence. Mutually exclusive with <see cref="Every"/>.</summary>
    public string? Cron { get; set; }
}
