# Browser tool

[Open FileChecklist](https://qugit104.github.io/FileChecklist/)

## Usage

1. Open an `.xlsx`, CSV or TSV checklist, or paste cells from Excel.
2. Select the worksheet and filename column. Adjust the header setting if needed.
3. Choose a folder or paste file paths, one per line.
4. Click **Check list**, select any ambiguous files, then export the CSV report.

Folder selection includes subfolders. Results initially show missing, ambiguous and invalid rows. The report includes every checklist row, its original columns and the selected path.

The Windows app adds file copying, task saving and supplemental delivery. See [downloads](https://github.com/qugit104/FileChecklist/releases).

## Formats and limits

- Excel: up to 16 MB, 100 worksheets, 512 columns and 20,000 rows per worksheet. Formatted values such as `0007` are retained. Formulas use their saved results.
- Text: up to two million characters per input. Paths are limited to 100,000 entries, with one path per line.
- Display: up to 500 result rows and 200 candidate paths at a time. Filters narrow the view; CSV export includes all rows.
- CSV: formula prefixes are escaped. When opening the report in Excel, import ID columns as text to retain leading zeros.

The checklist and folder names are processed in the browser. Folder selection reads names and relative paths. Reloading or closing the page clears the current data and selections.

## Development

Requires Node.js 24.

```sh
node --test tests/web/*.test.mjs
node --check docs/web/app.mjs
node tools/serve-demo.mjs
```

Open `http://127.0.0.1:4177`. GitHub Pages serves `docs/`.

Excel parsing runs in a worker with a 15-second timeout, using the bundled [SheetJS module](web/vendor/xlsx.mjs). See [third-party components](../THIRD-PARTY-NOTICES.md) for its version and license.
