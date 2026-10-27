using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TrackingScript.Helper;

public static class ParsingHelper
{
    public static long FileTime(string path)
    {
        var match = Regex.Match(
            Path.GetFileNameWithoutExtension(path),
            @"^([0-9]+)(?:\.([0-9]{1,9}))?(?:-[0-9]+)?$");

        if (!match.Success)
        {
            throw new ArgumentException(
                "Expected timestamp filename " +
                $"seconds.nanoseconds[-suffix].json: {path}");
        }

        var seconds = long.Parse(
            match.Groups[1].Value, CultureInfo.InvariantCulture);

        var nanoseconds = long.Parse(
            match.Groups[2].Value.PadRight(9, '0'),
            CultureInfo.InvariantCulture);

        return checked(seconds * TimeHelper.NanosecondsPerSecond + nanoseconds);
    }



    public static double ParseDouble(string value) =>
        double.Parse(value, CultureInfo.InvariantCulture);

    public static double ReadDouble(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
            ? ParseDouble(value.GetString()!)
            : value.GetDouble();

    public static long ReadLong(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
            ? long.Parse(value.GetString()!, CultureInfo.InvariantCulture)
            : checked((long)value.GetDecimal());

    public static string Text(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : value.ToString();

    public static Vector ReadVector(JsonElement value) => new(
        ReadDouble(value.GetProperty("x")),
        ReadDouble(value.GetProperty("y")));
}
