# Short protected lease diagnostic

TASK-003-REWORK-02 investigation only. This executable is outside `CleanSpace.slnx` and is never registered or referenced by the production app. Run as a normal Windows user on NTFS; it refuses an Administrator token. It does not call the production Temp scope resolver.

```powershell
dotnet restore diagnostics/ScanLeaseProbe/ScanLeaseProbe.csproj --disable-parallel --verbosity minimal -p:NuGetAudit=true -p:NuGetAuditMode=all
dotnet build diagnostics/ScanLeaseProbe/ScanLeaseProbe.csproj --no-restore --verbosity minimal -m:1 -p:UseSharedCompilation=false
dotnet run --project diagnostics/ScanLeaseProbe/ScanLeaseProbe.csproj --no-build --no-restore
```

Every run creates new GUID owners under ignored `artifacts/task-003-rework-02/fixtures`. Fixture setup writes synthetic files; child-process test plumbing may create files/directories, relocate directories within one checked owner, or create a junction/symlink. It refuses unowned/escaping/reparse ancestry and never moves an owner. Fixtures are retained; there is no deletion or cleanup. Symlink privilege failure 1314 is recorded as **not exercised**, without elevation or a settings change.

`ProtectedCursor` itself performs only directory metadata operations. It opens and validates four owned directories before each 4 KiB enumeration query, using READ | WRITE sharing (`3`) and the production directory access mask (`0x100081`). Opens below the synthetic anchor are relative, with OPEN_REPARSE_POINT; type, unsafe attributes, identity and expected volume-GUID path are checked before querying. These handles stay held through the query and bounded parsing, then close in reverse order even on failure/cancellation. A distinct fully shared (`7`) cursor survives between batches. It preserves enumeration position but is never queried outside a validated protected lease. Captured leaf identity must match on every reacquisition. There is no automatic retry after sharing/identity errors.

The synthetic owned anchor models an already established trusted boundary. The prototype does **not** establish production OS-known-folder trust, protect repository ancestors above that anchor, implement recursive traversal/file-by-file revalidation, or prove safety under arbitrary in-place reparse modifications. It is a fixed-depth experiment, not a replacement scope provider or a production scan engine.

Bounds: one 4 KiB buffer, at most 40 records per query, 2,048 examined entries/retained identities, 128 lease attempts, eight ancestry components maximum (four used), enumeration depth zero, five-second cooperative time budget. Instrumentation retains at most 512 restrictive SafeHandle wrappers, plus bounded timing arrays. One fully shared cursor stays open until disposal. Synchronous native calls and process scheduling have no hard latency guarantee.

JSON records separate child PIDs, native error codes, conservative whole-lease spans, and per-handle open-return-to-close intervals in anchor/ancestor/Temp/child order. The latter exclude time inside the open call; the whole span includes opens, validation and disposal. Deliberately stalled concurrency schedules are identified separately from ordinary batch timings. The A/C microbenchmarks use the same 4 KiB buffer and validate once per batch; they are not the unchanged production per-entry scan use case. B's reported drain time excludes constructor initialization, so the wall times are not throughput rankings.

The linked `ScanSharingProbe/NativeProbe.cs` is unchanged accepted diagnostic interop. Production native metadata queries are invoked by reflection. Only `LeaseNative` supplies the diagnostic share-3 relative directory open.

**PASS means the assertions and interference witnesses reproduced. It does not approve this architecture.** DELETE opens still fail during leases, and ancestor cross-parent moves can fail with OS error 5 even between leases because of the persistent descendant cursor. See `docs/TASK-003-REWORK-02-REPORT.md`. Production scanning must remain disabled pending review.
