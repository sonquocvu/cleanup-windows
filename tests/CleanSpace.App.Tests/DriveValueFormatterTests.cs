using System.Globalization;
using CleanSpace.App.Formatting;
using Xunit;

namespace CleanSpace.App.Tests;

public sealed class DriveValueFormatterTests
{
    [Theory]
    [InlineData(null, "Unavailable")]
    [InlineData(-1L, "Unavailable")]
    [InlineData(0L, "0 B")]
    [InlineData(1L, "1 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(1024L, "1 KiB")]
    [InlineData(1536L, "1.5 KiB")]
    [InlineData(1048576L, "1 MiB")]
    [InlineData(1073741824L, "1 GiB")]
    [InlineData(1099511627776L, "1 TiB")]
    [InlineData(long.MaxValue, "8 EiB")]
    public void BinaryUnitsAreExplicitAndRounded(long? bytes, string expected) =>
        Assert.Equal(expected, DriveValueFormatter.Bytes(bytes, CultureInfo.InvariantCulture));

    [Theory]
    [InlineData(null, "Usage unavailable")]
    [InlineData(0d, "0.0% used")]
    [InlineData(100d, "100.0% used")]
    [InlineData(33.33333, "33.3% used")]
    [InlineData(-1d, "Usage unavailable")]
    [InlineData(double.NaN, "Usage unavailable")]
    [InlineData(double.PositiveInfinity, "Usage unavailable")]
    public void PercentageNeverInventsUnknownUsage(double? value, string expected) =>
        Assert.Equal(expected, DriveValueFormatter.Percentage(value, CultureInfo.InvariantCulture));

    [Fact]
    public void FormattingRespectsCulture() =>
        Assert.Equal("1,5 KiB", DriveValueFormatter.Bytes(1536, CultureInfo.GetCultureInfo("de-DE")));
}
