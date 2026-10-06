import http from 'node:http';
import { readFile } from 'node:fs/promises';
const files = new Map([
  ['/', ['../docs/index.html', 'text/html; charset=utf-8']],
  ['/web/app.mjs', ['../docs/web/app.mjs', 'text/javascript; charset=utf-8']],
  ['/web/engine.mjs', ['../docs/web/engine.mjs', 'text/javascript; charset=utf-8']],
  ['/web/import.mjs', ['../docs/web/import.mjs', 'text/javascript; charset=utf-8']],
  ['/web/import-worker.mjs', ['../docs/web/import-worker.mjs', 'text/javascript; charset=utf-8']],
  ['/web/vendor/xlsx.mjs', ['../docs/web/vendor/xlsx.mjs', 'text/javascript; charset=utf-8']],
  ['/web/style.css', ['../docs/web/style.css', 'text/css; charset=utf-8']],
]);
http.createServer(async (req, res) => {
  const entry = files.get(new URL(req.url, 'http://127.0.0.1').pathname);
  if (!entry) { res.writeHead(404); res.end('Not found'); return; }
  try {
    const content = await readFile(new URL(entry[0], import.meta.url));
    res.writeHead(200, { 'content-type': entry[1], 'cache-control': 'no-store' }); res.end(content);
  } catch { res.writeHead(500); res.end('Unable to read demo files'); }
}).listen(4177, '127.0.0.1', () => console.log('FileChecklist demo: http://127.0.0.1:4177'));
