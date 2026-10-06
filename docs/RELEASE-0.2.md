# 0.2 release validation

## Changes

- Exact, filename-without-extension and ID-prefix matching. Prefix results require explicit confirmation, including a single candidate; `A0010` cannot match `A001`.
- `plan`, `scan`, `choose` and `copy` CLI commands, shared with the desktop file engine. Explicit `--yes` to copy, optional JSON output, and nonzero exit codes for unresolved rows.
- Candidate choices refer to the previously reviewed path even if new files change their displayed order.
- Filename matching remains case-insensitive. Physical path comparisons respect the host platform's case semantics.
- MIT license, English / Chinese README, reproducible sample data, issue forms and Windows / Linux CI.

## Local test evidence

Commands run from the project root using .NET SDK 9.0.312:

```powershell
dotnet run --project tests/FileChecklist.Tests -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/publish.ps1
```

The original 25 regression tests passed before implementation. Adding matching tests first produced 6 failures (`TOTAL 32; PASS 26; FAIL 6`, exit 1). Implementing the matching rules made all 32 pass. The CLI was then tested against a stub: 5 new CLI tests failed, exit 1; implementing the CLI made all 37 pass. A further test covers candidate list reordering (38 total); its initial Windows expected-path separator assertion was normalized to an absolute path, without changing which file it expects.

See the public [Actions runs](https://github.com/qugit104/FileChecklist/actions/workflows/verify.yml) for commit-specific results and packaged artifacts. A green test result covers the fixtures described here, not arbitrary filesystem conditions.

Final local run: **38 passed, 0 failed**, exit 0. Desktop build: 0 warnings, 0 errors. WPF smoke: exit 0, five checklist rows delivered with four distinct copied files. The matching-mode selector and normal/minimum window renderings were also checked.

## Coverage and remaining checks

Regression coverage includes CSV quoting, row preservation, exact/stem/prefix matching, missing paths, duplicate names, retained choices, changed sources, same-size/same-timestamp content mutation, saved task rescans, overlapping roots, output conflicts, conflicting files created after preview, source/output containment, repeat-row reuse, task roundtrips, supplemental delivery, pre-cancelled copy, CSV formula neutralization, CLI dry-runs, explicit copy consent, saved candidate identity, task non-overwrite and invalid options.

The Windows smoke flow loads real WPF windows, parses controls, scans a fixture, confirms a candidate, renders copy preview, copies files, saves/reloads the task, adds the missing file, and delivers only that addition. It calls product handlers; it does not automate the system file dialog or actual Excel clipboard.

Not covered: arbitrary cloud/network filesystems, disk-full / power-loss fault injection, every cancellation timing, high-DPI/multi-monitor combinations, stress at the maximum input limits, or human judgment of document versions.
