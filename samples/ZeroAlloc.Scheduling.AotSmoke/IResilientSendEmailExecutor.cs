using ZeroAlloc.Resilience;

namespace ZeroAlloc.Scheduling.AotSmoke;

// The ZeroAlloc.Resilience generator emits IResilientSendEmailExecutorResilienceProxy and
// AddResilientSendEmailExecutorResiliencePolicies() from this interface.
[Retry(MaxAttempts = 3, BackoffMs = 10)]
public interface IResilientSendEmailExecutor : IJobTypeExecutor;
