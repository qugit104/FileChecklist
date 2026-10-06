# Browser checklist companion

The browser tool compares a requested list with filenames and exports a CSV audit. It does not copy or hash files, save `.fctask` files, or replace the desktop / CLI workflow.

## Local development

Requires Node.js 24; no package install is needed.

```sh
node --test tests/web/engine.test.mjs
node --check docs/web/app.mjs
node tools/serve-demo.mjs
```

Open `http://127.0.0.1:4177`. The development server exposes only the four browser assets and binds to loopback. GitHub Pages serves the tracked `docs/` directory, with `.nojekyll` disabling Jekyll processing.

## Data handling

- Folder selection uses `File.name` / `File.webkitRelativePath`. No file-content reads, network calls, third-party scripts, accounts, or analytics.
- All input and choices live in memory. Closing or reloading the page loses them.
- A content security policy disables network connections and external scripts.
- A hosting provider still receives ordinary page requests. Filenames and checklist text are not included in URLs or sent to a server.
- CSV exports contain original checklist cells and chosen paths. Spreadsheet formula prefixes are neutralized with a leading apostrophe.
- Pasted paths use one line per file; enclosing double quotes are removed. Filenames containing line breaks cannot be represented in this input.

## Verification, 2026-10-06

Acceptance: preserve original rows and text, expose missing/ambiguous requests, require a choice for ID prefixes, invalidate results after input changes, export all rows even when filtered, and work without uploading the selected files.

Browser interaction checks below ran against the local server. The deployed HTML, JavaScript and CSS were separately fetched over HTTPS and compared with the tested source; all four matched exactly.

| Check | Result |
| --- | --- |
| Initial engine tests before implementation | FAIL: 11 of 12 failed, exit 1; the generic input-error assertion was then made specific |
| Unicode expansion regression before correction | FAIL: `SS.txt` incorrectly matched `ß.txt`, exit 1 |
| `node --test tests/web/engine.test.mjs` on Node v24.18.0 | PASS: 17 behavior tests, exit 0 |
| `node --check docs/web/app.mjs` | PASS: exit 0 |
| `git diff --check` | PASS: exit 0 |
| Actual browser: sample, choose approved duplicate, retain all five report rows | PASS |
| Actual browser: ID prefix excludes A0010, duplicate requests remain separate, one candidate still needs confirmation | PASS |
| Actual browser: editing clears stale results; filtered view exports all rows; formula text neutralized | PASS |
| Actual browser: choose the shipped sample folder | PASS: five relative paths loaded, then checked |
| English / Chinese language switch | PASS: choices retained |
| Layout at 1200 × 850 and 390 × 844 | PASS: desktop columns / mobile stack; no whole-page horizontal overflow |
| Public page and all three assets | PASS: HTTP 200, correct script/style MIME types, exact content match with local tested assets |
| GitHub Actions: browser tests / native verification / Pages deployment | PASS for commit `3054bba` |
| Interacting with the public URL in the in-app browser | NOT VERIFIED: browser control timed out on navigation; public HTTP checks succeeded |
| Browser CSV download completion | NOT VERIFIED: the in-app browser timed out waiting for a download event; no browser error was reported. The visible CSV text fallback was verified |
| Native desktop / CLI local tests in this change | NOT RUN: no native code changes; see the release validation and CI |

The limits (20,000 requests, 100,000 paths, two million characters per input) are guards, not performance guarantees. The table displays up to 500 filtered rows and up to 200 filtered candidates at a time. CSV export includes all results. A correct name does not establish a correct business version.
