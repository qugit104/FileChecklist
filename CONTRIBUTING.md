# Contributing

Useful reports include a small, invented checklist and folder tree that reproduce the problem. Please remove private file paths and never attach customer files.

Before adding a matching rule, describe what should match **and what must not match**. Ambiguous candidates must remain a user decision. Do not replace an existing target or modify source files.

Use .NET SDK 9. Run `dotnet run --project tests/FileChecklist.Tests -c Release` for the core and CLI; Windows UI changes also require `powershell -NoProfile -ExecutionPolicy Bypass -File tools/test.ps1`. Include a failing behavior test for matching and copy regressions. Visual adjustments need screenshots at normal and minimum window sizes.

Keep changes focused. The first release supports local files, CSV/TSV/pasted tables, one file selected per row, and a flat destination folder. Proposals for other workflows are welcome with a concrete example.
