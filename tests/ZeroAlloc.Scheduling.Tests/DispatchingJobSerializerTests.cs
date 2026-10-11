using System.Buffers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;
using ZeroAlloc.Mediator;
using ZeroAlloc.Scheduling.Mediator;
using ZeroAlloc.Serialisation;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.Scheduling.Tests;

/// <summary>
/// Scheduling#386: <see cref="DispatchingJobSerializer"/> writes the payload into a reused buffer
/// writer and copies it once into the exact-size <c>byte[]</c> the store persists. Enqueueing a job
/// therefore allocates that array and nothing else on top of the store, for a class and a struct job.
/// Scheduling#394: reading a payload back goes through
/// <see cref="ISerializerDispatcher.Deserialize{T}(ReadOnlyMemory{byte})"/>, so a struct job is not boxed.
/// </summary>
public sealed class DispatchingJobSerializerTests
{
    private const int Calls = 1_000;

    private static byte[]? _arraySink;
    private static PingValue _valueSink;
    private static PingValueRequest _requestSink;

    private readonly ITestOutputHelper _output;

    public DispatchingJobSerializerTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Serialize_ClassJob_IsByteIdenticalToTheDispatcherOutput()
    {
        var dispatcher = new SerializerDispatcher();
        var job = new Ping("ping");

        var payload = new DispatchingJobSerializer(dispatcher).Serialize(job);

        payload.Should().Equal(dispatcher.Serialize(job, typeof(Ping)).ToArray());
        Encoding.UTF8.GetString(payload).Should().Be("""{"Message":"ping"}""");
    }

    [Fact]
    public void Serialize_StructJob_IsByteIdenticalToTheDispatcherOutput()
    {
        var dispatcher = new SerializerDispatcher();
        var job = new PingValue(7, 42);

        var payload = new DispatchingJobSerializer(dispatcher).Serialize(job);

        payload.Should().Equal(dispatcher.Serialize(job, typeof(PingValue)).ToArray());
        Encoding.UTF8.GetString(payload).Should().Be("""{"Sequence":7,"Ticks":42}""");
    }

    [Fact]
    public void Serialize_RoundTripsThroughDeserialize()
    {
        var serializer = new DispatchingJobSerializer(new SerializerDispatcher());

        serializer.Deserialize<Ping>(serializer.Serialize(new Ping("a"))).Should().Be(new Ping("a"));
        serializer.Deserialize<PingValue>(serializer.Serialize(new PingValue(3, 4))).Should().Be(new PingValue(3, 4));
    }

    [Fact]
    public void Serialize_ReturnsAFreshArrayPerCall()
    {
        var serializer = new DispatchingJobSerializer(new SerializerDispatcher());

        var first = serializer.Serialize(new Ping("first"));
        var second = serializer.Serialize(new Ping("second"));

        first.Should().NotBeSameAs(second);
        Encoding.UTF8.GetString(first).Should().Be("""{"Message":"first"}""");
        Encoding.UTF8.GetString(second).Should().Be("""{"Message":"second"}""");
    }

    [Fact]
    public void Serialize_AfterTheDispatcherThrowsMidWrite_DoesNotLeakTheFailedBytes()
    {
        var dispatcher = new ScriptedDispatcher();
        var serializer = new DispatchingJobSerializer(dispatcher);

        dispatcher.Next = writer =>
        {
            Write(writer, "partial");
            throw new InvalidOperationException("boom");
        };
        var failing = () => serializer.Serialize(new Ping("x"));
        failing.Should().Throw<InvalidOperationException>().WithMessage("boom");

        dispatcher.Next = writer => Write(writer, "ok");
        Encoding.UTF8.GetString(serializer.Serialize(new Ping("x"))).Should().Be("ok");
    }

    [Fact]
    public void Serialize_CalledReentrantlyFromTheDispatcher_KeepsBothPayloadsApart()
    {
        var dispatcher = new ScriptedDispatcher();
        var serializer = new DispatchingJobSerializer(dispatcher);
        byte[]? inner = null;

        dispatcher.Next = writer =>
        {
            Write(writer, "outer-");
            dispatcher.Next = w => Write(w, "inner");
            inner = serializer.Serialize(new Ping("inner"));
            Write(writer, "end");
        };

        var outer = serializer.Serialize(new Ping("outer"));

        Encoding.UTF8.GetString(inner!).Should().Be("inner");
        Encoding.UTF8.GetString(outer).Should().Be("outer-end");
    }

    [Fact]
    public void Serialize_AfterALargePayload_StillProducesExactSmallPayloads()
    {
        var dispatcher = new ScriptedDispatcher();
        var serializer = new DispatchingJobSerializer(dispatcher);
        var large = new string('x', 1 << 20);

        dispatcher.Next = writer => Write(writer, large);
        serializer.Serialize(new Ping("large")).Should().HaveCount(1 << 20);

        dispatcher.Next = writer => Write(writer, "small");
        Encoding.UTF8.GetString(serializer.Serialize(new Ping("small"))).Should().Be("small");
    }

