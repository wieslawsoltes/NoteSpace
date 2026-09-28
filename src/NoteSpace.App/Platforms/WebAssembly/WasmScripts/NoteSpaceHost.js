/* Browser adapters only. The application, controls, model and renderer are C# / Uno / Skia. */
(() => {
    "use strict";
    const databaseName = "notespace-local-v1";
    let opening;
    let dirty = false;
    let accessibilityButtonWired = false;
    const isolateAccessibilityActivation = () => {
        if (accessibilityButtonWired) return;
        const button = document.getElementById("uno-enable-accessibility");
        if (!button) return;
        // Uno 6.7 handles Space/Enter on its opt-in DOM button but also bubbles
        // the key to the previously focused canvas control. Keep that activation
        // local; do not intercept native editor or other semantic-control keys.
        button.addEventListener("keydown", event => {
            if (event.key === "Enter" || event.key === " ") event.stopPropagation();
        });
        accessibilityButtonWired = true;
    };
    // A dialog can consume Enter on keydown, remove its editor, and restore
    // focus before keyup. Uno's semantic/native button bridge must not interpret
    // that unmatched release as activation of the newly focused toolbar button.
    // Store element identities only; no typed text or keystroke log is retained.
    const activationOrigins = new Map();
    const activationKey = key => key === "Enter" || key === " ";
    globalThis.addEventListener("keydown", event => {
        if (activationKey(event.key) && !event.repeat) activationOrigins.set(event.key, event.target);
    }, true);
    globalThis.addEventListener("keyup", event => {
        if (!activationKey(event.key)) return;
        const origin = activationOrigins.get(event.key);
        activationOrigins.delete(event.key);
        const target = event.target;
        const fromEditor = origin?.tagName === "INPUT" || origin?.tagName === "TEXTAREA" || origin?.isContentEditable;
        const toSemanticButton = target?.id?.startsWith("uno-semantics-")
            && (target.tagName === "BUTTON" || target.getAttribute?.("role") === "button");
        if (origin && origin !== target && toSemanticButton
            && (fromEditor || origin.id === "uno-enable-accessibility")) {
            event.preventDefault();
            event.stopImmediatePropagation();
        }
    }, true);
    globalThis.addEventListener("blur", () => activationOrigins.clear());
    const database = () => opening ??= new Promise((resolve, reject) => {
        const request = indexedDB.open(databaseName, 1);
        request.onupgradeneeded = () => request.result.createObjectStore("workspaces");
        request.onerror = () => { opening = undefined; reject(request.error); };
        request.onblocked = () => { opening = undefined; reject(new Error("Close older NoteSpace tabs to open the notebook database.")); };
        request.onsuccess = () => {
            const db = request.result;
            db.onversionchange = () => { db.close(); opening = undefined; };
            resolve(db);
        };
    });
    globalThis.NoteSpaceHost = Object.freeze({
        async load() {
            const db = await database();
            return new Promise((resolve, reject) => {
                const tx = db.transaction("workspaces", "readonly");
                const request = tx.objectStore("workspaces").get("default");
                request.onsuccess = () => resolve(request.result ? `${request.result.token}\n${request.result.json}` : "");
                request.onerror = () => reject(request.error);
            });
        },
        async save(json, expectedToken) {
            if (typeof json !== "string" || json.length > 32 * 1024 * 1024) throw new Error("Notebook exceeds the 32 MiB interchange limit.");
            const db = await database();
            return new Promise((resolve, reject) => {
                const tx = db.transaction("workspaces", "readwrite");
                const store = tx.objectStore("workspaces");
                const request = store.get("default");
                const token = crypto.randomUUID();
                let conflict = false;
                request.onsuccess = () => {
                    if ((request.result?.token ?? "") !== expectedToken) {
                        conflict = true; tx.abort(); return;
                    }
                    store.put({ token, json, savedAt: new Date().toISOString() }, "default");
                };
                tx.oncomplete = () => resolve(token);
                tx.onabort = () => reject(new Error(conflict ? "NOTESPACE_CONFLICT: another tab saved changes" : tx.error?.message ?? "Notebook save was aborted."));
                tx.onerror = () => reject(tx.error ?? new Error("Could not save notebook. Check available browser storage."));
            });
        },
        pick(accept) {
            return new Promise((resolve, reject) => {
                const input = document.createElement("input");
                input.type = "file"; input.accept = accept; input.style.display = "none";
                document.body.append(input);
                let done = false;
                const finish = (value, error) => { if (done) return; done = true; input.remove(); error ? reject(error) : resolve(value); };
                input.addEventListener("cancel", () => finish(""), { once: true });
                input.addEventListener("change", async () => {
                    const file = input.files?.[0]; if (!file) { finish(""); return; }
                    if (file.size > 32 * 1024 * 1024) { finish("", new Error("The selected file exceeds 32 MiB.")); return; }
                    try {
                        const reader = new FileReader();
                        reader.onerror = () => finish("", reader.error ?? new Error("File read failed."));
                        reader.onload = () => {
                            const result = String(reader.result); const comma = result.indexOf(",");
                            finish(`${file.name.replace(/[\r\n]/g, "_")}\n${file.type}\n${result.slice(comma + 1)}`);
                        };
                        reader.readAsDataURL(file);
                    } catch (error) { finish("", error); }
                }, { once: true });
                input.click();
            });
        },
        download(name, data, type) {
            const raw = atob(data);
            const bytes = new Uint8Array(raw.length);
            for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
            const url = URL.createObjectURL(new Blob([bytes], { type }));
            const link = document.createElement("a"); link.href = url; link.download = name; link.rel = "noopener";
            document.body.append(link); link.click(); link.remove();
            setTimeout(() => URL.revokeObjectURL(url), 60000);
        },
        report(json) {
            isolateAccessibilityActivation();
            // Read-only runtime metadata supports diagnostics and browser smoke tests.
            const state = JSON.parse(json); globalThis.noteSpaceState = Object.freeze(state);
            dirty = Boolean(state.dirty);
            document.documentElement.dataset.notespaceReady = state.ready ? "true" : "false";
            document.title = `${state.pageTitle || "Notebook"} — NoteSpace`;
        }
    });
    globalThis.addEventListener("beforeunload", event => {
        if (dirty) { event.preventDefault(); event.returnValue = ""; }
    });
})();
