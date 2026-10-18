# ADR-003 — Quick Scan safety decision and feature freeze

**Status: Accepted architecture decision — Quick Scan remains disabled pending a separately approved safe design.**

Decision date: 2026-10-18. Recorded under TASK-003-ADR-03 following acceptance of the TASK-003-SAFETY-01 investigation and TASK-003-REWORK-02 diagnostic/mitigation work. The production UI mitigation is approved; user-facing scanning is not accepted.

## Context and safety goals

CleanSpace must minimize interference with Windows and other applications, keep enumeration within an explicitly verified trusted scope, inspect metadata without reading file contents or mutating targets, and run as a standard user. These goals apply together: read-only access alone does not establish non-interference or containment. See [PRODUCT](PRODUCT.md), [ARCHITECTURE](ARCHITECTURE.md) and [SECURITY](SECURITY.md).

The implemented scanner resolves a verified current-user Local AppData Temp scope on the Windows volume, uses relative handle traversal, rejects unsafe/reparse entries, verifies volume/path/object identity, and bounds traversal and retained data. The original functional implementation and regression/UI results are recorded in [TASK-003-REPORT](TASK-003-REPORT.md). Its recommendation to accept scanning was followed by the sharing investigation and is not the current authorization state. Historical reports remain unchanged; this accepted decision governs current availability.

## Evaluated strategies

All measurements below are documented separate-process experiments in uniquely owned repository fixtures on the tested Windows/NTFS host. They do not establish universal guarantees across Windows versions, filesystems, filters, access masks or concurrent schedules. No new experiment is authorized by this ADR.

### A — Existing long-lived restrictive handles

The production native adapter requests READ_ATTRIBUTES (`0x80`) on the volume root; LIST_DIRECTORY, READ_ATTRIBUTES and SYNCHRONIZE (`0x100081`) on ancestors, Temp and child directories; and READ_ATTRIBUTES plus SYNCHRONIZE (`0x100080`) on files. All use FILE_SHARE_READ (`1`). Scope ancestry is held for the session, child directories for active subtree traversal, and file handles for individual metadata operations. Ancestors are not enumerated.

The [SAFETY-01 investigation](TASK-003-SAFETY-01-REPORT.md) recorded **18 successful opens and six ERROR_SHARING_VIOLATION (`32`) results** in the original 24-open matrix. Add-file and DELETE-access opens conflicted with each of the ancestor, Temp and child list handles. Attribute reads and write-attribute opens succeeded; root-style and file attribute-only handles did not cause those measured conflicts. This does not mean all writes or ordinary file creation are blocked.

Held ancestry protects the tested traversal schedules but imposes sharing restrictions across the scan/subtree lifetime. The default ten-second scan budget cannot interrupt an in-flight synchronous native call and is not a hard maximum handle lifetime. The measured interference prevents approval to enable this implementation. A is preserved for review, not approved for user-facing scanning.

### B — Short-lived protected ancestry/enumeration leases

The isolated [REWORK-02 prototype](TASK-003-REWORK-02-REPORT.md) opened and validated an owned anchor and three directory components before each bounded enumeration batch, using FILE_SHARE_READ | FILE_SHARE_WRITE (`3`) and the existing directory access mask. These handles pinned the tested ancestry through the query and bounded parsing, then closed. A separate fully shared (`7`) enumeration cursor survived between batches to preserve its position; fresh leases had to match its established identities before querying it.

In the 20-open during-lease matrix, **16 opens succeeded and four DELETE-access opens failed with 32**. Attribute, list, add-file and write-attribute opens succeeded, as did actual empty-file creation in the fixture. After releasing the restrictive handles, **four of four DELETE-access opens succeeded**. This improved write compatibility and reduced the observed restrictive interval; it did not eliminate sharing conflicts.

The tested ancestor/Temp/child relocations and the first leg of an out-and-back schedule were blocked during a protected query interval. A child left outside between leases was rejected before enumeration; replacement by an ordinary directory or actual junction was also rejected before query. In a between-lease out-and-back schedule, no query occurred while outside; enumeration resumed only after protected reacquisition. These results do not rely on discarding an already-outside query after later validation.

However, cross-parent moves of the ancestor and Temp fixture directories failed with **ERROR_ACCESS_DENIED (`5`) even between leases**, with no restrictive handles held and only the fully shared descendant cursor retained. This is OS refusal and residual interference, not scanner detection or a general containment guarantee.

