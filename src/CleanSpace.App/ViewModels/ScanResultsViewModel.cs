using CleanSpace.App.Formatting;
using CleanSpace.Application.UseCases;
using CleanSpace.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CleanSpace.App.ViewModels;

public partial class ScanResultsViewModel(QuickScanUseCase useCase, bool scanAvailable = false) : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? scanCancellation;
    private Guid? progressSession;
    private bool disposed;
    private long generation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool IsScanning { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial ScanStatus Status { get; private set; } = ScanStatus.Idle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilesText))]
    [NotifyPropertyChangedFor(nameof(BytesText))]
    [NotifyPropertyChangedFor(nameof(BytesExact))]
    [NotifyPropertyChangedFor(nameof(SkippedText))]
    public partial ScanTotals Totals { get; private set; } = new(0, 0, 0, 0);

    [ObservableProperty]
    public partial string Summary { get; private set; } = scanAvailable
        ? "Ready to inspect current-user Local AppData Temp metadata."
        : "Quick Scan is temporarily unavailable.";

    // Fail closed in production. Only isolated fake-provider diagnostics/tests opt in.
    // There is no setting or UI command that changes availability during this process.
    public bool IsScanAvailable { get; } = scanAvailable;
    public bool CanStart => IsScanAvailable && !IsScanning && !disposed;
    public string StatusText => "Status: " + Status;
    public string FilesText => "Files scanned: " + Totals.Files.ToString("N0");
    public string BytesText => "Logical bytes scanned: " + DriveValueFormatter.Bytes(Totals.LogicalBytes);
    public string BytesExact => DriveValueFormatter.ExactBytes(Totals.LogicalBytes);
    public string SkippedText => "Skipped entries: " + Totals.SkippedEntries.ToString("N0");

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (!CanStart) { return; }
        IsScanning = true;
        Status = ScanStatus.Scanning;
        Totals = new(0, 0, 0, 0);
        Summary = "Inspecting metadata. The total number of entries is unknown. You can navigate while scanning.";
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        scanCancellation = cancellation;
        progressSession = null;
        var currentGeneration = ++generation;
        // Constructed on WinUI's synchronization context; callbacks update bindable state on that context.
        var progress = new Progress<ScanProgress>(update =>
        {
            if (!IsScanning || disposed || generation != currentGeneration) { return; }
            progressSession ??= update.SessionId;
            if (progressSession == update.SessionId) { Totals = update.Totals; }
        });
        try
        {
            var result = await useCase.ExecuteAsync(progress, cancellation.Token);
            if (disposed) { return; }
            Status = result.Status;
            Totals = result.Totals;
            var notice = string.Join(", ", result.Warnings.Select(WarningText));
            Summary = $"{result.Status} after {result.Duration.TotalSeconds:N2} seconds."
                + (notice.Length > 0 ? " " + notice + "." : result.Status == ScanStatus.Completed
                    ? " All observed entries were processed." : " Partial totals retained; remaining entries were not counted.");
        }
        finally
        {
            scanCancellation = null;
            IsScanning = false;
        }
    }

    private static string WarningText(ScanWarning warning) => warning switch
    {
        ScanWarning.ScopeUnavailable => "Trusted Temp scope unavailable; no search was broadened",
        ScanWarning.EntryLimit => "Entry budget reached; remaining entries were not counted",
        ScanWarning.DepthLimit => "Depth budget reached; skipped subtrees were not counted",
        ScanWarning.TimeLimit => "Time budget reached; remaining entries were not counted",
        ScanWarning.MemoryLimit => "Retained-identity memory budget reached; remaining entries were not counted",
        ScanWarning.AlreadyScanning => "Another scan is already active",
        ScanWarning.ProviderFailure => "Metadata provider failed",
        ScanWarning.ChangedEntry => "Changing entries skipped",
        ScanWarning.UnsafeEntry => "Unsafe or redirected entries skipped",
        ScanWarning.InvalidMetadata => "Invalid or overflowing metadata skipped",
        ScanWarning.DuplicateIdentity => "Repeated file identities skipped",
        _ => "Unavailable entries skipped; skipped subtree contents were not counted"
    };

    [RelayCommand(CanExecute = nameof(IsScanning))]
    private void Cancel()
    {
        scanCancellation?.Cancel();
        Summary = "Cancellation requested. Waiting for the current Windows metadata call to return.";
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        lifetime.Cancel();
        lifetime.Dispose();
        GC.SuppressFinalize(this);
    }
}
