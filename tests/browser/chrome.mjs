import assert from 'node:assert/strict';
import { writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

// Edits use real keyboard and pointer input. Diagnostic geometry and IndexedDB
// reads observe the result only; no test-only document command API is used.
export async function chromeWorkflows({ browser, output }) {
    const context = await browser.newContext({ viewport: { width: 1600, height: 1000 } });
    const page = await context.newPage(); const tests = []; const errors = []; let stage = 'boot';
    page.on('pageerror', error => errors.push(error.message));
    const state = () => page.evaluate(() => globalThis.noteSpaceState);
    const wait = fn => page.waitForFunction(fn, null, { timeout: 45000 });
    const settle = async () => { await page.evaluate(() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))); await page.waitForTimeout(200); };
    const saved = () => page.evaluate(async () => { const raw = await NoteSpaceHost.load(); return JSON.parse(raw.slice(raw.indexOf('\n') + 1)); });
    const currentBlock = async () => { const w = await saved(); return w.notebooks.flatMap(n => n.sections).flatMap(s => s.pages).find(p => p.id === w.settings.selectedPageId).blocks[0]; };
    const style = (b, at) => b.marks.filter(m => at >= m.start && at < m.start + m.length).at(-1)?.format ?? b.format;
    const activate = async id => {
        const button = page.locator(`[xamlautomationid="${id}"]`).first();
        await button.waitFor({ state: 'attached', timeout: 10000 }); await button.focus(); await page.keyboard.press('Space'); await settle();
    };
    const enableAccessibility = async () => {
        const enable = page.getByRole('button', { name: 'Enable accessibility', exact: true });
        if (await enable.count()) { await enable.focus(); await page.keyboard.press('Space'); await settle(); }
    };
    try {
        await page.goto('http://127.0.0.1:4173/NoteSpace/', { waitUntil: 'domcontentloaded' });
        await page.waitForFunction(() => globalThis.noteSpaceState?.ready && !noteSpaceState.dirty, null, { timeout: 150000 }); await settle();
        await page.keyboard.press('Control+Alt+n'); await settle(); await page.keyboard.press('Control+a');
        await page.keyboard.type('Formatting toolbar'); await page.keyboard.press('Enter'); await settle();
        await page.mouse.dblclick(670, 445, { delay: 100 }); await settle();
        await page.keyboard.type('Alpha beta gamma'); await settle(); await wait(() => !noteSpaceState.dirty);
        await enableAccessibility();

        stage = 'pointer-mini-toolbar';
        await page.mouse.move(682, 470); await page.mouse.down(); await page.mouse.move(725, 470, { steps: 10 }); await page.mouse.up(); await settle();
        await wait(() => noteSpaceState.selectionToolbarVisible && noteSpaceState.textSelectionLength > 0);
        const selected = await state(); const textBefore = (await currentBlock()).text;
        // The selection toolbar is an overlay in NoteSurface; its reported rectangle
        // is public control geometry, not an invitation to bypass input dispatch.
        await page.mouse.click(410 + selected.selectionToolbarX + 110, 185 + selected.selectionToolbarY + 20);
        await settle(); await wait(() => !noteSpaceState.dirty);
        let b = await currentBlock(); assert.equal(b.text, textBefore); assert.equal(style(b, selected.textSelectionStart).bold, true);
        assert.equal((await state()).textSelectionLength, selected.textSelectionLength);
        tests.push('pointer mini-toolbar formats only the retained selection');
        await page.screenshot({ path: resolve(output, 'selection-mini-toolbar.png') });

        stage = 'mini-keyboard-dismiss';
        await page.keyboard.press('Alt+F10'); await settle(); await page.keyboard.press('Escape'); await settle();
        assert.equal((await state()).selectionToolbarVisible, false); assert.equal((await state()).richTextEditing, true);
        assert.equal((await currentBlock()).text, textBefore);
        tests.push('keyboard toolbar focus and dismissal preserve the text editor');
        await page.keyboard.press('Escape'); await settle();

        stage = 'simplified-ribbon';
        await activate('ribbon-mode-toggle'); await wait(() => noteSpaceState.simplifiedRibbon && !noteSpaceState.dirty);
        assert.equal((await saved()).settings.navigation.simplifiedRibbon, true);
        const controls = page.locator('[xamlautomationid="simplified-ribbon"]'); await controls.waitFor({ state: 'attached' });
        await page.screenshot({ path: resolve(output, 'simplified-ribbon.png') });
        tests.push('classic and simplified ribbon modes share persisted preferences');
        // The selected note remains selected after the mode switch. A command in the
        // new single-row strip must still change actual content, not a decorative state.
        await activate('command-italic'); await wait(() => !noteSpaceState.dirty);
        b = await currentBlock(); assert.equal(style(b, 0).italic, true); assert.equal(style(b, b.text.length - 1).italic, true);
        tests.push('simplified ribbon invokes the original editor command');

        stage = 'ribbon-overflow';
        await page.setViewportSize({ width: 720, height: 850 }); await settle(); await page.keyboard.press('Control+s'); await settle();
        await wait(() => noteSpaceState.ribbonOverflowCount > 0);
        const rebuilds = (await state()).compactRebuilds;
        await page.setViewportSize({ width: 721, height: 850 }); await settle(); await page.keyboard.press('Control+s'); await settle();
        assert.equal((await state()).compactRebuilds, rebuilds, 'No command rebuilding within the same overflow breakpoint');
        await activate('ribbon-overflow');
        await page.locator('[xamlautomationid="overflow-new-text"]').waitFor({ state: 'attached', timeout: 10000 });
        const before = (await state()).blockCount;
        const item = page.locator('[xamlautomationid="overflow-new-text"]'); await item.focus(); await page.keyboard.press('Enter'); await settle();
        await page.waitForFunction(n => noteSpaceState.blockCount === n + 1 && noteSpaceState.richTextEditing, before, { timeout: 10000 });
        await page.keyboard.type('Created from ribbon overflow'); await page.keyboard.press('Escape'); await settle(); await wait(() => !noteSpaceState.dirty);
        tests.push('narrow ribbon overflow retains all commands and stable layout caching');

        stage = 'ribbon-collapse';
        await page.keyboard.press('Control+F1'); await settle(); await wait(() => !noteSpaceState.dirty);
        assert.equal((await saved()).settings.ribbonCollapsed, true);
        await page.keyboard.press('Control+F1'); await settle(); await wait(() => !noteSpaceState.dirty);
        assert.equal((await saved()).settings.ribbonCollapsed, false);
        tests.push('Ctrl+F1 collapses and restores the active ribbon mode');

        stage = 'chrome-preferences';
        await page.setViewportSize({ width: 1600, height: 1000 }); await settle();
        await activate('tab-view'); await activate('command-selection-toolbar'); await wait(() => !noteSpaceState.dirty);
        assert.equal((await saved()).settings.navigation.showSelectionToolbar, false);
        await page.reload({ waitUntil: 'domcontentloaded' }); await wait(() => globalThis.noteSpaceState?.ready && !noteSpaceState.dirty); await settle();
        assert.equal((await state()).simplifiedRibbon, true); assert.equal((await saved()).settings.navigation.showSelectionToolbar, false);
        await enableAccessibility();
        // At simplified mode the canvas starts 58px higher than the classic ribbon.
        await page.mouse.dblclick(700, 410, { delay: 100 }); await settle();
        await wait(() => noteSpaceState.richTextEditing);
        await page.keyboard.press('Control+a'); await page.keyboard.press('Alt+F10'); await settle();
        assert.equal((await state()).selectionToolbarVisible, false);
        tests.push('disabled selection tools and simplified ribbon survive reload');
        await page.keyboard.press('Escape'); await settle(); await activate('ribbon-mode-toggle');
        await wait(() => !noteSpaceState.simplifiedRibbon && !noteSpaceState.dirty);
        assert.deepEqual(errors, []);
        await writeFile(resolve(output, 'chrome-result.json'), JSON.stringify({ passed: true, tests }, null, 2));
        console.log(`PASS ${tests.length} editor chrome workflows`); return tests;
    } catch (error) {
        await page.screenshot({ path: resolve(output, 'chrome-failure.png') }).catch(() => {});
        await writeFile(resolve(output, 'chrome-failure.json'), JSON.stringify({ stage, tests, errors, state: await state().catch(() => null), saved: await saved().catch(() => null) }, null, 2));
        await writeFile(resolve(output, 'chrome-failure-dom.html'), await page.content().catch(() => '')); throw error;
    } finally { await context.close(); }
}
