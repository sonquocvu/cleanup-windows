using CleanSpace.App.Formatting;
using CleanSpace.Application.UseCases;
using CleanSpace.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CleanSpace.App.ViewModels;

public partial class DashboardViewModel(DriveOverviewUseCase useCase) : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private Task? initialization;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRefresh))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Identity))]
    [NotifyPropertyChangedFor(nameof(FileSystem))]
    [NotifyPropertyChangedFor(nameof(Total))]
    [NotifyPropertyChangedFor(nameof(Used))]
    [NotifyPropertyChangedFor(nameof(Free))]
    [NotifyPropertyChangedFor(nameof(AvailableToUser))]
    [NotifyPropertyChangedFor(nameof(TotalExact))]
    [NotifyPropertyChangedFor(nameof(UsedExact))]
    [NotifyPropertyChangedFor(nameof(FreeExact))]
    [NotifyPropertyChangedFor(nameof(AvailableExact))]
    [NotifyPropertyChangedFor(nameof(TotalAccessible))]
    [NotifyPropertyChangedFor(nameof(UsedAccessible))]
    [NotifyPropertyChangedFor(nameof(FreeAccessible))]
    [NotifyPropertyChangedFor(nameof(AvailableAccessible))]
    [NotifyPropertyChangedFor(nameof(Percentage))]
    [NotifyPropertyChangedFor(nameof(UsageValue))]
    [NotifyPropertyChangedFor(nameof(HasUsage))]
    [NotifyPropertyChangedFor(nameof(AvailabilityNotice))]
    [NotifyPropertyChangedFor(nameof(UpdatedStatus))]
    public partial DriveOverview? Overview { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorMessage { get; private set; } = "";

    public bool CanRefresh => !IsLoading;
    public bool HasError => ErrorMessage.Length > 0;
    public bool HasUsage => Overview?.PercentageUsed is not null;
    public double UsageValue => Overview?.PercentageUsed ?? 0;
    public string Identity => Overview?.VolumeRoot ?? "Unavailable";
    public string FileSystem => "Filesystem: " + (Overview?.FileSystem ?? "Unavailable");
    public string Total => DriveValueFormatter.Bytes(Overview?.TotalBytes);
    public string Used => DriveValueFormatter.Bytes(Overview?.UsedBytes);
    public string Free => DriveValueFormatter.Bytes(Overview?.TotalFreeBytes);
    public string AvailableToUser => DriveValueFormatter.Bytes(Overview?.AvailableToUserBytes);
    public string TotalExact => DriveValueFormatter.ExactBytes(Overview?.TotalBytes);
    public string UsedExact => DriveValueFormatter.ExactBytes(Overview?.UsedBytes);
    public string FreeExact => DriveValueFormatter.ExactBytes(Overview?.TotalFreeBytes);
    public string AvailableExact => DriveValueFormatter.ExactBytes(Overview?.AvailableToUserBytes);
    public string TotalAccessible => "Total capacity: " + TotalExact;
    public string UsedAccessible => "Used space: " + UsedExact;
    public string FreeAccessible => "Free space (volume): " + FreeExact;
    public string AvailableAccessible => "Available to you: " + AvailableExact;
    public string Percentage => DriveValueFormatter.Percentage(Overview?.PercentageUsed);
    public string AvailabilityNotice => Overview?.AvailableToUserBytes is { } available
        && Overview.TotalFreeBytes is { } free && available < free
            ? "Available to you is lower than volume free space. Quotas or other Windows storage limits may apply."
            : "Free space is for the whole volume. Available to you accounts for quotas and Windows storage limits; these values may differ.";
    public string UpdatedStatus => Overview is { } overview
        ? "Updated " + overview.FetchedAt.ToLocalTime().ToString("G") + ". Values are a point-in-time snapshot."
        : "No current drive snapshot.";

    public Task InitializeAsync() => initialization ??= RefreshAsync(lifetime.Token);

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (IsLoading || lifetime.IsCancellationRequested) { return; }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        IsLoading = true;
        Overview = null; // Never present a stale snapshot as current after a failed refresh.
        ErrorMessage = "";
        try
        {
            var result = await useCase.ExecuteAsync(linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            Overview = result.Overview;
            ErrorMessage = result.Error ?? "";
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            // Closing the window or cancelling a command is not a volume failure.
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void Dispose()
    {
        lifetime.Cancel();
        lifetime.Dispose();
        GC.SuppressFinalize(this);
    }
}
