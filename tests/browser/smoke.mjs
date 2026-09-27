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
// Observe the native input bridge without changing document or input state.
await page.addInitScript(() => {
    globalThis.noteSpaceInputTrace = [];
    for (const kind of ['focusin', 'focusout', 'keydown', 'keyup', 'beforeinput', 'input']) {
        document.addEventListener(kind, event => {
            const key = event.key;
            queueMicrotask(() => {
                const active = document.activeElement;
                const input = document.getElementById('uno-input');
                const trace = globalThis.noteSpaceInputTrace;
                if (trace.length >= 240) trace.shift();
                trace.push({ kind, key, target: event.target?.id,
                    active: active?.id || active?.tagName,
                    text: input?.value, start: input?.selectionStart, end: input?.selectionEnd,
                    pageTitle: globalThis.noteSpaceState?.pageTitle });
            });
        });
    }
});
page.on('pageerror', error => errors.push(error.message));
page.on('console', message => logs.push(`${message.type()}: ${message.text()}`));
page.on('dialog', async dialog => { await dialog.dismiss(); });
const waitState = fn => page.waitForFunction(fn, null, { timeout: 45000 });
const state = () => page.evaluate(() => globalThis.noteSpaceState);
// Skia hit-test geometry updates on the next compositor frame, not the DOM click task.
async function settle() {
    await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
    await page.waitForTimeout(150);
}
async function waitEditorFocus() {
    await page.waitForFunction(() => {
        const element = document.activeElement;
        return element && (element.tagName === 'INPUT' || element.tagName === 'TEXTAREA') && element.id !== 'notespace-file-input';
    }, null, { timeout: 10000 });
}
async function inputCheckpoint(label) {
    const data = await page.evaluate(() => ({
        state: globalThis.noteSpaceState,
        active: document.activeElement?.outerHTML,
        input: [...document.querySelectorAll('input,textarea')].map(element => ({ id: element.id, value: element.value, start: element.selectionStart, end: element.selectionEnd })),
        trace: globalThis.noteSpaceInputTrace
    }));
    await writeFile(resolve(output, label + '.json'), JSON.stringify(data, null, 2));
}
async function button(name, fallback) {
    const target = page.getByRole('button', { name, exact: true }).first();
    if (await target.isVisible().catch(() => false)) await target.click();
    else await fallback();
    await settle();
}
try {
    await page.goto('http://127.0.0.1:4173/NoteSpace/', { waitUntil: 'domcontentloaded' });
    await page.waitForFunction(() => globalThis.noteSpaceState?.ready, null, { timeout: 150000 });
    assert.ok(!(await state()).status.includes('unavailable'), 'Storage initialized successfully');
    await waitState(() => globalThis.noteSpaceState?.ready && !globalThis.noteSpaceState.dirty);
    await settle();
    const initial = await state();
    assert.equal(initial.pageTitle, 'Welcome to NoteSpace');
    assert.equal(initial.pageCount, 5);
    assert.equal(initial.status, 'Saved on this device');
    await page.screenshot({ path: resolve(output, 'desktop.png') });
    await writeFile(resolve(output, 'initial-state.json'), JSON.stringify(initial, null, 2));
    await writeFile(resolve(output, 'initial-dom.html'), await page.content());

    await page.mouse.click(1100, 700);
    await button('Add page (Ctrl+Alt+N)', () => page.keyboard.press('Control+Alt+n'));
    await waitState(() => globalThis.noteSpaceState?.pageCount === 6);
    await settle(); await waitEditorFocus();
    await inputCheckpoint('title-before-select');
    await page.keyboard.press('Control+a');
    await inputCheckpoint('title-after-select');
    await page.keyboard.type('Browser smoke test');
    await inputCheckpoint('title-after-type');
    await page.waitForFunction(() => document.getElementById('uno-input')?.value === 'Browser smoke test', null, { timeout: 5000 });
    await page.keyboard.press('Enter');
    await waitState(() => globalThis.noteSpaceState?.pageTitle === 'Browser smoke test');

    await page.mouse.dblclick(670, 445, { delay: 100 });
    await settle(); await waitEditorFocus();
    await page.keyboard.type('A note created by real browser input.');
    await page.keyboard.press('Escape');
    await waitState(() => globalThis.noteSpaceState?.blockCount === 1 && !globalThis.noteSpaceState.dirty);
    await page.reload({ waitUntil: 'domcontentloaded' });
    await waitState(() => globalThis.noteSpaceState?.ready && !globalThis.noteSpaceState.dirty);
    await settle();
    assert.equal((await state()).pageTitle, 'Browser smoke test');
    assert.equal((await state()).blockCount, 1);

    // Real native-input selections must preserve mixed styles when another
    // attribute is applied across the selection. Storage reads only observe results.
    await page.mouse.dblclick(695, 463, { delay: 100 }); await settle(); await waitEditorFocus();
    await page.keyboard.press('Control+Home');
    for (let i = 0; i < 5; i++) await page.keyboard.press('Shift+ArrowRight');
    await page.keyboard.press('Control+b'); await settle();
    await page.keyboard.press('Control+End');
    for (let i = 0; i < 6; i++) await page.keyboard.press('Shift+ArrowLeft');
    await page.keyboard.press('Control+i'); await settle();
    await page.keyboard.press('Control+a'); await page.keyboard.press('Control+u'); await settle();
    await page.keyboard.press('Escape');
    await waitState(() => globalThis.noteSpaceState?.blockCount === 1 && !globalThis.noteSpaceState.dirty);
    const styled = await page.evaluate(async () => {
        const stored = await globalThis.NoteSpaceHost.load();
        const workspace = JSON.parse(stored.slice(stored.indexOf('\n') + 1));
        const current = workspace.notebooks.flatMap(n => n.sections).flatMap(s => s.pages).find(p => p.id === workspace.settings.selectedPageId);
        return current.blocks[0];
    });
    assert.equal(styled.text, 'A note created by real browser input.');
    assert.ok(styled.marks.some(m => m.start === 0 && m.format.bold), 'Initial bold range is preserved');
    assert.ok(styled.marks.some(m => m.start + m.length === styled.text.length && m.format.italic), 'Final italic range is preserved');
    assert.ok(styled.marks.every(m => m.format.underline), 'Underline applies uniformly across mixed styles');
    await page.screenshot({ path: resolve(output, 'rich-text.png') });

    await button('Draw', () => page.mouse.click(218, 60));
    await page.screenshot({ path: resolve(output, 'draw-tab.png') });
    await button('Pen', () => page.mouse.click(212, 120));
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

    // Create and traverse a real three-level page outline using UI accelerators.
    await page.keyboard.press('Control+Alt+Shift+n');
    await waitState(() => globalThis.noteSpaceState?.pageCount === 7 && globalThis.noteSpaceState.pageLevel === 1);
    await settle(); await waitEditorFocus(); await page.keyboard.press('Control+a'); await page.keyboard.type('Child page'); await page.keyboard.press('Enter');
    await waitState(() => globalThis.noteSpaceState?.pageTitle === 'Child page');
    await page.keyboard.press('Control+Alt+Shift+n');
    await waitState(() => globalThis.noteSpaceState?.pageCount === 8 && globalThis.noteSpaceState.pageLevel === 2);
    await settle(); await waitEditorFocus(); await page.keyboard.press('Control+a'); await page.keyboard.type('Nested child'); await page.keyboard.press('Enter');
    await waitState(() => globalThis.noteSpaceState?.pageTitle === 'Nested child' && !globalThis.noteSpaceState.dirty);
    await page.screenshot({ path: resolve(output, 'page-outline.png') });
    await page.keyboard.press('F6'); await settle();
    await page.keyboard.press('ArrowLeft');
    await waitState(() => globalThis.noteSpaceState?.pageTitle === 'Child page'); await settle();
    await page.keyboard.press('ArrowLeft');
    await waitState(() => globalThis.noteSpaceState?.pageCollapsed && globalThis.noteSpaceState.visiblePageCount === 5); await settle();
    await page.keyboard.press('ArrowLeft');
    await waitState(() => globalThis.noteSpaceState?.pageTitle === 'Browser smoke test'); await settle();
    await page.keyboard.press('ArrowLeft');
    await waitState(() => globalThis.noteSpaceState?.pageCollapsed && globalThis.noteSpaceState.visiblePageCount === 4 && !globalThis.noteSpaceState.dirty);
    await page.reload({ waitUntil: 'domcontentloaded' });
    await waitState(() => globalThis.noteSpaceState?.ready && !globalThis.noteSpaceState.dirty); await settle();
    assert.equal((await state()).pageCollapsed, true); assert.equal((await state()).visiblePageCount, 4);
    assert.equal((await state()).pageCount, 8);
    await page.keyboard.press('F6'); await settle(); await page.keyboard.press('ArrowRight');
    await waitState(() => globalThis.noteSpaceState?.visiblePageCount === 5 && !globalThis.noteSpaceState.pageCollapsed); await settle();
    await page.keyboard.press('ArrowRight');
    await waitState(() => globalThis.noteSpaceState?.pageTitle === 'Child page' && globalThis.noteSpaceState.pageCollapsed); await settle();
    await page.keyboard.press('ArrowRight');
    await waitState(() => globalThis.noteSpaceState?.visiblePageCount === 6 && !globalThis.noteSpaceState.pageCollapsed); await settle();
    await page.keyboard.press('ArrowRight');
    await waitState(() => globalThis.noteSpaceState?.pageTitle === 'Nested child' && !globalThis.noteSpaceState.dirty);

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
    await writeFile(resolve(output, 'result.json'), JSON.stringify({ passed: true, tests: ['boot', 'create page', 'edit title', 'edit note', 'autosave', 'reload', 'draw', 'undo', 'redo', 'ink persistence', 'mixed range formatting', 'nested subpages', 'keyboard parent navigation', 'collapse groups', 'collapse persistence', 'keyboard expand navigation', 'atomic storage conflict', 'mobile boot'] }, null, 2));
    console.log('PASS 18 browser workflows: editing, rich text, ink, history, page outline, persistence, conflict, mobile');
} catch (error) {
    await inputCheckpoint('failure-input-trace').catch(() => {});
    await page.screenshot({ path: resolve(output, 'failure.png') }).catch(() => {});
    await writeFile(resolve(output, 'failure-dom.html'), await page.content()).catch(() => {});
    await writeFile(resolve(output, 'failure-state.json'), JSON.stringify(await state().catch(() => null), null, 2) || 'null');
    await writeFile(resolve(output, 'failure-focus.json'), JSON.stringify(await page.evaluate(() => ({
        active: document.activeElement?.outerHTML,
        inputs: [...document.querySelectorAll('input,textarea')].map(element => ({ html: element.outerHTML, value: element.value, selectionStart: element.selectionStart, selectionEnd: element.selectionEnd }))
    })).catch(() => null), null, 2));
    throw error;
} finally {
    await writeFile(resolve(output, 'console.log'), logs.join('\n') + '\nERRORS\n' + errors.join('\n'));
    await browser.close(); await new Promise(resolve => server.close(resolve));
}
