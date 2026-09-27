# NoteSpace

**A familiar notebook workspace. An independent, reusable .NET foundation.**

[![Build, test and deploy](https://github.com/wieslawsoltes/NoteSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/NoteSpace/actions/workflows/build.yml)
[![Release](https://github.com/wieslawsoltes/NoteSpace/actions/workflows/release.yml/badge.svg)](https://github.com/wieslawsoltes/NoteSpace/actions/workflows/release.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-803AB3.svg)](LICENSE)

[**Open NoteSpace in your browser**](https://wieslawsoltes.github.io/NoteSpace/) · [Builds and packages](https://github.com/wieslawsoltes/NoteSpace/actions) · [Report an issue](https://github.com/wieslawsoltes/NoteSpace/issues)

NoteSpace brings a OneNote-style desktop notebook workflow to **Uno Platform**, with an original purple application chrome, grouped ribbon, notebook/section navigation, page lists, free-form notes, and a **SkiaSharp** rendering engine. The actual application is C# and Uno, not an HTML mock-up. JavaScript is limited to browser storage, file transfer, and runtime diagnostics.

> **Status: 0.1.0, early implementation.** This is an independent implementation inspired by familiar notebook workflows, not Microsoft OneNote. It is not an exact or complete clone, does not read `.one` / `.onepkg` files, and does not connect to OneDrive. See [Scope and limitations](#scope-and-limitations) before relying on it for important work. Export backups regularly.

## What works

| Area | Implemented workflow |
| --- | --- |
| Organization | Create and rename notebooks, sections, and pages; reorder sections/pages; move pages between sections; duplicate pages; nested section groups and cross-notebook section/group moves; non-destructive ungrouping; two-level subpage hierarchy with persistent collapse, keyboard navigation, subtree moves and drag-and-drop; favorites; recover deleted pages. |
| Free-form editing | Double-click to create/edit text; move containers by their top grip; resize by their lower-right corner; edit page titles; copy/paste note containers. |
| Text | Font family and size, bold, italic, underline, strikeout, mixed-style range formatting, canonical runs, style-preserving replacement, highlight, colors, alignment, bullets, numbering, heading styles, date/time and symbols. |
| Ink | Mouse/touch/pen drawing; pressure-sensitive pen width where supplied; highlighter; stroke eraser; rectangles, ellipses, lines; one undo step per gesture. |
| Content | Editable tables, PNG/JPEG/WebP images, embedded file attachments, checklists, tags, dividers, and starter templates. |
| Retrieval and history | Cross-notebook search, tagged-note navigation, replace-all, word count, undo/redo, manually saved page versions, recycle bin. |
| View | Light/dark chrome, paper colors, ruled/grid/dotted paper, zoom, page-width fit, full-page view, collapsible navigation/ribbon, section tabs layout. |
| Persistence | Debounced autosave, IndexedDB transactions in the browser, atomic desktop file replacement, and optimistic tokens that reject stale writes. |
| Interchange | Validated `.notespace` JSON backups; text/limited Markdown import; Markdown, escaped HTML, and Skia-rendered PNG page export. |

## Start using it

Open the [browser application](https://wieslawsoltes.github.io/NoteSpace/). A sample notebook is created only when no stored workspace exists.

1. Choose **Add page** to create a page. Double-click the title to rename it.
2. Double-click blank paper to create a note. Type normally; the native text input handles text selection and clipboard shortcuts.
3. Select a note to format it from **Home**. Drag its top grip to move it or its bottom-right handle to resize it.
4. Open **Draw** to choose a pen, highlighter, eraser, or shape. Scroll with the mouse wheel and pan with a middle-button drag.
5. Use **File → Export notebook** to save a complete `.notespace` backup. HTML and Markdown exports are readable content exports; PNG preserves the rendered page appearance, including ink.

Right-click a notebook, section, or page for its context menu. Choose **New subpage** to create a child, or **Make subpage** to indent a page beneath its previous sibling. Disclosure arrows expand or collapse a group. The **Order / Recent** button switches between hierarchy order and a flat recent list without changing stored order. The **History** tab contains page versions and the recycle bin.

### Page organization and formatting

A page can have two nested subpage levels. Reordering or moving to another section carries its complete subtree. Promotion places that subtree after the old parent's group, leaving its former siblings with the old parent. Deleting a page preserves its descendants and promotes them one level; recovery restores only the deleted page as a root. Duplication copies only the selected page and inserts it after its descendants. These are explicit NoteSpace semantics, not a claim of identical OneNote behavior.

Collapsing a group containing the selected page selects its parent. Search navigation reveals a matching descendant without discarding saved collapse preferences. Collapse state and organization changes are undoable and included in autosave and backups.

Formatting a mixed selection preserves existing per-run attributes. Bold, italic, underline and strike toggles enable the attribute across a mixed selection, then disable it when all selected text has it. Replace All preserves formatting between matches. An empty selection applies formatting to the whole container, not only future typing. The native text-input overlay still displays the base style while editing; rich styles appear on the Skia surface after editing.

### Section groups and drag-and-drop

Create a **Section Group** from Insert, a notebook's context menu, the **Group** button in navigation, or `Ctrl+Alt+G`. Group context menus create nested groups and sections, rename and reorder groups, move them between notebooks, or **Ungroup** while keeping every note. Section context menus move a section to another notebook or group. Groups support up to eight nested levels; sections appear before child groups at each level. Search navigation reveals the selected section's group ancestors without changing their saved collapse preferences.

In **Order** view, drag a page by its right-hand six-dot grip. The top or bottom quarter of a target row inserts before or after its complete subtree; the middle nests the dragged subtree as its last child. Purple indicators show valid destinations. `Escape`, releasing outside the list, or losing pointer capture cancels without changing the document. Dragging near an edge scrolls the list. Self/descendant drops and moves beyond the two-level subpage limit are rejected. Each committed drop is one undoable action. **Recent** view disables dragging; use context menus for keyboard reordering or cross-section movement.

Groups and drag-and-drop are independently reusable through `NotebookGroups`, `EditorSession`, `NotebookNavigator`, and `PageListControl`. See [organization contracts](docs/organization.md) for API examples and compatibility details.

### Keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| `Ctrl+S` | Save the workspace |
| `Ctrl+F` | Search notebooks |
| `Ctrl+Alt+N` | Add a page |
| `Ctrl+Alt+Shift+N` | Add a subpage of the current page |
| `Ctrl+Alt+G` | Create a section group |
| `F6` | Focus the page list |
| `Left` / `Right` in the page list | Collapse/expand or navigate to parent/first child |
| `F2` / `Delete` in the page list | Rename / request deletion with confirmation |
| `Ctrl+Z` / `Ctrl+Y` | Undo / redo |
| `Ctrl+B` / `Ctrl+I` / `Ctrl+U` | Bold / italic / underline |
| `Escape` while editing | Commit and leave the text editor |
| Native text shortcuts | Text selection, copying, cutting, and pasting inside an active editor |

The ribbon’s Copy/Paste operations transfer **note containers within the current session**. They are separate from the operating-system text clipboard.

## Build from source

The repository pins **Uno.Sdk 6.7.30** and uses its compatible **SkiaSharp 3.119.2** dependency. The SDK version is selected in `global.json`; `.NET 10.0.100` rolls forward to the installed stable .NET 10 feature band. Do not independently update SkiaSharp without checking Uno’s renderer compatibility.

```sh
git clone https://github.com/wieslawsoltes/NoteSpace.git
cd NoteSpace
dotnet workload install wasm-tools

# Portable document/editor/storage specifications
dotnet run --project tests/NoteSpace.Tests -c Release

# Browser development host
dotnet run --project src/NoteSpace.App -f net10.0-browserwasm

# Native Skia desktop host
dotnet run --project src/NoteSpace.App -f net10.0-desktop

# Production browser output
dotnet publish src/NoteSpace.App -c Release -f net10.0-browserwasm \
  -o artifacts/publish -p:WasmShellWebAppBasePath=/NoteSpace/
```

Find the generated `index.html` inside the publish directory and serve its containing directory. The application’s base path is `/NoteSpace/` for this repository’s GitHub Pages site. Override `WasmShellWebAppBasePath` for other hosting paths. Native desktop prerequisites follow the [Uno Platform setup documentation](https://platform.uno/docs/articles/get-started.html).

## Independently reusable packages

The application is the composition root. No library references the application.

```text
NoteSpace.Core
  ├── NoteSpace.Editor
  ├── NoteSpace.Storage
  └── NoteSpace.Rendering.Skia  ← Editor
          └── NoteSpace.Controls  ← Core / Editor
                  └── NoteSpace.App  ← Storage
```

| Package | Responsibility |
| --- | --- |
| `NoteSpace.Core` | Notebook object model, rich-text marks, ink, geometry, generated JSON metadata, import validation. No Uno or Skia dependency. |
| `NoteSpace.Editor` | Undoable transactions, page-outline queries and subtree operations, recovery, search, canonical rich-text range edits, ink geometry and simplification. No UI dependency. |
| `NoteSpace.Storage` | Persistence contract, atomic file store, conflict exception, content import/export. Browser-specific storage is injected by the app. |
| `NoteSpace.Rendering.Skia` | Viewport culling, text layout, native resource caches, paper rendering, tables, images, ink, and PNG export. No Uno dependency. |
| `NoteSpace.Controls` | Reusable Uno note surface, canvas adapter, vector icons, ribbon, navigation, page list, table editor, palettes, search, backstage and status controls. |

Pack an individual library:

```sh
dotnet pack src/NoteSpace.Core -c Release -o artifacts/packages
dotnet pack src/NoteSpace.Controls -c Release -o artifacts/packages
```

Embed the editor in a compatible Uno application:

```csharp
using NoteSpace.Controls;
using NoteSpace.Core;
using NoteSpace.Editor;

var session = new EditorSession(SampleWorkspace.Create());
var editor = new NoteSurface { Session = session };
window.Content = editor;
// Subscribe to session.Changed to connect your own persistence or host UI.
// Dispose the NoteSurface when its host is permanently closed.
```

Render without Uno:

```csharp
using NoteSpace.Rendering.Skia;

using var renderer = new PageRenderer();
byte[] png = renderer.ExportPng(page);
File.WriteAllBytes("page.png", png);
```

Native Skia consumers must include the appropriate SkiaSharp native-assets package for their runtime. See [architecture and extension points](docs/architecture.md).

### Reuse outline and rich-text operations without Uno

```csharp
var section = session.Document.Notebooks[0].Sections[0];
var outline = PageOutline.Build(section.Pages);
var visible = PageOutline.Visible(outline, session.SelectedPage?.Id);
session.AddSubpage(section.Pages[0].Id, "Research");
session.MovePageSibling(session.SelectedPage!.Id, -1);

RichText.Apply(block, start: 2, length: 5, format => format.Bold = true);
RichText.ReplaceAll(block, "draft", "reviewed");
var runs = RichText.GetRuns(block); // Detached, non-overlapping style runs.
```

`PageOutline.Build` interprets legacy orphan indentation without mutating the document. Organization edits normalize affected sections. `RichText` ranges use UTF-16 offsets and reject boundaries that split a surrogate pair. Formatting marks are normalized to ordered, non-overlapping runs rather than accumulating overlays. Use `EditorSession.EditPage` to wrap direct rich-text changes in undoable transactions.

## Persistence and data safety

The browser uses an IndexedDB database named `notespace-local-v1`. It is **local to the current browser profile and origin**. Clearing site data, using private browsing, losing the profile, or browser storage eviction can remove notes. Browser storage is not a backup or cloud synchronization service.

A save reads and compares the current token inside the same IndexedDB read/write transaction that writes the replacement. A stale editor is stopped instead of silently overwriting another tab’s work. This is conflict detection, not real-time collaboration or automatic merging. On conflict, export the current session and reload. A failed load does not replace existing stored data with the sample notebook.

The desktop adapter uses an exclusive lock, a same-directory temporary file, and an atomic replacement. History is bounded to 100 transactions and approximately 32 MiB of serialized snapshot characters, retaining the most recent transaction. Page versions are explicit snapshots, up to 20 per page, and are included in backups.

Imports validate schema, unique identities, finite geometry, text ranges, enums, and collection limits. Current limits include 32 MiB JSON characters, 16 MiB per attachment, 16 megapixels per image/PNG export, and bounded page/ink/table sizes. Large content can hit the aggregate limit before individual limits. Notes and attachments are **not encrypted at rest**. See [SECURITY.md](SECURITY.md).

## Continuous integration and releases

**Build, test and deploy** runs on pull requests, pushes to `main`, and manual dispatch. It runs portable specifications, publishes the browser app, drives Chromium through real note-editing and ink workflows, checks mixed-style formatting, nested page navigation, collapse persistence, section-group workflows, page drag-and-drop and stale-token rejection, builds the desktop target, packs all reusable libraries, and uploads browser/package/QA artifacts. Only successful main-branch builds deploy to GitHub Pages.

**Release** runs on version tags and manual dispatch. It runs portable, rendering and browser specifications, creates versioned library packages and browser output with a `SHA256SUMS` manifest, checks the version input, and attaches artifacts to a GitHub Release for `v*` tags. It does **not** publish packages to NuGet.org or require a commercial service. NuGet publication can be added separately with a deliberately configured publishing identity.

QA screenshots, portable/rendering test logs and browser diagnostic logs are available in the `notespace-browser-qa` workflow artifact. Browser tests use headless Chromium software rendering; they are not a substitute for physical pen/GPU testing on every platform.

## Scope and limitations

This implementation provides working notebook interactions but **does not establish full OneNote feature, visual, accessibility, performance, or file-format parity**.

Not implemented: Microsoft `.one` / `.onepkg` compatibility, OneDrive/Microsoft 365 synchronization, authenticated collaboration, CRDT/merge-based editing, OCR, handwriting recognition, ink-to-math, audio/video recording, transcription, web clipping, Outlook integration, password-protected sections, advanced printing, or complete equation editing.

Text layout is a custom Skia layout engine. Full script shaping, bidirectional layout, font fallback qualification, typography equivalence, and screen-reader semantics for canvas content remain work. The native input overlay displays a base text style while editing; selected-range styling appears in the Skia rendering after editing. Fonts depend on the host and may differ from Microsoft’s desktop fonts. Tables use a separate editor rather than OneNote’s in-place table interaction. Cross-section drag gestures, section/group drag-and-drop, mixed interleaving of sections and groups, and complete OneNote organization parity remain work. Cross-section page moves and cross-notebook section/group moves are available through dialogs.

Markdown import supports plain paragraphs, headings and simple task prefixes. HTML/Markdown exports flatten free-form placement and do not include ink; use PNG or the native `.notespace` backup when those details matter. Very large-document performance, arbitrary attachments, native desktop file pickers, touch/stylus hardware, and assistive technology require broader qualification. The app cannot promise preservation of unsupported OneNote content.

## Contributing and licensing

See [CONTRIBUTING.md](CONTRIBUTING.md). Contributions should include executable tests and distinguish implemented behavior from future parity goals.

NoteSpace source is **MIT licensed**. Uno Platform and SkiaSharp are permissively licensed external dependencies. NoteSpace uses original vector icons and does not bundle Microsoft application artwork or proprietary Microsoft font files. **Microsoft OneNote is a trademark of Microsoft Corporation.** This project is not affiliated with or endorsed by Microsoft.