For a stable 512-file fixture, ordinary batch lease spans ranged from **0.1650 to 0.6896 ms**. These are observations, not latency guarantees or accepted safety thresholds. An intentionally forced stall held a lease for **5058.1641 ms** before the cooperative time check refused the query. Native-call/filter latency and process scheduling remain relevant regardless of a short normal batch time.

Stable enumeration returned 512 files once; an explicit cursor restart suppressed 512 duplicate identities. Reopening the protective chain incurred 80 restrictive opens and 160 identity/path queries for that fixture, in addition to bootstrap/cursor operations. Closing the cursor itself loses enumeration position; restarting and skipping previously seen entries duplicates work and can become quadratic. Retaining it has the measured ancestor-move consequence.

B is a flat, depth-zero diagnostic with a synthetic established anchor, a 4 KiB buffer and bounded entries, identities, lease attempts and time. It does not implement production known-folder/system-volume provenance, protection of ancestry above that anchor, recursive cursor lifetimes or per-file revalidation. Its tested containment schedules do not establish a complete production safety proof. Remaining interference and design gaps prevent production approval.

### C — Fully shared best-effort enumeration

The SAFETY-01 candidate retained metadata-only rights and relative/reparse/identity checks but used FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE (`7`). All **24 compatible opens succeeded** in its fixture matrix. Open descendants could still affect some ancestor moves; full sharing did not prove zero interference.

The controlled schedule validated a directory, moved it outside the trusted subtree, then queried its retained handle. Native enumeration returned metadata while the directory was outside. Later validation detected a persistent move only after that access. In the out-and-back schedule, enumeration occurred while outside and subsequent identity/path validation passed after restoration. The experiment controlled the gap between the actual validator and native query using test-only candidate handles; it did not patch production flags or inject a live user-Temp race.

Relative handles bind an object, but those checks alone do not atomically establish its current trusted ancestry for the next query. A successful post-query validation cannot undo outside metadata access, and out-and-back relocation can evade it. C is rejected under the current trusted-scope requirement and must not be promoted to production as a simple sharing-flag change.

## Decision and consequences

Retain the approved production mitigation: Dashboard Quick Scan and Scan Results Start remain disabled, with a short temporary-unavailability notice. Direct UI command execution and navigation must not dispatch a production scan. Drive Overview, navigation and theme functionality remain available; Cleanup remains disabled.

None of A, B or C is approved for user-facing production scanning. A has measured long-lived interference; B has remaining conflicts, cursor effects and an incomplete production containment design; C demonstrates a breach of the current scope boundary. No claim is made that another safe design is impossible.

Preserve the scanner implementation, existing automated tests and diagnostic evidence for future review. Do not redesign the scanner, start another native prototype, add an enabling switch or re-enable scanning under this task. TASK-004 and TASK-005 remain **NOT AUTHORIZED**; accepting the mitigation, diagnostics or this ADR does not authorize those tasks, cleanup research or cleanup execution. Current task states are recorded in the [roadmap](ROADMAP.md).

Passing diagnostic assertions means expected observations were reproduced, including sharing violations and out-of-scope counterexamples. Functional builds, cancellation tests, startup smoke tests and responsive UI do not establish Safety Gate acceptance. Acceptance of an investigation is distinct from approval to expose the investigated feature.

## Limitations still requiring review

- Synchronous native calls, filesystem filters and process scheduling can delay cancellation and release of handles. Cooperative budgets and average/minimum/maximum fixture observations do not impose a universal latency bound.
- Hostile namespace mutation, in-place reparse changes, mutation above a synthetic anchor, recursive traversal and file-by-file identity/path races are not resolved by the flat lease prototype. Attribute-only file handles do not freeze content or pathname, and enumeration is not an atomic snapshot.
- Tests cover particular NTFS fixtures, masks and schedules on one host, not arbitrary filter interactions, ACLs, cloud hydration, filesystems or kernel behavior. OS-denied ancestor moves cannot be generalized into a safety mechanism.
- Actual native symlink replacement in REWORK-02 was **not exercised**: creation returned privilege error **1314**. No elevation or security-setting change was used to bypass it. Deterministic unsafe/symlink metadata tests and actual junction tests do not replace that missing native coverage.
- No host-wide filesystem trace established zero incidental operating-system/runtime writes. The scan boundary remains no deliberate target mutation, no file-content reads and no cleanup authority. Scan counts and identities never authorize future deletion or recycling.

