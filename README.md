# NoteSpace

**A familiar notebook workspace, built on reusable Uno Platform and SkiaSharp libraries.**

[![Build, test and deploy](https://github.com/wieslawsoltes/NoteSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/NoteSpace/actions/workflows/build.yml)
[![Release](https://github.com/wieslawsoltes/NoteSpace/actions/workflows/release.yml/badge.svg)](https://github.com/wieslawsoltes/NoteSpace/actions/workflows/release.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-803AB3.svg)](LICENSE)
[![NuGet](https://img.shields.io/nuget/vpre/NoteSpace.Core.svg?label=NuGet)](https://www.nuget.org/packages/NoteSpace.Core)
[![Downloads](https://img.shields.io/nuget/dt/NoteSpace.Core.svg)](https://www.nuget.org/packages/NoteSpace.Core)

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

Text notes use live Skia-rendered mixed formatting with shared caret and selection geometry. Character commands affect the selected range; at a caret they set the style for future typing without changing existing text. Ribbon Copy/Paste preserves selected rich fragments within the session, or transfers note containers outside text editing. Native clipboard shortcuts transfer plain text. The **Layout** tab adds superscript/subscript, format copying, indentation, spacing, tab intervals, and container size/position/order. Paragraph settings apply to the whole container. See [live editing and layout](docs/wysiwyg.md) for keyboard behavior, reusable APIs and remaining limitations.

### Navigation and page views

Use the quick-access **Back / Forward** buttons or **Alt+Left / Alt+Right** to revisit pages with their scroll position and zoom. **Ctrl+Page Up / Page Down** moves through the visible page list. Browsing history is independent of document undo, skips deleted pages and resets on reload/import.

The page-list **Order** menu offers manual order, titles A–Z/Z–A, modified-newest and created-newest sorting, plus optional text previews and dates. Sorted views keep subpages with their parents and never rewrite the saved manual order. Drag the notebook/page pane's right edge or the search pane's left edge to resize; Escape cancels, Home/double-click resets, and View → Reset pane widths restores all defaults. Widths and page-view options are saved in backups. Narrow windows preserve writing space, and phone-width navigation/search uses the full body. See [organization](docs/organization.md) for contracts and limits.

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

## Download

Every [release](https://github.com/wieslawsoltes/NoteSpace/releases/latest) ships a self-contained, single-file desktop app — no .NET install needed:

| OS | x64 | Arm64 |
| --- | --- | --- |
| Windows | `NoteSpace-<version>-win-x64.zip` | `NoteSpace-<version>-win-arm64.zip` |
| macOS | `NoteSpace-<version>-osx-x64.tar.gz` | `NoteSpace-<version>-osx-arm64.tar.gz` |
| Linux | `NoteSpace-<version>-linux-x64.tar.gz` | `NoteSpace-<version>-linux-arm64.tar.gz` |

Extract and run `NoteSpace` (`NoteSpace.exe` on Windows). Builds are not code-signed yet: on macOS clear the quarantine flag with `xattr -d com.apple.quarantine NoteSpace`; on Windows choose **More info → Run anyway** in SmartScreen. Verify downloads against `SHA256SUMS`.

## NuGet packages

All five libraries are MIT-licensed, versioned together with the app and published to [NuGet.org](https://www.nuget.org/packages?q=NoteSpace) on tagged releases, with symbol packages (`.snupkg`) and SourceLink. Every package targets `net10.0`. `NoteSpace.Core`, `NoteSpace.Editor` and `NoteSpace.Storage` have no UI dependency, and `NoteSpace.Rendering.Skia` needs only SkiaSharp. `NoteSpace.Controls` is an Uno Platform library (Uno.Sdk, Skia renderer) that depends on Uno.WinUI and SkiaSharp.Views.Uno.WinUI. No library references the app. The app supplies the storage, file and clipboard platform adapters and composes the controls.

```bash
dotnet add package NoteSpace.Core
```

| Package | Version | Downloads | Description |
|---|---|---|---|
| [NoteSpace.Core](https://www.nuget.org/packages/NoteSpace.Core) | [![NuGet](https://img.shields.io/nuget/vpre/NoteSpace.Core.svg)](https://www.nuget.org/packages/NoteSpace.Core) | [![Downloads](https://img.shields.io/nuget/dt/NoteSpace.Core.svg)](https://www.nuget.org/packages/NoteSpace.Core) | Notebook, rich-text and ink model, geometry, source-generated JSON and validation. |
| [NoteSpace.Editor](https://www.nuget.org/packages/NoteSpace.Editor) | [![NuGet](https://img.shields.io/nuget/vpre/NoteSpace.Editor.svg)](https://www.nuget.org/packages/NoteSpace.Editor) | [![Downloads](https://img.shields.io/nuget/dt/NoteSpace.Editor.svg)](https://www.nuget.org/packages/NoteSpace.Editor) | Transactions/history, page and section-group operations, scoped search/replace, rich text, tables and indexes. |
| [NoteSpace.Storage](https://www.nuget.org/packages/NoteSpace.Storage) | [![NuGet](https://img.shields.io/nuget/vpre/NoteSpace.Storage.svg)](https://www.nuget.org/packages/NoteSpace.Storage) | [![Downloads](https://img.shields.io/nuget/dt/NoteSpace.Storage.svg)](https://www.nuget.org/packages/NoteSpace.Storage) | Persistence contracts, atomic file storage, conflict detection and text/Markdown/HTML interchange. |
| [NoteSpace.Rendering.Skia](https://www.nuget.org/packages/NoteSpace.Rendering.Skia) | [![NuGet](https://img.shields.io/nuget/vpre/NoteSpace.Rendering.Skia.svg)](https://www.nuget.org/packages/NoteSpace.Rendering.Skia) | [![Downloads](https://img.shields.io/nuget/dt/NoteSpace.Rendering.Skia.svg)](https://www.nuget.org/packages/NoteSpace.Rendering.Skia) | SkiaSharp page composition, text layout, viewport culling, bounded caches and PNG export. |
| [NoteSpace.Controls](https://www.nuget.org/packages/NoteSpace.Controls) | [![NuGet](https://img.shields.io/nuget/vpre/NoteSpace.Controls.svg)](https://www.nuget.org/packages/NoteSpace.Controls) | [![Downloads](https://img.shields.io/nuget/dt/NoteSpace.Controls.svg)](https://www.nuget.org/packages/NoteSpace.Controls) | Uno note surface, ribbon, notebook/page navigation, search options, table editor, themes and icons. |

Dependencies follow the project references: `Editor` and `Storage` → `Core`; `Rendering.Skia` → `Editor` + SkiaSharp; `Controls` → `Editor` + `Rendering.Skia` + Uno Platform. To build packages locally, run `dotnet pack src/<Project> -c Release -o artifacts/packages`.

### NoteSpace.Core

The portable notebook model: workspaces, notebooks, nested section groups, sections, pages with subpage levels, free-form note blocks (text, headings, checklists, tables, images, attachments), rich-text marks, ink strokes and page versions, plus source-generated JSON serialization with full validation. No dependencies and no UI.

```bash
dotnet add package NoteSpace.Core
```

**Key types** (namespace `NoteSpace.Core`)

- `Workspace` / `Notebook` / `NoteSection` / `SectionGroup` / `NotePage` – the organization tree.
- `NoteBlock` – positioned content with `Kind`, `Text`, `Format`, `Marks`, `Tags`, `Cells` and attachment `Data`.
- `TextFormat`, `TextMark`, `InkStroke`/`InkPoint`, `NoteRect` – formatting, ink and geometry.
- `DocumentJson` – `Serialize`, `Deserialize` (validating), `Validate`, `Clone`, `PageJson`/`ReadPage`.
- `NotebookGroups` / `SampleWorkspace` – section-group outline helpers and the sample notebook.

**Usage**

```csharp
using NoteSpace.Core;

var page = new NotePage { Title = "Kickoff", Paper = PaperStyle.Ruled };
page.Blocks.Add(new NoteBlock { Kind = BlockKind.Heading, Text = "Agenda", Format = new TextFormat { FontSize = 24, Bold = true } });
page.Blocks.Add(new NoteBlock { Kind = BlockKind.Checklist, Text = "Book a room", Y = 200, Height = 42, Tags = ["todo"] });
page.Ink.Add(new InkStroke { Points = [new InkPoint(40, 320), new InkPoint(120, 340, 0.8f)] });

var workspace = new Workspace { Notebooks = [new Notebook { Title = "Research", Sections = [new NoteSection { Title = "Ideas", Pages = [page] }] }] };
DocumentJson.Validate(workspace);
string json = DocumentJson.Serialize(workspace);          // source-generated, trimming-friendly
Workspace restored = DocumentJson.Deserialize(json);      // validates identities, geometry and limits
```

### NoteSpace.Editor

The headless editing engine: an undoable `EditorSession` over a workspace with page, subpage, section and section-group operations, scoped literal search and style-preserving replace, rich-text range formatting, table/TSV operations, ink geometry and spatial page indexes. Depends on `NoteSpace.Core`; no UI.

```bash
dotnet add package NoteSpace.Editor
```

**Key types** (namespace `NoteSpace.Editor`)

- `EditorSession` – `EditPage`, `Execute`, `AddPage`/`AddSubpage`/`AddSection`, `Undo`/`Redo`, `SelectedPage`, `Changed`.
- `NoteSearchQuery` / `SearchHit` – scoped search (`Search`) and `ReplaceAll` with case/whole-word/tag/to-do filters.
- `RichText` – `Apply`, `GetRuns`, `ReplaceRange` over mixed-style ranges.
- `NoteTable` – cell edits, row/column insertion, `Transpose`, `ToTsv`/`ParseTsv`.
- `PageOutline`, `LiteralTextSearch`, `PageContentIndex`, `InkGeometry` – reusable helpers.

**Editing contract:** the session is single-writer. A successful `EditPage` replaces that page DTO, so keep IDs and resolve current objects after edits; its callback must mutate only the supplied detached page. Use `Execute` for cross-page and organization operations. Hosts that change collections outside transactions must call `InvalidateIndexes()`, and such changes get no undo or autosave notification. `SearchHit.Start`/`Length` are UTF-16 offsets and `Cell` identifies table matches.

**Usage**

```csharp
using NoteSpace.Core;
using NoteSpace.Editor;

var session = new EditorSession(SampleWorkspace.Create());
session.Changed += (_, change) => Console.WriteLine(change.Description);

var page = session.AddPage(session.Pages.First().Section.Id, "Draft notes");
session.AddBlock(page.Id, SampleWorkspace.Text("First draft of the plan", 48, 140, 520, 80));
session.EditPage(page.Id, "Bold first word", p => RichText.Apply(p.Blocks[0], 0, 5, f => f.Bold = true));

var query = new NoteSearchQuery("draft") { Scope = NoteSearchScope.Page, ScopeId = page.Id, WholeWord = true };
var hits = session.Search(query).ToList();
NoteReplaceResult result = session.ReplaceAll(query, "reviewed");   // one undoable transaction
Console.WriteLine($"{hits.Count} hits, {result.Matches} replaced; undo: {session.UndoDescription}");
session.Undo();
```

### NoteSpace.Storage

Persistence and interchange: the `IWorkspaceStore` contract with optimistic tokens, a desktop/CLI `FileWorkspaceStore` (exclusive lock, same-directory temporary file, atomic replace), validated immutable `WorkspaceSnapshot`s for fast autosave, and text/limited-Markdown import plus Markdown and escaped HTML export. Depends on `NoteSpace.Core`; no UI.

```bash
dotnet add package NoteSpace.Storage
```

**Key types** (namespace `NoteSpace.Storage`)

- `IWorkspaceStore` / `IWorkspaceSnapshotStore` – `LoadAsync`, `SaveAsync`, `SaveSnapshotAsync` with expected tokens.
- `FileWorkspaceStore` – atomic file-backed store for desktop and CLI hosts.
- `WorkspaceSnapshot` – `Capture(workspace)` and validating `Parse(json)`.
- `StorageConflictException` – thrown when another window saved first.
- `NoteExport` – `Markdown`, `Html`, `ImportText`, `SafeFileName`.

**Usage**

```csharp
using NoteSpace.Core;
using NoteSpace.Storage;

var store = new FileWorkspaceStore(Path.Combine(AppContext.BaseDirectory, "workspace.notespace"));
StoredWorkspace? stored = await store.LoadAsync();
Workspace workspace = stored?.Document ?? SampleWorkspace.Create();
try
{
    string token = await store.SaveSnapshotAsync(WorkspaceSnapshot.Capture(workspace), stored?.Token);
}
catch (StorageConflictException) { /* Changed elsewhere: export, then reload before saving. */ }

var page = workspace.Notebooks[0].Sections[0].Pages[0];
File.WriteAllText("page.md", NoteExport.Markdown(page));
NotePage imported = NoteExport.ImportText("Shopping\n\n[ ] Milk", "Imported");
```

### NoteSpace.Rendering.Skia

An independent SkiaSharp page renderer: paper styles, rich text layout, tables, images, attachments and ink with per-line/per-cell viewport culling, revision-keyed layout reuse, bounded image caches and PNG export. Use it for headless thumbnails and exports or inside any Skia canvas. Depends on `NoteSpace.Editor` and SkiaSharp; no Uno dependency. Include the Skia native-assets package for your platform.

```bash
dotnet add package NoteSpace.Rendering.Skia
```

**Key types** (namespace `NoteSpace.Rendering.Skia`)

- `PageRenderer` – `Render(canvas, page, width, height, options)`, `ExportPng(page, scale)`, `RegisterTypeface`, `Layout(block)`.
- `RenderOptions` – `Zoom`, offsets, `Dark`, selection/editing state and `ContentRevision` for cache reuse.
- `RendererStatistics` – cache hits, layout builds and drawn blocks/cells for diagnostics.
- `TextLayout` / `TextFragment` – measured text lines for hit testing.

**Usage**

```csharp
using NoteSpace.Core;
using NoteSpace.Rendering.Skia;
using SkiaSharp;

var page = SampleWorkspace.Create().Notebooks[0].Sections[0].Pages[0];
using var renderer = new PageRenderer();                  // dispose to release native caches
File.WriteAllBytes("page.png", renderer.ExportPng(page, scale: 2));

using var surface = SKSurface.Create(new SKImageInfo(1024, 768));
renderer.Render(surface.Canvas, page, 1024, 768, new RenderOptions { Zoom = 1.25f, ContentRevision = 1 });
Console.WriteLine($"{renderer.Statistics.BlocksDrawn} blocks drawn");
```

### NoteSpace.Controls

Reusable Uno Platform notebook UI: the interactive `NoteSurface` (text editing, move/resize grips, ink and shapes, tables), a Skia `NoteCanvas`, `RibbonControl`, `NotebookNavigator`, `PageListControl` with drag/drop, search options/results, the table editor, backstage, status bar, light/dark `OfficeTheme` and original icons. Depends on `NoteSpace.Editor` and `NoteSpace.Rendering.Skia`; requires Uno Platform.

```bash
dotnet add package NoteSpace.Controls
```

**Key types** (namespace `NoteSpace.Controls`)

- `NoteSurface` – `Session`, `Tool`, `PenColor`, `FormatSelection`, `ApplyTableCommand`, `RevealSearchResult`, `Error`.
- `RibbonControl` – `Configure(tabs, theme, collapsed)` with `RibbonTab`/`RibbonGroup`/`RibbonCommand`, `CommandInvoked`.
- `NotebookNavigator` / `PageListControl` – `Bind(...)` to a workspace or section, selection and command events.
- `SearchOptionsControl`, `SearchResultsControl`, `NoteTableEditor`, `BackstageControl`, `NoteStatusBar`.
- `OfficeTheme` – `Light`/`Dark` palettes and brush helpers.

**Usage**

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NoteSpace.Controls;
using NoteSpace.Core;
using NoteSpace.Editor;

var session = new EditorSession(SampleWorkspace.Create());
var surface = new NoteSurface { Session = session };
surface.Error += (_, message) => Console.WriteLine(message);

var ribbon = new RibbonControl();
ribbon.Configure([new RibbonTab("Draw", [new RibbonGroup("Tools", [new RibbonCommand("pen", "Pen", "pen"), new RibbonCommand("select", "Select", "page")])])], OfficeTheme.Light, collapsed: false);
ribbon.CommandInvoked += (_, id) => surface.Tool = id == "pen" ? DrawingTool.Pen : DrawingTool.Select;

var root = new Grid { RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition() } };
Grid.SetRow(surface, 1);
root.Children.Add(ribbon); root.Children.Add(surface);
window.Content = root;
window.Closed += (_, _) => surface.Dispose();              // subscribe to session.Changed for persistence
```

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

**Release** runs for `v*` tags or a supplied manual version. It validates the version, runs portable/rendering/browser gates, publishes self-contained single-file desktop executables for Windows, macOS and Linux (x64 and arm64), builds the browser output, packs versioned libraries with symbols and emits `SHA256SUMS`. Tags attach all assets to a GitHub Release and publish the packages to NuGet.org with [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing) (OIDC, no stored API key) from the protected `nuget` environment. Manual runs are dry runs: they build and upload every asset as workflow artifacts but publish nothing. Headless Chromium uses software rendering; mobile startup checks do not establish physical stylus/GPU, screen-reader or complete touch qualification.

## Remaining boundaries

This preview does **not** establish full OneNote feature, visual, accessibility, performance or file-format parity. Remaining areas include `.one`/`.onepkg`, Microsoft 365/OneDrive and authenticated collaboration, automatic merging/CRDTs, OCR and handwriting recognition, ink-to-math, equations, audio/video/transcription, web clipping, Outlook integration, protected sections and advanced printing.

Live mixed-style text editing is implemented; independently formatted paragraphs, full bidirectional/script shaping, qualified font fallback, IME presentation and touch/screen-reader rich-text interaction remain work. Browser preview uses packaged Open Sans aliases for common families, not Microsoft fonts or exact font equivalence. Tables do not support merged/rich cells or variable row/column sizing. Desktop platform file-picker workflows and larger/hardware-specific datasets need further qualification.

Markdown import supports basic paragraphs, headings and task prefixes. HTML/Markdown exports flatten placement and omit ink; PNG preserves rendered appearance, and `.notespace` preserves the implemented model. Unsupported OneNote content cannot be promised lossless conversion.

## Contributing and license

See [CONTRIBUTING.md](CONTRIBUTING.md). Include executable regressions and distinguish implemented behavior from parity goals. Source is **MIT licensed**. Uno Platform and SkiaSharp are external permissively licensed dependencies. Icons are original; proprietary Microsoft fonts/artwork are not bundled. Microsoft OneNote is a trademark of Microsoft Corporation. NoteSpace is not affiliated with or endorsed by Microsoft.
