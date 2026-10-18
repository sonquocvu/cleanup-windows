using System.Globalization;

namespace CleanSpace.App.Formatting;

public static class DriveValueFormatter
{
    private static readonly string[] Units = ["B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB"];

    public static string Bytes(long? bytes, IFormatProvider? culture = null)
    {
        if (bytes is null or < 0) { return "Unavailable"; }
        var value = (decimal)bytes.Value;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return value.ToString("0.##", culture ?? CultureInfo.CurrentCulture) + " " + Units[unit];
    }

    public static string ExactBytes(long? bytes) => bytes is null ? "Unavailable"
        : bytes.Value.ToString("N0", CultureInfo.CurrentCulture) + " bytes";

    public static string Percentage(double? percentage, IFormatProvider? culture = null) =>
        percentage is { } value && double.IsFinite(value) && value is >= 0 and <= 100
            ? value.ToString("0.0", culture ?? CultureInfo.CurrentCulture) + "% used" : "Usage unavailable";
}
