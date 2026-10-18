using System.ComponentModel;
using System.Security;
using CleanSpace.Application.Abstractions;
using CleanSpace.Application.DTOs;
using CleanSpace.Application.UseCases;
using Xunit;

namespace CleanSpace.Application.Tests;

public sealed class DriveOverviewUseCaseTests
{
    [Fact]
    public async Task QuotaDoesNotReduceVolumeCapacityOrInflateUsedSpace()
    {
        var provider = new FakeDriveProvider();
        var result = await new DriveOverviewUseCase(provider).ExecuteAsync();
        Assert.Null(result.Error);
        Assert.Equal(@"Q:\", result.Overview!.VolumeRoot);
        Assert.Equal(1000, result.Overview.TotalBytes);
        Assert.Equal(250, result.Overview.TotalFreeBytes);
        Assert.Equal(100, result.Overview.AvailableToUserBytes);
        Assert.Equal(750, result.Overview.UsedBytes);
        Assert.Equal(75, result.Overview.PercentageUsed);
        Assert.Equal(1, provider.Calls);
    }

    [Theory]
    [InlineData("access")]
    [InlineData("io")]
    [InlineData("not-ready")]
    [InlineData("missing")]
    [InlineData("security")]
    [InlineData("unsupported")]
    [InlineData("overflow")]
    public async Task ProviderFailuresReturnRedactedRetryableErrors(string failure)
    {
        var provider = new FakeDriveProvider { Failure = failure switch
        {
            "access" => new UnauthorizedAccessException("private-path"),
            "io" => new IOException("private-path"),
            "not-ready" => new Win32Exception(21, "private-path"),
            "missing" => new DriveNotFoundException("private-path"),
            "security" => new SecurityException("private-path"),
            "unsupported" => new NotSupportedException("private-path"),
            _ => new OverflowException("private-path")
        }};
        var result = await new DriveOverviewUseCase(provider).ExecuteAsync();
        Assert.Null(result.Overview);
        Assert.Contains("Refresh", result.Error);
        Assert.DoesNotContain("private-path", result.Error);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(100, 101, 0)]
    [InlineData(100, 10, 11)]
    public async Task BadProviderMetricsFailClosed(long total, long free, long available)
    {
        var provider = new FakeDriveProvider { Value = new(@"Q:\", total, free, available, "NTFS", DateTimeOffset.UtcNow) };
        var result = await new DriveOverviewUseCase(provider).ExecuteAsync();
        Assert.Null(result.Overview);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData(@"Q:\private-path\")]
    [InlineData(@"Q:")]
    [InlineData("")]
    [InlineData(@"\\server\private-path\")]
    public async Task UncertainIdentityIsNotDisplayed(string root)
    {
        var result = await new DriveOverviewUseCase(new FakeDriveProvider { Value = new(root, 100, 25, 25, "NTFS", DateTimeOffset.UtcNow) }).ExecuteAsync();
        Assert.Null(result.Overview);
        Assert.DoesNotContain("private-path", result.Error);
    }

    [Fact]
    public async Task OpaqueVolumeIdentifierIsSupportedAndUnsafeFilesystemLabelIsHidden()
    {
        const string root = @"\\?\Volume{12345678-1234-1234-1234-123456789abc}\";
        var result = await new DriveOverviewUseCase(new FakeDriveProvider { Value = new(root, null, null, null, @"private-path\name", DateTimeOffset.UtcNow) }).ExecuteAsync();
        Assert.Null(result.Error);
        Assert.Equal(root, result.Overview!.VolumeRoot);
        Assert.Null(result.Overview.FileSystem);
        Assert.Null(result.Overview.PercentageUsed);
    }

    [Fact]
    public async Task PreCancelledRequestNeverCallsProvider()
    {
        var provider = new FakeDriveProvider();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DriveOverviewUseCase(provider).ExecuteAsync(new CancellationToken(true)));
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task CancellationAfterProviderReturnsDiscardsSnapshot()
    {
        using var cancellation = new CancellationTokenSource();
        var provider = new FakeDriveProvider { BeforeReturn = cancellation.Cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DriveOverviewUseCase(provider).ExecuteAsync(cancellation.Token));
    }

    private sealed class FakeDriveProvider : IDriveInfoProvider
    {
        public DriveInfoSnapshot Value { get; init; } = new(@"Q:\", 1000, 250, 100, "NTFS", DateTimeOffset.UtcNow);
        public Exception? Failure { get; init; }
        public Action? BeforeReturn { get; init; }
        public int Calls { get; private set; }
        public Task<DriveInfoSnapshot> GetSystemDriveAsync(CancellationToken cancellationToken)
        {
            Calls++;
            BeforeReturn?.Invoke();
            return Failure is null ? Task.FromResult(Value) : Task.FromException<DriveInfoSnapshot>(Failure);
        }
    }
}
