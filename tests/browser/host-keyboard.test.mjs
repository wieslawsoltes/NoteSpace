import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';
import { test } from 'node:test';

// Exercise the production adapter's event routing without loading a notebook,
// browser, database, or native runtime. Integration tests cover real dialog input.
const source = readFileSync(new URL('../../src/NoteSpace.App/Platforms/WebAssembly/WasmScripts/NoteSpaceHost.js', import.meta.url), 'utf8');
function adapter() {
    const listeners = new Map();
    const scope = { addEventListener: (name, listener) => {
        const handlers = listeners.get(name) || []; handlers.push(listener); listeners.set(name, handlers);
    } };
    runInNewContext(source, scope);
    return (type, target, key = 'Enter', repeat = false) => {
        const event = { key, target, repeat, prevented: false, stopped: false,
            preventDefault() { this.prevented = true; }, stopImmediatePropagation() { this.stopped = true; } };
        for (const listener of listeners.get(type) || []) listener(event);
        return event;
    };
}
const editor = () => ({ tagName: 'TEXTAREA', id: 'uno-semantics-editor' });
const button = () => ({ tagName: 'BUTTON', id: 'uno-semantics-toolbar' });

test('Dialog Enter release cannot activate a newly focused semantic toolbar button', () => {
    const send = adapter(); send('keydown', editor());
    const release = send('keyup', button()); assert.equal(release.prevented, true); assert.equal(release.stopped, true);
});
test('Normal button Enter and Space retain native activation', () => {
    const send = adapter(); const target = button();
    for (const key of ['Enter', ' ']) { send('keydown', target, key); const release = send('keyup', target, key); assert.equal(release.prevented, false); assert.equal(release.stopped, false); }
});
test('Native text and cell-to-cell input retain their key releases', () => {
    const send = adapter(); const target = editor();
    send('keydown', target); assert.equal(send('keyup', target).stopped, false);
    send('keydown', target); assert.equal(send('keyup', editor()).stopped, false);
    send('keydown', target, 'a'); assert.equal(send('keyup', button(), 'a').stopped, false);
});
test('Accessibility opt-in release does not activate a different canvas control', () => {
    const send = adapter(); send('keydown', { id: 'uno-enable-accessibility', tagName: 'DIV' }, ' ');
    assert.equal(send('keyup', button(), ' ').stopped, true);
});
test('Unmatched releases and focus departure do not retain stale editor identities', () => {
    const send = adapter(); const target = button();
    assert.equal(send('keyup', target).stopped, false);
    send('keydown', editor()); send('blur', null); assert.equal(send('keyup', target).stopped, false);
    send('keydown', editor()); send('keyup', target); assert.equal(send('keyup', target).stopped, false);
});
test('The guard is limited to semantic buttons, not unrelated host elements', () => {
    const send = adapter(); send('keydown', editor());
    assert.equal(send('keyup', { tagName: 'BUTTON', id: 'host-button' }).stopped, false);
    send('keydown', editor());
    assert.equal(send('keyup', { tagName: 'INPUT', id: 'uno-semantics-checkbox' }).stopped, false);
});
