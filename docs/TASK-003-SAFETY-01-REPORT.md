# TASK-003-SAFETY-01 — Non-Interfering Filesystem Scan Handles

**Recommendation: REMAIN BLOCKED.** The original production directory handles measurably cause sharing violations. A test-only change to READ | WRITE | DELETE sharing eliminates the measured conflicts, but permits directory relocation between ancestry validation and enumeration. A controlled out-and-back schedule also defeats a later identity/path check. No minimal production correction preserving the current traversal boundary was established, so the unsafe flag change was **not applied**.

This audit was performed on 2026-10-18 using the available Windows toolchain. Audited restore/build, all **167 existing tests**, the new native diagnostic assertions, **three production EXE startup smoke tests**, and actual WinUI Start/Cancel verification completed. Those regression passes do not resolve the acceptance blocker.

## Scope and preservation

Read AGENTS.md, PRODUCT, ARCHITECTURE, SECURITY, TASK-003-REPORT, the original Quick Scan task, and the three production scanning implementation files. This task adds only a separate diagnostic/test executable and this report. It does not implement TASK-004 or change production scope, ports, UI, flags, budgets, packages or settings.

A baseline captured before edits covers **121 existing tracked/staged files**; every file's SHA256 remains unchanged. Evidence: `artifacts/task-003-safety-01/baseline-files.json` and `preservation.json`. Existing user work was preserved. This agent did not stage, commit, push, install tools, change security settings or elevate privileges.

## A — Actual original handle configuration

`WindowsScanNative.OpenVolume` passes share mode **1** to `CreateFileW`. `OpenChild` passes ShareAccess **1** to `NtCreateFile` for both directory and file requests. Thus every explicit sharing mask is FILE_SHARE_READ, with no hidden retry/fallback using a different mask.

| Handle role | Requested access | Original share mask | Lifetime |
| --- | --- | --- | --- |
| Windows volume root directory, opened by volume GUID | FILE_READ_ATTRIBUTES (`0x80`) | READ (`0x1`) | Scope lease/session |
| Trusted ancestors | FILE_LIST_DIRECTORY + FILE_READ_ATTRIBUTES + SYNCHRONIZE (`0x100081`) | READ (`0x1`) | Scope lease/session, including time spent scanning descendants |
| Temp root | Same directory access (`0x100081`) | READ (`0x1`) | Root enumeration, then scope disposal |
| Child enumeration directories | Same directory access (`0x100081`) | READ (`0x1`) | Depth-first subtree traversal, until popped/disposed |
| File metadata | FILE_READ_ATTRIBUTES + SYNCHRONIZE (`0x100080`) | READ (`0x1`) | One metadata validation operation, ending at the using/dispose boundary |

The scope's ancestor handles are retained by `WindowsTempScopeProvider.Lease`. Child handles are retained by the traversal stack. The default traversal budget is ten seconds, but an in-flight synchronous filesystem call can exceed it; sharing lifetime is not a hard ten-second guarantee.

Root opens use OPEN_EXISTING (`3`) and BACKUP_SEMANTICS | OPEN_REPARSE_POINT (`0x02200000`). Relative directory opens use FILE_OPEN (`1`) and options `0x00200021` (DIRECTORY_FILE, SYNCHRONOUS_IO_NONALERT, OPEN_REPARSE_POINT). File opens use FILE_OPEN and options `0x00600060` (NON_DIRECTORY_FILE, SYNCHRONOUS_IO_NONALERT, OPEN_REPARSE_POINT, OPEN_NO_RECALL). No file data-read, write, delete or permission-change access is requested by the scanner.

The mask alone is not sufficient to infer interference. Windows exempts attribute-only access from ordinary data-sharing checks. The experiment below confirms the difference: the list-directory handles conflict with specified add-file/delete access; the attribute-only handles do not. WRITE_ATTRIBUTES also succeeds. This corrects any reading of TASK-003-REPORT that all scanner handles block all directory/file writes. It does not establish that ordinary child-file creation or all application writes are blocked.

