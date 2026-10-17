# TASK-004 — Safety Policy (read-only)

## Objective
Codify pure, deterministic policy and Windows identity interfaces so later cleanup cannot bypass review. No cleanup implementation.

## Scope
- Introduce `SafetyDecision` = Eligible / ReviewRequired / Protected with rationale codes.
- Implement approved-scope provenance and metadata evaluation in Core, without OS APIs.
- Define Infrastructure file/volume identity revalidation abstraction; keep it read-only.
- Unit tests: trusted/untrusted root, reparse, cloud placeholder, hard links, changed timestamps/identity, locked files represented as unknown, noncanonical paths, System32 and Documents fixtures, expired session.
- Integration fixtures: explicitly test-owned directories, symlink and junction behavior where allowed.
- Add short security decision record highlighting what remains unresolved for future Shell cleanup.

## Prohibitions
Never delete, move, recycle or truncate file; no overwrite, elevation, permission adjustment or registry mutation.

## Acceptance
All unknown conditions fail closed; no Shell cleanup compiled; tests execute with evidence; threat model updated; wait for human approval.
