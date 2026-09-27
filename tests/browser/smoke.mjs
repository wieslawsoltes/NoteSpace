import { chromium } from 'playwright';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { readFile, stat, mkdir, writeFile } from 'node:fs/promises';
import { resolve, extname, sep } from 'node:path';

const site = resolve(process.env.NOTESPACE_SITE || 'artifacts/site');
const output = resolve('artifacts/qa');
await mkdir(output, { recursive: true });
const types = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.wasm': 'application/wasm', '.svg': 'image/svg+xml', '.png': 'image/png', '.woff': 'font/woff', '.woff2': 'font/woff2' };
const server = createServer(async (req, res) => {
    try {
        let pathname = decodeURIComponent(new URL(req.url, 'http://localhost').pathname);
        if (!pathname.startsWith('/NoteSpace/')) { res.writeHead(302, { location: '/NoteSpace/' }); res.end(); return; }
        let path = resolve(site, pathname.slice('/NoteSpace/'.length));
        if (path !== site && !path.startsWith(site + sep)) { res.writeHead(403); res.end(); return; }
        if ((await stat(path)).isDirectory()) path = resolve(path, 'index.html');
        res.writeHead(200, { 'content-type': types[extname(path)] || 'application/octet-stream', 'cache-control': 'no-store' });
        res.end(await readFile(path));
    } catch { res.writeHead(404); res.end('Not found'); }
});
await new Promise(resolve => server.listen(4173, '127.0.0.1', resolve));
const browser = await chromium.launch({ headless: true, args: ['--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'] });
const context = await browser.newContext({ viewport: { width: 1600, height: 1000 } });
const page = await context.newPage();
const errors = []; const logs = [];
page.on('pageerror', error => errors.push(error.message));
page.on('console', message => logs.push(`${message.type()}: ${message.text()}`));
page.on('dialog', async dialog => { await dialog.dismiss(); });
const waitState = fn => page.waitForFunction(fn, null, { timeout: 150000 });
const state = () => page.evaluate(() => globalThis.noteSpaceState);
async function button(name, fallback) {
    const target = page.getByRole('button', { name, exact: true }).first();
    if (await target.isVisible().catch(() => false)) await target.click();
    else await fallback();
}
try {
    await page.goto('http://127.0.0.1:4173/NoteSpace/', { waitUntil: 'domcontentloaded' });
    await waitState(() => globalThis.noteSpaceState?.ready && !globalThis.noteSpaceState.dirty);
    const initial = await state();
    assert.equal(initial.pageTitle, 'Welcome to NoteSpace');
    assert.equal(initial.pageCount, 5);
    await page.screenshot({ path: resolve(output, 'desktop.png') });
    await writeFile(resolve(output, 'initial-state.json'), JSON.stringify(initial, null, 2));
    await writeFile(resolve(output, 'initial-dom.html'), await page.content());

    await page.mouse.click(1100, 700);
    await button('Add page (Ctrl+Alt+N)', () => page.keyboard.press('Control+Alt+n'));
    await waitState(() => globalThis.noteSpaceState?.pageCount === 6);
    await page.keyboard.press('Control+a');
    await page.keyboard.type('Browser smoke test');
    await page.keyboard.press('Enter');
    await waitState(() => globalThis.noteSpaceState?.pageTitle === 'Browser smoke test');

    await page.mouse.dblclick(670, 445, { delay: 100 });
    await page.keyboard.type('A note created by real browser input.');
    await page.keyboard.press('Escape');
    await waitState(() => globalThis.noteSpaceState?.blockCount === 1 && !globalThis.noteSpaceState.dirty);
    await page.reload({ waitUntil: 'domcontentloaded' });
    await waitState(() => globalThis.noteSpaceState?.ready && !globalThis.noteSpaceState.dirty);
    assert.equal((await state()).pageTitle, 'Browser smoke test');
    assert.equal((await state()).blockCount, 1);

    await button('Draw', () => page.mouse.click(218, 60));
    await button('Pen', () => page.mouse.click(219, 120));
    await waitState(() => globalThis.noteSpaceState?.tool === 'Pen');
    await page.mouse.move(740, 590); await page.mouse.down();
    await page.mouse.move(785, 550, { steps: 8 }); await page.mouse.move(855, 600, { steps: 12 }); await page.mouse.up();
    await waitState(() => globalThis.noteSpaceState?.inkCount === 1);
    await page.keyboard.press('Control+z');
    await waitState(() => globalThis.noteSpaceState?.inkCount === 0);
    await page.keyboard.press('Control+y');
    await waitState(() => globalThis.noteSpaceState?.inkCount === 1 && !globalThis.noteSpaceState.dirty);
    await page.screenshot({ path: resolve(output, 'editing-and-ink.png') });
    await page.reload({ waitUntil: 'domcontentloaded' });
    await waitState(() => globalThis.noteSpaceState?.ready && !globalThis.noteSpaceState.dirty);
    assert.equal((await state()).inkCount, 1);

    const conflict = await page.evaluate(async () => {
        const stored = await globalThis.NoteSpaceHost.load();
        const split = stored.indexOf('\n'); const token = stored.slice(0, split); const json = stored.slice(split + 1);
        await globalThis.NoteSpaceHost.save(json, token);
        try { await globalThis.NoteSpaceHost.save(json, token); return false; }
        catch (error) { return String(error).includes('NOTESPACE_CONFLICT'); }
    });
    assert.equal(conflict, true, 'IndexedDB rejects stale revisions atomically');

    const mobile = await browser.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true });
    const mobilePage = await mobile.newPage();
    await mobilePage.goto('http://127.0.0.1:4173/NoteSpace/', { waitUntil: 'domcontentloaded' });
    await mobilePage.waitForFunction(() => globalThis.noteSpaceState?.ready, null, { timeout: 150000 });
    await mobilePage.screenshot({ path: resolve(output, 'mobile.png') });
    await mobile.close();
    assert.deepEqual(errors, [], 'No unhandled browser errors');
    await writeFile(resolve(output, 'result.json'), JSON.stringify({ passed: true, tests: ['boot', 'create page', 'edit title', 'edit note', 'autosave', 'reload', 'draw', 'undo', 'redo', 'ink persistence', 'atomic storage conflict', 'mobile boot'] }, null, 2));
    console.log('PASS browser boot, editing, ink, undo/redo, persistence, storage conflict, and mobile boot');
} catch (error) {
    await page.screenshot({ path: resolve(output, 'failure.png') }).catch(() => {});
    await writeFile(resolve(output, 'failure-dom.html'), await page.content()).catch(() => {});
    await writeFile(resolve(output, 'failure-state.json'), JSON.stringify(await state().catch(() => null), null, 2) || 'null');
    throw error;
} finally {
    await writeFile(resolve(output, 'console.log'), logs.join('\n') + '\nERRORS\n' + errors.join('\n'));
    await browser.close(); await new Promise(resolve => server.close(resolve));
}
