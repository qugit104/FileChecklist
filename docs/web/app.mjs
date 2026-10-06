import { parseTable, audit, reportCsv, limits } from './engine.mjs';

const $ = id => document.getElementById(id);
const english = Object.fromEntries([...document.querySelectorAll('[data-i18n]')].map(el => [el.dataset.i18n, el.textContent]));
Object.assign(english, {
  sample: 'Example data · edit either box to try your own list', stale: 'Inputs changed. Check the list again.', checked: 'Checked locally in this tab',
  review: 'Review', change: 'Change choice', missingHelp: 'No matching filename', invalidHelp: 'Use a filename or ID, without a folder path',
  chooseHelp: 'Choose a path to confirm this row', needs: '{count} candidate(s)', row: 'Row {number}: {name}',
  counts: '{total} rows · {ready} matched · {missing} missing · {ambiguous} need a choice · {invalid} invalid',
  showing: 'Showing {shown} of {total} rows. Export includes every row.', candidateCount: 'Showing {shown} of {total} candidates. Filter the paths to narrow the list.',
  folderLoaded: '{count} filenames loaded. Click Check list.', working: 'Checking…', csv: 'filechecklist-report.csv',
  noRows: 'No rows in this view.', columnName: 'Column {number}', folderUnsupported: 'This browser does not support folder selection. Paste file paths instead.',
  browserError: 'Unable to check this input.', empty: 'Paste at least one requested file.',
});
const chinese = {
  download: '下载', title: '清单上的文件，都齐了吗？',
  intro: '粘贴 Excel 清单，对照文件夹里的文件名。逐行找出缺件和重名，保留重复请求与备注。',
  privacy: '在当前浏览器内运行，无需账号、不上传数据。选择文件夹只使用文件名，不读取文件内容。',
  listTitle: '1. 需要哪些文件', example: '载入示例', listHint: '直接粘贴 Excel 单元格，或 CSV 文本。原始列会保留在报告中。',
  delimiter: '分隔符', auto: '自动判断', tab: '制表符', comma: '逗号', column: '文件名所在列', header: '第一行是表头',
  filesTitle: '2. 已有哪些文件', folder: '选择文件夹…', filesHint: '每行一个文件路径，带扩展名。相同文件名用不同的文件夹路径区分。',
  mode: '匹配方式', exact: '完整文件名', stem: '不含扩展名', prefix: '文件编号前缀', check: '核对清单', results: '核对结果',
  export: '导出 CSV 报告', show: '显示', all: '全部行', Missing: '缺件', Ambiguous: '待选择', Ready: '已匹配', Invalid: '清单有误',
  requested: '请求文件', status: '状态', original: '其它原始列', matchedPath: '匹配文件 / 选择', close: '关闭',
  reviewHelp: '选择前核对完整路径。文件名本身不能保证文档的业务版本正确。', pathFilter: '筛选候选路径', candidates: '候选文件', confirm: '使用选中路径',
  reportHint: '报告保留全部行和选择结果，只核对名称，不检查内容。CSV 中状态使用英文；前导零保留为原始文本，Excel 导入时请将对应列设为文本。',
  next: '还需要把文件收集到一起？',
  nextBody: '桌面版和命令行版可以复制已确认文件、用 SHA-256 校验，并保存任务，方便以后补交缺件。此网页只核对名称并导出报告，不复制文件，也不保存任务。',
  getTool: '下载 Windows 桌面版 / Linux 命令行版', rules: '匹配规则', feedback: '反馈实际使用场景', limits: '网页限制与隐私说明',
  limitsBody: '最多 20,000 行清单、100,000 个文件路径，每个文本框最多 200 万字符。这是输入上限，不是性能承诺。界面一次显示前 500 行，CSV 导出全部结果。文件夹选择取决于浏览器支持，也可以手动粘贴路径。内容只保存在内存中，关闭标签页后消失。网站托管商会收到常规页面请求；粘贴内容和所选文件不会发送到服务器。',
  source: '查看源码', sample: '当前是示例数据 · 修改文本即可核对自己的清单', stale: '输入已修改，请重新核对。', checked: '已在当前浏览器内完成核对',
  reportTitle: 'CSV 报告', saveReport: '下载 .csv 文件', saveHint: '如果浏览器阻止下载，可复制下方文本，保存为 UTF-8 编码的 .csv 文件。', reportText: '全部报告行',
  review: '选择文件', change: '更改选择', missingHelp: '没有找到匹配的文件名', invalidHelp: '请输入文件名或编号，不要包含文件夹路径',
  chooseHelp: '选择一个路径以确认此行', needs: '{count} 个候选', row: '第 {number} 行：{name}',
  counts: '{total} 行 · {ready} 已匹配 · {missing} 缺件 · {ambiguous} 待选择 · {invalid} 清单有误',
  showing: '显示 {shown} / {total} 行，导出包含所有行。', candidateCount: '显示 {shown} / {total} 个候选，可通过路径筛选缩小范围。',
  folderLoaded: '已读入 {count} 个文件名，请点击核对清单。', working: '核对中…', csv: '清单核对报告.csv',
  noRows: '当前筛选没有结果。', columnName: '原始列 {number}', folderUnsupported: '当前浏览器不支持选择文件夹，请直接粘贴文件路径。',
  browserError: '无法核对此输入。', empty: '请先粘贴至少一个请求文件。',
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
    const paths = $('paths').value.split(/\r?\n/).map(x => x.trim().replace(/^"(.*)"$/, '$1')).filter(Boolean);
    rows = audit(padded, paths, $('mode').value, Number($('column').value) - 1);
    hasResults = true; $('filter').value = 'all'; $('results').hidden = false;
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
  const filtered = rows.filter(r => $('filter').value === 'all' || r.status === $('filter').value);
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
function loadExample() {
  $('checklist').value = 'file\tnote\nA001_spec.txt\tRequested by design\nA002_report.txt\tConfirm approved version\nA003_attachment.txt\tStill waiting\nguide.md\tInclude instructions\nA001_spec.txt\tRepeated request';
  $('paths').value = 'project/A001_spec.txt\nproject/approved/A002_report.txt\nproject/draft/A002_report.txt\nproject/guide.md\nproject/A0010_unrelated.txt';
  $('delimiter').value = 'auto'; $('column').value = '1'; $('header').checked = true; $('mode').value = 'exact';
  example = true; runCheck();
}
for (const id of ['checklist', 'paths', 'delimiter', 'column', 'header', 'mode']) $(id).addEventListener('input', invalidate);
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
    $('paths').value = paths; invalidate(); state('folderLoaded', { count: files.length });
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
translate(); loadExample();
