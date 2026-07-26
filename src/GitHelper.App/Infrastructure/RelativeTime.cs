using System.Globalization;

namespace GitHelper.App.Infrastructure;

/// <summary>
/// Describes a timestamp in words. Takes "now" explicitly rather than reading the clock so
/// callers and tests are deterministic.
/// </summary>
public static class RelativeTime
{
    public static string Describe(DateTimeOffset when, DateTimeOffset now)
    {
        var elapsed = now - when;

        // Clock skew between machines can date a commit slightly in the future.
        if (elapsed < TimeSpan.FromMinutes(1)) return "just now";

        if (elapsed < TimeSpan.FromHours(1))
        {
            var minutes = (int)elapsed.TotalMinutes;
            return minutes == 1 ? "a minute ago" : $"{minutes} minutes ago";
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            var hours = (int)elapsed.TotalHours;
            return hours == 1 ? "an hour ago" : $"{hours} hours ago";
        }

        if (elapsed < TimeSpan.FromDays(30))
        {
            var days = (int)elapsed.TotalDays;
            return days == 1 ? "yesterday" : $"{days} days ago";
        }

        return when.ToString("d MMM yyyy", CultureInfo.CurrentCulture);
    }
}
