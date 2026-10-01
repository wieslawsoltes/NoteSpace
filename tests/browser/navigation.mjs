import assert from 'node:assert/strict';
import { writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

// Document changes are exclusively through public controls and real keyboard/pointer input.
export async function navigationWorkflows({ browser, output }) {
    const context = await browser.newContext({ viewport: { width: 1600, height: 1000 } });
    const page = await context.newPage(); const tests = []; const errors = []; let stage = 'boot';
    page.on('pageerror', error => errors.push(error.message));
    const state = () => page.evaluate(() => globalThis.noteSpaceState);
    const wait = fn => page.waitForFunction(fn, null, { timeout: 45000 });
    const settle = async () => { await page.evaluate(() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))); await page.waitForTimeout(170); };
    const saved = () => page.evaluate(async () => { const raw = await NoteSpaceHost.load(); return JSON.parse(raw.slice(raw.indexOf('\n') + 1)); });
    const sections = w => w.notebooks.flatMap(n => n.sections);
    const allPages = w => sections(w).flatMap(s => s.pages);
    const byId = id => page.locator(`[xamlautomationid="${id}"]`);
    const enableAccessibility = async () => {
        const button = page.getByRole('button', { name: 'Enable accessibility', exact: true });
        if (await button.count()) { await button.focus(); await page.keyboard.press('Space'); await settle(); }
    };
    const activate = async id => { await byId(id).waitFor({ state: 'attached' }); await byId(id).focus(); await page.keyboard.press('Enter'); await settle(); };
    const option = async command => { await activate('page-view-options'); await activate('command-' + command); await wait(() => !noteSpaceState.dirty); };
    const createPage = async (title, child = false) => {
        await page.keyboard.press(child ? 'Control+Alt+Shift+n' : 'Control+Alt+n'); await settle();
        await page.keyboard.press('Control+a'); await page.keyboard.type(title); await page.keyboard.press('Enter');
        await page.waitForFunction(title => noteSpaceState.pageTitle === title && !noteSpaceState.dirty, title); await settle();
        return (await state()).pageId;
    };
    const visibleIds = () => byId('page-outline').locator('[xamlautomationid^="page-"]').evaluateAll(nodes => nodes.map(n => n.getAttribute('xamlautomationid').slice(5)));
    const drag = async (start, dx, cancel = false) => {
        await page.mouse.move(start, 630); await page.mouse.down(); await page.mouse.move(start + dx, 630, { steps: 10 }); await settle();
        if (cancel) await page.keyboard.press('Escape');
        await page.mouse.up(); await settle(); await wait(() => !noteSpaceState.dirty);
    };
    try {
        await page.goto('http://127.0.0.1:4173/NoteSpace/', { waitUntil: 'domcontentloaded' });
        await page.waitForFunction(() => globalThis.noteSpaceState?.ready && !noteSpaceState.dirty, null, { timeout: 150000 }); await settle();
        const zulu = await createPage('Zebra view'); const child = await createPage('Nested view', true); const alpha = await createPage('Alpha view');
        await enableAccessibility();
        stage = 'section-switch';
        const initialWorkspace = await saved();
        const currentSectionId = (await state()).sectionId;
        const otherSection = sections(initialWorkspace).find(s => s.id !== currentSectionId && s.pages.length);
        await activate('section-' + otherSection.id);
        assert.equal((await state()).sectionId, otherSection.id);
        assert.deepEqual(await visibleIds(), otherSection.pages.map(p => p.id));
        await page.keyboard.press('Alt+ArrowLeft'); await settle();
        assert.equal((await state()).pageId, alpha);
        await page.keyboard.press('Alt+ArrowLeft'); await settle();
        await page.keyboard.press('Alt+ArrowRight'); await settle();
        tests.push('section switching binds the destination outline and browsing restores it');
        stage = 'history-viewports';
        await activate('tab-View'.toLowerCase()); await activate('command-zoom-in');
        await page.mouse.move(1000, 650); await page.mouse.wheel(0, 720); await settle(); await page.keyboard.press('Control+s'); await settle();
        const originalView = await state(); assert.ok(originalView.viewOffsetY > 0); assert.ok(originalView.zoom > 1);
        await page.keyboard.press('Alt+ArrowLeft'); await settle(); assert.equal((await state()).pageId, child);
        await page.keyboard.press('Alt+ArrowRight'); await settle();
        const restored = await state(); assert.equal(restored.pageId, alpha); assert.equal(restored.viewOffsetY, originalView.viewOffsetY); assert.equal(restored.zoom, originalView.zoom);
        tests.push('back and forward restore page scroll and zoom');

        stage = 'history-draft-flush';
        await activate('tab-home'); await activate('command-new-text'); await settle();
        await page.keyboard.type('Draft preserved by page history'); await page.keyboard.press('Alt+ArrowLeft'); await settle();
        assert.equal((await state()).pageId, child); await wait(() => !noteSpaceState.dirty);
        assert.ok(allPages(await saved()).find(p => p.id === alpha).blocks.some(b => b.text === 'Draft preserved by page history'));
        await page.keyboard.press('Alt+ArrowRight'); await settle(); assert.equal((await state()).pageId, alpha);
        tests.push('page history commits the active rich draft before navigating');

        stage = 'sort-and-previews';
        const w = await saved(); const sectionId = (await state()).sectionId;
        const originalOrder = sections(w).find(s => s.id === sectionId).pages.map(p => [p.id, p.level]);
        await option('sort-title'); let ids = await visibleIds();
        assert.equal(ids[0], alpha); assert.equal(ids[ids.indexOf(zulu) + 1], child);
        assert.deepEqual(sections(await saved()).find(s => s.id === sectionId).pages.map(p => [p.id, p.level]), originalOrder);
        assert.equal((await state()).pageSort, 'TitleAscending'); tests.push('alphabetic sorting preserves subtrees and manual organization');
        await option('sort-title-descending'); ids = await visibleIds(); assert.equal(ids[0], zulu); assert.equal(ids[1], child);
        await option('sort-created'); assert.equal((await state()).pageSort, 'CreatedNewest');
        await option('sort-modified'); assert.equal((await state()).pageSort, 'ModifiedNewest');
        await option('sort-manual'); assert.deepEqual(await visibleIds(), originalOrder.map(p => p[0]));
        tests.push('five page sort modes and return to saved manual order');
        await option('page-previews'); await option('page-dates');
        assert.equal((await state()).pagePreviews, true); assert.equal((await state()).pageDates, true);
        assert.ok(await byId('page-preview-' + alpha).count()); assert.ok(await byId('page-date-' + alpha).count());
        await page.screenshot({ path: resolve(output, 'navigation-page-previews.png') });
        tests.push('page preview and date display options');

        stage = 'resize-panes';
        let s = await state(); await drag(s.notebookPaneWidth - 3, 80);
        assert.equal((await saved()).settings.navigation.notebookWidth, 284);
        s = await state(); await drag(s.notebookPaneWidth + s.pagePaneWidth - 3, 100);
        assert.equal((await saved()).settings.navigation.pageWidth, 306);
        s = await state(); await drag(s.notebookPaneWidth + s.pagePaneWidth - 3, 60, true);
        assert.equal((await saved()).settings.navigation.pageWidth, 306); assert.equal((await state()).pagePaneWidth, 306);
        tests.push('pane drag resizing persists once and Escape restores the starting width');
        await page.screenshot({ path: resolve(output, 'navigation-resized.png') });
        await page.reload({ waitUntil: 'domcontentloaded' }); await wait(() => globalThis.noteSpaceState?.ready && !noteSpaceState.dirty); await settle();
        s = await state(); assert.equal(s.notebookPaneWidth, 284); assert.equal(s.pagePaneWidth, 306); assert.ok(s.pagePreviews && s.pageDates);
        await enableAccessibility(); tests.push('pane widths and page display settings survive reload');

        stage = 'keyboard-resize';
        await page.mouse.click(s.notebookPaneWidth + s.pagePaneWidth - 3, 630); await page.keyboard.press('ArrowRight'); await settle();
        await wait(() => !noteSpaceState.dirty); assert.equal((await saved()).settings.navigation.pageWidth, 316);
        await page.keyboard.press('Home'); await settle(); await wait(() => !noteSpaceState.dirty); assert.equal((await saved()).settings.navigation.pageWidth, 206);
        tests.push('pane edges support keyboard resizing and default-width reset');

        stage = 'selection-reuse';
        await option('page-previews'); await option('page-dates');
        await page.keyboard.press('F6'); await page.keyboard.press('Home'); await settle();
        const rowsBefore = (await state()).pageRowsBuilt;
        const order = await visibleIds(); assert.equal((await state()).pageId, order[0]);
        await page.keyboard.press('End'); await settle(); assert.equal((await state()).pageId, order.at(-1));
        await page.keyboard.press('Home'); await settle(); assert.equal((await state()).pageId, order[0]);
        assert.equal((await state()).pageRowsBuilt, rowsBefore);
        tests.push('Home and End navigate visible pages without rebuilding rows');
        await page.keyboard.press('Control+PageDown'); await settle(); assert.equal((await state()).pageId, order[1]);
        await page.keyboard.press('Control+PageUp'); await settle(); assert.equal((await state()).pageId, order[0]);
        assert.equal((await state()).pageRowsBuilt, rowsBefore); tests.push('page-only navigation reuses the existing page row controls');

        stage = 'search-edge';
        await page.keyboard.press('Control+e'); await settle(); assert.ok((await state()).searchOpen);
        s = await state(); await drag(1600 - s.searchPaneWidth + 3, -40);
        assert.equal((await saved()).settings.navigation.searchWidth, 350);
        await activate('close-search'); assert.equal((await state()).searchOpen, false);
        tests.push('resizable search pane and explicit close control');

        stage = 'responsive';
        await page.setViewportSize({ width: 900, height: 850 }); await settle(); await page.keyboard.press('Control+e'); await settle();
        s = await state(); assert.ok(s.notebookPaneWidth + s.pagePaneWidth + s.searchPaneWidth <= 620.01);
        assert.equal((await saved()).settings.navigation.notebookWidth, 284);
        await page.setViewportSize({ width: 390, height: 844 }); await settle(); s = await state();
        assert.ok(s.searchPaneWidth >= 389 && s.notebookPaneWidth === 0 && s.pagePaneWidth === 0);
        await page.screenshot({ path: resolve(output, 'navigation-phone-search.png') });
        await activate('close-search'); await page.mouse.click(20, 20); await settle(); await page.keyboard.press('Control+s'); await settle();
        s = await state(); assert.ok(Math.abs(s.notebookPaneWidth + s.pagePaneWidth - 390) < 1);
        await page.screenshot({ path: resolve(output, 'navigation-phone-pages.png') });
        tests.push('narrow layouts reserve paper space and phone panes use a full-width view');
        await page.setViewportSize({ width: 1600, height: 1000 }); await settle();
        await activate('tab-view'); await activate('command-reset-pane-widths'); await wait(() => !noteSpaceState.dirty);
        s = await state(); assert.equal(s.notebookPaneWidth, 204); assert.equal(s.pagePaneWidth, 206);
        tests.push('reset pane layout restores all saved defaults');
        assert.deepEqual(errors, []);
        await writeFile(resolve(output, 'navigation-result.json'), JSON.stringify({ passed: true, tests }, null, 2));
        console.log(`PASS ${tests.length} navigation UX workflows`); return tests;
    } catch (error) {
        await page.screenshot({ path: resolve(output, 'navigation-failure.png') }).catch(() => {});
        await writeFile(resolve(output, 'navigation-failure.json'), JSON.stringify({ stage, tests, errors, state: await state().catch(() => null), focus: await page.evaluate(() => document.activeElement?.outerHTML).catch(() => null) }, null, 2));
        await writeFile(resolve(output, 'navigation-failure-dom.html'), await page.content().catch(() => ''));
        throw error;
    } finally { await context.close(); }
}
