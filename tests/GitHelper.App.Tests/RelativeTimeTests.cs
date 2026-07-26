using GitHelper.App.Infrastructure;

namespace GitHelper.App.Tests;

public class RelativeTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(30, "just now")]
    [InlineData(60, "a minute ago")]
    [InlineData(120, "2 minutes ago")]
    [InlineData(3600, "an hour ago")]
    [InlineData(7200, "2 hours ago")]
    [InlineData(86400, "yesterday")]
    [InlineData(172800, "2 days ago")]
    public void Describe_UsesPlainEnglishForRecentTimes(int secondsAgo, string expected)
    {
        var when = Now.AddSeconds(-secondsAgo);

        Assert.Equal(expected, RelativeTime.Describe(when, Now));
    }

    [Fact]
    public void Describe_FallsBackToADateBeyondAMonth()
    {
        var when = Now.AddDays(-90);

        var described = RelativeTime.Describe(when, Now);

        // Well past "N days ago" territory, so it should read as a date, not a duration.
        Assert.DoesNotContain("ago", described);
        Assert.Contains("2026", described);
    }

    [Fact]
    public void Describe_TreatsAFutureTimestampAsJustNowRatherThanNegative()
    {
        // Clock skew between machines can produce commits dated slightly ahead.
        var when = Now.AddMinutes(5);

        Assert.Equal("just now", RelativeTime.Describe(when, Now));
    }
}
