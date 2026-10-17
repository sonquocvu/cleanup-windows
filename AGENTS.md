# CleanSpace — Instructions for Codex

## Mission
Build a minimal, responsive and safety-first **Windows native disk analyzer** in C#/.NET 10 + WinUI 3. Implement **one approved task at a time**. Read `docs/PRODUCT.md`, `docs/ARCHITECTURE.md`, `docs/SECURITY.md`, and the selected `tasks/*.md` before editing code.

## Non-negotiable safety constraints

- The initial releases are **read-only**. Do not introduce file deletion, directory deletion, move, recycle, truncate, overwrite, ACL/permission changes, registry cleaning, shell cleanup commands, or privileged helpers unless a separately approved task specifically authorizes the operation.
- No admin requirement, UAC escalation, Windows service, kernel driver, auto-start, scheduled cleanup, background daemon, or hidden cleanup.
- Do not scan `C:\\` recursively. Only enumerate explicitly approved, verified user-scoped directories. Never follow symlinks, junctions, reparse points, mount points, or cloud placeholders.
- Fail closed on unreadable metadata, uncertain path/identity, access denied, changed files, or operation races.
- Never touch `Windows`, `WinSxS`, `System32`, `Program Files`, user Documents/Downloads/Desktop, browser profiles, restore points, or update caches as cleanup targets.
- Do not claim "freed space" when files were only moved to the Recycle Bin. Separate discovery estimates, logical file sizes, moved-to-bin totals and actual free-space deltas.
- No destructive tests on real user folders or system folders. Use disposable fixtures strictly within test-created paths; do not use real user `%TEMP%` as a destructive fixture.
- Never disable Defender, SmartScreen, UAC or Windows safeguards to make a build or test pass.

## Architecture and coding conventions

- Projects: `CleanSpace.App`, `CleanSpace.Application`, `CleanSpace.Core`, `CleanSpace.Infrastructure` plus test projects.
- Core is UI/OS-independent. Application defines orchestration and interfaces. Infrastructure implements OS/file-system ports. App owns WinUI views, view models and DI composition root.
- WinUI code-behind contains only UI wiring. Use MVVM and async/cancellable use cases.
- Use stable Windows App SDK packages pinned via central package management. Avoid preview/experimental dependencies unless approved.
- Use `CancellationToken` and `IProgress`/batched updates for potentially long scans; keep file-system work off the UI thread.
- Prefer small components and clear names. Add tests for rules and edge cases. No broad refactor beyond the approved task.
- Logging must avoid full user file paths by default. Do not send telemetry or access network unless approved for a particular feature.

## Delivery protocol

For each task:
1. Inspect applicable instructions and current code, then briefly state scope.
2. Implement only the selected task, with tests and no unrelated edits.
3. Run build/tests **on the actual available Windows toolchain**; include exact commands and evidence. Never claim success from static inspection.
4. Report files changed, architectural decisions, tests passed/failed/not run, safety checks and blockers.
5. Stop. Do not start the next task, commit, push, deploy, publish, install global tools, or modify security settings without explicit authorization.

If tasks conflict with safety constraints, stop and request review. If tools are missing, report the exact missing prerequisites and do not fabricate verification results.
