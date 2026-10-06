import * as XLSX from './vendor/xlsx.mjs';
import { limits } from './engine.mjs';

export function readWorkbook(bytes) {
  const data = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
  if (data.byteLength > 16_000_000) throw new Error('Use an Excel file smaller than 16 MB.');
  if (data[0] !== 0x50 || data[1] !== 0x4b) throw new Error('Use an unencrypted .xlsx file saved by Excel.');
  let book;
  try { book = XLSX.read(data, { type: 'array', cellText: true, cellFormula: false, sheetRows: limits.rows + 2 }); }
  catch { throw new Error('Use an unencrypted .xlsx file saved by Excel.'); }
  if (!book.SheetNames.length || book.SheetNames.length > 100) throw new Error('Use a workbook with 1 to 100 sheets.');
  let characters = 0;
  return book.SheetNames.map(name => {
    const sheet = book.Sheets[name];
    const range = XLSX.utils.decode_range(sheet['!fullref'] || sheet['!ref'] || 'A1');
    if (range.e.r >= limits.rows + 1 || range.e.c >= 512) throw new Error('Keep each sheet within 20,000 rows and 512 columns.');
    const rows = XLSX.utils.sheet_to_json(sheet, { header: 1, raw: false, defval: '', blankrows: false }).map(row => row.map(String));
    for (const row of rows) for (const cell of row) characters += cell.length;
    if (characters > limits.characters) throw new Error('The checklist exceeds 2,000,000 characters.');
    return { name, rows };
  });
}

export function suggest(rows) {
  if (!rows.length) return { hasHeader: false, nameColumn: 0 };
  const first = rows[0].map(s => s.trim().replace(/[ _]/g, '').toLowerCase());
  for (const label of ['文件名', '文件名称', '附件名称', '附件名', 'filename', 'file', 'documentname', '文件编号', '编号', 'documentid', 'id']) {
    const nameColumn = first.indexOf(label);
    if (nameColumn >= 0) return { hasHeader: true, nameColumn };
  }
  return { hasHeader: false, nameColumn: 0 };
}

export function toText(rows) { return rows.map(row => row.map(cell => '"' + cell.replace(/"/g, '""') + '"').join('\t')).join('\n'); }
