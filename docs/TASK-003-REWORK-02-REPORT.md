# TASK-003-REWORK-02 — Quick Scan mitigation and lease investigation

**Recommendation: retain the production UI mitigation; TASK-003 REMAINS BLOCKED.** Quick Scan is unavailable from both production entry points and direct UI command invocation. The scanner itself is preserved. The diagnostic lease design protected the tested enumeration schedules and allowed write-compatible opens, but still caused DELETE sharing violations during leases and ancestor move failures between leases. Strict production containment together with sufficiently low interference has **not** been demonstrated. No lease or full-sharing implementation was promoted to production.

Investigation and verification: 2026-10-18, Windows x64, normal-user token. Full audited restore/build, **169 solution tests**, **208 final lease diagnostic assertions**, the unchanged **214-assertion sharing diagnostic**, three production startup smoke tests and real WinUI verification passed. Diagnostic PASS means the expected results, including conflicts, were reproduced. Native symlink replacement was **not exercised** after creation returned privilege error 1314.

## Production mitigation and preservation

Read AGENTS.md, PRODUCT, ARCHITECTURE, SECURITY, both prior TASK-003 reports, the attached selected rework task, current scanner, presentation, tests and diagnostic sources before editing.

The exact production change is limited to four files:

| File | Change |
| --- | --- |
| `src/CleanSpace.App/ViewModels/ScanResultsViewModel.cs` | Constructor availability defaults to **false**; immutable `IsScanAvailable` gates `CanStart`. The existing Start method checks `CanStart` before changing state or calling the use case. Initial summary says “Quick Scan is temporarily unavailable.” |
| `src/CleanSpace.App/ViewModels/MainViewModel.cs` | Dashboard command has a false availability predicate and an explicit invocation guard before navigation/use-case dispatch. Scan Results description states temporary unavailability. Ordinary navigation remains available. |
| `src/CleanSpace.App/App.xaml.cs` | Production composition explicitly passes `scanAvailable: false`, as well as retaining the false constructor default. No user setting, environment switch or command enables it. |
| `src/CleanSpace.App/MainWindow.xaml` | Informational notice: “Quick Scan is temporarily unavailable. Drive information is available. Cleanup remains unavailable.” |

Existing command bindings disable Dashboard Quick Scan and Scan Results Start; no view or scanner replacement was needed. Startup still initializes only settings and Drive Overview. Cleanup's disabled navigation is unchanged. The scanner remains registered for preservation, but no current UI path dispatches it. This is an application composition gate, not a security boundary against code that deliberately constructs another model.

All three existing scan ViewModel tests retain their original assertions; their fake-provider construction now explicitly opts in. Two new theory cases cover the default-disabled and explicitly-disabled constructions: both commands report disabled, direct `ExecuteAsync` calls do nothing across every navigation page, provider calls remain zero, status stays Idle and totals remain zero. The App test project links actual Main/Settings ViewModels to test the shell path. The existing fake-provider QuickScanUiProbe explicitly opts in so its lifecycle diagnostic remains usable. Its opt-in does not affect production composition. No test was removed or weakened.

Added only `diagnostics/ScanLeaseProbe/{ScanLeaseProbe.csproj,Fixture.cs,LeaseNative.cs,ProtectedCursor.cs,Program.cs,README.md}` and this report. It is outside the solution and has no production reference. It links the unchanged accepted `ScanSharingProbe/NativeProbe.cs` for existing diagnostic interop.

SHA256 baseline covers **127 existing files**: **120 unchanged**, exactly seven changed (the four production files, two App test files, and fake UI probe composition above). All six scanner contract/model/use-case/native/scope files remain byte-for-byte unchanged. Package/SDK pins, solution, security policy, previous reports and startup observer are unchanged. Evidence: `artifacts/task-003-rework-02/baseline-files.json`, `preservation.json`. No staging, commit, push or TASK-004 work occurred.

## Native sharing and lease mechanism

Original production access/sharing remains exactly as accepted in the preceding audit:

