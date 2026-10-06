import test from 'node:test';
import assert from 'node:assert/strict';
import { parseTable, audit, reportCsv } from '../../docs/web/engine.mjs';

test('quoted CSV retains embedded commas, newlines and leading zeros', () => {
  assert.deepEqual(parseTable('\ufeffname,note\r\n"a,b.txt","001\nnext"\r\n', ','), [['name','note'],['a,b.txt','001\nnext']]);
});
test('unterminated quotes are rejected', () => assert.throws(() => parseTable('"unfinished', ','), /quote/i));
test('exact lookup preserves duplicates and missing rows', () => {
  const rows = audit([['a.txt','one'],['lost.txt','two'],['a.txt','three']], ['root/a.txt'], 'exact', 0);
  assert.deepEqual(rows.map(r => r.status), ['Ready','Missing','Ready']);
  assert.deepEqual(rows.map(r => r.cells[1]), ['one','two','three']);
});
test('two paths sharing a filename remain ambiguous', () => {
  const [r] = audit([['a.txt']], ['one/a.txt','two/a.txt'], 'exact', 0);
  assert.equal(r.status,'Ambiguous'); assert.equal(r.candidates.length,2); assert.equal(r.selected,null);
});
test('case-insensitive stem lookup retains both extensions', () => {
  const [r] = audit([['a001']], ['A001.PDF','other/A001.dwg','A0010.pdf'], 'stem', 0);
  assert.equal(r.status,'Ambiguous'); assert.equal(r.candidates.length,2);
});
test('prefix stops at identifier boundaries and always requires a choice', () => {
  const [r] = audit([['A-001']], ['A-001_report.pdf','A-0010_report.pdf','XA-001.txt'], 'prefix', 0);
  assert.deepEqual(r.candidates,['A-001_report.pdf']); assert.equal(r.status,'Ambiguous'); assert.equal(r.selected,null);
});
test('dot separator and Chinese bracket are supported', () => {
  const [r] = audit([['A001']], ['A001.v2.pdf','A001（报告）.pdf'], 'prefix', 0);
  assert.equal(r.candidates.length,2);
});
test('exact mode does not fall back to stem', () => assert.equal(audit([['A001']],['A001.pdf'],'exact',0)[0].status,'Missing'));
test('blank or path-like requested names remain invalid', () => {
  assert.deepEqual(audit([['','note'],['../a.txt'],['C:\\a.txt']], ['a.txt'], 'exact', 0).map(r => r.status), ['Invalid','Invalid','Invalid']);
});
test('duplicate identical paths do not create ambiguity', () => assert.equal(audit([['a.txt']], ['root/a.txt','root/a.txt'], 'exact', 0)[0].candidates.length,1));
test('report preserves original rows and neutralizes spreadsheet formulas', () => {
  const rows = audit([['a.txt','=1+1'],['missing','001']], ['a.txt'], 'exact', 0);
  const csv = reportCsv(['file','note'],rows);
  assert.match(csv,/"'=1\+1"/); assert.match(csv,/"missing","001"/); assert.match(csv,/Missing/);
});
test('invalid column, mode and oversized input are rejected', () => {
  assert.throws(() => audit([['a.txt']], [],'bogus',0), /mode/i);
  assert.throws(() => audit([['a.txt']], [],'exact',1), /column/i);
  assert.throws(() => parseTable('x'.repeat(2000001),','), /characters/i);
});

test('TSV and escaped quotes retain text, ignore only fully blank rows', () => {
  assert.deepEqual(parseTable('file\tnote\r\n\t\r\n\tMissing name\r\n"a""b"\t001\r\n', '\t'), [['file','note'],['','Missing name'],['a"b','001']]);
  assert.throws(() => parseTable('"a"extra,b', ','), /closing quote/);
});
test('variable-width rows are padded without changing original data', () => {
  const input = [['a.txt'], ['b.txt','note']];
  const rows = audit(input, ['a.txt','b.txt'], 'exact', 0);
  assert.deepEqual(rows[0].cells,['a.txt','']);
  assert.deepEqual(input[0],['a.txt']);
});
test('reports round-trip selected paths, quotes and multiline notes', () => {
  const rows = audit([['a.txt','one\ntwo "quoted"']], ['folder/a.txt'], 'exact', 0);
  assert.deepEqual(parseTable(reportCsv(['file','note'],rows), ',')[1],['a.txt','one\ntwo "quoted"','1','Ready','folder/a.txt','1']);
});
test('prefix indexing does not duplicate a candidate or expand unrequested keys', () => {
  const [r] = audit([['A']],['A_file_report.final.pdf'],'prefix',0);
  assert.deepEqual(r.candidates,['A_file_report.final.pdf']);
});

test('case matching does not expand distinct Unicode filenames into a match', () => {
  assert.equal(audit([['SS.txt']],['ß.txt'],'exact',0)[0].status,'Missing');
  assert.equal(audit([['é.txt']],['É.txt'],'exact',0)[0].status,'Ready');
});
