# TASK-001 — Windows Solution Foundation

## Objective
Build a native WinUI 3 solution skeleton for CleanSpace. Do not implement file scanning or cleanup.

## Pre-read
`AGENTS.md`, `docs/PRODUCT.md`, `docs/ARCHITECTURE.md`, `docs/SECURITY.md`, `docs/ROADMAP.md`.

## Allowed work
- Inspect the local Windows toolchain and report .NET, MSBuild/Visual Studio, Windows App SDK availability.
- Create `CleanSpace.slnx` (or `.sln` when more compatible) and `src/CleanSpace.App`, `.Application`, `.Core`, `.Infrastructure` and `tests/CleanSpace.Core.Tests`, `.Application.Tests`.
- Set valid project references and DI registrations; use centrally pinned stable NuGet packages.
- WinUI app shell: `MainWindow` with NavigationView and placeholders Dashboard, Scan Results, Cleanup (disabled/unavailable), Settings.
- MVVM: Main and Settings ViewModels; light/dark/system theme selection persisted locally.
- Basic unit test proving Core project is testable; add README build commands adapted to observed local Windows environment.

## Forbidden
- No real file enumeration, drive scan, cleanup, `File.Delete`, directory move, Shell invocation, registry/UAC/service, machine-wide setting changes.
- No preemptive implementation of Tasks 002+.
- No commit, push, installation of global tools or network behavior outside package restore without approval.

## Acceptance criteria
- Solution references follow architecture; Domain/Core no WinUI dependencies.
- UI opens; page navigation and theme choices work when tested interactively.
- WinUI build on available Windows toolchain and Core/Application tests pass; if not possible, report blockers, do not claim pass.
- `git diff --check` clean; all files in diff belong to Task 001.
- Cleanup action visibly unavailable and no deletion capability exists.

## Required completion report
1. Summary and file paths added/changed.
2. Exact toolchain versions and build/test commands/results.
3. Which manual UI checks were observed vs not performed.
4. Any assumptions or deviations needing approval.
5. Stop; wait for approval before Task 002.
