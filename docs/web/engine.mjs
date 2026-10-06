export const limits = Object.freeze({ characters: 2_000_000, rows: 20_000, paths: 100_000, matches: 200_000 });

export function parseTable(text, separator) {
  if (text.length > limits.characters) throw new Error('The checklist exceeds 2,000,000 characters. Split it into smaller lists.');
  if (separator !== ',' && separator !== '\t') throw new Error('Choose comma or tab as the separator.');
  text = text.replace(/^\uFEFF+/, '');
  const result = [];
  let row = [], field = '', quoted = false, closed = false;
  const endField = () => { row.push(field); field = ''; closed = false; };
  const endRow = () => {
    endField();
    if (row.some(x => x.trim() !== '')) result.push(row);
    row = [];
    if (result.length > limits.rows + 1) throw new Error('The checklist exceeds 20,000 rows. Split it into smaller lists.');
  };
  for (let i = 0; i < text.length; i++) {
    const ch = text[i];
    if (quoted) {
      if (ch === '"') {
        if (text[i + 1] === '"') { field += '"'; i++; }
        else { quoted = false; closed = true; }
      } else field += ch;
    } else if (ch === separator) endField();
    else if (ch === '\r' || ch === '\n') {
      endRow();
      if (ch === '\r' && text[i + 1] === '\n') i++;
    } else if (ch === '"' && !field && !closed) quoted = true;
    else if (closed) {
      if (ch !== ' ') throw new Error('Unexpected text after a closing quote. Check the CSV format.');
    } else field += ch;
  }
  if (quoted) throw new Error('An opening quote has no closing quote. Check the CSV format.');
  if (field || row.length || closed) endRow();
  return result;
}

// Avoid full Unicode expansions such as ß -> SS, which change the filename.
const nameKey = name => Array.from(name, ch => {
  const upper = ch.toUpperCase();
  return Array.from(upper).length === 1 ? upper : ch;
}).join('');

function matchKeys(path, mode) {
  const name = path.split(/[\\/]/).at(-1);
  if (mode === 'exact') return [nameKey(name)];
  const dot = name.lastIndexOf('.');
  const stem = dot < 0 ? name : name.slice(0, dot);
  const keys = [nameKey(stem)];
  if (mode === 'prefix') {
    for (let i = 1; i < stem.length; i++) {
      if (/\s/.test(stem[i]) || '_-.([（【'.includes(stem[i])) keys.push(nameKey(stem.slice(0, i)));
    }
  }
  return new Set(keys);
}

export function audit(data, paths, mode, column) {
  if (!['exact', 'stem', 'prefix'].includes(mode)) throw new Error('Unknown matching mode.');
  if (!data.length) throw new Error('The checklist contains no file rows.');
  if (data.length > limits.rows) throw new Error('The checklist exceeds 20,000 rows.');
  if (paths.length > limits.paths) throw new Error('The folder list exceeds 100,000 files.');
  const width = data.reduce((max, row) => Math.max(max, row.length), 0);
  if (!Number.isInteger(column) || column < 0 || column >= width) throw new Error('The filename column is outside the checklist.');
  const names = new Set(data.map(cells => nameKey((cells[column] ?? '').trim())));
  const index = new Map();
  let matchCount = 0;
  for (const path of new Set(paths)) {
    if (!path || /[\\/]$/.test(path)) continue;
    for (const key of matchKeys(path, mode)) {
      if (!names.has(key)) continue;
      if (++matchCount > limits.matches) throw new Error('Too many matching paths. Use a more specific list or a smaller folder.');
      if (!index.has(key)) index.set(key, []);
      index.get(key).push(path);
    }
  }
  for (const candidates of index.values()) candidates.sort();
  return data.map((original, i) => {
    const cells = Array.from({ length: width }, (_, c) => original[c] ?? '');
    const name = cells[column].trim();
    const valid = name !== '' && name !== '.' && name !== '..' && !/[\x00-\x1f<>:"/\\|?*]/.test(name);
    const candidates = valid ? (index.get(nameKey(name)) ?? []) : [];
    const status = !valid ? 'Invalid' : !candidates.length ? 'Missing' : (mode !== 'prefix' && candidates.length === 1) ? 'Ready' : 'Ambiguous';
    return { number: i + 1, cells, name, status, candidates, selected: status === 'Ready' ? candidates[0] : null };
  });
}

export function reportCsv(headers, rows) {
  const width = rows.reduce((max, r) => Math.max(max, r.cells.length), headers.length);
  const quote = value => {
    let text = String(value ?? '');
    if (/^[\s]*[=+\-@]/.test(text)) text = "'" + text;
    return '"' + text.replaceAll('"', '""') + '"';
  };
  const table = [
    [...Array.from({ length: width }, (_, i) => headers[i] || `Column ${i + 1}`), 'Row', 'Status', 'Selected path', 'Candidate count'],
    ...rows.map(r => [...Array.from({ length: width }, (_, i) => r.cells[i] ?? ''), r.number, r.status, r.selected ?? '', r.candidates.length])
  ];
  return '\uFEFF' + table.map(row => row.map(quote).join(',')).join('\r\n') + '\r\n';
}
