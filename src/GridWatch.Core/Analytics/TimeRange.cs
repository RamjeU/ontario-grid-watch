namespace GridWatch.Core.Analytics;

/// <summary>A validated, half-open UTC time range [From, To).</summary>
public sealed record TimeRange(DateTime FromUtc, DateTime ToUtc)
{
    public static readonly TimeSpan MaxSpan = TimeSpan.FromDays(92);

    /// <summary>
    /// Validates query parameters. Missing values default to the <paramref name="defaultSpan"/>
    /// ending now; spans over <paramref name="maxSpan"/> (default <see cref="MaxSpan"/>) are rejected
    /// to bound query cost.
    /// </summary>
    public static bool TryCreate(
        DateTimeOffset? from,
        DateTimeOffset? to,
        TimeSpan defaultSpan,
        DateTime nowUtc,
        out TimeRange range,
        out string? error,
        TimeSpan? maxSpan = null)
    {
        var limit = maxSpan ?? MaxSpan;
        var end = to?.UtcDateTime ?? nowUtc;
        var start = from?.UtcDateTime ?? end - defaultSpan;
        range = new TimeRange(DateTime.SpecifyKind(start, DateTimeKind.Utc), DateTime.SpecifyKind(end, DateTimeKind.Utc));

        if (start >= end)
        {
            error = "'from' must be earlier than 'to'.";
            return false;
        }

        if (end - start > limit)
        {
            error = $"The range can be at most {limit.TotalDays:0} days.";
            return false;
        }

        error = null;
        return true;
    }
}
