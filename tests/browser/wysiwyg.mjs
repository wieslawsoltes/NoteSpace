import assert from 'node:assert/strict';
import { writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

export async function wysiwygWorkflows({ browser, output }) {
    const context = await browser.newContext({ viewport: { width: 1600, height: 1000 } });
    const page = await context.newPage(); const errors = []; const tests = []; let stage = 'boot';
    page.on('pageerror', error => errors.push(error.message));
    const settle = async () => { await page.evaluate(() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))); await page.waitForTimeout(180); };
    const wait = fn => page.waitForFunction(fn, null, { timeout: 45000 });
    const state = () => page.evaluate(() => globalThis.noteSpaceState);
    const text = () => page.evaluate(() => document.getElementById('uno-input')?.value);
    const saved = () => page.evaluate(async () => {
        const value = await globalThis.NoteSpaceHost.load(); const w = JSON.parse(value.slice(value.indexOf('\n') + 1));
        return w.notebooks.flatMap(n => n.sections).flatMap(s => s.pages).find(p => p.id === w.settings.selectedPageId);
    });
    const styleAt = (block, at) => block.marks.filter(m => at >= m.start && at < m.start + m.length).at(-1)?.format ?? block.format;
    try {
        await page.goto('http://127.0.0.1:4173/NoteSpace/', { waitUntil: 'domcontentloaded' });
        await page.waitForFunction(() => globalThis.noteSpaceState?.ready && !globalThis.noteSpaceState.dirty, null, { timeout: 150000 }); await settle();
        await page.keyboard.press('Control+Alt+n'); await settle();
        await page.keyboard.press('Control+a'); await page.keyboard.type('Live rich editing'); await page.keyboard.press('Enter'); await settle();
        await page.mouse.dblclick(670, 445, { delay: 100 }); await wait(() => globalThis.noteSpaceState.richTextEditing); await settle();
        await page.keyboard.type('Alpha beta'); await settle();
        await wait(() => !globalThis.noteSpaceState.dirty); assert.equal(await text(), 'Alpha beta');
        assert.equal((await state()).richTextEditing, true); tests.push('live Skia draft remains active through autosave');

        stage = 'selected-formatting';
        await page.keyboard.press('Control+Home'); await settle();
        for (let i = 0; i < 5; i++) await page.keyboard.press('Shift+ArrowRight'); await settle();
        assert.equal((await state()).textSelectionStart, 0); assert.equal((await state()).textSelectionLength, 5);
        const before = await page.screenshot({ clip: { x: 660, y: 442, width: 400, height: 70 } });
        await page.keyboard.press('Control+b'); await settle();
        assert.equal((await state()).richTextEditing, true); assert.equal((await state()).textSelectionLength, 5);
        const after = await page.screenshot({ clip: { x: 660, y: 442, width: 400, height: 70 } });
        assert.ok(!before.equals(after), 'Formatting changes the live text pixels, not only saved markup');
        await page.screenshot({ path: resolve(output, 'wysiwyg-active-selection.png') });
        await wait(() => !globalThis.noteSpaceState.dirty);
        let b = (await saved()).blocks[0]; assert.equal(styleAt(b, 0).bold, true); assert.equal(styleAt(b, 7).bold, false);
        tests.push('selected formatting is visible while typing and preserves other text');

        stage = 'typing-format';
        await page.keyboard.press('Control+End'); await settle();
        await page.keyboard.press('Control+b'); await settle();
        await page.keyboard.type(' Bold'); await settle(); await wait(() => !globalThis.noteSpaceState.dirty);
        b = (await saved()).blocks[0]; assert.equal(b.text, 'Alpha beta Bold'); assert.equal(styleAt(b, 12).bold, true); assert.equal(styleAt(b, 6).bold, false);
        tests.push('collapsed formatting applies only to subsequent typing');

        stage = 'grapheme-deletion';
        await page.keyboard.insertText('😀e\u0301'); await settle();
        await page.keyboard.press('Backspace'); await settle(); assert.equal(await text(), 'Alpha beta Bold😀');
        await page.keyboard.press('Backspace'); await settle(); assert.equal(await text(), 'Alpha beta Bold');
        tests.push('native Unicode input and whole-grapheme deletion');

        stage = 'tabs-and-paragraphs';
        await page.keyboard.press('Tab'); await settle(); await page.keyboard.type('Next');
        await page.keyboard.press('Enter'); await settle(); await page.keyboard.type('Line'); await settle();
        assert.equal(await text(), 'Alpha beta Bold\tNext\nLine');
        await page.keyboard.press('Home'); await settle(); assert.equal((await state()).textSelectionStart, 21);
        await page.keyboard.press('ArrowUp'); await settle(); assert.ok((await state()).textSelectionStart < 21);
        tests.push('tabs paragraphs and visual-line keyboard navigation');

        stage = 'format-painter';
        await page.keyboard.press('Control+Home'); await settle();
        await page.keyboard.press('Shift+ArrowRight'); await settle(); await page.keyboard.press('Control+Shift+c'); await settle();
        await page.keyboard.press('Control+End'); await settle();
        for (let i = 0; i < 4; i++) await page.keyboard.press('Shift+ArrowLeft'); await settle();
        await page.keyboard.press('Control+Shift+v'); await settle(); await wait(() => !globalThis.noteSpaceState.dirty);
        b = (await saved()).blocks[0]; assert.equal(styleAt(b, 22).bold, true); tests.push('format copy and paste retains the selected range');

        stage = 'pointer-selection';
        const sx = 410 + b.x + 12; const sy = 185 + b.y + 24;
        await page.mouse.move(sx, sy); await page.mouse.down(); await page.mouse.move(sx + 65, sy, { steps: 8 }); await page.mouse.up(); await settle();
        assert.ok((await state()).textSelectionLength > 0); assert.equal((await state()).richTextEditing, true);
        tests.push('pointer selection follows rendered glyphs');

        stage = 'paragraph-indent';
        await page.keyboard.press('Control+m'); await settle(); await wait(() => !globalThis.noteSpaceState.dirty);
        assert.equal((await saved()).blocks[0].textFlow.leftIndent, 24);
        await page.keyboard.press('Control+Shift+m'); await settle(); await wait(() => !globalThis.noteSpaceState.dirty);
        assert.equal((await saved()).blocks[0].textFlow.leftIndent, 0); tests.push('live indentation and undoable text layout');
        await page.screenshot({ path: resolve(output, 'wysiwyg-layout.png') });

        stage = 'resize-and-undo';
        await page.keyboard.press('Escape'); await settle(); await wait(() => !globalThis.noteSpaceState.dirty);
        b = (await saved()).blocks[0];
        const hx = 410 + b.x + b.width - 2; const hy = 185 + b.y + b.height - 2;
        await page.mouse.move(hx, hy); await page.mouse.down(); await page.mouse.move(hx - 120, hy, { steps: 10 }); await page.mouse.up(); await settle();
        await wait(() => !globalThis.noteSpaceState.dirty); assert.ok(Math.abs((await saved()).blocks[0].width - (b.width - 120)) < 1);
        await page.keyboard.press('Control+z'); await settle(); await wait(() => !globalThis.noteSpaceState.dirty); assert.equal((await saved()).blocks[0].width, b.width);
        tests.push('container resizing reflows text and supports undo');

        stage = 'reload';
        const committed = (await saved()).blocks[0]; await page.reload({ waitUntil: 'domcontentloaded' });
        await wait(() => globalThis.noteSpaceState?.ready && !globalThis.noteSpaceState.dirty); await settle();
        assert.deepEqual((await saved()).blocks[0], committed); tests.push('rich styles and layout survive reload');
        await page.screenshot({ path: resolve(output, 'wysiwyg-committed.png') });
        stage = 'rapid-repeated-input';
        await page.mouse.dblclick(800, 750, { delay: 100 }); await wait(() => globalThis.noteSpaceState.richTextEditing); await settle();
        await page.keyboard.type('aaaa');
        // Deliberately do not insert frame waits between commands. A dispatcher
        // backlog must not reorder the selected offset, typing style or text.
        await page.keyboard.press('Control+Home'); await page.keyboard.press('ArrowRight');
        await page.keyboard.press('Control+b'); await page.keyboard.type('a'); await settle();
        await wait(() => !globalThis.noteSpaceState.dirty);
        b = (await saved()).blocks[1]; assert.equal(b.text, 'aaaaa');
        assert.equal(styleAt(b, 1).bold, true); assert.equal(styleAt(b, 0).bold, false); assert.equal(styleAt(b, 2).bold, false);
        tests.push('rapid repeated-character insertion preserves the actual styled offset');

        stage = 'rapid-queued-text-projection';
        await page.keyboard.press('Control+End'); await page.keyboard.press('Enter');
        await page.keyboard.type('xy'); await page.keyboard.press('Backspace'); await page.keyboard.type('z');
        await page.keyboard.press('Tab'); await page.keyboard.type('q'); await settle();
        assert.equal(await text(), 'aaaaa\nxz\tq');
        await page.keyboard.press('Escape'); await settle(); await wait(() => !globalThis.noteSpaceState.dirty);
        assert.equal((await saved()).blocks[1].text, 'aaaaa\nxz\tq');
        assert.deepEqual((await saved()).blocks[0], committed);
        tests.push('rapid host navigation and native typing retain all text in order');
        assert.deepEqual(errors, []); await writeFile(resolve(output, 'wysiwyg-result.json'), JSON.stringify({ passed: true, tests }, null, 2));
        console.log(`PASS ${tests.length} WYSIWYG browser workflows`); return tests;
    } catch (error) {
        await page.screenshot({ path: resolve(output, 'wysiwyg-failure.png') }).catch(() => {});
        await writeFile(resolve(output, 'wysiwyg-failure.json'), JSON.stringify({ stage, tests, errors, state: await state().catch(() => null), text: await text().catch(() => null), saved: await saved().catch(() => null) }, null, 2));
        await writeFile(resolve(output, 'wysiwyg-failure-dom.html'), await page.content()); throw error;
    } finally { await context.close(); }
}
