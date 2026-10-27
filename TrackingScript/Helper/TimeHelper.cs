using System.Globalization;

namespace TrackingScript.Helper;

public static class TimeHelper
{
    public const long NanosecondsPerSecond = 1_000_000_000;
    public static string Utc(long nanoseconds, bool shortFormat = false)
    {
        var seconds = Math.DivRem(
            nanoseconds, NanosecondsPerSecond, out var remainder);

        if (remainder < 0)
        {
            seconds--;
            remainder += NanosecondsPerSecond;
        }

        var timestamp = DateTimeOffset.FromUnixTimeSeconds(seconds);

        return shortFormat
            ? timestamp.ToString(
                "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                + $".{remainder / 1_000_000:000}"
            : timestamp.ToString(
                "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)
                + $".{remainder:000000000}Z";
    }

    public static string Duration(long nanoseconds)
    {
        var milliseconds = checked((long)Math.Round(nanoseconds / 1_000_000.0));
        var hours = milliseconds / 3_600_000;
        milliseconds %= 3_600_000;
        var minutes = milliseconds / 60_000;
        milliseconds %= 60_000;
        var seconds = milliseconds / 1_000;
        milliseconds %= 1_000;

        return $"{hours:00}:{minutes:00}:{seconds:00}.{milliseconds:000}";
    }

    public static int UpperBound(long[] values, long target)
    {
        var low = 0;
        var high = values.Length;

        while (low < high)
        {
            var middle = low + (high - low) / 2;

            if (values[middle] <= target)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    public static long ToNanoseconds(double seconds) =>
        checked((long)Math.Round(seconds * NanosecondsPerSecond));
}
