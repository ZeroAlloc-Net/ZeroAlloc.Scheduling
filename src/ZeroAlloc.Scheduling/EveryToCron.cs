namespace ZeroAlloc.Scheduling;

/// <summary>Converts <see cref="Every"/> enum values to cron expressions.</summary>
public static class EveryToCron
{
    public static string Convert(Every every) => every switch
    {
        Every.Minute         => "* * * * *",
        Every.FiveMinutes    => "*/5 * * * *",
        Every.FifteenMinutes => "*/15 * * * *",
        Every.ThirtyMinutes  => "*/30 * * * *",
        Every.Hour           => "0 * * * *",
        Every.SixHours       => "0 */6 * * *",
        Every.TwelveHours    => "0 */12 * * *",
        Every.Day            => "0 0 * * *",
        Every.Week           => "0 0 * * 0",
        _ => throw new ArgumentOutOfRangeException(nameof(every), every, null),
    };
}
