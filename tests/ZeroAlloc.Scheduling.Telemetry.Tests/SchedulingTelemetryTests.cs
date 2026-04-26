using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Scheduling;

namespace ZeroAlloc.Scheduling.Telemetry.Tests;

public class SchedulingTelemetryTests
{
    [Fact]
    public async Task WithTelemetry_HotPathCall_StartsActivity()
    {
        using var listener = new TestActivityListener("ZeroAlloc.Scheduling");
        var fake = new FakeExecutor();

        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddScheduling();
        builder.Services.AddTransient<IJobTypeExecutor>(_ => fake);
        builder.WithTelemetry();

        var sp = services.BuildServiceProvider();
        var executor = sp.GetServices<IJobTypeExecutor>().First();

        await executor.ExecuteAsync(Array.Empty<byte>(), CreateContext(sp), CancellationToken.None);

        listener.StoppedActivities.Should().ContainSingle();
        listener.StoppedActivities[0].DisplayName.Should().Be("scheduling.job_execute");
    }

    [Fact]
    public async Task WithTelemetry_InnerThrows_RecordsErrorStatus()
    {
        using var listener = new TestActivityListener("ZeroAlloc.Scheduling");
        var fake = new ThrowingExecutor();

        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddScheduling();
        builder.Services.AddTransient<IJobTypeExecutor>(_ => fake);
        builder.WithTelemetry();

        var sp = services.BuildServiceProvider();
        var executor = sp.GetServices<IJobTypeExecutor>().First();

        Func<Task> act = async () =>
            await executor.ExecuteAsync(Array.Empty<byte>(), CreateContext(sp), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        listener.StoppedActivities.Should().ContainSingle();
        listener.StoppedActivities[0].Status.Should().Be(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task WithTelemetry_RecordsCounterAndHistogram()
    {
        long counterValue = 0;
        double histogramValue = 0;

        using var meterListener = new System.Diagnostics.Metrics.MeterListener();
        meterListener.InstrumentPublished = (instrument, l) =>
        {
            if (string.Equals(instrument.Meter.Name, "ZeroAlloc.Scheduling", StringComparison.Ordinal))
                l.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((inst, m, _, _) =>
        {
            if (string.Equals(inst.Name, "scheduling.jobs_total", StringComparison.Ordinal))
                Interlocked.Add(ref counterValue, m);
        });
        meterListener.SetMeasurementEventCallback<double>((inst, m, _, _) =>
        {
            if (string.Equals(inst.Name, "scheduling.job_duration_ms", StringComparison.Ordinal))
                histogramValue = m;
        });
        meterListener.Start();

        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddScheduling();
        builder.Services.AddTransient<IJobTypeExecutor>(_ => new FakeExecutor());
        builder.WithTelemetry();

        var sp = services.BuildServiceProvider();
        var executor = sp.GetServices<IJobTypeExecutor>().First();

        await executor.ExecuteAsync(Array.Empty<byte>(), CreateContext(sp), CancellationToken.None);

        counterValue.Should().Be(1);
        histogramValue.Should().BeGreaterThanOrEqualTo(0); // duration in ms; may be 0 on a no-op call
    }

    [Fact]
    public async Task WithTelemetry_TwoCalls_DoesNotDoubleWrap()
    {
        using var listener = new TestActivityListener("ZeroAlloc.Scheduling");
        var fake = new FakeExecutor();

        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddScheduling();
        builder.Services.AddTransient<IJobTypeExecutor>(_ => fake);
        builder.WithTelemetry().WithTelemetry();   // call twice

        var sp = services.BuildServiceProvider();
        var executor = sp.GetServices<IJobTypeExecutor>().First();

        await executor.ExecuteAsync(Array.Empty<byte>(), CreateContext(sp), CancellationToken.None);

        // Single span — not two nested spans from a doubly-wrapped proxy.
        listener.StoppedActivities.Should().ContainSingle();
    }

    private static JobContext CreateContext(IServiceProvider sp) => new JobContext
    {
        JobId = default,
        Attempt = 1,
        ScheduledAt = DateTimeOffset.UtcNow,
        Services = sp,
    };

    private sealed class FakeExecutor : IJobTypeExecutor
    {
        public string TypeName => "Fake";
        public int MaxAttempts => 0;
        public ValueTask ExecuteAsync(byte[] payload, JobContext ctx, CancellationToken ct) => default;
    }

    private sealed class ThrowingExecutor : IJobTypeExecutor
    {
        public string TypeName => "Throwing";
        public int MaxAttempts => 0;
        public ValueTask ExecuteAsync(byte[] payload, JobContext ctx, CancellationToken ct)
            => throw new InvalidOperationException("boom");
    }
}