## Gates before any re-enablement

Re-enabling Quick Scan requires a **separate architectural approval** of a specifically proposed safe design, supported by evidence covering all of the following:

1. Define the intended trusted-scope guarantee and show how it is maintained during concurrent directory relocation, replacement and out-and-back races. Do not justify outside access with a later successful validation.
2. Demonstrate acceptable non-interference with concurrent compatible file and directory operations, including creation-compatible access and rename/DELETE-access behavior on ancestors and descendants. Review residual interference explicitly; shorter observed holds alone do not satisfy this gate.
3. Specify handle lifetimes, cursor ownership/invalidation, reopening costs, memory/depth/entry/time budgets and cancellation/disposal behavior. State synchronous-call and scheduling limitations, without inventing quantitative guarantees from fixture averages.
4. Demonstrate reparse-point, volume, ancestry and file-identity protections under the reviewed concurrent schedules, with missing native coverage resolved or explicitly adjudicated during approval. Uncertainty must fail closed.
5. Preserve metadata-only access and standard-user execution, with no file-content reading, target mutation, elevation, privileged helpers or cleanup capability.
6. Provide separately authorized native concurrency fixtures and full regression evidence, including containment, sharing, cancellation, disposal, budgets and cursor/completeness limitations. Tests must stay in owned fixtures; diagnostics passing is evidence for review, not automatic acceptance.
7. Complete actual production WinUI runtime acceptance for the proposed implementation, including startup, explicit Start/Cancel behavior, responsive navigation, themes, disabled Cleanup and the absence of unintended scan entry points.

Any proposal to relax the current trusted-scope model requires **explicit product/security approval before implementation**. This ADR supplies neither that approval nor approval to begin a replacement implementation or new experiment. Additional task authorization remains necessary before that work starts.

## Supporting evidence and freeze verification

| Source | Existing evidence and interpretation |
| --- | --- |
| [TASK-003-REPORT](TASK-003-REPORT.md) | Original implementation, 167 tests and startup/functional UI evidence; its acceptance recommendation is historical. |
| [TASK-003-SAFETY-01-REPORT](TASK-003-SAFETY-01-REPORT.md) | Original/candidate access matrix and deterministic relocation counterexamples; 214 diagnostic assertions reproduced the findings. |
| [TASK-003-REWORK-02-REPORT](TASK-003-REWORK-02-REPORT.md) | Accepted mitigation/investigation, 169 solution tests, 208 final lease assertions, 214 reference assertions, three production startup smoke tests and real WinUI disablement/navigation/theme checks. |

Detailed raw evidence locations are retained in those reports, including `artifacts/task-003-safety-01/sharing-results.json` and `artifacts/task-003-rework-02/{lease-results.json,reference-sharing-results.json,test-summary.json,ui/}`. The repository reports are the durable explanations; ignored local artifacts are supporting evidence, not required portable documentation links.

For this documentation-only task, source review confirmed that [production DI](../src/CleanSpace.App/App.xaml.cs) explicitly constructs the model with `scanAvailable: false`; [ScanResultsViewModel](../src/CleanSpace.App/ViewModels/ScanResultsViewModel.cs) also defaults false and guards Start; and [MainViewModel](../src/CleanSpace.App/ViewModels/MainViewModel.cs) disables and guards Dashboard dispatch. Navigation code changes the selected page without starting a scan. The scanner remains registered and preserved; the gate is application composition, not protection against deliberately written code that opts in.

The existing [ProductionGateBlocksDirectCommandsAndNavigation theory](../tests/CleanSpace.App.Tests/ScanResultsViewModelTests.cs) covers default-disabled and explicitly-disabled construction, false command predicates, direct execution across all navigation pages, zero provider calls, Idle status and zero examined entries. Its prior passing execution is documented in REWORK-02. Fake-provider lifecycle tests explicitly opt in without enabling production.

This task reviews those sources and performs documentation/Git checks only. It does not rerun builds, tests, startup observers, native probes or any filesystem scanner. No production source, existing tests or historical report is changed. Stop here for architectural review.
