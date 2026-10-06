# Command line

Windows and Linux CLI packages are in [Releases](https://github.com/qugit104/FileChecklist/releases). No .NET installation is needed for the self-contained packages. From source, use .NET SDK 9 and replace `filechecklist` below with `dotnet run --project src/FileChecklist.Cli --`.

## Try the included files

From the project root or extracted CLI package:

```sh
filechecklist plan examples/checklist.csv --header --source examples/files --output delivery --task sample.fctask
filechecklist choose sample.fctask --row 2 --candidate 1
filechecklist copy sample.fctask --yes
```

On PowerShell, use `./filechecklist.exe` for an executable in the current directory. The sample intentionally contains a missing file, so `plan` and `copy` return exit code **2**, not 0. Candidate 1/2 is listed with its complete path; review the path before choosing. The copied source remains unchanged.

Add the missing sample file under `examples/files`, then run:

```sh
filechecklist scan sample.fctask
filechecklist copy sample.fctask --yes
```

Completed rows are rechecked and retained. Only newly ready rows are copied. Each copy operation produces a CSV report, including unresolved rows.

## Matching modes

| Mode | Checklist key | Files considered | Selection |
| --- | --- | --- | --- |
| `exact` (default) | `A001_spec.txt` | That complete name | Automatic only if unique |
| `stem` | `A001_spec` | `A001_spec.txt`, `A001_spec.pdf` | Automatic only if unique |
| `prefix` | `A001` | `A001.txt`, `A001_spec.txt`, `A001-final.pdf` | Always requires `choose` |

All name matching ignores case. Prefixes must start at the beginning of the filename stem and end at the end of the stem or before whitespace, `_`, `-`, `.`, `(`, `[`, `（`, or `【`. Thus `A001` never matches `A0010_spec.txt` or `XA001_spec.txt`. A key may itself contain separators: `A-001` can match `A-001_spec.txt`.

```sh
filechecklist plan examples/ids.csv --header --source examples/files --output delivery-by-id --task ids.fctask --mode prefix
```

## Options and exit codes

- `--source`: repeat for several source folders. Subfolders are included; links are skipped.
- `--column 2`: 1-based column containing the name or ID. Default 1.
- `--header`: treat the first nonblank row as a header; otherwise all rows are data.
- `--delimiter comma|tab`: `.csv` defaults to comma; other extensions default to tab. Input must be UTF-8, optionally with BOM.
- `--rename-conflicts`: explicitly allow names such as `file (2).txt`. Existing files are never replaced.
- `--json`: machine-readable rows, candidates, plans, scan errors and copy results. Successful structured output uses English enum names. Error text is plain text; some detailed diagnostics and the CSV report are currently Chinese.

| Exit | Meaning |
| --- | --- |
| 0 | Plan ready, or requested operation completed with no unresolved rows |
| 2 | Missing / unconfirmed / changed rows, incomplete scan or target conflicts remain; partial delivery may have succeeded |
| 1 | File, task or operational error |
| 64 | Invalid command or options; `copy` without `--yes` |

`plan` writes a **new** task file; it refuses to replace an existing task. `scan`, `choose` and `copy` update the supplied task. Candidate numbers refer to the last saved scan: newly appearing files cannot silently change the selected path. A changed or vanished candidate requires another review.

Task files and reports contain local paths. Review them before sharing. Matching verifies names and copy integrity, not whether the document is the correct business version.
