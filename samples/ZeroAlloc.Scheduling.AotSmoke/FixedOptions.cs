using System;
using Microsoft.Extensions.Options;

namespace ZeroAlloc.Scheduling.AotSmoke;

/// <summary>Options that never change, for a store built by hand.</summary>
internal sealed class FixedOptions(SchedulingOptions value) : IOptionsMonitor<SchedulingOptions>
{
    public SchedulingOptions CurrentValue => value;

    public SchedulingOptions Get(string? name) => value;

    public IDisposable? OnChange(Action<SchedulingOptions, string?> listener) => null;
}
