# 0.3 — English desktop workflow

The English browser demo and README previously led to a Chinese-only desktop app. This version makes the actual import → scan → choose → copy → resume workflow available in English, while retaining Simplified Chinese.

The UI follows the process display culture (Chinese for `zh` locales, English otherwise). Desktop launch options `--lang en` and `--lang zh` override it. Built-in scan details, copy errors, generated column labels and CSV reports use that language. Operating-system dialogs and errors can still follow OS settings. User filenames, original columns, notes and task titles are not translated. Existing `.fctask` files keep the same schema and require a fresh scan as before.

Status cells are wider so “Ready to copy” fits. Truncated candidate paths expose the complete path in a tooltip. The help text now documents all three matching modes. Packages include the public documentation referenced by their READMEs.

## Acceptance and verification

| Requirement | Evidence |
| --- | --- |
| English scan, errors and report labels | New core tests deliver a Chinese-named source, retain original cells, and verify English report status/headers |
| Background scans inherit language | A worker-thread scan reports an incomplete scan in English |
| Existing Chinese task resumes in English | Completed copy remains verified; original Chinese title remains; invalid-row guidance refreshes after scan |
| Chinese behavior retained | Original 38 behavior tests now explicitly select Chinese UI culture for deterministic diagnostic assertions; no assertions removed |
| Real UI works in both languages | `tools/test.ps1` starts isolated WPF processes with `--lang zh` and `--lang en`; each imports, scans, chooses a candidate, previews, copies, restores and supplements |
| Report corresponds to selected language | Each UI smoke inspects the generated report, including its missing row |
| Layout remains usable | Rendered screenshots inspected at 1240 × 780 and the 1020 × 620 minimum; import and copy-preview windows inspected |

Baseline: `.NET SDK 9.0.312`, 38 tests passed, WPF workflow passed. Before implementation, all three new language tests failed (exit 1) with Chinese statuses / messages. The expanded cross-language invalid-row assertion also failed before its scan-message correction. After the correction, all **41 tests** and both WPF language workflows passed (exit 0, desktop build: zero warnings/errors).

Commands executed from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test.ps1
dotnet run --project tests/FileChecklist.Tests -c Release
```

The existing browser has no source changes in this release; its 17 tests were not rerun locally. No claims are made about every Windows version, high-DPI scale, or OS-provided dialog translation. The executable remains unsigned. See [0.2 validation](RELEASE-0.2.md) for copy-integrity and matching coverage.
