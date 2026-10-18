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
