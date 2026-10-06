import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { readWorkbook, suggest, toText } from '../../docs/web/import.mjs';
import { parseTable, audit } from '../../docs/web/engine.mjs';
const fixture = readFileSync(new URL('../fixtures/checklist.xlsx', import.meta.url));
test('xlsx preserves worksheets, notes, repeats, cached values and displayed zero padding', () => {
  const sheets = readWorkbook(fixture);
  assert.deepEqual(sheets.map(s => s.name), ['交付清单', '无表头']);
  assert.equal(sheets[0].rows[1][2], '0007');
  assert.equal(sheets[0].rows[1][3], '已保存结果');
  assert.equal(sheets[0].rows[4][1], '合同.pdf');
  assert.equal(sheets[0].rows[5][0], '两行\n备注，保留原文');
  assert.deepEqual(parseTable(toText(sheets[0].rows), '\t'), sheets[0].rows);
});
test('xlsx suggested filename column leads to real missing / duplicate results', () => {
  const { rows } = readWorkbook(fixture)[0];
  assert.deepEqual(suggest(rows), { hasHeader: true, nameColumn: 1 });
  const result = audit(rows.slice(1), ['客户/合同.pdf', '盖章/报价单.pdf', '草稿/报价单.pdf', '附件.txt'], 'exact', 1);
  assert.deepEqual(result.map(r => r.status), ['Ready', 'Ambiguous', 'Missing', 'Ready', 'Ready']);
});
test('headerless imports keep first file and unknown headings are not silently dropped', () => {
  assert.deepEqual(suggest(readWorkbook(fixture)[1].rows), { hasHeader: false, nameColumn: 0 });
  assert.deepEqual(suggest([['合同.pdf'], ['报价单.pdf']]), { hasHeader: false, nameColumn: 0 });
  assert.deepEqual(suggest([['Notes', 'Filename'], ['001', 'a.txt']]), { hasHeader: true, nameColumn: 1 });
  assert.equal(suggest([['可能是文件'], ['a.txt']]).hasHeader, false);
});
test('invalid and oversized workbook input gives a useful rejection', () => {
  assert.throws(() => readWorkbook(new TextEncoder().encode('hello')), /xlsx|Excel/i);
  assert.throws(() => readWorkbook(new Uint8Array(16_000_001)), /16 MB/);
});
