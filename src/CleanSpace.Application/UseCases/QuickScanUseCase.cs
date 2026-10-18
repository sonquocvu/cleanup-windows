using CleanSpace.Application.Abstractions;
using CleanSpace.Core.Models;

namespace CleanSpace.Application.UseCases;

public sealed class QuickScanUseCase(IScanScopeProvider provider, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private int active;

    public async Task<ScanSession> ExecuteAsync(IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default, ScanLimits? limits = null)
    {
        var id = Guid.NewGuid();
        var started = clock.GetUtcNow();
        if (Interlocked.CompareExchange(ref active, 1, 0) != 0)
        {
            return new(id, ScanStatus.Failed, null, started, TimeSpan.Zero, new(0, 0, 0, 0),
                new[] { ScanWarning.AlreadyScanning });
        }
        try
        {
            // Do not pass cancellation to Task.Run: even pre-cancelled requests return a session summary.
            return await Task.Run(() => Scan(id, started, limits ?? new(), progress, cancellationToken));
        }
        finally { Volatile.Write(ref active, 0); }
    }

    private ScanSession Scan(Guid id, DateTimeOffset started, ScanLimits limits,
        IProgress<ScanProgress>? progress, CancellationToken token)
    {
        var stamp = clock.GetTimestamp();
        long files = 0, bytes = 0, skipped = 0, examined = 0;
        var warnings = new HashSet<ScanWarning>();
        var seen = new HashSet<ulong>(); // Separate retained-identity memory budget; no paths/results cache.
        var stack = new Stack<IScanDirectory>();
        IScanScopeLease? lease = null;
        ScanScope? scope = null;
        var status = ScanStatus.Completed;
        var lastProgress = TimeSpan.Zero;
        TimeSpan Elapsed() => clock.GetElapsedTime(stamp);
        ScanTotals Totals() => new(files, bytes, skipped, examined);
        void Skip(ScanWarning warning) { skipped++; warnings.Add(warning); }
        void CheckBudget()
        {
            token.ThrowIfCancellationRequested();
            if (Elapsed() >= limits.MaxDuration) { throw new ScanBudgetException(); }
        }
        try
        {
            CheckBudget();
            lease = provider.Open(token);
            CheckBudget();
            lease.Validate();
            scope = lease.Scope;
            stack.Push(lease.Root);
            seen.Add(lease.Root.Identity.Id);
            while (stack.TryPeek(out var directory))
            {
                CheckBudget();
                if (examined >= limits.MaxEntries) { warnings.Add(ScanWarning.EntryLimit); break; }
                ScanEntry? entry;
                try
                {
                    lease.Validate();
                    directory.Validate();
                    entry = directory.ReadNext();
                }
                catch (Exception ex) when (IsMetadataFailure(ex))
                {
                    Skip(ScanWarning.EntryUnavailable);
                    stack.Pop().Dispose();
                    continue;
                }
                CheckBudget();
                if (entry is null) { stack.Pop().Dispose(); continue; }
                examined++;
                if (!entry.HasSafeName || entry.IsUnsafe || entry.IsProtectedDirectory) { Skip(ScanWarning.UnsafeEntry); }
                else if (entry.Id == 0 || entry.LogicalBytes < 0 || entry.ModifiedTime < 0)
                { Skip(ScanWarning.InvalidMetadata); }
                else if (seen.Contains(entry.Id)) { Skip(ScanWarning.DuplicateIdentity); }
                else if (seen.Count >= limits.MaxTrackedIdentities) { warnings.Add(ScanWarning.MemoryLimit); break; }
                else
                {
                    seen.Add(entry.Id);
                    try
                    {
                        CheckBudget();
                        if (entry.IsDirectory)
                        {
                            if (stack.Count > limits.MaxDepth) { Skip(ScanWarning.DepthLimit); }
                            else
                            {
                                var child = directory.OpenDirectory(entry);
                                if (child.Identity.Id != entry.Id || !child.Identity.IsDirectory || child.Identity.IsUnsafe)
                                { child.Dispose(); Skip(ScanWarning.ChangedEntry); }
                                else { stack.Push(child); }
                            }
                        }
                        else
                        {
                            var current = directory.ReadFileMetadata(entry);
                            CheckBudget();
                            if (current != entry) { Skip(ScanWarning.ChangedEntry); }
                            else
                            {
                                var total = checked(bytes + entry.LogicalBytes);
                                files++;
                                bytes = total;
                            }
                        }
                    }
                    catch (OverflowException) { Skip(ScanWarning.InvalidMetadata); }
                    catch (Exception ex) when (IsMetadataFailure(ex)) { Skip(ScanWarning.EntryUnavailable); }
                }
                var elapsed = Elapsed();
                if (elapsed - lastProgress >= TimeSpan.FromMilliseconds(150))
                {
                    progress?.Report(new(id, Totals(), elapsed));
                    lastProgress = elapsed;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { status = ScanStatus.Cancelled; }
        catch (ScanBudgetException) { warnings.Add(ScanWarning.TimeLimit); }
        catch (Exception ex) when (IsMetadataFailure(ex))
        {
            warnings.Add(scope is null ? ScanWarning.ScopeUnavailable : ScanWarning.ProviderFailure);
            status = scope is null ? ScanStatus.Failed : ScanStatus.Partial;
        }
        catch (Exception)
        {
            // Port failures are redacted; never publish exception text or a user path.
            warnings.Add(ScanWarning.ProviderFailure);
            status = ScanStatus.Failed;
        }
        finally
        {
            while (stack.TryPop(out var directory)) { directory.Dispose(); }
            lease?.Dispose();
        }
        if (status == ScanStatus.Completed && warnings.Count > 0) { status = ScanStatus.Partial; }
        return new(id, status, scope, started, Elapsed(), Totals(), warnings.Order().ToArray());
    }

    private static bool IsMetadataFailure(Exception ex) => ex is IOException or UnauthorizedAccessException
        or System.ComponentModel.Win32Exception or ArgumentException or NotSupportedException;

    private sealed class ScanBudgetException : Exception;
}
