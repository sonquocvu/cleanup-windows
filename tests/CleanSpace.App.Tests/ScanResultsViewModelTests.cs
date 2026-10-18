using CleanSpace.App.ViewModels;
using CleanSpace.Application.Abstractions;
using CleanSpace.Core.Models;
using Xunit;

namespace CleanSpace.App.Tests;

public sealed class ScanResultsViewModelTests
{
    [Fact]
    public async Task PendingScanAllowsCancellationAndPreventsOverlap()
    {
        var provider = new PendingProvider();
        using var model = new ScanResultsViewModel(new(provider), scanAvailable: true);
        Assert.Equal(ScanStatus.Idle, model.Status);
        Assert.True(model.StartCommand.CanExecute(null));
        Assert.False(model.CancelCommand.CanExecute(null));
        var running = model.StartCommand.ExecuteAsync(null);
        await provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ScanStatus.Scanning, model.Status);
        Assert.False(model.CanStart);
        Assert.True(model.CancelCommand.CanExecute(null));
        await model.StartCommand.ExecuteAsync(null);
        Assert.Equal(1, provider.Calls);
        model.CancelCommand.Execute(null);
        await running;
        Assert.Equal(ScanStatus.Cancelled, model.Status);
        Assert.True(model.CanStart);
        Assert.Contains("Cancelled", model.Summary);
    }

    [Fact]
    public async Task FailureIsRedactedAndRepeatedScansResetResults()
    {
        var provider = new FailedProvider();
        using var model = new ScanResultsViewModel(new(provider), scanAvailable: true);
        await model.StartCommand.ExecuteAsync(null);
        Assert.Equal(ScanStatus.Failed, model.Status);
        Assert.Contains("scope unavailable", model.Summary);
        Assert.DoesNotContain("private-path", model.Summary);
        await model.StartCommand.ExecuteAsync(null);
        Assert.Equal(2, provider.Calls);
        Assert.Equal(0, model.Totals.Files);
    }

    [Fact]
    public async Task ClosingCancelsWorkerAndDisablesFutureStarts()
    {
        var provider = new PendingProvider();
        var model = new ScanResultsViewModel(new(provider), scanAvailable: true);
        var running = model.StartCommand.ExecuteAsync(null);
        await provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        model.Dispose();
        await running;
        Assert.False(model.IsScanning);
        Assert.False(model.CanStart);
        model.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductionGateBlocksDirectCommandsAndNavigation(bool explicitAvailability)
    {
        var provider = new FailedProvider();
        using var model = explicitAvailability
            ? new ScanResultsViewModel(new(provider), scanAvailable: false)
            : new ScanResultsViewModel(new(provider));
        // Settings/drive are never used by shell navigation or scan commands in this test.
        var shell = new MainViewModel(null!, null!, model);
        Assert.False(model.IsScanAvailable);
        Assert.False(model.StartCommand.CanExecute(null));
        Assert.False(shell.QuickScanCommand.CanExecute(null));
        Assert.False(model.CancelCommand.CanExecute(null));
        await model.StartCommand.ExecuteAsync(null); // Direct invocation must also fail closed.
        await shell.QuickScanCommand.ExecuteAsync(null);
        foreach (var page in Enum.GetValues<ShellPage>())
        {
            shell.SelectedPage = page;
            await model.StartCommand.ExecuteAsync(null);
            await shell.QuickScanCommand.ExecuteAsync(null);
            Assert.Equal(page, shell.SelectedPage);
        }
        Assert.Equal(0, provider.Calls);
        Assert.Equal(ScanStatus.Idle, model.Status);
        Assert.Equal(0, model.Totals.EntriesExamined);
        Assert.Contains("temporarily unavailable", model.Summary);
    }

    private sealed class PendingProvider : IScanScopeProvider
    {
        public int Calls;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IScanScopeLease Open(CancellationToken cancellationToken)
        {
            Calls++;
            Entered.TrySetResult();
            cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
            cancellationToken.ThrowIfCancellationRequested();
            throw new IOException();
        }
    }
    private sealed class FailedProvider : IScanScopeProvider
    {
        public int Calls;
        public IScanScopeLease Open(CancellationToken cancellationToken)
        { Calls++; throw new UnauthorizedAccessException("private-path"); }
    }
}