| Role | Requested access | ShareAccess | Lifetime |
| --- | --- | --- | --- |
| Volume GUID root | READ_ATTRIBUTES `0x80` | READ `1` | Whole scope/session |
| Trusted ancestors, Temp and child directories | LIST_DIRECTORY + READ_ATTRIBUTES + SYNCHRONIZE `0x100081` | READ `1` | Scope/session or active subtree |
| File metadata | READ_ATTRIBUTES + SYNCHRONIZE `0x100080` | READ `1` | One metadata operation |
| Prototype owned anchor and three fixture ancestors/leaf | Directory `0x100081` | READ \| WRITE `3` | One initialization/batch/failure lease |
| Prototype enumeration cursor | Directory `0x100081` | READ \| WRITE \| DELETE `7` | Diagnostic session, including gaps |
| Prototype anchor path bootstrap | READ_ATTRIBUTES `0x80` | All `7` | Initial final-GUID-path lookup only |

The owned anchor is an ordinary movable fixture directory, unlike an immutable volume GUID root. Its list-access share-3 handle intentionally pins that synthetic boundary too. Attribute-only access does not provide the same sharing exclusion. This adds a fourth protected fixture directory; it is not a proposal to enumerate a production volume root.

Directory relative opens use FILE_OPEN, DIRECTORY_FILE, SYNCHRONOUS_IO_NONALERT and OPEN_REPARSE_POINT (`0x00200021`), matching production except share `3`. Absolute anchor/cursor bootstrap uses OPEN_EXISTING, BACKUP_SEMANTICS and OPEN_REPARSE_POINT. No target content-read, write, DELETE or privilege access is requested by the cursor/lease code. Ancestors are opened and inspected, never enumerated. Only the fixed owned leaf is enumerated.

Before each native batch, the prototype reopens the anchor and three components, opens children relative to their held parent, and checks directory type, nonzero stable identity, unsafe/reparse attributes and the exact expected final volume-GUID path. Every component remains pinned through the native query and bounded record parsing. The retained fully shared cursor was created beneath the initial protected chain; subsequent leases must match those identities before it is queried. A changed leaf cannot silently redirect that cursor to a replacement. Disposal runs in reverse order, including failed acquisition. There is no busy-file retry loop.

This tests an **already established synthetic trusted anchor**. It does not implement OS-known-folder/system-volume provenance, protect ancestors above the owned anchor, or prove arbitrary production path trust. It is flat enumeration, depth zero below the selected leaf, with no recursive port adapter or per-file metadata revalidation. These limits are deliberate: production redesign is not authorized.

The applicable native semantics were checked against Microsoft's [CreateFile sharing rules](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew), [relative NtCreateFile requests](https://learn.microsoft.com/en-us/windows/win32/api/winternl/nf-winternl-ntcreatefile) and [handle-based directory information queries](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getfileinformationbyhandleex). Sharing exclusion lasts until close; shorter lifetime does not eliminate conflicts. Attribute-only access is treated differently by sharing checks.

## Separate-process results and containment schedules

Fixtures are new GUID owners under ignored `artifacts/task-003-rework-02/fixtures`. Every competing filesystem operation runs in a child process with its PID/error recorded. Setup writes synthetic zero-to-two-byte files. Test plumbing checks both resolved paths before any directory relocation, confines them to one marked owner, rejects reparse ancestry/escapes, and refuses owner relocation. Creation, move and link operations exist only in fixture plumbing. No fixture is removed. The prototype never calls the real Temp scope resolver, moves a real profile/system directory, or reads file contents.

`0` means success; `32` means ERROR_SHARING_VIOLATION. Competing opens request share `7` and never delete or write through those handles.

