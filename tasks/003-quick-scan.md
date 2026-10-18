# TASK-003 — Quick Scan (strictly read-only)

**Current status: Scanner implemented but user-facing scan BLOCKED; mitigation accepted.** Keep Dashboard Quick Scan and Scan Results Start disabled. Preserve scanner code/tests. No redesign, new native prototype or re-enablement is authorized by this specification. [ADR-003](../docs/ADR-003-QUICK-SCAN-SAFETY.md) records the accepted decision and evidence gates; a separately approved safe design is required. The original scope below is retained as planning context, not current feature availability or authorization for classification/TASK-004 work.

## Objective
List conservative temporary-file candidates from a single current-user, validated Temp scope. No cleanup or file content reads.

## Scope
- Resolve and validate a trustworthy current-user LocalAppData Temp root on system volume. Reject redirected, reparse or untrusted roots.
- Implement incremental metadata enumeration, skip reparse nodes, nested redirections, offline placeholders; handle inaccessible entries and return counts.
- Add `QuickScanUseCase`, cancellation, single-scan guard, bounded batch progress, ViewModel and results list.
- Candidate metadata: opaque session-bound ID, display path, size, modified time, reason and safety classification.
- A candidate may be suggested if conservative eligibility metadata exists (e.g. unchanged >=7 days) but must not imply deletion permission.
- Tests: denied access, cancellation, nested symlink, empty tree, huge/deep tree, path prefix collision, unexpected volume.

## Prohibitions
Do not add a cleanup operation, never open file contents, no hashing, no registry/OS cleanup, no privileges, no arbitrary `C:\\` recursive scan, no real-world destructive tests.

## Acceptance
Scan and cancel both keep UI responsive; errors display non-destructively; results never claim guaranteed space recovery; unchanged before/after fixture hashes/metadata as practicable; observed build and test commands; stop after task.
