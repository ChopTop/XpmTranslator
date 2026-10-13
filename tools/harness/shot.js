// Render the PC Manager web UI with a stubbed WebView2 bridge and take screenshots.
// usage: node shot.js <harnessDir> <outDir> [clickText ...]
const puppeteer = require('puppeteer-core');
const path = require('path');
const fs = require('fs');

const [harness, outDir, ...clicks] = process.argv.slice(2);
const stubs = fs.existsSync(path.join(__dirname, 'stubs.json'))
  ? JSON.parse(fs.readFileSync(path.join(__dirname, 'stubs.json'), 'utf8')) : {};

(async () => {
  const browser = await puppeteer.launch({
    executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    headless: true, ignoreDefaultArgs: ['--hide-scrollbars'],
    args: ['--allow-file-access-from-files', '--disable-web-security'],
  });
  const page = await browser.newPage();
  await page.setViewport({ width: +(process.env.W || 900), height: +(process.env.H || 590), deviceScaleFactor: 1 });
  const calls = new Set();
  page.on('console', m => { if (m.text().startsWith('BRIDGE ')) calls.add(m.text().slice(7)); });
  page.on('pageerror', e => console.log('PAGEERROR', e.message.slice(0, 200)));
  await page.evaluateOnNewDocument((stubs) => {
    const listeners = [];
    window.chrome = window.chrome || {};
    window.chrome.webview = {
      addEventListener: (t, fn) => { if (t === 'message') listeners.push(fn); },
      removeEventListener: () => {},
      postMessage: (msg) => {
        const d = msg && msg.data;
        if (!d || !d.request) return;
        const m = d.request.method;
        console.log('BRIDGE ' + m);
        const data = m in stubs ? stubs[m] : {};
        setTimeout(() => listeners.forEach(fn => fn({ data: { id: d.id, response: { code: 0, data, message: '' } } })), 5);
      },
    };
  }, stubs);
  await page.goto('file:///' + path.join(harness, 'dist', 'index.html').replace(/\\/g, '/'));
  await new Promise(r => setTimeout(r, 1500));
  fs.mkdirSync(outDir, { recursive: true });
  await page.screenshot({ path: path.join(outDir, '00_start.png') });
  const overflowCheck = () => page.evaluate(() => {
    const W = window.innerWidth;
    return [...document.querySelectorAll('body *')]
      .filter(e => {
        if (e.children.length !== 0 || !e.textContent.trim()) return false;
        const r = e.getBoundingClientRect();
        if (!r.width || r.bottom < 0 || r.top > window.innerHeight) return false;
        if (e.scrollWidth > e.clientWidth + 1 && e.clientWidth > 0) return true;
        if (r.right > W + 1) return true;
        const bt = e.closest('[class*="button"],[class*="btn"],button');
        if (bt && e.getClientRects().length > 1) return true;  // label wrapped inside a button
        // text sticking out of its closest card-like ancestor
        const p = e.parentElement && e.parentElement.closest('[class*="card"],[class*="box"],[class*="item"],[class*="btn"],[class*="button"],[class*="Button"],button');
        if (p) { const pr = p.getBoundingClientRect(); if (r.right > pr.right + 2) return true; }
        return false;
      })
      .slice(0, 25).map(e => (e.className || e.tagName) + ' | ' + e.textContent.trim().slice(0, 60));
  });
  let i = 1;
  const allOv = {};
  for (const c of clicks) {
    const ok = await page.evaluate((txt) => {
      const els = [...document.querySelectorAll('body *')].filter(e => e.children.length === 0 && e.textContent.trim() === txt.split('#')[0]);
      if (!els.length) return false;
      els[(+txt.split('#')[1] || 1) - 1].click();
      return true;
    }, c);
    await new Promise(r => setTimeout(r, 1200));
    const name = String(i++).padStart(2, '0') + '_' + c.replace(/[^\w]+/g, '_') + '.png';
    await page.screenshot({ path: path.join(outDir, name) });
    console.log(ok ? 'clicked' : 'NOT FOUND', c, '->', name);
    for (const o of await overflowCheck()) (allOv[o] = allOv[o] || []).push(c);
  }
  for (const [k, v] of Object.entries(allOv)) console.log('OV', k, ' @', v[0]);
  // report elements whose text overflows its box
  const ov = await page.evaluate(() => [...document.querySelectorAll('body *')]
    .filter(e => e.children.length === 0 && e.textContent.trim() && (e.scrollWidth > e.clientWidth + 1) && getComputedStyle(e).overflow !== 'visible')
    .slice(0, 30).map(e => e.className + ' | ' + e.textContent.trim().slice(0, 50)));
  console.log('OVERFLOW', JSON.stringify(ov, null, 0));
  if (process.env.QUERY) {
    const info = await page.evaluate((qs) => qs.split('|').map(q => {
      const el = q.startsWith('css:') ? document.querySelector(q.slice(4)) : [...document.querySelectorAll('body *')].find(e => e.children.length === 0 && e.textContent.trim().startsWith(q));
      if (!el) return q + ' => not found';
      const chain = [];
      for (let e = el; e && chain.length < 5; e = e.parentElement) {
        const cs = getComputedStyle(e);
        const rr = e.getBoundingClientRect(); chain.push(`${e.tagName}.${e.className} [x=${Math.round(rr.left)}..${Math.round(rr.right)} w=${e.clientWidth} pos=${cs.position} disp=${cs.display} ws=${cs.whiteSpace} ov=${cs.overflow} fs=${cs.fontSize}]`);
      }
      return q + ' => ' + chain.join(' < ');
    }), process.env.QUERY);
    info.forEach(l => console.log('Q', l));
  }
  console.log('CALLS', [...calls].join(' '));
  await browser.close();
})();
