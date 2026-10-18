using CleanSpace.Core.Models;
using Xunit;

namespace CleanSpace.Core.Tests;

public sealed class DriveOverviewTests
{
    [Theory]
    [InlineData(1000, 250, 250, 750, 75)]
    [InlineData(1000, 250, 100, 750, 75)]
    [InlineData(1000, 0, 0, 1000, 100)]
    [InlineData(1000, 1000, 1000, 0, 0)]
    [InlineData(3, 2, 1, 1, 33.33333333333333)]
    public void UsedSpaceAlwaysUsesVolumeFreeSpace(long total, long free, long available, long used, double percentage)
    {
        var overview = Create(total, free, available);
        Assert.Equal(used, overview.UsedBytes);
        Assert.Equal(percentage, overview.PercentageUsed!.Value, 10);
        Assert.Equal(available, overview.AvailableToUserBytes);
    }

    [Theory]
    [InlineData(-1L, 0L, 0L)]
    [InlineData(100L, -1L, 0L)]
    [InlineData(100L, 10L, -1L)]
    [InlineData(100L, 101L, 0L)]
    [InlineData(100L, 10L, 11L)]
    [InlineData(100L, null, 101L)]
    [InlineData(null, 10L, 11L)]
    public void InvalidMetricsAreRejected(long? total, long? free, long? available) =>
        Assert.Throws<ArgumentException>(() => Create(total, free, available));

    [Fact]
    public void ZeroCapacityHasNoPercentage()
    {
        var overview = Create(0, 0, 0);
        Assert.Equal(0, overview.UsedBytes);
        Assert.Null(overview.PercentageUsed);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(100L, null, 10L)]
    [InlineData(null, 10L, 5L)]
    public void MissingCapacityDoesNotCreateUsedSpaceOrPercentage(long? total, long? free, long? available)
    {
        var overview = Create(total, free, available);
        Assert.Null(overview.UsedBytes);
        Assert.Null(overview.PercentageUsed);
    }

    [Fact]
    public void LargeValuesAreNotTruncated()
    {
        var overview = Create(long.MaxValue, 0, 0);
        Assert.Equal(long.MaxValue, overview.UsedBytes);
        Assert.Equal(100, overview.PercentageUsed);
    }

    private static DriveOverview Create(long? total, long? free, long? available) =>
        new(@"Q:\", total, free, available, "NTFS", DateTimeOffset.UtcNow);
}
