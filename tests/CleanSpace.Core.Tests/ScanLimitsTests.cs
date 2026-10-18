using CleanSpace.Core.Models;
using Xunit;

namespace CleanSpace.Core.Tests;

public sealed class ScanLimitsTests
{
    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(100001, 1, 1)]
    [InlineData(1, -1, 1)]
    [InlineData(1, 33, 1)]
    [InlineData(1, 1, 0)]
    [InlineData(1, 1, 31)]
    public void UnsupportedBudgetsAreRejected(int entries, int depth, int seconds) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScanLimits(entries, depth, TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void DefaultsAreBounded()
    {
        var limits = new ScanLimits();
        Assert.Equal(50_000, limits.MaxEntries);
        Assert.Equal(16, limits.MaxDepth);
        Assert.Equal(TimeSpan.FromSeconds(10), limits.MaxDuration);
    }
}
