# TASK-005 — Recycle Mechanism Security Spike (NOT production cleanup)

## Gating
Only begin after Tasks 001-004 approved. Must remain separate from shipping code; request permission before any **destructive** sandbox experiment.

## Research questions
1. What are the behavior and limitations of Shell `IFileOperation` with `FOFX_RECYCLEONDELETE`, `FOFX_WANTNUKEWARNING` and relevant flags?
2. How does STA threading fit with a WinUI 3 app without blocking its UI?
3. Can per-object identity be reliably tied to an actual recycle action under adversarial rename/reparse races? If not, which operation mode and UX are acceptable or must be declined?
4. How do same-volume identity, hardlinks, cloud/offline files, process locks and Recycle Bin unavailable cases behave?
5. Can tests reliably distinguish moved-to-bin from permanently deleted outcomes; how should reporting work?

## Deliverables
- Security decision record with alternatives, tradeoffs, risks and explicit go/no-go recommendation.
- Mock-based tests; optionally controlled disposable fixture demonstrations only after additional authorization.
- No runtime cleanup hook and no permission escalation.

## Exit condition
Human architectural/security review is mandatory before Task 006 may be authorized.
