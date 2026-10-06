// Reproducible, fictional spreadsheet fixture, shared by desktop and browser tests.
import * as XLSX from '../../docs/web/vendor/xlsx.mjs';
import { writeFileSync } from 'node:fs';
const book = XLSX.utils.book_new();
const sheet = XLSX.utils.aoa_to_sheet([
  ['备注', '文件名', '编号'],
  ['用于归档', '合同.pdf', 7],
  ['请用盖章版', '报价单.pdf', '002'],
  ['待补', '验收单.pdf', '003'],
  ['财务也需要', '合同.pdf', '004'],
  ['两行\n备注，保留原文', '附件.txt', '005'],
]);
sheet.C2.z = '0000';
sheet.D1 = { t: 's', v: '计算备注' };
sheet.D2 = { t: 's', f: '"已保存结果"', v: '已保存结果' };
sheet['!ref'] = 'A1:D6';
XLSX.utils.book_append_sheet(book, sheet, '交付清单');
XLSX.utils.book_append_sheet(book, XLSX.utils.aoa_to_sheet([['readme.txt'], ['说明.pdf']]), '无表头');
writeFileSync(new URL('checklist.xlsx', import.meta.url), XLSX.write(book, { type: 'buffer', bookType: 'xlsx', compression: true }));
