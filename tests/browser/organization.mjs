import assert from 'node:assert/strict';
import { writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

// All mutations use actual pointer/keyboard input. Saved JSON is read only for assertions.
export async function organizationWorkflows({ page, state, waitState, settle, output }) {
    let stage = 'organization-start';
    const completed = [];
    const saved = () => page.evaluate(async () => {
        const text = await globalThis.NoteSpaceHost.load();
        return JSON.parse(text.slice(text.indexOf('\n') + 1));
    });
    const allPages = w => w.notebooks.flatMap(n => n.sections).flatMap(s => s.pages);
    const allGroups = w => w.notebooks.flatMap(n => n.sectionGroups);
    const snapshot = async label => {
        stage = label;
        await settle();
        await page.screenshot({ path: resolve(output, label + '.png') });
        await writeFile(resolve(output, label + '.json'), JSON.stringify({ state: await state(), saved: await saved() }, null, 2));
    };
    const input = async text => {
        await page.waitForFunction(() => ['INPUT', 'TEXTAREA'].includes(document.activeElement?.tagName), null, { timeout: 10000 });
        await page.keyboard.press('Control+a'); await page.keyboard.type(text); await settle();
        await page.keyboard.press('Enter'); await settle();
    };
    const contextCommand = async (id, name) => {
        const target = page.locator(`[xamlautomationid="group-${id}"]`);
        await target.waitFor({ state: 'attached', timeout: 10000 });
        const bounds = await target.boundingBox(); assert.ok(bounds && bounds.width > 0);
        await page.mouse.click(bounds.x + bounds.width / 2, bounds.y + bounds.height / 2, { button: 'right' }); await settle();
        const item = page.getByRole('menuitem', { name, exact: true });
        await item.waitFor({ state: 'attached', timeout: 10000 });
        const menu = await item.boundingBox(); assert.ok(menu && menu.width > 0);
        await page.mouse.click(menu.x + menu.width / 2, menu.y + menu.height / 2); await settle();
    };
    // Fixed viewport matches the baseline smoke harness. Grips are at the right
    // edge of the 206px page list; rows are 38px high with a 267px starting Y.
    const gripX = 392, targetX = 306, rowY = i => 286 + 38 * i;
    const drag = async (from, to, cancel = false) => {
        await page.mouse.move(gripX, rowY(from)); await page.mouse.down();
        await page.mouse.move(targetX, to, { steps: 18 }); await settle();
        await page.screenshot({ path: resolve(output, stage + '-preview.png') });
        if (cancel) await page.keyboard.press('Escape');
        await page.mouse.up(); await settle();
    };
    try {
        await waitState(() => !globalThis.noteSpaceState.dirty);
        const original = await saved();
        const child = allPages(original).find(p => p.title === 'Child page');
        const nested = allPages(original).find(p => p.title === 'Nested child');
        const sectionId = (await state()).sectionId;
        const section = w => w.notebooks.flatMap(n => n.sections).find(s => s.id === sectionId);
        const initialOrder = section(original).pages.map(p => [p.id, p.level]);

        stage = 'drag-cancel'; const revision = (await state()).revision;
        await drag(4, rowY(2) - 15, true);
        assert.equal((await state()).revision, revision, 'Escape cancels without an edit');
        assert.deepEqual(section(await saved()).pages.map(p => [p.id, p.level]), initialOrder);
        completed.push('page drag cancellation');

        stage = 'drag-before'; await drag(4, rowY(2) - 15);
        await waitState(() => globalThis.noteSpaceState.pageTitle === 'Child page' && globalThis.noteSpaceState.pageLevel === 0 && !globalThis.noteSpaceState.dirty);
        const moved = section(await saved()).pages;
        assert.equal(moved[2].id, child.id); assert.equal(moved[3].id, nested.id); assert.equal(moved[3].level, 1);
        await snapshot('drag-before'); completed.push('page subtree drag reorder');
        await page.keyboard.press('Control+z'); await waitState(() => !globalThis.noteSpaceState.dirty); await settle();
        assert.deepEqual(section(await saved()).pages.map(p => [p.id, p.level]), initialOrder);
        completed.push('page drag undo');

        stage = 'drag-inside'; await drag(4, rowY(2));
        await waitState(() => globalThis.noteSpaceState.pageTitle === 'Child page' && !globalThis.noteSpaceState.dirty);
        const inside = section(await saved()).pages;
        assert.equal(inside[3].id, child.id); assert.equal(inside[3].level, 1);
        assert.equal(inside[4].id, nested.id); assert.equal(inside[4].level, 2);
        completed.push('page drag reparent');
        await page.keyboard.press('Control+z'); await settle(); await page.keyboard.press('Control+y');
        await waitState(() => !globalThis.noteSpaceState.dirty); await settle();
        assert.deepEqual(section(await saved()).pages.map(p => [p.id, p.level]), inside.map(p => [p.id, p.level]));
        const invalidRevision = (await state()).revision;
        stage = 'drag-invalid'; await drag(3, rowY(4));
        assert.equal((await state()).revision, invalidRevision, 'Dragging a parent into its descendant is rejected');
        completed.push('invalid page drop rejection');
        await page.reload({ waitUntil: 'domcontentloaded' });
        await waitState(() => globalThis.noteSpaceState?.ready && !globalThis.noteSpaceState.dirty); await settle();
        assert.deepEqual(section(await saved()).pages.map(p => [p.id, p.level]), inside.map(p => [p.id, p.level]));
        completed.push('page drag redo and persistence');

        // Menu keyboard focus differs between native and semantic input modes.
        // Enable the public accessibility UI and target the named rendered item,
        // rather than assuming Home/ArrowDown selected a particular menu index.
        const enable = page.getByRole('button', { name: 'Enable accessibility', exact: true });
        if (await enable.count()) { await enable.focus(); await page.keyboard.press('Space'); await settle(); }
        if (!await page.locator('[xamlautomationid="page-outline"]').count()) { await page.mouse.click(20, 20); await settle(); }

        stage = 'create-section-group'; await page.keyboard.press('Control+Alt+g'); await settle(); await input('Research group');
        await waitState(() => globalThis.noteSpaceState.sectionGroupCount === 1 && !globalThis.noteSpaceState.dirty);
        const group = allGroups(await saved()).find(g => g.title === 'Research group'); assert.ok(group);
        await snapshot('section-group-created'); completed.push('section group creation');

        stage = 'create-nested-group'; await contextCommand(group.id, 'New nested group'); await input('Drafts');
        await waitState(() => globalThis.noteSpaceState.sectionGroupCount === 2 && !globalThis.noteSpaceState.dirty);
        const drafts = allGroups(await saved()).find(g => g.title === 'Drafts'); assert.equal(drafts.parentId, group.id);
        await snapshot('nested-section-groups'); completed.push('nested section group creation');

        stage = 'create-grouped-section'; await contextCommand(drafts.id, 'New section in group'); await input('Research notes');
        await waitState(() => !!globalThis.noteSpaceState.sectionGroupId && !globalThis.noteSpaceState.dirty);
        assert.equal((await state()).sectionGroupId, drafts.id);
        assert.equal(allPages(await saved()).length, allPages(original).length + 1);
        await snapshot('grouped-section'); completed.push('section creation inside group');

        // Select an ungrouped section before collapsing so reload does not need
        // to temporarily reveal the selected section's collapsed ancestors.
        stage = 'collapse-section-group'; await page.mouse.click(86, 337); await settle();
        await page.mouse.click(100, 413); await settle(); await waitState(() => !globalThis.noteSpaceState.dirty);
        assert.equal(allGroups(await saved()).find(g => g.id === group.id).isCollapsed, true);
        await page.reload({ waitUntil: 'domcontentloaded' });
        await waitState(() => globalThis.noteSpaceState?.ready && !globalThis.noteSpaceState.dirty); await settle();
        assert.equal(allGroups(await saved()).find(g => g.id === group.id).isCollapsed, true);
        await snapshot('section-group-collapse'); completed.push('section group collapse persistence');

        stage = 'rename-section-group'; await contextCommand(group.id, 'Rename group'); await input('Research archive');
        await waitState(() => !globalThis.noteSpaceState.dirty);
        assert.equal(allGroups(await saved()).find(g => g.id === group.id).title, 'Research archive');
        completed.push('section group rename');

        stage = 'ungroup-section-group';
        // Menu items use viewport coordinates; the confirmation popup below
        // still requires its measured canvas coordinates in this Uno version.
        const groupRow = page.locator(`[xamlautomationid="group-${group.id}"]`);
        if (!await groupRow.count()) { await page.mouse.click(20, 20); await settle(); }
        await groupRow.waitFor({ state: 'attached', timeout: 10000 });
        const groupBounds = await groupRow.boundingBox();
        assert.ok(groupBounds && groupBounds.width > 0, 'Section group is visible before opening its menu');
        await page.mouse.click(groupBounds.x + groupBounds.width / 2, groupBounds.y + groupBounds.height / 2, { button: 'right' });
        await settle();
        await snapshot('ungroup-menu');
        const menuItem = page.getByRole('menuitem', { name: 'Ungroup (keep all notes)', exact: true });
        await menuItem.waitFor({ state: 'attached', timeout: 10000 });
        const menuBounds = await menuItem.boundingBox();
        assert.ok(menuBounds && menuBounds.width > 0, 'Ungroup menu item is rendered');
        await page.mouse.click(menuBounds.x + menuBounds.width / 2, menuBounds.y + menuBounds.height / 2);
        await settle();
        await snapshot('ungroup-confirmation');
        const ungroup = page.getByRole('button', { name: 'Ungroup', exact: true });
        await ungroup.waitFor({ state: 'attached', timeout: 10000 });
        const bounds = await ungroup.boundingBox();
        assert.ok(bounds && bounds.width > 0 && bounds.height > 0, 'Ungroup confirmation is rendered');
        // Uno's popup automation peer reports this button relative to its local
        // presenter, not the viewport (24,24 versus the rendered dialog). Use the
        // measured canvas center in this fixed 1600x1000 test viewport instead.
        await page.mouse.click(676, 564);
        await waitState(() => globalThis.noteSpaceState.sectionGroupCount === 1 && !globalThis.noteSpaceState.dirty);
        let restored = await saved();
        assert.equal(allGroups(restored).find(g => g.id === drafts.id).parentId, null);
        assert.equal(allPages(restored).length, allPages(original).length + 1);
        await page.mouse.click(177, 21); await waitState(() => globalThis.noteSpaceState.sectionGroupCount === 2 && !globalThis.noteSpaceState.dirty);
        restored = await saved(); assert.equal(allGroups(restored).find(g => g.id === drafts.id).parentId, group.id);
        await snapshot('section-groups-final'); completed.push('non-destructive ungroup and undo');
        await writeFile(resolve(output, 'organization-result.json'), JSON.stringify({ passed: true, tests: completed }, null, 2));
        console.log(`PASS ${completed.length} organization browser workflows`);
        return completed;
    } catch (error) {
        await writeFile(resolve(output, 'organization-failure.json'), JSON.stringify({ stage, completed, state: await state(), saved: await saved() }, null, 2));
        await writeFile(resolve(output, 'organization-dom.html'), await page.content());
        throw error;
    }
}