    [Fact]
    public Task EnqueueAsync_ClassJob_AllocatesOnlyThePayloadArray()
        => AssertEnqueueAllocatesOnlyThePayloadAsync(new Ping("ping"));

    [Fact]
    public Task EnqueueAsync_StructJob_AllocatesOnlyThePayloadArray()
        => AssertEnqueueAllocatesOnlyThePayloadAsync(new PingValue(7, 42));

    private async Task AssertEnqueueAllocatesOnlyThePayloadAsync<TJob>(TJob job) where TJob : IJob
    {
        var store = new DiscardingStore();
        var scheduler = new DefaultScheduler(
            store,
            new DispatchingJobSerializer(new SerializerDispatcher()),
            new StaticOptionsMonitor(new SchedulingOptions()),
            []);

        // Warm up: tiered JIT, and the thread's cached Utf8JsonWriter and buffer writer.
        for (int i = 0; i < 10; i++)
            await scheduler.EnqueueAsync(job).ConfigureAwait(false);
        int payloadLength = store.LastPayloadLength;
        long arrayBytes = MeasureArray(payloadLength);

        bool allSynchronous = true;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Calls; i++)
            allSynchronous &= scheduler.EnqueueAsync(job).IsCompletedSuccessfully;
        long total = GC.GetAllocatedBytesForCurrentThread() - before;
        // Whole bytes per call. A one-off allocation during the loop, such as the runtime promoting a
        // method to tier 1 part way through, adds well under a byte per call and rounds away, while
        // any allocation made on every call adds at least one object header to each of them.
        long perCall = total / Calls;

        _output.WriteLine($"{typeof(TJob).Name}: {total} B over {Calls} enqueues, {perCall} B each; payload byte[{payloadLength}] is {arrayBytes} B");