Native API references: [CreateFile access/sharing](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew), [NtCreateFile relative opens and flags](https://learn.microsoft.com/en-us/windows/win32/api/winternl/nf-winternl-ntcreatefile), and [final-path queries](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getfinalpathnamebyhandlew).

## B — Measured separate-process interference

Added `diagnostics/ScanSharingProbe`, a non-shipping console test harness outside the production solution. Original-mode opens invoke the **compiled production native methods** by reflection. The production directory validator is also used unchanged. The test-only candidate uses identical relative-open access/disposition/options with ShareAccess **7**. The root-style candidate uses the same CreateFile request with sharing **7**.

Every competing open runs in a distinct child process, records its PID/native result, requests sharing READ | WRITE | DELETE itself, and closes the handle without performing writes/deletion. The role/rights matrix contains **48 cross-process open attempts**: 24 original and 24 candidate. Tests assert normal-user execution and separate process identity.

All targets are unique GUID owners under `artifacts/task-003-safety-01/fixtures/`. A synthetic `ancestor/Temp/child/file.bin` hierarchy stands in for production roles. Volume-root-style flags are exercised on an owned directory, **not the actual Windows volume root**. The probe never resolves or opens the real user's Temp scope. Artifacts/repository ancestry, owner marker, containment and reparse flags are checked; fixtures remain on disk without cleanup.

Original results (`0` = success; `32` = ERROR_SHARING_VIOLATION):

| Scanner-held fixture role | Other process: read attributes `0x80` | List directory `0x1` | Add file / write data `0x2` | Write attributes `0x100` | Delete access only `0x10000` |
| --- | --- | --- | --- | --- | --- |
| Volume-root-style attribute handle | 0 | 0 | 0 | 0 | 0 |
| Trusted ancestor list handle | 0 | 0 | **32** | 0 | **32** |
| Temp-root list handle | 0 | 0 | **32** | 0 | **32** |
| Child list handle | 0 | 0 | **32** | 0 | **32** |
| File metadata attribute handle | 0 | Not requested | 0 | 0 | 0 |

For a directory, bit `0x2` is FILE_ADD_FILE; for a file it is FILE_WRITE_DATA. No content was written through these probe handles. Delete access was requested without DELETE_ON_CLOSE or any disposition/deletion call.

Original configuration: **18 successes, 6 sharing violations**. Full-sharing candidate: **24 successes, 0 sharing violations**. These results apply to compatible sharing on the same owned objects, not to unrelated ACL restrictions or another process requesting incompatible exclusive access. The ancestor conflict was reproduced while the scanner also held the descendant chain.

Raw evidence: `artifacts/task-003-safety-01/sharing-results.json`, copied from `sharing-probe-4229c3073e11443ba5eed637ea33f63c.json`. All open results include child PIDs and requested masks.

## C — Candidate evaluation and why no patch was applied

The candidate keeps minimal requested rights, relative parent handles, reparse-object opens and the production identity/final-volume-GUID-path validator. Allowing DELETE sharing means a held directory handle may survive a rename or relocation; the handle continues referring to the original directory object. A relative open remains bound to that object, which does not prove its **current ancestry** still lies inside Temp.

The controlled schedules produced these results:

| Scenario using full sharing | Observation |
| --- | --- |
| Move an open child from `ancestor/Temp/child` to `outside/relocated` | Separate-process move succeeded. Subsequent production Validate and ReadNext rejected the changed ancestry. |
| Attempt cross-parent relocation of an ancestor with its descendant handles open | Windows refused with ERROR_ACCESS_DENIED (`5`). This is recorded as OS rejection, not scanner detection. |
| Attempt cross-parent relocation of Temp with its child handle open | Windows likewise refused with `5`; not counted as scanner detection. |
| Move the held child and create a replacement at its old name | The enumerated old ID was rejected when opening the replacement; the old held directory was rejected by validation. |
| Validate child; relocate it; create `outside-only-entry`; query its retained handle | The real native directory query returned metadata including that outside entry. Later validation rejected the relocation, after enumeration had already occurred outside the trusted path. |
| Validate child; relocate it; create `inserted-while-outside`; query metadata; move it back; validate again | Both identity/path validations passed. The captured metadata came from enumeration while the object was outside Temp. |

The last two tests deliberately expand the actual `ReadNext` boundary into `Validate(); [controlled concurrent action]; ReadDirectory(handle)`. They call the compiled production validation/query methods with test-only candidate handles. They are deterministic schedules of that gap, **not a claim that production was patched or that a live production race was injected**. The out-and-back case also models a proposed added post-query validation: the current ID/path/type/attribute validator passes after restoration.

The source/destination of every fixture directory move is resolved and checked to remain inside the same uniquely owned artifact tree before the move. No owner, real Temp, user data or system directory is moved. All such operations exist only in the diagnostic's guarded child-process test plumbing, never in production.

These observations distinguish two guarantees:

- Relative handles and OPEN_REPARSE_POINT prevent a path replacement from silently redirecting an open to a junction target. Those mechanisms remain in the candidate.
- They do not atomically couple **current trusted ancestry** with the next enumeration when concurrent relocation is allowed. Before/after path checks are observations, not an atomic containment guard. Discarding results afterward does not undo the metadata access, and out-and-back relocation can evade those checks.

Accordingly, the proposed production changes from share `1` to share `7` were evaluated only in the diagnostic and **rejected for this task**. No partial production flag/access change, shorter-lock workaround, busy-file retry loop, privilege helper or broader scanning scope was introduced. The existing implementation remains interference-prone; it is not presented as fixed.

This is evidence that the simple flag change, including an extra current-style post-check, does not preserve the required traversal boundary. It is not a proof that no different safe design exists. Architectural review must decide whether to authorize a different scan-session scope definition or a separate design investigation that can bind containment to enumeration without disruptive sharing. Neither relaxation nor redesign was assumed here.

## D — Exact regression and runtime verification

Working directory: `E:\01 - Work\cleanup-windows`. Windows `10.0.26300.0`, x64; .NET SDK `10.0.302`, test runtime `10.0.10`. Audited restore, test hosts, probes and production observations used the normal-user Windows session outside the restricted process sandbox. The diagnostic refuses an Administrator token; all production observers report `Elevated: false`.

```powershell
dotnet restore CleanSpace.slnx --disable-parallel --force --no-http-cache --verbosity minimal -m:1 -p:NuGetAudit=true -p:NuGetAuditMode=all
dotnet build CleanSpace.slnx --no-restore --no-incremental --verbosity minimal -m:1 -p:UseSharedCompilation=false
dotnet test CleanSpace.slnx --no-build --no-restore --verbosity normal -m:1 --logger 'trx;LogFilePrefix=safety-01' --results-directory artifacts/task-003-safety-01/test-results --blame-hang-timeout 30s
```

| Check | Result |
| --- | --- |
| Forced solution restore / NuGet audit, all dependencies | Exit 0, eight projects restored, no warnings; `restore-audit.log` |
| Full solution build | Exit 0, 0 warnings/errors, 25.56 s; `build.log` |
| All existing automated tests | Exit 0, **167 passed / 0 failed / 0 skipped**, 10.88 s; `tests.log`, `test-results/`, `test-summary.json` |
| Test assembly counts | App 29; Application 67; Core 31; Integration 40 |
| Existing native reparse/path guards | PASS, including actual junction exclusion/root rejection, invalid names and changing metadata |
| Existing budgets, cancellation, overlapping-session guards and content/mutation API guards | PASS in the full suite |

New native fixture diagnostic commands:

```powershell
dotnet restore diagnostics/ScanSharingProbe/ScanSharingProbe.csproj --disable-parallel --verbosity minimal -p:NuGetAudit=true -p:NuGetAuditMode=all
dotnet build diagnostics/ScanSharingProbe/ScanSharingProbe.csproj --no-restore --verbosity minimal -m:1 -p:UseSharedCompilation=false
dotnet run --project diagnostics/ScanSharingProbe/ScanSharingProbe.csproj --no-build --no-restore
```

Final probe build: exit 0, 0 warnings/errors, 2.27 s. Final run: exit 0, **214 assertions passed**. These include the 48 cross-process opens and controlled race witnesses; they are not 214 additional xUnit test cases. **Probe PASS means the audit assertions/counterexamples were reproduced, not that the full-sharing candidate passed the safety gate.** Logs: `probe-restore.log`, `probe-build-final.log`, `sharing-probe-console-final.log`.

An initial diagnostic run exited 1 when it assumed every full-sharing ancestor relocation would succeed. Its raw JSON/console output are retained. Windows denied that specific ancestor move. The final diagnostic separately records OS-denied moves and successful child moves, rather than misclassifying that denial as scanner detection. No production change resulted from this diagnostic correction.

### Production startup smoke tests

```powershell
./diagnostics/Verify-ProductionStartup.ps1 -RunId Task003-Safety01-1
./diagnostics/Verify-ProductionStartup.ps1 -RunId Task003-Safety01-2
./diagnostics/Verify-ProductionStartup.ps1 -RunId Task003-Safety01-3
```

Output was captured with `Tee-Object` in `artifacts/task-003-safety-01/production-{1,2,3}.log`. Each launched the actual rebuilt production EXE from its output directory, observed a stable CleanSpace window and pinned XAML module, then exited normally via Alt+F4. No process was killed. All times are 2026-10-18, Asia/Bangkok (UTC+07:00).

| Run | PID / window | Start → exit | Observer/process exit | Crash events 1000/1026 |
| --- | --- | --- | --- | --- |
| Safety01-1 | 31460 / 2556076 | 11:10:07 → 11:14:52 | 0 / 0, PASS | None |
| Safety01-2 | 30104 / 461640 | 11:14:54 → 11:15:19 | 0 / 0, PASS | None |
| Safety01-3 | 29732 / 1246794 | 11:15:22 → 11:16:02 | 0 / 0, PASS | None |

All three share managed assembly SHA256 `B07C0E7C80E5B03B2F064179671F44125D8BBEB1A874038AB49B900FE2D2E0AA`, executable SHA256 `9F0A29DE551165AF5E3497A2AEF757495EDF6696EC0BA8F75EABE7A83847EF96` and MainWindow XBF `7C19765A3335D29DEFB4A100F477F86D03E52E88FD7C682CFE1B0843AC573509`. Loaded XAML matches the existing pinned package hash `AAD12524765E6FB63F0AE26A45A9BA3104F24FDE66413D8A3036FBED74A1E990`; SDK 2.5.1 / resolved WinUI 2.3.9 remains unchanged. Consolidated evidence: `runtime-summary.json`; detailed observer JSON/checkpoints: `artifacts/fix-06/launches/Task003-Safety01-*`.

### Actual WinUI Start/Cancel

Native computer-use automation initially could not activate the window. After one failed recovery it stopped requesting input; the user brought CleanSpace forward, and a fresh snapshot/restore allowed verification to complete. No terminal or other app was controlled.

The actual production Dashboard Quick Scan button navigated to Scan Results. Settled accessibility evidence showed Scanning, changing counters, an indeterminate progress bar, Start disabled and Cancel enabled. Cancel produced **Cancelled after 9.27 seconds**, retaining **320 files**, **1.04 GiB displayed logical size**, and **7 skipped entries** with the notice "Changing entries skipped." Start reenabled, Cancel disabled, and Cleanup stayed disabled. No target mutation was performed. Evidence: `ui/scanning.txt`, `ui/cancelled.txt`, `ui/startup-3.txt`.

This verifies the unchanged application's functional lifecycle, not non-interference under a production patch. Only the fixture diagnostic uses candidate sharing. Final process check found zero CleanSpace/probe processes.

## Changed files

Only these six new files belong to this task; no pre-existing source file was edited:

- `diagnostics/ScanSharingProbe/ScanSharingProbe.csproj` — standalone test executable referencing existing Infrastructure; no package additions.
- `diagnostics/ScanSharingProbe/NativeProbe.cs` — actual-production reflection adapter and isolated candidate native opens.
- `diagnostics/ScanSharingProbe/OwnedFixture.cs` — guarded repository-owned fixture paths, handle chain and retained artifacts.
- `diagnostics/ScanSharingProbe/Program.cs` — separate-process matrix, relocation/replacement schedules and assertions/JSON evidence.
- `diagnostics/ScanSharingProbe/README.md` — reproducible commands, isolation and interpretation.
- `docs/TASK-003-SAFETY-01-REPORT.md` — this report.

## Acceptance status and remaining limits

| Required conclusion | Status |
| --- | --- |
| Compatible write/delete opens work on scanner-held production directory configuration | **FAIL: six measured sharing violations** |
| Full-sharing candidate permits those compatible opens | PASS in owned fixtures only |
| Candidate rejects persistent child movement/replacement | PASS after the change becomes observable |
| Candidate preserves current ancestry throughout enumeration | **FAIL: controlled validation/query and out-and-back schedules** |
| Existing reparse safeguards, budgets, cancellation, metadata-only APIs | Regression PASS; production code unchanged |
| No unrelated source/package/security changes | PASS; 121 pre-existing file hashes preserved |

No file deletion, cleanup, Recycle Bin, production move/rename, permission change, network feature, service, driver, automatic scan or escalation was introduced. The fixture-only directory moves/replacements are the controlled experiments explicitly authorized in Phase C. No user/system Temp was mutated or used as a concurrency fixture. Build/test artifacts, owner markers and result JSON are ordinary repository writes; no host-wide trace was collected and no claim of zero disk writes is made.

The native results cover this host's owned fixture filesystem and specific compatible access masks. They do not certify every filesystem, ACL, sharing mode, filter or concurrent schedule. Some ancestor relocations were OS-denied; they must not be generalized into a guarantee. Attribute-only file handles still do not freeze file content or pathname, and existing metadata snapshot limitations remain. No aggressive retry or unexplored API design was used to conceal these limits.

**REMAIN BLOCKED:** non-interference has not been corrected safely in production. Return for architectural review of this concrete tradeoff. No TASK-004 work or subsequent implementation is authorized by this report.
