// print outerHTML (trimmed) of first element matching a selector after clicking through items
const puppeteer = require('puppeteer-core');
const path = require('path');
const fs = require('fs');
const [harness, sel, ...clicks] = process.argv.slice(2);
const stubs = JSON.parse(fs.readFileSync(path.join(__dirname, 'stubs.json'), 'utf8'));
(async () => {
  const b = await puppeteer.launch({ executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe', headless: true, ignoreDefaultArgs: ['--hide-scrollbars'], args: ['--allow-file-access-from-files'] });
  const p = await b.newPage();
  await p.setViewport({ width: 916, height: 583 });
  await p.evaluateOnNewDocument((stubs) => {
    const L = [];
    window.chrome = { webview: { addEventListener: (t, f) => L.push(f), removeEventListener: () => {}, postMessage: (m) => {
      const d = m && m.data; if (!d || !d.request) return;
      const r = d.request.method in stubs ? stubs[d.request.method] : {};
      setTimeout(() => L.forEach(f => f({ data: { id: d.id, response: { code: 0, data: r, message: '' } } })), 5);
    } } };
  }, stubs);
  await p.goto('file:///' + path.join(harness, 'dist', 'index.html').split(path.sep).join('/'));
  await new Promise(r => setTimeout(r, 1500));
  for (const c of clicks) {
    const [t, n] = c.split('#');
    await p.evaluate((t, n) => { const e = [...document.querySelectorAll('body *')].filter(e => e.children.length === 0 && e.textContent.trim() === t); e[(+n || 1) - 1].click(); }, t, n);
    await new Promise(r => setTimeout(r, 1200));
  }
  const out = await p.evaluate((sel) => {
    const e = document.querySelector(sel); if (!e) return 'not found';
    const walk = (el, d) => { const r = el.getBoundingClientRect(); let s = '  '.repeat(d) + `<${el.tagName.toLowerCase()} class="${el.className}" style="${el.getAttribute('style') || ''}"> [${Math.round(r.left)}..${Math.round(r.right)}] ${el.children.length ? '' : el.textContent.trim().slice(0, 30)}\n`; for (const c of el.children) s += walk(c, d + 1); return s; };
    return walk(e, 0);
  }, sel);
  console.log(out);
  const sw0 = await p.evaluate(() => { const c = document.querySelector('[class*=setting-container-content--]'); if (!c) return []; const cr = c.getBoundingClientRect().right; return [...c.querySelectorAll('*')].filter(e => e.getBoundingClientRect().right > cr - 8).map(e => e.className + ' r=' + Math.round(e.getBoundingClientRect().right) + ' cr=' + Math.round(cr)).slice(0, 8); }); console.log('WIDE', sw0.join(' | '));
  const sw = await p.evaluate(() => [...document.querySelectorAll('body *')].filter(e => e.scrollWidth > e.clientWidth + 2 && getComputedStyle(e).overflowX !== 'visible' && getComputedStyle(e).overflowX !== 'hidden').map(e => e.className + ' sw=' + e.scrollWidth + ' cw=' + e.clientWidth).slice(0, 5));
  console.log('HSCROLL', sw.join(' | '));
  await b.close();
})();
