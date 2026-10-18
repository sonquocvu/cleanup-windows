# Scan handle sharing audit — TASK-003-SAFETY-01

Non-shipping Windows diagnostic/test executable. It is outside the production solution and has no reference from the app. No packages, production flags or production interfaces are changed.

```powershell
dotnet restore diagnostics/ScanSharingProbe/ScanSharingProbe.csproj --disable-parallel --verbosity minimal -p:NuGetAudit=true -p:NuGetAuditMode=all
dotnet build diagnostics/ScanSharingProbe/ScanSharingProbe.csproj --no-restore --verbosity minimal -m:1 -p:UseSharedCompilation=false
dotnet run --project diagnostics/ScanSharingProbe/ScanSharingProbe.csproj --no-build --no-restore
```

The original-mode tests invoke the compiled production native opens and directory validator by reflection. Volume-root-style access is tested on an owned fixture directory, never on a real Windows volume root. The test-only candidate uses the same native access, disposition, options and relative parent handles, with sharing set to READ | WRITE | DELETE. It is never injected into the production application.

Each open attempt runs in a distinct child process with its own normal-user token and requests mutually compatible sharing. The matrix records 48 opens: original/candidate configurations, volume-root-style/ancestor/Temp-root/child/file metadata roles, and attribute/list/add-file/delete access combinations. Access-right requests do not themselves write or delete anything.

Controlled fixture schedules test persistent child relocation, attempted ancestor/Temp-root relocation, replacement, the gap between ancestry validation and a native directory query, and relocation out and back between two validations. The latter tests deliberately expose counterexamples to a full-sharing patch. **Exit 0 means the audit assertions and counterexamples were reproduced; it does not mean the candidate or product is safe to accept.** Windows-denied relocation attempts are recorded as OS rejection, not scanner detection.

Only uniquely named GUID owners under `artifacts/task-003-safety-01/fixtures/` are accepted. Parent and child verify repository ancestry, an owner marker, segment containment and absence of reparse ancestors. Source and destination are resolved and checked before every fixture directory move. Moves cannot target the owner itself. There is no delete, overwrite, recycle, cleanup, registry, network, elevation or security-setting operation. No file contents are read; the probe creates its own tiny fixture file and owner marker, queries metadata and records JSON. Fixtures are retained without cleanup.

The `--child` modes are private test plumbing, guarded to this artifact namespace. They must not be copied into production. Child mutation modes are only for the explicitly authorized relocation/replacement tests; production user/system Temp is never a fixture target.

Results are written to `artifacts/task-003-safety-01/sharing-probe-<GUID>.json`. They include native error codes and child process IDs. The `Names` values in race witnesses come from directory metadata records, not file contents.