        allSynchronous.Should().BeTrue("the measurement is only valid when every call stays on this thread");
        perCall.Should().Be(arrayBytes,
            "enqueueing allocates the exact-size payload array the store persists, and nothing else");
    }

    [Fact]
    public void Deserialize_StructJob_AllocatesOnlyWhatSystemTextJsonDoes()
    {
        var serializer = new DispatchingJobSerializer(new SerializerDispatcher());
        var payload = serializer.Serialize(new PingValue(7, 42));

        long perCall = AllocationGate.MeasureBytesPerCall(
            Calls, () => _valueSink = serializer.Deserialize<PingValue>(payload));
        long formatBytes = MeasureSystemTextJsonRead(payload);

        _output.WriteLine($"Deserialize<PingValue>: {perCall} B per call; System.Text.Json alone: {formatBytes} B");
        _valueSink.Should().Be(new PingValue(7, 42));
        // Exactly the format's own cost: System.Text.Json reads a record struct through its constructor,
        // and that path allocates the argument state it collects the constructor arguments in. The
        // dispatcher and DispatchingJobSerializer add nothing on top; before #394 the dispatcher boxed
        // the struct, 32 B more.
        perCall.Should().Be(formatBytes);
    }

    [Fact]
    public void MediatorExecutor_StructJob_AllocatesOnlyWhatSystemTextJsonDoes()
    {
        var serializer = new DispatchingJobSerializer(new SerializerDispatcher());
        var handler = new CountingHandler();
        var executor = new MediatorJobTypeExecutor<PingValueRequest>(serializer, handler);
        var payload = serializer.Serialize(new PingValueRequest(7, 42));
        var ctx = new JobContext
        {
            JobId = JobId.New(),
            Attempt = 1,
            ScheduledAt = DateTimeOffset.UnixEpoch,
            Services = new ServiceCollection().BuildServiceProvider(),
        };

        bool allSynchronous = true;
        long perCall = AllocationGate.MeasureBytesPerCall(
            Calls,
            () => allSynchronous &= executor.ExecuteAsync(payload, ctx, CancellationToken.None).IsCompletedSuccessfully);
        long formatBytes = AllocationGate.MeasureBytesPerCall(
            Calls,
            () => _requestSink = JsonSerializer.Deserialize(payload, PingJsonContext.Default.PingValueRequest));

        _output.WriteLine($"MediatorJobTypeExecutor<PingValueRequest>: {perCall} B per job; System.Text.Json alone: {formatBytes} B");
        allSynchronous.Should().BeTrue("the measurement is only valid when every call stays on this thread");
        handler.Last.Should().Be(new PingValueRequest(7, 42));
        // The worker's whole per-job read path for a struct job: deserialize the payload and hand it to the
        // handler. Only System.Text.Json allocates, as above: the job is not boxed, and the synchronous
        // handler leaves no state machine behind.
        perCall.Should().Be(formatBytes);
    }

    private static long MeasureSystemTextJsonRead(byte[] payload)
        => AllocationGate.MeasureBytesPerCall(
            Calls, () => _valueSink = JsonSerializer.Deserialize(payload, PingJsonContext.Default.PingValue));

    [Fact]
    public void Deserialize_ClassJob_DispatcherReturnsNull_Throws()
    {
        var serializer = new DispatchingJobSerializer(new FixedResultDispatcher(null));

        var act = () => serializer.Deserialize<Ping>([1]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Deserializing Ping returned null or wrong type.");
    }

    [Fact]
    public void Deserialize_ClassJob_DispatcherReturnsWrongType_Throws()
    {
        var serializer = new DispatchingJobSerializer(new FixedResultDispatcher("not a ping"));

        var act = () => serializer.Deserialize<Ping>([1]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Deserializing Ping returned null or wrong type.")
            .WithInnerException<InvalidCastException>();
    }

    [Fact]
    public void Deserialize_StructJob_DispatcherReturnsWrongType_Throws()
    {
        var serializer = new DispatchingJobSerializer(new FixedResultDispatcher(new Ping("x")));

        var act = () => serializer.Deserialize<PingValue>([1]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Deserializing PingValue returned null or wrong type.")
            .WithInnerException<InvalidCastException>();
    }

    [Fact]
    public void Deserialize_GenericOverloadReturnsNull_Throws()
    {
        var serializer = new DispatchingJobSerializer(new NullGenericDispatcher());

        var act = () => serializer.Deserialize<Ping>([1]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Deserializing Ping returned null or wrong type.");
    }

    private static long MeasureArray(int length)
    {
        _arraySink = new byte[length];
        long before = GC.GetAllocatedBytesForCurrentThread();
        _arraySink = new byte[length];
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static void Write(IBufferWriter<byte> writer, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        bytes.CopyTo(writer.GetSpan(bytes.Length));
        writer.Advance(bytes.Length);
    }

    /// <summary>A dispatcher whose generic writer overload runs whatever the test scripted next.</summary>
    private sealed class ScriptedDispatcher : ISerializerDispatcher
    {
        public Action<IBufferWriter<byte>> Next { get; set; } = _ => { };

        public ReadOnlyMemory<byte> Serialize(object value, Type type)
        {
            var writer = new ArrayBufferWriter<byte>();
            Next(writer);
            return writer.WrittenMemory;
        }

        public void Serialize(IBufferWriter<byte> writer, object value, Type type) => Next(writer);

        public void Serialize<T>(IBufferWriter<byte> writer, T value) => Next(writer);

        public object? Deserialize(ReadOnlyMemory<byte> data, Type type) => throw new NotSupportedException();
    }

    /// <summary>
    /// A hand-written dispatcher that implements only the object-returning read, so the generic read uses
    /// the interface's default implementation, which unboxes this result.
    /// </summary>
    private sealed class FixedResultDispatcher(object? result) : ISerializerDispatcher
    {
        public ReadOnlyMemory<byte> Serialize(object value, Type type) => throw new NotSupportedException();

        public object? Deserialize(ReadOnlyMemory<byte> data, Type type) => result;
    }

    /// <summary>A dispatcher whose generic read returns null, as a generated one does when the serializer does.</summary>
    private sealed class NullGenericDispatcher : ISerializerDispatcher
    {
        public ReadOnlyMemory<byte> Serialize(object value, Type type) => throw new NotSupportedException();

        public object? Deserialize(ReadOnlyMemory<byte> data, Type type) => throw new NotSupportedException();

        public T? Deserialize<T>(ReadOnlyMemory<byte> data) => default;
    }

    private sealed class CountingHandler : IRequestHandler<PingValueRequest, Unit>
    {
        public PingValueRequest Last { get; private set; }

        public ValueTask<Unit> Handle(PingValueRequest request, CancellationToken ct)
        {
            Last = request;
            return ValueTask.FromResult(Unit.Value);
        }
    }

    /// <summary>A store that keeps nothing, so the measurement is the scheduler's and the serializer's.</summary>
    private sealed class DiscardingStore : IJobStore
    {
        public int LastPayloadLength { get; private set; }

        public ValueTask EnqueueAsync(string typeName, byte[] payload, DateTimeOffset scheduledAt, int maxAttempts, string? cronExpression, CancellationToken ct)
        {
            LastPayloadLength = payload.Length;
            return default;
        }

        public ValueTask<IReadOnlyList<JobEntry>> FetchPendingAsync(int batchSize, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask MarkSucceededAsync(JobId id, DateTimeOffset? nextRunAt, string? cronExpression, int maxAttempts, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask MarkFailedAsync(JobId id, int attempts, DateTimeOffset nextRetryAt, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask DeadLetterAsync(JobId id, string error, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask UpsertRecurringAsync(string typeName, byte[] payload, DateTimeOffset scheduledAt, string? cronExpression, int maxAttempts, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class StaticOptionsMonitor(SchedulingOptions value) : IOptionsMonitor<SchedulingOptions>
    {
        public SchedulingOptions CurrentValue => value;
        public SchedulingOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<SchedulingOptions, string?> listener) => null;
    }
}