| Held mode/role | Attribute `0x80` | List `1` | Add-file `2` | Write attributes `0x100` | DELETE access `0x10000` |
| --- | --- | --- | --- | --- | --- |
| A: original root-style attribute handle | 0 | 0 | 0 | 0 | 0 |
| A: each original ancestor/Temp/child list handle | 0 | 0 | **32** | 0 | **32** |
| A: original file-attribute handle | 0 | Not requested | 0 | 0 | 0 |
| B: each of four share-3 protected directories, during lease | 0 | 0 | 0 | 0 | **32** |
| B: same directories between leases, fully shared cursor retained | Not repeated | Not repeated | Not repeated | Not repeated | **0** |
| C: full-sharing directory candidate | 0 | 0 | 0 | 0 | 0 |

Reference diagnostic rerun: A **18 successes / 6 conflicts**, C **24 successes / 0 conflicts**, including file-metadata rows. B during-lease matrix: **16 successes / 4 conflicts**; between-lease DELETE opens: **4/4 successful**. Actual creation of a new empty fixture file during a protected lease also succeeded. These are compatible-access observations on this NTFS host, not a promise that exclusive opens, ACL restrictions or every application operation will succeed.

| Schedule | Observed result |
| --- | --- |
| Move ancestor, Temp or child after protected validation and before enumeration | Each separate-process move failed with **32**. The subsequent query occurred while those same pins remained held. |
| Out-and-back during protected query interval | First leg failed with **32**, so the relocation schedule could not enter the gap. |
| Child moved out between leases and left outside | Move succeeded; fresh ancestry open failed (`C0000034`) **before any cursor enumeration**. |
| Child moved out, new entry created there, child moved back, then next batch | Both moves succeeded; query count stayed **zero while outside**. Only protected reacquisition then enumerated the restored in-scope directory. This accepts its current metadata, not an outside query later justified by postvalidation. |
| Child replaced with ordinary directory | New identity rejected before querying retained cursor. |
| Child replaced with actual junction to owned outside directory | Reparse/identity rejection before enumeration; target not followed. |
| Child replaced with symlink | Creation returned **1314**; actual symlink replacement **not exercised**. No elevation, privilege grant or developer-mode change. Existing deterministic unsafe/symlink metadata tests still pass. |
| Ancestor and Temp cross-parent moves **between leases**, with zero restrictive handles | Both failed with **ERROR_ACCESS_DENIED 5** while only the fully shared descendant cursor remained open. This is OS refusal/residual interference, not scanner detection. |
| Unchanged full-sharing candidate's validation/query gap and out-and-back | Reference probe again enumerated outside trusted ancestry; out-and-back passed later validation. C remains unacceptable. |

Cancellation was injected with a lease held before the query and after a successful query/before parsing. All restrictive handles closed. Tests also cover exceptions after each of four acquisitions, before/after query, a disposed-cursor native-query failure, missing/replaced/reparse ancestry, entry/memory/time budgets, and normal EOF. SafeHandle closure is asserted; separate-process DELETE opens after cancellation/acquisition failures succeed. This is controlled coverage of implemented paths, not exhaustive kernel fault injection or process-crash testing.

## Duration, cursor and performance evidence

Final result: `artifacts/task-003-rework-02/lease-results.json`, copied from `lease-probe-b7c101393ed045fc8a0b9c9449b43f9a.json`. Each lease records a conservative span from before opening to after disposal. Per-handle intervals run from native open return to close completion, in anchor/ancestor/Temp/child order; they exclude time inside the open call. These are application stopwatch observations, not a filesystem filter/ETW trace.

For **512 stable files**, 19 native calls including EOF:

| Measurement | Final observation |
| --- | --- |
| Ordinary batch lease spans, 19 samples | Min **0.1650 ms**, median **0.1839 ms**, max **0.6896 ms** |
| Anchor observed open-return-to-close | 0.1566–0.6645 ms |
| Ancestor observed interval | 0.1091–0.5929 ms |
| Temp observed interval | 0.0696–0.5412 ms |
| Leaf observed interval | 0.0353–0.4915 ms |
| B enumeration drain | **4.3953 ms**, excludes constructor initialization |
| B initialization protected span for that fixture | 0.3683 ms |
| B restrictive directory opens / ID-and-path queries | **80 / 160**; additionally one temporary attribute anchor and one full-sharing cursor open, plus three bootstrap/cursor metadata queries |
| A flat native control, 4 retained opens / 19 queries | **4.9583 ms** whole open-to-dispose envelope |
| C flat full-sharing control, 4 retained opens / 19 queries | **3.1739 ms** whole open-to-dispose envelope |

