namespace ZeroAlloc.Scheduling.Tests;

/// <summary>
/// Runs <see cref="WorkerAllocationTests"/> after the parallel test classes, so the allocation
/// gate measures once tiered compilation can settle.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WorkerAllocationCollection
{
    public const string Name = "worker-allocation-non-parallel";
}
