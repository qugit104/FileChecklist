import { readWorkbook } from './import.mjs';
self.onmessage = ({ data }) => {
  try { self.postMessage({ sheets: readWorkbook(data) }); }
  catch (error) { self.postMessage({ error: error.message }); }
};