A/C controls use the same 4 KiB buffer and the unchanged validator once per native batch. They do not execute the production per-entry traversal/use case. B's drain timer excludes its constructor; A/C timers include their setup/disposal. These single-fixture timings, measured near other build activity, are not comparative throughput rankings or evidence of worst-case latency. They show measured local durations and the extra reopen count. The cold first B initialization elsewhere took **4.7214 ms**.

The deliberately held sharing matrix lasted **2067.0148 ms**, and the gated move schedule **385.7845 ms**, because the parent waited for separate child processes while holding the lease. These forced holds are reported separately, not hidden in ordinary-batch averages. A forced scheduling stall produced a **5058.1641 ms** span; the five-second cooperative budget then refused the query. Small batches do **not** impose a hard hold-duration limit on a descheduled thread, slow native call or filesystem filter.

The fully shared file object preserves native enumeration position: **512/512** stable entries returned once with zero duplicates, across repeated close/reopen cycles of the protective chain. An explicit diagnostic cursor restart produced **512 duplicate identities**, all suppressed, and zero newly accepted files (38 cumulative native calls). Closing the cursor itself instead would lose enumeration position; restarting and skipping prior entries duplicates I/O and can become quadratic. Retaining this cursor avoids that restart cost but introduces the measured ancestor-move refusal. Current identity/path checks invalidate a moved/replaced target before the next query; there is no promise of completeness under concurrent insertion, deletion or renaming.

Bounds are one **4 KiB** buffer, at most **40 records/query**, **2,048** examined entries/retained IDs, **128** lease attempts, eight ancestry components maximum (four used), depth **zero**, and a cooperative **five-second** budget. Diagnostic handle/timing instrumentation is also bounded to at most 512 restrictive wrappers plus timing arrays. A 2,050-file fixture stopped at the entry/memory budget with 2,048 retained IDs. A nested fixture directory was skipped without traversing it. Escaped and unowned fixture paths were refused. No whole directory content list, unbounded recursion, aggressive reopen retry or production setting was added.

## Alternatives for architectural review

| Criterion | A — existing long restrictive handles | B — short protected leases | C — fully shared best effort |
| --- | --- | --- | --- |
| Containment | Existing relative opens and held ancestry prevent the tested relocations; reparse/identity checks remain. File metadata is still not an atomic snapshot. | Tested enumeration protected while all synthetic ancestors are pinned. A complete production trust resolver, recursive design and in-place reparse/namespace analysis are outstanding. | Relocation can put the next handle query outside scope; postvalidation and ABA checks do not repair it. |
| Interference | Six measured sharing conflicts; restrictive ancestry held across session/subtrees. | Four DELETE-open conflicts during lease; compatible writes pass. Fully shared retained cursor still causes two between-lease ancestor move errors 5. No hard maximum hold time. | Zero measured compatible-open conflicts, but open descendants can still affect ancestor moves. |
| I/O/memory | Chain opened once; production 64 KiB buffer per active depth, bounded IDs/entries. | Reopen/identity/path work each batch; 80 restrictive opens in flat fixture vs A/C four. Prototype 4 KiB plus bounded IDs/metrics; a recursive product design would add cursor/lifetime complexity. | Native cursor can stream cheaply; bounded buffers possible, but safety failure remains. |
| Lifetime/complexity | Simple DFS retained handles; default scan budget ten seconds does not interrupt synchronous calls. | Restrictive handles per batch, full-sharing cursor per session. Requires explicit cursor ownership, reacquisition, invalidation, budgets and failure disposal. | Long fully shared handles plus checks; simpler interference policy but weaker containment. |
| Future cleanup | Scanner counts/identities do not authorize action. | Between-lease gaps invalidate any assumption of durable authorization; a new independently reviewed execution boundary is required. | Cannot support cleanup under the existing strict boundary; no promotion authorized. |

