using System;

namespace ZeroAlloc.Scheduling.AotSmoke;

internal sealed class EmptyServiceProvider : IServiceProvider
{
    public object? GetService(Type serviceType) => null;
}
