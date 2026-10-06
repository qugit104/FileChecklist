# Contributing

Useful reports include a small, invented checklist and folder tree that reproduce the problem. Please remove private file paths and never attach customer files.

Before adding a matching rule, describe what should match **and what must not match**. Ambiguous candidates must remain a user decision. Do not replace an existing target or modify source files.

Use .NET SDK 9. Run `dotnet run --project tests/FileChecklist.Tests -c Release` for the core and CLI; Windows UI changes also require `powershell -NoProfile -ExecutionPolicy Bypass -File tools/test.ps1`. Include a failing behavior test for matching and copy regressions. Visual adjustments need screenshots at normal and minimum window sizes.

Built-in Chinese strings are message keys; `src/FileChecklist.Core/English.json` holds their English translations. Use `Text.T` for plain strings and `Text.F` for interpolated messages so argument values are preserved. Desktop XAML uses the `Loc` markup extension. Do not translate user data. The UI smoke runner checks English and Chinese import, scan, selection, preview, report, restore and supplement flows.

Keep changes focused. The first release supports local files, CSV/TSV/pasted tables, one file selected per row, and a flat destination folder. Proposals for other workflows are welcome with a concrete example.

For the browser companion, use Node.js 24 and run `node --test tests/web/engine.test.mjs`. Start `node tools/serve-demo.mjs` for local UI checks. Keep file contents and checklist data in the browser, and preserve the distinction between a filename match and a verified file copy. [Browser validation and limits](docs/BROWSER.md).
