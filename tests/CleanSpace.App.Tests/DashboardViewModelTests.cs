using CleanSpace.App.ViewModels;
using CleanSpace.Application.Abstractions;
using CleanSpace.Application.DTOs;
using CleanSpace.Application.UseCases;
using Xunit;

namespace CleanSpace.App.Tests;

public sealed class DashboardViewModelTests
{
    [Fact]
    public async Task LoadingDisablesRefreshAndDuplicateRequestsAreIgnored()
    {
        var provider = new FakeProvider();
        using var viewModel = new DashboardViewModel(new(provider));
        var loading = viewModel.InitializeAsync();
        Assert.True(viewModel.IsLoading);
        Assert.False(viewModel.RefreshCommand.CanExecute(null));
        Assert.Null(viewModel.Overview);
        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(1, provider.Calls);
        provider.Complete(1000, 250, 100);
        await loading;
        Assert.False(viewModel.IsLoading);
        Assert.True(viewModel.RefreshCommand.CanExecute(null));
        Assert.True(viewModel.HasUsage);
        Assert.Equal(75, viewModel.UsageValue);
        Assert.Contains("lower", viewModel.AvailabilityNotice);
    }

    [Fact]
    public async Task RefreshReadsFreshDataAndClearsStaleValuesOnFailureThenRecovers()
    {
        var provider = new FakeProvider();
        using var viewModel = new DashboardViewModel(new(provider));
        var first = viewModel.InitializeAsync();
        provider.Complete(1000, 200, 200);
        await first;
        var second = viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.Null(viewModel.Overview);
        provider.Fail();
        await second;
        Assert.True(viewModel.HasError);
        Assert.False(viewModel.HasUsage);
        Assert.Equal("Unavailable", viewModel.Total);
        Assert.DoesNotContain("private-path", viewModel.ErrorMessage);
        var third = viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.False(viewModel.HasError);
        provider.Complete(2000, 1000, 500);
        await third;
        Assert.Equal(50, viewModel.UsageValue);
        Assert.Equal(3, provider.Calls);
    }

    [Theory]
    [InlineData(0L, 0L, 0L)]
    [InlineData(null, null, null)]
    [InlineData(100L, null, null)]
    public async Task UnknownUsageNeverShowsAZeroPercentBar(long? total, long? free, long? available)
    {
        var provider = new FakeProvider();
        using var viewModel = new DashboardViewModel(new(provider));
        var load = viewModel.InitializeAsync();
        provider.Complete(total, free, available);
        await load;
        Assert.False(viewModel.HasUsage);
        Assert.Equal("Usage unavailable", viewModel.Percentage);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task InitializeOnlyReadsOnceAndCancellationDoesNotBecomeAnError()
    {
        var provider = new FakeProvider();
        using var viewModel = new DashboardViewModel(new(provider));
        var first = viewModel.InitializeAsync();
        Assert.Same(first, viewModel.InitializeAsync());
        provider.Complete(100, 50, 50);
        await first;
        var refresh = viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.RefreshCommand.Cancel();
        await refresh;
        Assert.False(viewModel.IsLoading);
        Assert.False(viewModel.HasError);
        Assert.Null(viewModel.Overview);
    }

    [Fact]
    public async Task ClosingCancelsPendingRetrieval()
    {
        var provider = new FakeProvider();
        var viewModel = new DashboardViewModel(new(provider));
        var loading = viewModel.InitializeAsync();
        viewModel.Dispose();
        await loading;
        Assert.False(viewModel.IsLoading);
        Assert.False(viewModel.HasError);
        Assert.True(provider.LastToken.IsCancellationRequested);
    }

    [Fact]
    public async Task AccessibleMetricsUpdateAndClearWhenInvalidDataReplacesSnapshot()
    {
        var provider = new FakeProvider();
        using var viewModel = new DashboardViewModel(new(provider));
        var changed = new HashSet<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        var loading = viewModel.InitializeAsync();
        provider.Complete(100, 25, 10);
        await loading;
        Assert.Equal("Total capacity: 100 bytes", viewModel.TotalAccessible);
        Assert.Equal("Used space: 75 bytes", viewModel.UsedAccessible);
        Assert.Equal("Free space (volume): 25 bytes", viewModel.FreeAccessible);
        Assert.Equal("Available to you: 10 bytes", viewModel.AvailableAccessible);
        Assert.Contains(nameof(viewModel.TotalAccessible), changed);
        Assert.Contains(nameof(viewModel.UsedAccessible), changed);
        Assert.Contains(nameof(viewModel.FreeAccessible), changed);
        Assert.Contains(nameof(viewModel.AvailableAccessible), changed);

        var refresh = viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("Total capacity: Unavailable", viewModel.TotalAccessible);
        Assert.Equal("Used space: Unavailable", viewModel.UsedAccessible);
        Assert.Equal("Free space (volume): Unavailable", viewModel.FreeAccessible);
        Assert.Equal("Available to you: Unavailable", viewModel.AvailableAccessible);
        provider.Complete(100, 101, 0);
        await refresh;
        Assert.True(viewModel.HasError);
        Assert.Null(viewModel.Overview);
        Assert.False(viewModel.HasUsage);
        Assert.Equal("Total capacity: Unavailable", viewModel.TotalAccessible);

        var recovery = viewModel.RefreshCommand.ExecuteAsync(null);
        provider.Complete(0, 0, 0);
        await recovery;
        Assert.False(viewModel.HasError);
        Assert.Equal("Total capacity: 0 bytes", viewModel.TotalAccessible);
        Assert.Equal("Used space: 0 bytes", viewModel.UsedAccessible);
        Assert.Equal("Free space (volume): 0 bytes", viewModel.FreeAccessible);
        Assert.Equal("Available to you: 0 bytes", viewModel.AvailableAccessible);
        Assert.False(viewModel.HasUsage);
    }

    [Theory]
    [InlineData(100L, 0d)]
    [InlineData(0L, 100d)]
    public async Task PositiveCapacityKeepsValidEmptyAndFullUsageBars(long free, double percentage)
    {
        var provider = new FakeProvider();
        using var viewModel = new DashboardViewModel(new(provider));
        var loading = viewModel.InitializeAsync();
        provider.Complete(100, free, free);
        await loading;
        Assert.True(viewModel.HasUsage);
        Assert.Equal(percentage, viewModel.UsageValue);
        Assert.False(viewModel.HasError);
    }

    private sealed class FakeProvider : IDriveInfoProvider
    {
        private TaskCompletionSource<DriveInfoSnapshot> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public Task<DriveInfoSnapshot> GetSystemDriveAsync(CancellationToken cancellationToken)
        {
            Calls++;
            LastToken = cancellationToken;
            pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return pending.Task.WaitAsync(cancellationToken);
        }
        public void Complete(long? total, long? free, long? available) =>
            pending.SetResult(new(@"Q:\", total, free, available, "NTFS", DateTimeOffset.UtcNow));
        public void Fail() => pending.SetException(new IOException("private-path"));
    }
}
