namespace ZeroAlloc.Scheduling;

/// <summary>Enqueues jobs for background execution.</summary>
public interface IScheduler
{
    /// <summary>Enqueues a fire-and-forget job to run as soon as possible.</summary>
    ValueTask EnqueueAsync<TJob>(TJob job, CancellationToken ct = default) where TJob : IJob;

    /// <summary>Enqueues a delayed job to run after <paramref name="delay"/>.</summary>
    ValueTask EnqueueAsync<TJob>(TJob job, TimeSpan delay, CancellationToken ct = default) where TJob : IJob;
}
