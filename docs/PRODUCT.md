# CleanSpace — Product Requirements (MVP)

## Vision
A minimal Windows 10/11 native desktop application that **explains disk usage and identifies conservatively scoped, potentially unnecessary files**, with user control over any future cleanup.

## User experience
- Native Fluent UI, light/dark/system theme, keyboard navigation and accessible contrast.
- Four navigation areas: Dashboard, Scan Results, Cleanup (disabled until authorized), Settings.
- Dashboard shows OS volume's total/used/free space and action to launch Quick Scan.
- Scan Results groups candidates by rule/category, shows metadata and reason, and lets users review without changing files.
- Precise distinction: *possible candidate size* is not *guaranteed recoverable bytes*.

## MVP v0.1 scope
- Detect the real Windows system volume and display available/used/total bytes.
- Quick Scan only in approved, per-user temporary-file scope; metadata only; no file content/hashing.
- Cancel scan, keep UI responsive, report skipped/inaccessible entries.
- Show candidate list and categorization with non-destructive reasons.
- Domain risk classes: `Eligible`, `ReviewRequired`, `Protected` (classification alone cannot grant deletion).
- No destructive functionality: no cleanup adapter in runtime, buttons disabled or explicitly marked unavailable.

## v0.2 scope
- Reusable, unit-tested safety policy, path and object-identity abstractions, edge-case and adversarial test fixtures.
- Review-only cleanup plan generation, without executing it.

## v0.3 conditional scope (requires explicit review)
- Recycle Bin cleanup for **user-confirmed and strongly verified** candidates only, if a safe execution design survives threat modeling and Windows integration testing.
- Real per-item outcome and accurate reporting; no fallback to permanent deletion.

## Explicit non-goals for MVP
- AI autonomous deletion, registry cleaner, RAM optimizer, driver cleaner, WinSxS/Windows Update deletion.
- Auto cleanup, scheduled runs, start-on-boot, permanent deletion, privilege elevation.
- Scanning entire drive by default, deletion from Downloads/Documents/Desktop, deduplication and browser-profile cleaning.
- Claims of guaranteed performance improvement or zero risk.

## Quality requirements
- Read-only scan makes no deliberate writes to targets.
- No requirement for admin privileges.
- Off-UI-thread work; cancellation and bounded progress update frequency.
- Testable core with deterministic fake file-system fixtures.
- Distribution packaging deferred until toolchain, signing, installation and uninstallation are validated.

## UX acceptance
User can open the app, see system drive usage, choose Quick Scan, cancel it, inspect candidates and understand why **nothing has been deleted**.
