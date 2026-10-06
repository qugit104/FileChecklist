import { parseTable, audit, reportCsv, limits } from './engine.mjs';
import { suggest, toText } from './import.mjs';

const $ = id => document.getElementById(id);
const english = Object.fromEntries([...document.querySelectorAll('[data-i18n]')].map(el => [el.dataset.i18n, el.textContent]));
Object.assign(english, {
  "sample": "Example",
  "stale": "Check again",
  "checked": "Complete",
  "review": "Select file",
  "change": "Change",
  "missingHelp": "No matching file",
  "invalidHelp": "Enter a filename or ID without a folder path",
  "chooseHelp": "Select a file",
  "needs": "{count} files",
  "row": "Row {number}: {name}",
  "counts": "{total} rows · {ready} found · {missing} missing · {ambiguous} to confirm · {invalid} invalid",
  "showing": "{shown} / {total} rows",
  "candidateCount": "{shown} / {total} files",
  "folderLoaded": "{count} files",
  "working": "Checking…",
  "csv": "filechecklist-report.csv",
  "noRows": "No rows",
  "columnName": "Column {number}",
  "folderUnsupported": "Folder selection is unavailable. Paste file paths instead.",
  "browserError": "Unable to check this input.",
  "empty": "Add a checklist.",
  "attention": "Needs attention",
  "start": "Ready",
  "recognition": "{rows} rows · Column: {column}",
  "folderCount": "{count} files",
  "reading": "Opening…",
  "fileError": "Choose .xlsx, CSV or TSV. Convert .xls to .xlsx first.",
  "importError": "Unable to open this file. Save it as an unencrypted .xlsx and try again.",
  "needFolder": "Select a folder or paste file paths.",
  "noRequests": "Empty worksheet.",
  "exampleSource": "Example checklist"
});
const chinese = {
  "download": "下载",
  "title": "文件清单核对",
  "intro": "按 Excel 清单查找文件，导出核对结果。",
  "listTitle": "1. 清单",
  "example": "示例",
  "listHint": "粘贴 Excel 单元格或 CSV 文本。",
  "delimiter": "分隔符",
  "auto": "自动",
  "tab": "制表符",
  "comma": "逗号",
  "column": "文件名列",
  "header": "首行为表头",
  "filesTitle": "2. 文件夹",
  "folder": "选择文件夹…",
  "filesHint": "每行一个文件路径。",
  "mode": "匹配方式",
  "exact": "完整文件名",
  "stem": "不含扩展名",
  "prefix": "编号前缀",
  "check": "核对",
  "results": "核对结果",
  "export": "导出 CSV",
  "show": "显示",
  "all": "全部",
  "Missing": "未找到",
  "Ambiguous": "待确认",
  "Ready": "已找到",
  "Invalid": "格式错误",
  "requested": "文件名",
  "status": "状态",
  "original": "其他列",
  "matchedPath": "文件路径",
  "close": "关闭",
  "reviewHelp": "选择要使用的文件。",
  "pathFilter": "筛选路径",
  "candidates": "候选文件",
  "confirm": "选择此文件",
  "reportHint": "导出包含全部行。",
  "next": "Windows 桌面版",
  "nextBody": "复制文件、保存任务、继续补件。",
  "getTool": "下载",
  "rules": "匹配规则",
  "feedback": "反馈",
  "limits": "使用说明",
  "limitsBody": "Excel 文件上限 16 MB，清单上限 20000 行，文件路径上限 100000 个，每个文本框上限 200 万字符。表格最多显示 500 行，报告导出全部行。数据在浏览器内处理，刷新或关闭页面后清空。用 Excel 打开 CSV 时，将编号列设为文本可保留前导零。",
  "source": "源码",
  "sample": "示例",
  "stale": "待重新核对",
  "checked": "核对完成",
  "reportTitle": "CSV 报告",
  "saveReport": "下载 CSV",
  "saveHint": "复制下方文本，或下载 CSV 文件。",
  "reportText": "CSV 内容",
  "review": "选择文件",
  "change": "更改",
  "missingHelp": "未找到匹配文件",
  "invalidHelp": "填写文件名或编号，不含文件夹路径",
  "chooseHelp": "选择文件",
  "needs": "{count} 个文件",
  "row": "第 {number} 行：{name}",
  "counts": "{total} 项 · {ready} 已找到 · {missing} 未找到 · {ambiguous} 待确认 · {invalid} 格式错误",
  "showing": "显示 {shown} / {total} 行",
  "candidateCount": "显示 {shown} / {total} 个文件",
  "folderLoaded": "{count} 个文件",
  "working": "核对中…",
  "csv": "清单核对报告.csv",
  "noRows": "无结果",
  "columnName": "第 {number} 列",
  "folderUnsupported": "浏览器不支持选择文件夹，请粘贴文件路径。",
  "browserError": "无法核对此输入。",
  "empty": "请导入清单。",
  "openList": "打开 Excel / CSV…",
  "pasteList": "粘贴清单",
  "dropHint": "拖入 .xlsx 文件",
  "sheet": "工作表",
  "editList": "编辑清单",
  "adjust": "导入设置",
  "folderHint": "包含子文件夹。",
  "editPaths": "文件路径",
  "nameOptions": "匹配选项",
  "prefixHelp": "按编号匹配后需确认文件。",
  "attention": "待处理",
  "start": "待核对",
  "recognition": "{rows} 行 · 文件名列：{column}",
  "folderCount": "{count} 个文件",
  "reading": "读取中…",
  "fileError": "请选择 .xlsx、CSV 或 TSV。旧版 .xls 请先另存为 .xlsx。",
  "importError": "无法打开文件，请另存为未加密的 .xlsx 后重试。",
  "needFolder": "请选择文件夹或粘贴文件路径。",
  "noRequests": "工作表为空。",
  "exampleSource": "示例清单"
};
const errorTranslations = {
  'The checklist exceeds 2,000,000 characters. Split it into smaller lists.': '清单超过 200 万字符，请分批处理。',
  'The folder list exceeds 100,000 files.': '文件列表超过 100,000 个文件，请缩小文件夹范围。',
  'The paths exceed 2,000,000 characters.': '文件路径超过 200 万字符，请缩小文件夹范围。',
  'The checklist exceeds 20,000 rows. Split it into smaller lists.': '清单超过 20,000 行，请分批处理。',
  'The checklist exceeds 20,000 rows.': '清单超过 20,000 行，请分批处理。',
  'The checklist contains no file rows.': '清单没有文件行，请检查表头选项。',
  'The filename column is outside the checklist.': '文件名列超出了清单范围。',
  'An opening quote has no closing quote. Check the CSV format.': '清单中有未闭合的引号，请检查 CSV 格式。',
  'Unexpected text after a closing quote. Check the CSV format.': '引号结束后有多余字符，请检查 CSV 格式。',
  'Too many matching paths. Use a more specific list or a smaller folder.': '匹配路径过多，请使用更准确的编号或缩小文件夹范围。',
};
let language = (new URLSearchParams(location.search).get('lang') ?? navigator.language).startsWith('zh') ? 'zh' : 'en';
let rows = [], headers = [], reviewRow = null, example = false, hasResults = false;
let stateKey = '', stateValues = {};
let reportUrl = null;
Object.assign(errorTranslations, {
  'Use an Excel file smaller than 16 MB.': '请使用小于 16 MB 的 Excel 文件。',
  'Use an unencrypted .xlsx file saved by Excel.': '请用 Excel 另存为未加密的 .xlsx 文件后重试。',
  'Use a workbook with 1 to 100 sheets.': '请将工作表数量控制在 1 到 100 个。',
  'Keep each sheet within 20,000 rows and 512 columns.': '每个工作表请控制在 20000 行、512 列以内。',
  'The checklist exceeds 2,000,000 characters.': '清单内容过多，请拆成较小的表格。',
});
let workbook = [], listName = '', importSerial = 0, reading = false;
const t = (key, values = {}) => (language === 'zh' ? chinese[key] ?? english[key] : english[key] ?? key)
  .replace(/\{(\w+)\}/g, (_, key) => values[key] ?? '');
