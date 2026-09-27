import assert from 'node:assert/strict';
import { writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

// Isolated profile: organization/conflict tests must not change the table fixture.
// Document mutations use the rendered UI; storage is read only for assertions.
export async function tableWorkflows({ browser, output }) {
    const context = await browser.newContext({ viewport: { width: 1600, height: 1000 }, permissions: ['clipboard-read', 'clipboard-write'] });
    const page = await context.newPage(); const errors = []; const completed = []; let stage = 'table-boot';
    page.on('pageerror', error => errors.push(error.message));
    const state = () => page.evaluate(() => globalThis.noteSpaceState);
    const wait = fn => page.waitForFunction(fn, null, { timeout: 45000 });
    const settle = async () => { await page.evaluate(() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))); await page.waitForTimeout(180); };
    const saved = () => page.evaluate(async () => {
        const value = await globalThis.NoteSpaceHost.load(); const w = JSON.parse(value.slice(value.indexOf('\n') + 1));
        return w.notebooks.flatMap(n => n.sections).flatMap(s => s.pages).find(p => p.title === 'Weekly planning').blocks.find(b => b.kind === 3);
    });
    const committed = async () => { await settle(); await wait(() => !globalThis.noteSpaceState.dirty); };
    const type = async text => { await wait(() => ['INPUT', 'TEXTAREA'].includes(document.activeElement?.tagName)); await page.keyboard.press('Control+a'); await page.keyboard.type(text); };
    const cell = async (row, column = 0) => {
        // Surface origin (410,185), table origin (48,330), column width 680/3.
        await page.mouse.dblclick(458 + 35 + column * (680 / 3), 185 + 330 + 21 + 42 * row, { delay: 100 }); await settle();
        await page.waitForFunction(({ row, column }) => globalThis.noteSpaceState?.editingTableCell && globalThis.noteSpaceState.tableRow === row && globalThis.noteSpaceState.tableColumn === column, { row, column }, { timeout: 10000 });
    };
    const ribbon = async (label, x, y) => {
        const button = page.getByRole('button', { name: label, exact: true }).first();
        // Semantic nodes are pointer-transparent; the canvas receives real input.
        await page.mouse.click(x, y);
        await settle();
    };
    try {
        await page.goto('http://127.0.0.1:4173/NoteSpace/', { waitUntil: 'domcontentloaded' });
        await page.waitForFunction(() => globalThis.noteSpaceState?.ready && !globalThis.noteSpaceState.dirty, null, { timeout: 150000 }); await settle();
        await page.mouse.click(300, 324); await wait(() => globalThis.noteSpaceState.pageTitle === 'Weekly planning'); await settle();
        stage = 'table-in-place'; await cell(1); await type('Reviewed');
        stage = 'table-forward-tab'; await page.keyboard.press('Tab'); await wait(() => globalThis.noteSpaceState.editingTableCell && globalThis.noteSpaceState.tableColumn === 1); await settle();
        await type('Ada'); stage = 'table-backward-tab'; await page.keyboard.press('Shift+Tab');
        await wait(() => globalThis.noteSpaceState.editingTableCell && globalThis.noteSpaceState.tableRow === 1 && globalThis.noteSpaceState.tableColumn === 0);
        await settle(); stage = 'table-forward-enter'; await page.keyboard.press('Enter');
        await wait(() => globalThis.noteSpaceState.editingTableCell && globalThis.noteSpaceState.tableRow === 2); await settle();
        await page.keyboard.press('Escape'); await committed();
        let table = await saved(); assert.equal(table.cells[1][0], 'Reviewed'); assert.equal(table.cells[1][1], 'Ada');
        completed.push('in-place table cell edit', 'table Tab and Shift+Tab navigation', 'table Enter row navigation');
        await page.screenshot({ path: resolve(output, 'table-in-place.png') });

        stage = 'table-append-row'; await cell(3, 2); await page.keyboard.press('Tab');
        await wait(() => globalThis.noteSpaceState.editingTableCell && globalThis.noteSpaceState.tableRow === 4 && globalThis.noteSpaceState.tableColumn === 0); await settle();
        await type('New task'); await page.keyboard.press('Escape'); await committed();
        assert.equal((await saved()).cells[4][0], 'New task'); completed.push('Tab appends a table row');
        await page.keyboard.press('Control+z'); await committed(); assert.equal((await saved()).cells[4][0], '');
        await page.keyboard.press('Control+z'); await committed(); assert.equal((await saved()).cells.length, 4);
        await page.keyboard.press('Control+y'); await committed(); await page.keyboard.press('Control+y'); await committed();
        assert.equal((await saved()).cells[4][0], 'New task'); completed.push('table page-history undo and redo');

        // Enable semantic UI only after keyboard navigation checks. Uno's enable
        // button may forward Space to the toolbar; restore navigation as necessary.
        stage = 'table-ribbon';
        const enable = page.getByRole('button', { name: 'Enable accessibility', exact: true });
        if (await enable.count()) { await enable.focus(); await page.keyboard.press('Space'); await settle(); }
        if (!await page.locator('[xamlautomationid="page-outline"]').count()) { await page.mouse.click(20, 20); await settle(); }
        await ribbon('Table', 480, 60);
        await page.screenshot({ path: resolve(output, 'table-tools.png') });
        // All new ribbon controls have stable semantic names/automation IDs.
        const command = async (id) => {
            const item = page.locator(`[xamlautomationid="command-table-${id}"]`);
            await item.waitFor({ state: 'attached', timeout: 10000 }); const b = await item.boundingBox();
            assert.ok(b && b.width > 0); await page.mouse.click(b.x + b.width / 2, b.y + b.height / 2); await committed();
        };
        await command('row-above'); assert.equal((await saved()).cells.length, 6);
        await command('delete-row'); assert.equal((await saved()).cells.length, 5);
        await command('column-right'); assert.equal((await saved()).cells[0].length, 4);
        await command('transpose'); table = await saved(); assert.equal(table.cells.length, 4); assert.equal(table.cells[0].length, 5);
        await command('transpose'); assert.equal((await saved()).cells.length, 5);
        completed.push('table ribbon row and column operations', 'table transpose');
        await command('copy');
        const copied = await page.evaluate(() => navigator.clipboard.readText()); assert.ok(copied.includes('Reviewed') && copied.includes('\t'));
        // Preparing the OS clipboard is not a hidden document mutation.
        await page.evaluate(() => navigator.clipboard.writeText('"quoted\tcell"\tB\nC\t"line\nnext"'));
        const destination = await state(); await command('paste'); table = await saved();
        assert.equal(table.cells[destination.tableRow][destination.tableColumn], 'quoted\tcell');
        assert.equal(table.cells[destination.tableRow + 1][destination.tableColumn + 1], 'line\nnext');
        completed.push('quoted TSV system clipboard copy and paste');
        await page.screenshot({ path: resolve(output, 'table-clipboard.png') });
        await page.reload({ waitUntil: 'domcontentloaded' }); await wait(() => globalThis.noteSpaceState?.ready && !globalThis.noteSpaceState.dirty); await settle();
        assert.deepEqual((await saved()).cells, table.cells); completed.push('table autosave and reload');
        assert.deepEqual(errors, []); await writeFile(resolve(output, 'tables-result.json'), JSON.stringify({ passed: true, tests: completed }, null, 2));
        console.log(`PASS ${completed.length} table browser workflows`); return completed;
    } catch (error) {
        await page.screenshot({ path: resolve(output, 'table-failure.png') }).catch(() => {});
        await writeFile(resolve(output, 'table-failure.json'), JSON.stringify({ stage, error: String(error.stack || error), completed, errors, state: await state().catch(() => null), table: await saved().catch(() => null) }, null, 2));
        await writeFile(resolve(output, 'table-failure-dom.html'), await page.content()); throw error;
    } finally { await context.close(); }
}
