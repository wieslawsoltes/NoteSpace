# NoteSpace

**A familiar notebook workspace, built on reusable Uno Platform and SkiaSharp libraries.**

[![Build, test and deploy](https://github.com/wieslawsoltes/NoteSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/NoteSpace/actions/workflows/build.yml)
[![Release](https://github.com/wieslawsoltes/NoteSpace/actions/workflows/release.yml/badge.svg)](https://github.com/wieslawsoltes/NoteSpace/actions/workflows/release.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-803AB3.svg)](LICENSE)

[**Open the browser application**](https://wieslawsoltes.github.io/NoteSpace/) · [Builds and packages](https://github.com/wieslawsoltes/NoteSpace/actions) · [Architecture](docs/architecture.md) · [Report an issue](https://github.com/wieslawsoltes/NoteSpace/issues)

NoteSpace is a C#/Uno application with an original purple desktop shell, grouped ribbon, notebook navigation, free-form pages and Skia rendering. JavaScript supplies browser storage, file/clipboard transfer and runtime diagnostics; it is not a parallel HTML editor.

> **0.1.0 preview.** This independent implementation is inspired by Microsoft OneNote workflows, but it is not an exact or complete clone. It does not read `.one`/`.onepkg` files or synchronize with OneDrive. Export backups before relying on it for important work.

## Working features

| Area | Implemented workflows |
| --- | --- |
| Organization | Notebooks, sections, nested section groups, two subpage levels, persistent collapse, favorites, subtree drag/drop and reordering, cross-section page moves, cross-notebook section/group moves, non-destructive ungrouping, page duplication and recycle-bin recovery. |
| Notes and formatting | Free-form text containers, title editing, move/resize grips, canonical mixed-style ranges, font size/family, bold/italic/underline/strike, colors/highlights, headings, lists, alignment, links, tags and checklists. |
| Tables and attachments | In-place plain-text cell editing; keyboard traversal and row append; row/column insertion/deletion, clear cell, transpose, quoted TSV clipboard transfer; a whole-table editor; images and embedded file attachments. |
| Drawing | Pressure-aware pen input where provided, highlighter, stroke eraser, rectangles, ellipses and lines; gesture-level undo. |
| Search and history | Notebook/section/page scopes, literal case/whole-word matching, tag/to-do filters, exact text/cell navigation, scoped style-preserving Replace All, chronological undo/redo and manually saved page versions. |
| View and storage | Light/dark chrome, paper color and ruled/grid/dotted paper, zoom/fit, full-page view, collapsible ribbon/navigation, section tabs, autosave and optimistic stale-write protection. |
| Interchange | Validated `.notespace` JSON backups, text/limited Markdown import, Markdown and escaped HTML export, and rendered PNG export including ink. |

## Use the workspace

Open the [browser app](https://wieslawsoltes.github.io/NoteSpace/). A sample notebook is created only when no saved workspace exists. Choose **Add page**, then type its title. Double-click blank paper to add a note; double-click a note to edit. Drag a container's top grip to move it or its lower-right corner to resize. Use **Draw** for ink and shapes, the wheel to scroll, and middle-button dragging to pan.

Right-click notebooks, sections, groups or pages for organization actions. A page's right-edge grip moves its subtree: the upper/lower row zones insert before/after, and the middle zone reparents. Invalid depth/cycle targets are rejected; Escape cancels a drag. Recent order is a view, not a destructive sort. Group and page collapse preferences survive backups and reloads. See [organization contracts](docs/organization.md) for exact deletion, promotion, duplication and ungrouping semantics.

For tables, double-click a cell and type. Tab/Shift+Tab move between cells; Enter/Shift+Enter move by row; the final forward cell appends a row. Ctrl+Enter remains available for multiline input. **Table** contains structural operations and quoted TSV copy/paste, preserving embedded tabs/newlines/quotes. Cells remain plain text with uniform row height and equal column widths; merged/rich cells are not implemented.

Inside native text editors, normal operating-system selection and clipboard shortcuts apply. Ribbon Copy/Paste transfers note containers within the current session. An empty formatting selection applies to the whole text container, not only subsequent typing. Mixed-range styles are retained, but the native input overlay displays the base style; final rich formatting is drawn on the Skia surface.

### Search and replace

`Ctrl+F` opens the search pane. Choose all notebooks, this notebook, this section or this page; toggle case and whole-word matching; or filter tags and open/completed to-dos. Empty to-do queries list matching tasks. Results include their location, reveal collapsed navigation ancestors and select the matched text or table cell. The pane debounces typing for 180 ms and displays up to 200 results.

`Ctrl+H` opens **Replace text**, sharing scope and matching options. Completion appears in a non-modal result bar with Undo and Dismiss, without stealing keyboard focus. The operation-specific Undo rejects stale history or a pending text draft; normal Ctrl+Z remains available. Replacement changes note text and table cells in one undoable transaction while leaving titles, tags and attachment filenames unchanged. Formatting between matches is preserved. A validation/reflow failure rolls back the entire action; the search result cap does not limit replacement. Matching is literal and ordinal, not regular-expression, OCR, stemming or locale-sensitive linguistic search. Whole-word boundaries include Unicode letters, numbers, combining marks and connector punctuation.

| Shortcut | Action |
| --- | --- |
| `Ctrl+S`, `Ctrl+F`, `Ctrl+H` | Save, search, scoped replacement |
| `Ctrl+Alt+N`, `Ctrl+Alt+Shift+N` | New page, new subpage |
| `Ctrl+Alt+G` | New section group |
| `F6`; Left/Right in page list | Focus pages; collapse/expand or parent/child navigation |
| `F2`, Delete in page list | Rename; confirmed deletion |
| `Ctrl+Z`, `Ctrl+Y` | Undo, redo |
| `Ctrl+B`, `Ctrl+I`, `Ctrl+U` | Bold, italic, underline |
| Escape while editing | Commit and leave the editor |

## Build and test

The repository pins **Uno.Sdk 6.7.30**, **.NET 10** and compatible **SkiaSharp 3.119.2**. `global.json` allows the installed stable .NET 10 feature band. Keep Uno's graphics/native dependencies aligned when updating SkiaSharp. Desktop prerequisites follow the [Uno setup guide](https://platform.uno/docs/articles/get-started.html).

```sh
git clone https://github.com/wieslawsoltes/NoteSpace.git
cd NoteSpace
dotnet workload install wasm-tools

dotnet run --project tests/NoteSpace.Tests -c Release
dotnet run --project tests/NoteSpace.Rendering.Tests -c Release

dotnet run --project src/NoteSpace.App -f net10.0-browserwasm
dotnet run --project src/NoteSpace.App -f net10.0-desktop

dotnet publish src/NoteSpace.App -c Release -f net10.0-browserwasm \
  -o artifacts/publish -p:WasmShellWebAppBasePath=/NoteSpace/
```

Serve the directory containing the generated `index.html`. GitHub Pages uses `/NoteSpace/`; override `WasmShellWebAppBasePath` for another hosting path. The workflow stages output in `artifacts/site`, installs `tests/browser` dependencies and runs `node tests/browser/smoke.mjs` against the real compiled application.

## Reusable libraries

No library references the app. Each package can be built independently; the app supplies storage/file/clipboard platform adapters and composes the controls.

| Package | Responsibility |
| --- | --- |
| `NoteSpace.Core` | Notebook/rich-text/ink model, geometry, source-generated JSON and validation; no Uno or Skia dependency. |
| `NoteSpace.Editor` | Transactions/history, page/section-group operations, scoped search/replacement, rich-text edits, table/TSV operations and spatial indexes; no UI dependency. |
| `NoteSpace.Storage` | Persistence contract, atomic file storage, conflict handling and supported import/export formats. |
| `NoteSpace.Rendering.Skia` | Independent page composition, text layout, viewport culling, bounded native caches and PNG export; no Uno dependency. |
| `NoteSpace.Controls` | Uno note surface, canvas adapter, ribbon, notebook/page navigation, search/options, table editor, themes, icons, palettes, backstage and status controls. |

```sh
dotnet pack src/NoteSpace.Core -c Release -o artifacts/packages
dotnet pack src/NoteSpace.Controls -c Release -o artifacts/packages
```

```csharp
using NoteSpace.Controls;
using NoteSpace.Core;
using NoteSpace.Editor;

var session = new EditorSession(SampleWorkspace.Create());
var editor = new NoteSurface { Session = session };
window.Content = editor;
// Subscribe to session.Changed for your host's persistence and command UI.
// Dispose the surface when its host is permanently closed.

var query = new NoteSearchQuery("draft") {
    Scope = NoteSearchScope.Page,
    ScopeId = session.SelectedPage!.Id,
    WholeWord = true
};
var hits = session.Search(query).ToList();
var result = session.ReplaceAll(query, "reviewed");
```

`SearchHit.Start/Length` use UTF-16 offsets and `Cell` identifies table matches. `SearchOptionsControl` and `NoteSurface.RevealSearchResult` are reusable separately. `PageOutline`, `NotebookGroups`, `NoteTable`, `LiteralTextSearch` and `PageContentIndex` are usable without Uno. A headless rendering host can call `new PageRenderer().ExportPng(page)` and must dispose its renderer and include the appropriate Skia native-assets package.

**Editing contract:** the session is single-writer. A successful `EditPage` replaces that page DTO; keep IDs and resolve current objects after edits. Its callback must mutate only the supplied detached page. Use `Execute` for cross-page/organization operations. Hosts deliberately changing collections outside transactions must invalidate lookup projections; such changes do not acquire undo or autosave notifications.

## Performance and resource ownership

The renderer reuses geometry through `RenderOptions.ContentRevision`, completed-ink recordings, native fonts, title/date glyphs and text layout. It culls individual lines and table cells as well as off-screen containers. Live resize avoids stale layout without repeatedly decoding unchanged image content. Layout cache hits update usage bookkeeping in place.

Advance the content revision after every mutation, including in-place changes to image bytes. Without a token, geometry and image content are processed defensively. `MaximumImageCacheBytes` defaults to **64 MiB of retained encoded-plus-decoded image payload**, also capped at 24 entries. Least-recently-used entries are evicted before replacement allocation; valid images larger than the cache budget are drawn transiently. This is not a bound on decoder temporaries, Skia object overhead, total application memory or GPU copies. Invalid image results are retained only for a stable revision. Dispose the renderer to release its native caches.

Page edits retain only the edited page's before/after JSON. Global operations still use workspace snapshots. Full-workspace identity/content validation and aggregate JSON checks remain linear, and page drafts still copy attachments. Size-only checks stream the generated JSON into a UTF-16 character counter instead of allocating another whole-workspace string. Page-scoped searches use the session page index after its initial build; section and notebook scopes enumerate only the selected container. Queries inside transactions retain live-graph semantics. Search scans existing strings/cells without constructing a concatenated table and stops at its result cap; cancellation is checked between pages, blocks and cells. Hidden searches and unchanged saves are skipped.

CI runs the **same benchmark source against this code and baseline `b75a277` on the same runner**, recording seven-sample medians, managed allocations and retained history in `performance.md` and JSON artifacts. These are headless CPU/Skia workloads, **not browser FPS, GPU timings, startup or a Microsoft OneNote comparison**. Consult every workload, including regressions; no noisy timing threshold is used as a correctness gate.

## Persistence and data safety

Browser data lives in IndexedDB `notespace-local-v1`, local to this profile and origin. Clearing site data, private browsing, profile loss or eviction can remove notes. **Local storage is not backup or cloud synchronization.** Export `.notespace` backups regularly. Notes and attachments are not encrypted at rest.

Autosave captures a `WorkspaceSnapshot`: one immutable, validated JSON string independent of later edits. Built-in stores implement the optional `IWorkspaceSnapshotStore` fast path, eliminating the old clone/deserialization/re-serialization round trip. Existing `IWorkspaceStore` integrations remain supported. `WorkspaceSnapshot.Parse` validates external JSON before it can enter this path.

A browser save compares its token and writes inside the same IndexedDB transaction. Desktop saves use an exclusive lock, same-directory temporary file and atomic replacement. Stale tabs are stopped, not silently merged: export the conflicted session and reload. Failed loads enter a non-saving recovery session rather than replacing stored data with sample content.

Validation covers schema, global identities/references, finite geometry, ranges and collection limits. Limits include 32 MiB JSON characters, 16 MiB per attachment, 16 megapixels per decoded image/PNG export, two subpage levels and eight section-group levels. The aggregate limit may be reached first. Undo retains up to 100 transactions and approximately 32 MiB serialized UTF-16 characters, retaining the newest transaction; manual page versions are capped at 20 per page. See [SECURITY.md](SECURITY.md).

## CI and releases

**Build, test and deploy** runs on pull requests, `main` pushes and manual dispatch. It runs portable/rendering specifications and paired benchmarks, publishes WebAssembly, drives real-input Chromium workflows, builds desktop and packs all five libraries. Only successful main builds deploy GitHub Pages. QA artifacts include screenshots, input diagnostics limited to test fixtures, logs and benchmarks. Independent browser-suite failures are collected without disabling their assertions.

**Release** runs for `v*` tags or a supplied manual version. It validates the version, runs portable/rendering/browser gates, builds desktop/browser output, packs versioned libraries and emits `SHA256SUMS`. Tags attach assets to a GitHub Release. It does **not** publish to NuGet.org. Headless Chromium uses software rendering; mobile startup checks do not establish physical stylus/GPU, screen-reader or complete touch qualification.

## Remaining boundaries

This preview does **not** establish full OneNote feature, visual, accessibility, performance or file-format parity. Remaining areas include `.one`/`.onepkg`, Microsoft 365/OneDrive and authenticated collaboration, automatic merging/CRDTs, OCR and handwriting recognition, ink-to-math, equations, audio/video/transcription, web clipping, Outlook integration, protected sections and advanced printing.

Styled in-place rich text, full bidirectional/script shaping and qualified font fallback remain work. Browser preview uses packaged Open Sans aliases for common families, not Microsoft fonts or exact font equivalence. Tables do not support merged/rich cells or variable row/column sizing. Desktop platform file-picker workflows and larger/hardware-specific datasets need further qualification.

Markdown import supports basic paragraphs, headings and task prefixes. HTML/Markdown exports flatten placement and omit ink; PNG preserves rendered appearance, and `.notespace` preserves the implemented model. Unsupported OneNote content cannot be promised lossless conversion.

## Contributing and license

See [CONTRIBUTING.md](CONTRIBUTING.md). Include executable regressions and distinguish implemented behavior from parity goals. Source is **MIT licensed**. Uno Platform and SkiaSharp are external permissively licensed dependencies. Icons are original; proprietary Microsoft fonts/artwork are not bundled. Microsoft OneNote is a trademark of Microsoft Corporation. NoteSpace is not affiliated with or endorsed by Microsoft.
