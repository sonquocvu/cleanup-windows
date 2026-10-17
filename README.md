# CleanSpace — Codex Handoff Blueprint

CleanSpace is a safety-first Windows desktop app for disk space analysis and carefully gated cleanup.

**Status:** Planning documents only. No app has been built or tested by these files.

## Working agreement

- Human: owns approval, product priorities and validation on a real Windows PC.
- Architect (ChatGPT): owns requirements, architecture, safety constraints, task specs and technical review.
- Codex: implements one approved task at a time, reports exact changes and observed build/test results.

## Repository preparation

1. Create an empty Git repository named `CleanSpace` on your Windows PC.
2. Copy **this folder's contents** to its root (`AGENTS.md` must be at repo root).
3. Open the local folder in the Codex Windows app (native PowerShell environment; no WSL needed).
4. Start with `tasks/001-foundation.md` using the handoff prompt below.
5. Review diffs and test output. Approve one task before opening the next.
6. Do not enable any cleanup operations until the separate security gate is approved.

## First Codex message

> Read `AGENTS.md`, `docs/PRODUCT.md`, `docs/ARCHITECTURE.md`, `docs/SECURITY.md`, and `tasks/001-foundation.md`. Implement **only Task 001**. First inspect the Windows development environment. Follow the task scope exactly. Do not implement scanning, delete/move/recycle functions or privilege elevation. Run the actual build/tests supported by this machine and report exact commands/results. If a toolchain prerequisite is missing, explain it and stop instead of claiming success. Do not start Task 002 or commit/push without approval.

## Source-of-truth precedence

1. Current human approval for the task
2. `AGENTS.md` hard safety constraints
3. `docs/SECURITY.md`
4. `docs/ARCHITECTURE.md`
5. `docs/PRODUCT.md` and `docs/ROADMAP.md`
6. Approved individual task spec

If instructions conflict, **do not implement** the unsafe or contradictory step. Surface the conflict and ask for architectural review.

## Documents

- `AGENTS.md` — Codex repository guidance and prohibitions.
- `docs/PRODUCT.md` — product scope and exclusions.
- `docs/ARCHITECTURE.md` — project topology, contracts and flows.
- `docs/SECURITY.md` — threat model, cleanup invariants, release gates.
- `docs/ROADMAP.md` — milestones, dependencies and definition of done.
- `tasks/001-foundation.md` — initial implementation task.
- `tasks/002-drive-overview.md` — system drive overview.
- `tasks/003-quick-scan.md` — allowlisted read-only scanner.
- `tasks/004-safety-engine.md` — domain policy engine.
- `tasks/005-recycle-research.md` — non-production safety spike (no app cleanup).
- `tasks/006-gated-recycle.md` — conditional implementation requiring fresh explicit approval.

## References

- WinUI 3 and Windows App SDK: https://learn.microsoft.com/en-us/windows/apps/
- Windows App SDK stable downloads: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads
- Windows Shell IFileOperation: https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifileoperation
- Codex development workflow: https://developers.openai.com/cookbook/examples/codex/iterating-development-workflows-with-codex
