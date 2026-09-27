# Architecture

## Dependency boundaries

`Core` defines data and validation. `Editor` applies UI-independent mutations. `Storage` consumes the model through `IWorkspaceStore`. `Rendering.Skia` turns model state into pixels and never references Uno. `Controls` adapts the renderer and editing session to Uno input, layout, and accessibility primitives. `App` composes these libraries and supplies platform services.

All five libraries are independently packable. A headless consumer can reference Core/Editor/Storage without loading Uno or native Skia. A rendering-only consumer can use `PageRenderer` without any application shell. A Uno consumer can reuse `NoteSurface`, `RibbonControl`, `NotebookNavigator`, `PageListControl`, `NoteTableEditor`, `SearchResultsControl`, `BackstageControl`, `ColorPalette`, `NoteStatusBar`, `NoteIcon`, and the theme/button primitives separately.

## Editing transactions

`EditorSession.Execute` owns document mutation. A transaction snapshots the pre-edit state, validates the resulting document, records a bounded undo entry, clears redo, and raises one change notification. Container drag, resize, and ink gestures use temporary render state until pointer release; canceled gestures do not enter history. Text input commits in debounced batches and is flushed before navigation/export.

Models are mutable DTOs for efficient source-generated serialization. Consumers must not mutate documents concurrently. The session is a single-writer abstraction intended to be owned by a UI dispatcher or another explicitly serialized command loop. This is not a thread-safe collaborative document engine.

## Rendering

`PageRenderer` uses document-space geometry and a viewport transform. It culls off-screen containers/strokes, caches text layout by content/format geometry, caches typefaces, and bounds decoded image caches. Transient selection, hover, drag and ink previews are supplied with `RenderOptions`, not written into persisted content. Native resources belong to the renderer and must be disposed.

The renderer uses Skia text measurements and grapheme-aware fallback wrapping. It is not a replacement for a fully shaped multilingual rich-text engine. The native Uno text overlay handles editing, while Skia handles final page composition. More advanced script shaping, semantic accessibility and incremental large-document indexing are extension areas.

## Persistence

`IWorkspaceStore.LoadAsync` returns a document and opaque token. `SaveAsync` accepts the last observed token and returns a new one. Implementations must reject stale writes.

The browser adapter performs compare-and-swap inside a single IndexedDB read/write transaction. The desktop adapter holds an exclusive lock while comparing a SHA-256 content token, writes a same-directory temporary file, and replaces the destination. The app serializes autosaves and tracks edits made while an asynchronous save is in flight. The saved state is acknowledged only after the store completes.

Loading invalid or unavailable storage enters a non-saving recovery session rather than overwriting stored data. Conflict detection is intentionally not presented as cloud sync or automatic collaboration.

## Interchange

`.notespace` files are versioned camel-case JSON workspaces. Source-generated metadata is defined in Core. Attachments are base64-encoded JSON data. Input validation checks supported schema, identities, finite values, range bounds and collection limits. Unknown future schema versions are rejected, not silently downgraded.

Markdown/HTML export is a reading-order conversion, not a pixel-perfect page format. PNG is a rendered page export and `.notespace` is the lossless format for the implemented model.

## Extending the product

Add domain operations to Editor, native rendering to Rendering.Skia, host-independent UI to Controls, and service coordination to App. Implement a remote `IWorkspaceStore` for a server-backed store, but introduce an explicit authentication, authorization, merge, and offline synchronization design before advertising multi-user collaboration.

A new ribbon command needs an actual handler and executable verification. Do not add decorative buttons that imply an unsupported capability. New model fields should include round-trip, validation, undo/redo, and export behavior where appropriate.

## Outline and range-editing contracts

`PageOutline` is a linear-time read-only projection of section order: each entry includes its effective depth, parent index, and exclusive subtree end. It accepts older orphan indentation defensively; transaction-based organization commands normalize only affected sections. Persisted `NotePage.IsCollapsed` remains compatible with schema-1 files that omit the property. `PageListControl` delegates commands to its host and uses this projection for disclosure, keyboard navigation, and search-target reveal. Recent order is a view, not a destructive sort.

`RichText.GetRuns` resolves legacy overlapping marks using an event sweep and last-mark-wins ordering. Range formatting splits at run boundaries and retains unrelated attributes and hyperlinks. Edits write back canonical, non-overlapping marks. `ReplaceRange` uses explicit UTF-16 ranges; whole-value native input uses a minimal-difference adapter. `ReplaceAll` walks non-overlapping ordinal matches and preserves intervening runs. These operations do not provide full Unicode grapheme editing, script shaping, or an in-place rich-text input control. Wrap direct mutations in `EditorSession.EditPage` to get validation, rollback, undo and autosave notification.


`NotebookGroups` provides a notebook-local section-group graph and navigation projection. Sections remain in `Notebook.Sections`; `NoteSection.GroupId` and `SectionGroup.ParentId` hold organization. This keeps existing page enumeration, search and recovery independent of group visibility. IDs are globally validated by `DocumentJson`, references must belong to the same notebook, cycles are rejected, and group depth is bounded to eight. See [organization contracts](organization.md).

`EditorSession.CanMovePageRelative` validates a read-only drop preview; `MovePageRelative` revalidates and commits the entire page subtree in one transaction. `PageListControl` owns pointer capture, thresholds, indicators and cancellation, delegating validation/commit to its host. It never edits the model during pointer movement. Context menus remain the keyboard alternative to drag operations.