function state(key, values = {}) { stateKey = key; stateValues = values; $('input-state').textContent = t(key, values); }
function showError(error) { $('error').textContent = language === 'zh' ? errorTranslations[error.message] ?? error.message : error.message; $('error').hidden = false; }
function clearReport() {
  $('report').hidden = true; $('report-text').value = ''; $('report-download').removeAttribute('href');
  if (reportUrl) URL.revokeObjectURL(reportUrl);
  reportUrl = null;
}
function invalidate() {
  rows = []; headers = []; hasResults = false; reviewRow = null; example = false;
  $('results').hidden = true; $('review').hidden = true; $('error').hidden = true;
  clearReport();
  state('stale');
}
function translate() {
  document.documentElement.lang = language === 'zh' ? 'zh-CN' : 'en';
  document.title = language === 'zh' ? 'FileChecklist — 按 Excel 清单核对文件' : 'FileChecklist — Check a spreadsheet against your files';
  for (const el of document.querySelectorAll('[data-i18n]')) el.textContent = t(el.dataset.i18n);
  $('language').textContent = language === 'zh' ? 'English' : '中文';
  $('language').lang = language === 'zh' ? 'en' : 'zh-CN';
  document.querySelector('.table-scroll').setAttribute('aria-label', t('results'));
  if (stateKey) state(stateKey, stateValues);
  updatePreview(false); updateFolderStatus();
  $('list-source').textContent = listName || t('dropHint');
  $('error').hidden = true;
  if (hasResults) render();
  if (reviewRow) openReview(reviewRow, false);
}
function detectSeparator(text) {
  let quoted = false;
  for (let i = 0; i < text.length; i++) {
    if (text[i] === '"') {
      if (quoted && text[i + 1] === '"') i++;
      else quoted = !quoted;
    } else if (!quoted && text[i] === '\t') return '\t';
    else if (!quoted && (text[i] === '\n' || text[i] === '\r')) break;
  }
  return ',';
}
function runCheck() {
  if (reading) return;
  clearReport();
  $('error').hidden = true; $('review').hidden = true; reviewRow = null;
  try {
    const separator = $('delimiter').value === 'auto' ? detectSeparator($('checklist').value) : $('delimiter').value === 'tab' ? '\t' : ',';
    const table = parseTable($('checklist').value, separator);
    if (!table.length) throw new Error(t('empty'));
    const width = table.reduce((n, cells) => Math.max(n, cells.length), 0);
    const padded = table.map(cells => Array.from({ length: width }, (_, i) => cells[i] ?? ''));
    headers = $('header').checked ? padded.shift() : Array.from({ length: width }, (_, i) => t('columnName', { number: i + 1 }));
    if ($('paths').value.length > limits.characters) throw new Error('The paths exceed 2,000,000 characters.');
    if (!$('paths').value.trim()) throw new Error(t('needFolder'));
    const paths = $('paths').value.split(/\r?\n/).map(x => x.trim().replace(/^"(.*)"$/, '$1')).filter(Boolean);
    if (!padded.length) throw new Error(t('noRequests'));
    rows = audit(padded, paths, $('mode').value, Number($('column').value) - 1);
    hasResults = true; $('filter').value = rows.some(r => r.status !== 'Ready') ? 'attention' : 'all'; $('results').hidden = false;
    state(example ? 'sample' : 'checked'); render();
  } catch (error) {
    rows = []; hasResults = false; $('results').hidden = true;
    showError(error); state('stale');
  }
}
function element(tag, text, className) {
  const el = document.createElement(tag); el.textContent = text;
  if (className) el.className = className;
  return el;
}
function render() {
  const counts = Object.fromEntries(['Ready', 'Missing', 'Ambiguous', 'Invalid'].map(s => [s, rows.filter(r => r.status === s).length]));
  $('summary').textContent = t('counts', { total: rows.length, ready: counts.Ready, missing: counts.Missing, ambiguous: counts.Ambiguous, invalid: counts.Invalid });
  const filtered = rows.filter(r => $('filter').value === 'all' || ($('filter').value === 'attention' ? r.status !== 'Ready' : r.status === $('filter').value));
  const visible = filtered.slice(0, 500), body = document.createDocumentFragment();
  $('visible-count').textContent = t('showing', { shown: visible.length, total: filtered.length });
  for (const row of visible) {
    const tr = document.createElement('tr');
    tr.append(element('td', row.number), element('td', row.name || '—'), element('td', t(row.status), 'status-' + row.status));
    const others = row.cells.filter((_, i) => i !== Number($('column').value) - 1).join(' · ');
    tr.append(element('td', others || '—'));
    const choice = document.createElement('td');
    if (row.selected) choice.append(element('span', row.selected, 'path'));
    if (row.status === 'Missing') choice.append(element('span', t('missingHelp'), 'hint'));
    else if (row.status === 'Invalid') choice.append(element('span', t('invalidHelp'), 'hint'));
    else if (row.candidates.length > 1 || $('mode').value === 'prefix') {
      if (!row.selected) choice.append(element('span', t('needs', { count: row.candidates.length }), 'path'));
      const button = element('button', t(row.selected ? 'change' : 'review'), 'row-action');
      button.type = 'button'; button.setAttribute('aria-label', t('row', { number: row.number, name: row.name }) + ' — ' + button.textContent);
      button.addEventListener('click', () => openReview(row)); choice.append(button);
    }
    tr.append(choice); body.append(tr);
  }
  if (!visible.length) { const tr = document.createElement('tr'), td = element('td', t('noRows')); td.colSpan = 5; tr.append(td); body.append(tr); }
  $('result-rows').replaceChildren(body);
}
function openReview(row, focus = true) {
  reviewRow = row; $('review').hidden = false;
  $('review-heading').textContent = t('row', { number: row.number, name: row.name });
  $('candidate-filter').value = ''; renderCandidates();
  if (focus) { $('review').scrollIntoView({ block: 'nearest' }); $('candidates').focus(); }
}
function renderCandidates() {
  if (!reviewRow) return;
  const query = $('candidate-filter').value.toLocaleLowerCase();
  const filtered = reviewRow.candidates.filter(p => p.toLocaleLowerCase().includes(query));
  const options = filtered.slice(0, 200).map(path => { const option = element('option', path); option.value = path; return option; });
  $('candidates').replaceChildren(...options);
  if (reviewRow.selected && options.some(o => o.value === reviewRow.selected)) $('candidates').value = reviewRow.selected;
  else $('candidates').selectedIndex = options.length ? 0 : -1;
  $('confirm').disabled = !options.length;
  $('candidate-count').textContent = t('candidateCount', { shown: options.length, total: filtered.length });
}
function updatePreview(detect = true) {
  try {
    const sep = $('delimiter').value === 'auto' ? detectSeparator($('checklist').value) : $('delimiter').value === 'tab' ? '\t' : ',';
    const table = parseTable($('checklist').value, sep), guess = suggest(table);
    const width = table.reduce((n, row) => Math.max(n, row.length), 0);
    if (detect) $('header').checked = guess.hasHeader;
    const selected = detect ? guess.nameColumn : Number($('column').value) - 1;
    const names = Array.from({ length: width }, (_, i) => $('header').checked ? table[0]?.[i] || t('columnName', { number: i + 1 }) : t('columnName', { number: i + 1 }));
    $('column').replaceChildren(...names.map((name, i) => { const el = element('option', name); el.value = String(i + 1); return el; }));
    $('column').value = String(Math.min(Math.max(0, selected), Math.max(0, width - 1)) + 1);
    const body = table.slice($('header').checked ? 1 : 0);
    $('recognition').textContent = table.length ? t('recognition', { rows: body.length, column: names[Number($('column').value) - 1] }) : '';
    const preview = document.createElement('table'), head = document.createElement('thead'), hr = document.createElement('tr');
    for (const name of names.slice(0, 4)) { const th = element('th', name); th.scope = 'col'; hr.append(th); } head.append(hr); preview.append(head);
    const tb = document.createElement('tbody');
    for (const cells of body.slice(0, 4)) { const tr = document.createElement('tr'); for (const cell of cells.slice(0, 4)) tr.append(element('td', cell)); tb.append(tr); }
    preview.append(tb); $('list-preview').replaceChildren(preview); $('list-preview').hidden = !table.length;
  } catch { $('list-preview').hidden = true; $('recognition').textContent = ''; }
}
function updateFolderStatus() {
  const count = $('paths').value.split(/\r?\n/).filter(x => x.trim()).length;
  $('folder-status').textContent = count ? t('folderCount', { count }) : t('folderHint');
}
function applySheet() {
  const sheet = workbook[Number($('sheet').value)]; if (!sheet) return;
  const text = toText(sheet.rows);
  if (text.length > limits.characters) throw new Error('The checklist exceeds 2,000,000 characters.');
  $('checklist').value = text; $('delimiter').value = 'tab'; invalidate(); updatePreview(true);
}
function parseWorkbook(buffer) {
  return new Promise((resolve, reject) => {
    const worker = new Worker(new URL('./import-worker.mjs', import.meta.url), { type: 'module' });
    const stop = () => { clearTimeout(timer); worker.terminate(); };
    const timer = setTimeout(() => { stop(); reject(new Error(t('importError'))); }, 15000);
    worker.onmessage = ({ data }) => { stop(); data.error ? reject(new Error(data.error)) : resolve(data.sheets); };
    worker.onerror = () => { stop(); reject(new Error(t('importError'))); };
    worker.postMessage(buffer, [buffer]);
  });
}
async function loadFile(file) {
  if (!file) return;
  const serial = ++importSerial; reading = true; $('check').disabled = true; state('reading'); $('error').hidden = true;
  try {
    if (file.size > 16_000_000) throw new Error('Use an Excel file smaller than 16 MB.');
    const extension = file.name.split('.').pop().toLowerCase();
    if (!['xlsx', 'csv', 'tsv', 'txt'].includes(extension)) throw new Error(t('fileError'));
    const buffer = await file.arrayBuffer();
    let sheets;
    if (extension === 'xlsx') sheets = await parseWorkbook(buffer);
    else {
      let text;
      try { text = new TextDecoder('utf-8', { fatal: true }).decode(buffer); }
      catch { text = new TextDecoder('gb18030', { fatal: true }).decode(buffer); }
      sheets = [{ name: file.name, rows: parseTable(text, extension === 'csv' ? ',' : detectSeparator(text)) }];
    }
    if (serial !== importSerial) return;
    workbook = sheets; $('sheet').replaceChildren(...sheets.map((sheet, i) => { const opt = element('option', sheet.name); opt.value = String(i); return opt; }));
    $('sheet-label').hidden = sheets.length < 2; $('sheet').value = '0'; applySheet();
    listName = file.name; $('list-source').textContent = listName; $('paste-panel').open = false;
  } catch (error) { if (serial === importSerial) showError(error); }
  finally { if (serial === importSerial) { reading = false; $('check').disabled = false; if (stateKey === 'reading') state('start'); } }
}
function loadExample() {
  ++importSerial; reading = false; $('check').disabled = false; workbook = []; $('sheet-label').hidden = true;
  const zh = language === 'zh';
  $('checklist').value = zh ? '文件名\t备注\n合同.pdf\t归档用\n报价单.pdf\t用双方盖章版\n验收单.pdf\t项目负责人提供\n附件.txt\t设备序列号\n合同.pdf\t财务留存' : 'Filename\tNote\nContract.pdf\tArchive copy\nQuote.pdf\tUse the signed version\nAcceptance.pdf\tFrom project lead\nAttachment.txt\tEquipment serial numbers\nContract.pdf\tFinance copy';
  $('paths').value = zh ? '客户资料/合同.pdf\n客户资料/已盖章/报价单.pdf\n客户资料/草稿/报价单.pdf\n客户资料/附件.txt' : 'Customer/Contract.pdf\nCustomer/Signed/Quote.pdf\nCustomer/Draft/Quote.pdf\nCustomer/Attachment.txt';
  $('delimiter').value = 'tab'; $('mode').value = 'exact'; listName = t('exampleSource'); $('list-source').textContent = listName;
  updatePreview(true); updateFolderStatus(); example = true; runCheck();
}
$('open-list').addEventListener('click', () => { $('list-input').value = ''; $('list-input').click(); });
$('list-input').addEventListener('change', () => loadFile($('list-input').files[0]));
$('sheet').addEventListener('change', () => { try { applySheet(); } catch (error) { showError(error); } });
$('paste-toggle').addEventListener('click', () => { $('paste-panel').open = true; $('checklist').focus(); });
$('drop-zone').addEventListener('dragover', event => { event.preventDefault(); event.dataTransfer.dropEffect = 'copy'; });
$('drop-zone').addEventListener('drop', event => { event.preventDefault(); loadFile(event.dataTransfer.files[0]); });
$('checklist').addEventListener('input', () => { ++importSerial; reading = false; $('check').disabled = false; listName = ''; $('list-source').textContent = t('dropHint'); workbook = []; $('sheet-label').hidden = true; invalidate(); updatePreview(true); });
$('paths').addEventListener('input', () => { invalidate(); updateFolderStatus(); });
for (const id of ['delimiter', 'column', 'header', 'mode']) $(id).addEventListener('change', () => { invalidate(); updatePreview(false); });
$('check').addEventListener('click', runCheck);
$('example').addEventListener('click', loadExample);
$('language').addEventListener('click', () => { language = language === 'en' ? 'zh' : 'en'; translate(); });
$('filter').addEventListener('change', render);
$('candidate-filter').addEventListener('input', renderCandidates);
$('close-review').addEventListener('click', () => { $('review').hidden = true; reviewRow = null; });
$('confirm').addEventListener('click', () => {
  if (!reviewRow || !reviewRow.candidates.includes($('candidates').value)) return;
  reviewRow.selected = $('candidates').value; reviewRow.status = 'Ready';
  clearReport();
  $('review').hidden = true; reviewRow = null; render();
});
$('folder').addEventListener('click', () => {
  if (!('webkitdirectory' in $('folder-input'))) { showError(new Error(t('folderUnsupported'))); return; }
  $('folder-input').value = ''; $('folder-input').click();
});
$('folder-input').addEventListener('change', () => {
  const files = $('folder-input').files;
  if (!files.length) return;
  try {
    if (files.length > limits.paths) throw new Error('The folder list exceeds 100,000 files.');
    const paths = Array.from(files, file => file.webkitRelativePath || file.name).join('\n');
    if (paths.length > limits.characters) throw new Error('The paths exceed 2,000,000 characters.');
    $('paths').value = paths; invalidate(); updateFolderStatus(); state('folderLoaded', { count: files.length });
  } catch (error) { showError(error); }
});
$('export').addEventListener('click', () => {
  if (!hasResults) return;
  clearReport();
  const csv = reportCsv(headers, rows);
  reportUrl = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
  $('report-download').href = reportUrl; $('report-download').download = t('csv');
  $('report-text').value = csv; $('report').hidden = false;
  $('report').scrollIntoView({ block: 'nearest' }); $('report-text').focus();
});
translate(); state('start');
