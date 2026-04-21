namespace ZeroAlloc.Scheduling.Tests;

public sealed class EveryToCronTests
{
    [Theory]
    [InlineData(Every.Minute,         "* * * * *")]
    [InlineData(Every.FiveMinutes,    "*/5 * * * *")]
    [InlineData(Every.FifteenMinutes, "*/15 * * * *")]
    [InlineData(Every.ThirtyMinutes,  "*/30 * * * *")]
    [InlineData(Every.Hour,           "0 * * * *")]
    [InlineData(Every.SixHours,       "0 */6 * * *")]
    [InlineData(Every.TwelveHours,    "0 */12 * * *")]
    [InlineData(Every.Day,            "0 0 * * *")]
    [InlineData(Every.Week,           "0 0 * * 0")]
    public void Convert_KnownValue_ReturnsExpectedCron(Every every, string expected)
        => EveryToCron.Convert(every).Should().Be(expected);

    [Fact]
    public void Convert_UnknownValue_ThrowsArgumentOutOfRangeException()
        => Assert.Throws<ArgumentOutOfRangeException>(() => EveryToCron.Convert((Every)99));
}
