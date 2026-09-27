import assert from 'node:assert/strict';
import { writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

// UI input performs every edit. Runtime/IndexedDB reads only observe results.
export async function searchWorkflows({ browser, output }) {
    const context = await browser.newContext({ viewport: { width: 1600, height: 1000 } });
    const page = await context.newPage(); const completed = []; const errors = []; let stage = 'search-boot';
    page.on('pageerror', error => errors.push(error.message));
    const wait = fn => page.waitForFunction(fn, null, { timeout: 45000 });
    const settle = async () => { await page.evaluate(() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))); await page.waitForTimeout(250); };
    const click = async id => {
        const item = page.locator(`[xamlautomationid="${id}"]`).first();
        await item.waitFor({ state: 'attached', timeout: 10000 }); const b = await item.boundingBox();
        assert.ok(b && b.width > 0, `Visible ${id}`); await page.mouse.click(b.x + b.width / 2, b.y + b.height / 2); await settle();
    };
    const result = () => page.locator('[xamlautomationid^="search-result-"]');
    const count = async expected => {
        await page.waitForFunction(n => document.querySelectorAll('[xamlautomationid^="search-result-"]').length === n, expected, { timeout: 10000 }); await settle();
    };
    const query = async text => {
        await page.keyboard.press('Control+f'); await settle();
        await page.keyboard.press('Control+a'); if (text.length) await page.keyboard.type(text); else await page.keyboard.press('Backspace');
        await settle();
    };
    const choose = async (id, index) => {
        await click(id); await page.keyboard.press('Home');
        for (let i = 0; i < index; i++) await page.keyboard.press('ArrowDown');
        await page.keyboard.press('Enter'); await settle();
    };
    const saved = () => page.evaluate(async () => {
        const value = await globalThis.NoteSpaceHost.load(); return JSON.parse(value.slice(value.indexOf('\n') + 1));
    });
    const findPage = (w, title) => w.notebooks.flatMap(n => n.sections).flatMap(s => s.pages).find(p => p.title === title);
    try {
        await page.goto('http://127.0.0.1:4173/NoteSpace/', { waitUntil: 'domcontentloaded' });
        await page.waitForFunction(() => globalThis.noteSpaceState?.ready && !globalThis.noteSpaceState.dirty, null, { timeout: 150000 }); await settle();
        const enable = page.getByRole('button', { name: 'Enable accessibility', exact: true });
        if (await enable.count()) { await enable.focus(); await page.keyboard.press('Space'); await settle(); }
        if (!await page.locator('[xamlautomationid="page-outline"]').count()) { await page.mouse.click(20, 20); await settle(); }

        stage = 'search-table-location'; await query('Design review'); await count(1);
        const tableResult = await result().first().getAttribute('xamlautomationid'); await click(tableResult);
        await wait(() => globalThis.noteSpaceState.pageTitle === 'Weekly planning' && globalThis.noteSpaceState.editingTableCell && globalThis.noteSpaceState.tableRow === 1 && globalThis.noteSpaceState.tableColumn === 0);
        assert.equal(await page.evaluate(() => document.getElementById('uno-input')?.value), 'Design review');
        completed.push('search navigates to exact table cell');

        stage = 'search-whole-word'; await query('plan'); await count(2);
        await click('search-whole-word'); await count(1); completed.push('whole-word search filter');
        stage = 'search-case'; await click('search-match-case'); await query('PLAN'); await count(0);
        await click('search-match-case'); await query('plan'); await count(1); completed.push('case-sensitive search filter');
        await click('search-whole-word'); await count(2);
        stage = 'search-scope'; await choose('search-scope', 3); await count(1);
        assert.ok((await result().first().getAttribute('aria-label'))?.includes('Weekly planning'));
        completed.push('current-page search scope');
        await choose('search-scope', 0); await count(2);

        stage = 'search-todos'; await query(''); await choose('search-filter', 2); await count(4);
        await choose('search-filter', 3); await count(0); completed.push('open and completed to-do search');
        await choose('search-filter', 0);
        stage = 'search-text-selection'; await query('possibility'); await count(1);
        await click(await result().first().getAttribute('xamlautomationid'));
        await wait(() => globalThis.noteSpaceState.pageTitle === 'Welcome to NoteSpace'); await settle();
        const selected = await page.evaluate(() => { const e = document.getElementById('uno-input'); return e?.value.slice(e.selectionStart, e.selectionEnd); });
        assert.equal(selected, 'possibility'); completed.push('search selects the matched text range');
        await page.keyboard.press('Escape'); await settle();

        stage = 'scoped-replace'; await query('plan'); await choose('search-scope', 3); await click('search-whole-word'); await count(1);
        const before = await saved();
        await page.keyboard.press('Control+h'); await settle();
        await page.keyboard.press('Control+a'); await page.keyboard.type('plan');
        await page.keyboard.press('Tab'); await page.keyboard.type('roadmap');
        await page.keyboard.press('Shift+Tab'); await page.keyboard.press('Enter'); await settle();
        // The completion dialog is dismissible without relying on its popup peer's local bounds.
        await page.keyboard.press('Escape'); await wait(() => !globalThis.noteSpaceState.dirty); await settle();
        const after = await saved();
        assert.ok(findPage(after, 'Welcome to NoteSpace').blocks.some(b => b.text.includes('Make a roadmap.')));
        assert.deepEqual(findPage(after, 'Weekly planning'), findPage(before, 'Weekly planning'));
        completed.push('scoped whole-word replace through the dialog');
        await page.keyboard.press('Control+z'); await wait(() => !globalThis.noteSpaceState.dirty); await settle();
        assert.deepEqual(findPage(await saved(), 'Welcome to NoteSpace'), findPage(before, 'Welcome to NoteSpace'));
        completed.push('scoped replace is one undo action');
        await page.screenshot({ path: resolve(output, 'scoped-search.png') });
        assert.deepEqual(errors, []);
        await writeFile(resolve(output, 'search-result.json'), JSON.stringify({ passed: true, tests: completed }, null, 2));
        console.log(`PASS ${completed.length} search browser workflows`); return completed;
    } catch (error) {
        await page.screenshot({ path: resolve(output, 'search-failure.png') }).catch(() => {});
        await writeFile(resolve(output, 'search-failure.json'), JSON.stringify({ stage, completed, errors,
            state: await page.evaluate(() => globalThis.noteSpaceState).catch(() => null), saved: await saved().catch(() => null),
            input: await page.evaluate(() => ({ html: document.activeElement?.outerHTML, value: document.getElementById('uno-input')?.value })).catch(() => null)
        }, null, 2));
        await writeFile(resolve(output, 'search-failure-dom.html'), await page.content()); throw error;
    } finally { await context.close(); }
}
