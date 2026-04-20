namespace ZeroAlloc.Scheduling.Tests;

public sealed class DefaultJobSerializerTests
{
    private readonly IJobSerializer _sut = new DefaultJobSerializer();

    private sealed record TestJob(string Name, int Value);

    [Fact]
    public void RoundTrip_PreservesAllProperties()
    {
        var job = new TestJob("hello", 42);

        var bytes = _sut.Serialize(job);
        var result = _sut.Deserialize<TestJob>(bytes);

        result.Name.Should().Be("hello");
        result.Value.Should().Be(42);
    }

    [Fact]
    public void Serialize_ProducesNonEmptyBytes()
    {
        var bytes = _sut.Serialize(new TestJob("x", 1));
        bytes.Should().NotBeEmpty();
    }
}
