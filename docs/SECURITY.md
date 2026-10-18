# CleanSpace — Safety Policy and Threat Model

## Security goals
Prevent CleanSpace from deleting system files, deleting unexpected user data, elevating privileges, following redirected paths, or misrepresenting outcomes. Safety is an explicit release gate; no claims of risk-free cleanup.

## Default posture
- Deny by default. No automatic deletes.
- App starts and normally runs without Administrator.
- User consent for each cleanup plan; no "select all by default" on ambiguous content.
- `Eligible` in classification means **reviewable**, never blanket deletion authority.
- No permanent delete, recursive directory delete, empty Recycle Bin, mass cleanup or command execution in MVP.
- All scan roots must originate from trusted application configuration, not user-supplied arbitrary paths.

## Threats and mitigations
| Threat | Defense | Test |
|---|---|---|
| Junction/symlink escapes approved scope | Reject reparse points in roots, ancestors and entries; verify actual target / volume | Junction points into test protected tree |
| Relative path or prefix confusion | Canonical normalization with segment boundaries + handle-based verification where needed | Prefix siblings, Unicode/case, trailing separators |
| Changed file after scan | Session-bound candidate identity + fresh validation; reject uncertain operations | Replace and update between scan and use |
| TOCTOU between validation and Shell action | Design review, adversarial experiments; **do not release until object binding/risk is resolved to acceptable level** | Concurrent rename/swap stress test |
| System data accidentally classified as junk | Explicit per-user allowlist; protected area defense in depth | System-like paths in fixtures |
| Cloud placeholder hydration/data removal | Skip offline/recall attributes and reparse entries | Cloud-like synthetic metadata fixtures |
| Hard-link unintended mutation/space reporting | Skip multi-link files for initial cleanup | Multi-link fixture |
| File in use | Skip or fail operation without privilege escalation | Open locked file fixture |
| Cleanup failure turns permanent | Recycle-only adapter must refuse fallback | Mock bin unavailable |
| User surprise or data leak | Preview, explicit approval, local logs with path redaction | UI/telemetry test |
| Recursion, infinite loop, RAM pressure | Bounded enumeration, no following links, cancellation, incremental processing | Deep/wide directory fixtures |

## Trusted scan scope for v0.1

**Current Safety Gate:** user-facing TASK-003 scanning remains **BLOCKED** under the accepted [ADR-003](ADR-003-QUICK-SCAN-SAFETY.md). Retain the approved UI disablement and preserve scanner code/tests. Passing functional/diagnostic tests does not approve any evaluated scanning strategy. The scope rules below remain requirements for a separately approved design; do not relax them to resolve sharing conflicts. Any scope-model relaxation requires explicit product/security approval **before implementation**. No new prototype, re-enablement, TASK-004 or TASK-005 work is authorized by this decision.

- A specific verified current-user Local AppData Temp path on the OS volume. Resolve using Windows known-folder/environment APIs with strict validation; missing/redirected/unexpected locations fail closed.
- Do not treat all of `%TEMP%`, any user-entered root, or `C:\\` as inherently safe.
- An entry is not necessarily safe just because its path is beneath the scoped root, or because it is older than 7 days. Age is only a conservative filter.
- Collect metadata only and do not delete files during scan. No OS optimization commands.
- Reject candidate and parent entries with reparse-point flags or identity anomalies. If root's ownership/trust cannot be proven with available APIs, return zero eligible candidates and explain why.

## Eligibility conditions (future cleanup)
All conditions required:
1. Comes from the current, trusted allowlist scope.
2. File, not directory; regular local file on expected volume, not a reparse point or placeholder; multi-link files excluded.
3. Age filter satisfied (initial suggestion: unchanged >=7 days; not sufficient by itself).
4. Trusted canonical resolution and object identity can be verified.
5. File attributes/identity/size/timestamps unchanged between preview and execution.
6. Per-object operations bind to or reliably target the validated object, with residual risk explicitly reviewed.
7. Explicit approval for that session/plan, still within expiration.
8. No access-denied, OS-protected, process-locked or other uncertain condition.

If ANY requirement is false, unknown or unsupported: skip/reject. Never relax a condition automatically.

## Dangerous paths (cleanup denied, defense in depth)
OS directory, WinSxS, System32, Program Files, Program Files (x86), ProgramData, Recycle Bin internals, root of system drive, user Documents/Downloads/Desktop, browser profiles, backup/sync roots, Windows Update internals and installer caches. Protected paths supplement but DO NOT replace narrow allowlisting.

## Recycle Bin reality
Shell `IFileOperation` provides Windows file operations with recycle-related flags and requires STA. A path-based call cannot by itself prove the target is still the object validated immediately before the call. A security spike must analyze behavior under concurrent replacement and decide whether stronger handle-bound operations or other design changes are required. Do not silently perform permanent deletion if the bin operation fails. Moving to bin generally does not reclaim storage.

## Test isolation rules
- Synthetic files only in a uniquely generated test-owned directory under a developer-chosen test fixture root, never real user or Windows Temp for destructive tests.
- Clearly mark and validate fixture root before deleting test artifacts; no open-ended recursive cleanup.
- Run mutation tests with tightly scoped fixtures and test identity assertions.
- Do not disable Defender, UAC or Windows security systems.

## Release gate for enabling cleanup
Requires explicit owner approval after:
- Written threat model updated with remaining risks and accepted limitations.
- Safety policy unit tests and Windows integration tests pass.
- Adversarial junction/symlink/TOCTOU, offline-placeholder and hard-link cases reviewed.
- Shell behavior and all failure/fallback paths audited.
- User sees exact targets and confirms action; no automatic cleanup.
- Full beta QA on expendable synthetic files with no system folder used as test target.

If gate is not satisfied, ship **read-only Analyzer** only.
