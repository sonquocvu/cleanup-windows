# TASK-006 — Controlled Recycle Cleanup (BLOCKED by default)

**DO NOT IMPLEMENT WITHOUT A NEW EXPLICIT APPROVAL** based on Task 005 security findings. This file is planning input, not standing permission to delete anything.

## Conditional objective
After security review, implement a narrowly scoped, user-confirmed, recycle-only cleanup mechanism for safe test-proven file candidates.

## Minimum conditions
- No elevation, no recursive deletion, no permanent delete, no fallback when recycle is unavailable.
- UI shows item count, logical bytes, and explicit pre-operation consent. No pre-selected ambiguous candidates.
- Revalidate identity and path with approved anti-TOCTOU design; reject candidates that cannot be safely bound to actual operation target.
- Isolated Windows Shell/OS adapter with correct STA behavior, per-file success/failure/cancel outcomes and rigorous error handling.
- Tests use only expendable fixtures and require separate approval for destructive tests.
- Report "moved to Recycle Bin" separately from "actual free-space change".

## Gate / approval
Requires documented threat-model acceptance, passing adversarial tests, independent diff review and user authorization. If identity-binding cannot be demonstrated satisfactorily, leave CleanSpace a read-only analyzer instead.
