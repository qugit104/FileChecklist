# FileChecklist

Collect files from a spreadsheet checklist. See what's missing, choose between duplicates, and resume when the missing files arrive.

[中文说明](README.zh-CN.md) · [Download](https://github.com/qugit104/FileChecklist/releases) · [CLI reference](docs/CLI.md) · [Report a problem](https://github.com/qugit104/FileChecklist/issues/new/choose)

![Windows desktop](docs/desktop.png)

## When this helps

Someone sends you a list of 200 filenames or document IDs. The files are scattered across folders, some have several versions, and a few haven't arrived yet.

FileChecklist keeps the **requested rows** as the source of truth. It retains notes and repeated requests, records missing items, and copies only confirmed files. Reopen the task later to complete the missing rows without copying everything again.

## Try it

**Windows desktop:** download the `FileChecklist-0.2.0-win-x64.zip` release asset, extract it, and run `FileChecklist.exe`. The interface is currently in Chinese. Choose **帮助 → 打开示例**, then **扫描文件** to try an isolated sample. No .NET installation is required.

**Command line:** download a CLI package, or build with .NET SDK 9. The included sample contains a duplicate name, a repeated request, and one missing attachment:

```sh
filechecklist plan examples/checklist.csv --header --source examples/files --output delivery --task sample.fctask
filechecklist choose sample.fctask --row 2 --candidate 1
filechecklist copy sample.fctask --yes
```

Review the printed candidate path before choosing. The sample returns exit code **2** while its missing attachment remains. `plan` never copies files; `copy` requires `--yes`. On PowerShell use `./filechecklist.exe`. [Full CLI guide](docs/CLI.md).

## Match names or IDs

| Mode | Request | Example match | Behavior |
| --- | --- | --- | --- |
| Complete name | `A001_spec.pdf` | `A001_spec.pdf` | Unique match is ready to copy |
| Without extension | `A001_spec` | `A001_spec.pdf` | Multiple extensions require a choice |
| ID prefix | `A001` | `A001_spec.pdf` | Every candidate requires confirmation |

`A001` does **not** match `A0010_spec.pdf`. Matching is case-insensitive; prefix boundaries are explicit, not a similarity score. One file can be selected per row. See [exact matching rules](docs/CLI.md#matching-modes).

## What gets preserved

- Checklist order, duplicate requests and original columns.
- Your choice between same-name files, after rescanning confirms it is unchanged.
- Every row in the CSV report, including missing and unconfirmed items.
- Completed rows when resuming: source and delivered copies are checked by SHA-256.
- Existing destination files. Conflicts are blocked unless you explicitly enable adding a numeric suffix.

Source files are not modified. There is no account, upload service, telemetry or AI model. Tasks and reports are ordinary local files.

## Limits of this release

- CSV / TSV and pasted Excel cells; direct `.xlsx` import is not implemented.
- Ordinary local folders. Links and reparse points are skipped and reported; cloud placeholders and network drives are not validated workflows.
- Flat output directory. No folder structure replication or automatic multi-file selection per row.
- Desktop UI and CSV diagnostics are currently Chinese. CLI commands and structured status names are English; some detailed error messages remain Chinese.
- Reading matched files for hashing can take time. Large-directory / 100,000-row limits are input guards, not measured performance promises.
- Windows executable is unsigned. Task snapshots and reports contain local paths; review them before sharing.
- A correct filename and checksum do not establish that a document is the correct business version.

## Build and verify

```sh
dotnet run --project tests/FileChecklist.Tests -c Release
dotnet run --project src/FileChecklist.Cli -- --help
```

On Windows, `powershell -NoProfile -ExecutionPolicy Bypass -File tools/test.ps1` also builds and runs the actual WPF import → scan → choose → preview → copy → restore → supplement flow. `tools/publish.ps1` creates portable Windows packages.

The core and CLI have no third-party NuGet dependencies. CI runs behavior tests on Windows and Linux and packages both command-line targets. [Validation notes](docs/RELEASE-0.2.md) · [Contributing](CONTRIBUTING.md) · [MIT license](LICENSE).

## Feedback that will shape the next version

Does your checklist contain IDs, complete filenames, or something else? Which step still requires manual work? Share a small **invented** example in a [workflow issue](https://github.com/qugit104/FileChecklist/issues/new?template=workflow.yml). Real customer documents are not needed.
