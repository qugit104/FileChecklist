# Contributing

For a bug report, include the version, operating system, matching mode, steps to reproduce and a small checklist with its folder layout. Use sample filenames in public issues.

## Development

Requires .NET SDK 9 and Node.js 24.

```powershell
dotnet run --project tests/FileChecklist.Tests -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test.ps1
node --test tests/web/*.test.mjs
node tools/serve-demo.mjs
```

The PowerShell test script runs the core tests and the Windows desktop workflow in English and Chinese. Run it for desktop changes. For matching or copying fixes, include a test that reproduces the bug. Include screenshots for layout changes.

## Matching and copying

Matching changes need examples of accepted and rejected filenames. Ambiguous candidates require a user selection. Copying preserves source files and refuses to overwrite destination files.

The browser and desktop support `.xlsx`, CSV, TSV and pasted tables. The CLI reads CSV and TSV. The desktop and CLI collect selected files into a flat output folder.

## Localization

Chinese strings are message keys; `src/FileChecklist.Core/English.json` contains the English translations. Use `Text.T` for strings and `Text.F` for interpolated messages. Desktop XAML uses the `Loc` markup extension. Original filenames and checklist cells remain in their input language.