B reduces the observed conflict window and permits write-compatible access, but does not establish non-interfering operation. A low-impact acceptance threshold was not assumed, and the synthetic-anchor proof was not represented as production containment certification. Retain the mitigation and stop this investigation for architectural review. No trust requirement was relaxed.

## Exact regression and runtime verification

Working directory `E:\01 - Work\cleanup-windows`; Windows `10.0.26300.0`, .NET SDK `10.0.302`, x64 .NET test runtime `10.0.10`. Host tool invocations used the normal-user session outside the restricted process sandbox, without Administrator/UAC elevation. Diagnostics reject Administrator tokens; production observers report `Elevated: false`.

```powershell
dotnet restore CleanSpace.slnx --disable-parallel --force --no-http-cache --verbosity minimal -m:1 -p:NuGetAudit=true -p:NuGetAuditMode=all
dotnet build CleanSpace.slnx --no-restore --no-incremental --verbosity minimal -m:1 -p:UseSharedCompilation=false
dotnet test CleanSpace.slnx --no-build --no-restore --verbosity normal -m:1 --logger 'trx;LogFilePrefix=rework-02' --results-directory artifacts/task-003-rework-02/test-results --blame-hang-timeout 30s
dotnet restore diagnostics/ScanLeaseProbe/ScanLeaseProbe.csproj --disable-parallel --verbosity minimal -p:NuGetAudit=true -p:NuGetAuditMode=all
dotnet build diagnostics/ScanLeaseProbe/ScanLeaseProbe.csproj --no-restore --verbosity minimal -m:1 -p:UseSharedCompilation=false
dotnet run --project diagnostics/ScanLeaseProbe/ScanLeaseProbe.csproj --no-build --no-restore
dotnet run --project diagnostics/ScanSharingProbe/ScanSharingProbe.csproj --no-build --no-restore
dotnet build diagnostics/QuickScanUiProbe/QuickScanUiProbe.csproj --no-restore --verbosity minimal -m:1 -p:UseSharedCompilation=false
dotnet build diagnostics/DriveOverviewUiProbe/DriveOverviewUiProbe.csproj --no-restore --verbosity minimal -m:1 -p:UseSharedCompilation=false
git diff --check
```

Outputs were captured via `Tee-Object` under `artifacts/task-003-rework-02/`, preserving native exit codes. No audit warnings were suppressed.

| Verification | Evidence/result |
| --- | --- |
| Full forced audited restore | Exit 0; eight projects, no warnings; `restore-audit.log` |
| Full no-incremental solution build | Exit 0; **0 warnings/errors**, **26.06 s**; `build.log` |
| All solution tests | **169 passed / 0 failed / 0 skipped**, **11.42 s**; App 31, Application 67, Core 31, Integration 40; `tests.log`, `test-results/`, `test-summary.json` |
| Scanner regression/API guards | Existing cancellation, budgets, path/volume trust, real junction rejection, metadata changes and reviewed metadata-only native import checks pass. Scanner sources unchanged. |
| Lease prototype | Audited restore exit 0; final build 0 warnings/errors, **2.47 s**; final run exit 0, **208 assertions**; `probe-restore.log`, `probe-build-final.log`, `probe-console-final.log`, `lease-results.json` |
| Reference A/C probe | Exit 0, **214 assertions**; `reference-sharing-console.log`, `reference-sharing-results.json` (rerun `sharing-probe-ab49a22b931d4c8d9d765887de06f90e.json`) |
| Existing fake WinUI probe compatibility builds | Both exit 0, 0 warnings/errors; Quick Scan **14.02 s**, Drive Overview **13.93 s**; corresponding `*-probe-compat-build.log` |
| Final whitespace/scope check | `git diff --check` exit 0 (Git's LF/CRLF conversion notices only); no staged changes or active app/probe processes; `final-checks.json`, `preservation.json` |

An earlier lease run passed 204 assertions; the final run adds ancestor movement checks and per-handle timing. Both JSONs remain retained; final evidence above is authoritative. No build/test failure was converted into a pass. The unavailable native symlink scenario remains explicitly unverified.

Three unchanged production observer invocations:

```powershell
./diagnostics/Verify-ProductionStartup.ps1 -RunId Task003-Rework02-1
./diagnostics/Verify-ProductionStartup.ps1 -RunId Task003-Rework02-2
./diagnostics/Verify-ProductionStartup.ps1 -RunId Task003-Rework02-3
```

| Run | PID / window | Start → normal exit (UTC+07:00) | Result |
| --- | --- | --- | --- |
| 1 | 26932 / 658012 | 15:56:19 → 15:58:20 | PASS, exit 0, no matching crash events |
| 2 | 5932 / 2884278 | 15:58:22 → 15:58:47 | PASS, exit 0, no matching crash events |
| 3 | 11856 / 1182022 | 15:58:50 → 15:59:12 | PASS, exit 0, no matching crash events |

All three launched the actual rebuilt production EXE, observed a stable window and matching pinned XAML module, queried crash events successfully, and were closed normally with Alt+F4. No process was killed. Detailed JSON/checkpoints remain under `artifacts/fix-06/launches/Task003-Rework02-*`; console logs are `production-{1,2,3}.log`. EXE SHA256 `7B51618CFBA530B76AA4544C7A42850D2EC9E7C3C090645772558C4B7340BBCB`; managed assembly `0B694CEFA38A508DEC395E0CEA4FFF035B323B00848BDC1220D86027C52ED6E4`; MainWindow XBF `44E53D3527BD733410F801977F6B70F2B4985DF34C38AF735D23A08561492408`. SDK remains WindowsAppSDK 2.5.1 / WinUI package 2.3.9; XAML payload matches its pinned hash.

Native computer-use automation inspected actual production screenshots/accessibility trees. Retained text under `artifacts/task-003-rework-02/ui/` shows Dashboard Quick Scan disabled, disabled-button click leaving Dashboard unchanged, Scan Results Idle/zero totals with disabled Start/Cancel, and disabled Start click leaving it unchanged. Dashboard/Scan Results/Settings navigation works. Drive Refresh updated the timestamp and volume values. Light and Dark were applied and visually inspected; the original **System** selection was then restored and applied. Cleanup remained disabled throughout. Two further startups again showed the unavailable notice and disabled Quick Scan. Final process check found no production/probe processes.

## Remaining limitations and decision

The diagnostic establishes controlled fixture results, not a universal containment proof. Native symlink creation was unavailable; arbitrary in-place reparse changes, filesystem filter behavior, hostile mutation of the synthetic boundary's repository ancestry, recursive cursor lifetimes, file-by-file validation and cancellation inside a blocked native call remain unresolved. A current final-path/identity observation alone never authorizes a later unprotected query. Concurrent changes also prevent a complete atomic scan snapshot, regardless of deduplication.

No cleanup/deletion/recycle operation, content hashing/reading, file-moving product feature, ACL change, registry cleanup, service, privilege helper, safeguard change, runtime network feature, SDK/package update or real user/system Temp mutation was introduced. Only guarded test plumbing relocates owned fixture directories. Theme verification uses the already authorized app settings adapter and restores the original preference. No host-wide filesystem trace was collected; this is not a claim that Windows/.NET/build tools perform zero disk writes.

**Accept the temporary UI mitigation for review; do not accept or re-enable TASK-003 scanning.** Choosing a production replacement requires an explicit architecture decision about interference, latency and a complete containment design. Option C remains rejected under the current requirement. Stop here for architectural review; no TASK-004, cleanup, commit or push.
