# Notebook organization

## Section groups

`Notebook.SectionGroups` stores an ordered, flat list of `SectionGroup` records. Each record has an ID, title, color, optional parent-group ID, and persistent collapsed state. Sections remain in `Notebook.Sections`; their optional `GroupId` associates them with a group. A null parent/group ID means the notebook root. This organization is separate from a page's two-level subpage outline.

The model validates group IDs globally alongside notebooks, sections, pages, blocks and strokes. Group parents and section memberships must resolve within the same notebook. Cycles, missing references, null collections, more than 1,000 groups per notebook, and paths deeper than eight groups are rejected. Existing workspace, section and page limits still apply.

`NotebookGroups.Build` returns sections before child groups at each level, maintaining sibling order within those categories. It can hide collapsed descendants, reveal the ancestor path of a selected section, or include all entries. Queries do not mutate the document. `Ancestors`, `SubtreeIds` and `Path` expose IDs and readable destinations without any Uno dependency.

```csharp
var notebook = session.Document.Notebooks[0];
var research = session.AddSectionGroup(notebook.Id, "Research");
var drafts = session.AddSectionGroup(notebook.Id, "Drafts", research.Id);
var section = session.AddSectionInGroup(notebook.Id, "Ideas", drafts.Id);
session.SetSectionGroupCollapsed(research.Id, true);
var visible = NotebookGroups.Build(notebook, revealSectionId: section.Id);
```

All editor methods execute as a single validated transaction. A rejected operation restores the previous workspace and preserves existing redo history. Retain IDs rather than mutable object references across undo/redo or a rejected transaction, because restoration rehydrates the model.

## Moving and ungrouping

`MoveSection` relocates a complete section to a notebook root or group. `MoveSectionGroup` carries every descendant group and every section associated with that subtree, including all page content and histories. Destination ancestors expand. Moves reject a target inside the moving group's subtree and validate resulting depth before committing. Sibling reordering does not cross parent/group boundaries.

```csharp
session.MoveSection(section.Id, anotherNotebook.Id, destinationGroup.Id);
session.MoveSectionGroup(research.Id, anotherNotebook.Id); // Notebook root.
session.MoveSectionSibling(section.Id, -1);
session.MoveSectionGroupSibling(research.Id, 1);
```

**Ungroup is non-destructive.** It removes only the selected group. Direct sections and child groups move to its parent, or to the notebook root. No page is deleted or moved to the recycle bin. Undo restores the organization. Deleting a whole notebook still moves all of its pages to the existing recycle bin; page recovery does not reconstruct a deleted notebook's group structure.

The navigator can temporarily reveal the current section while retaining saved collapse flags. Explicitly collapsing that group hides it until another navigation request reveals the selected section again. Horizontal section tabs are a flat view, with full group paths in their tooltips.

## Page subtree drops

`PageDropRequest` identifies source and target pages plus `Before`, `After`, or `Inside` placement. Before/after means a sibling of the target, outside its descendants; inside appends a child after all existing descendants. Every descendant of the source travels with it and retains relative indentation. Source/target pages may belong to different sections in the reusable API.

```csharp
var request = new PageDropRequest(sourcePage.Id, targetPage.Id, PageDropPlacement.Inside);
if (session.CanMovePageRelative(request))
    session.MovePageRelative(request);
```

Preview validation does not mutate legacy indentation. Commit normalizes affected sections and verifies IDs, self/descendant relationships and depth again. Invalid and unchanged drops do not consume undo history. A valid move selects the moved root and expands an inside destination.

`PageListControl` exposes `CanDrop` and `PageMoveRequested`; hosts wire those to their own session and flush any pending text before a commit. Dragging starts from the dedicated grip after a movement threshold. Only pointer release commits. Capture loss, Escape, unbinding, unloading, or a release outside the list cancels and clears the preview. Edge scrolling uses the list's scroll viewer; recent-order display disables drag handles.

The app supports drag gestures **within the visible section**. Cross-section movement is through the Move dialog. Dragging whole sections or groups, cross-window dragging, multi-page selections and physical stylus/device qualification are not provided by this change.

## File compatibility

The new properties are additive to schema 1. Older NoteSpace workspaces that omit them load as ungrouped. Current exports preserve group IDs, nesting, colors and collapse state. Older application versions do not understand group properties and may discard grouping when resaving; retain a current-version `.notespace` backup before opening it in an older build. This is NoteSpace model compatibility, not Microsoft `.one`/`.onepkg` compatibility.

## Verification

Portable organization specifications cover nesting, group moves across notebooks, non-destructive ungrouping, path validation, rollback and persistence. Deterministic randomized group and page moves verify identity conservation, valid depth, and undo/redo restoration. Browser checks use real keyboard and pointer input, observing saved JSON only to assert results. They do not invoke a hidden model-mutation API. Headless Chromium tests do not establish physical GPU/stylus, mobile drag, accessibility or full OneNote parity.

## Browsing and page presentation

The quick-access Back/Forward buttons and Alt+Left/Right traverse a bounded, in-session visit history. Each visit retains its page ID, scroll offsets and zoom, not a copy of notebook content. Pending text is committed before leaving. Missing/deleted pages are skipped; browsing does not restore deleted notes or consume document undo. A new destination after going back replaces the forward branch. Reload/import starts a new browsing session.

The page-list **Order** button opens manual, title A–Z/Z–A, modified-newest and created-newest views plus optional text previews/dates. Sorting applies to siblings and keeps every subtree attached to its parent. It changes the presentation only; returning to Manual restores the saved order. Collapse/reveal and keyboard navigation continue to work in sorted views. Drag and manual reorder controls are hidden while sorted. Preferences are workspace-wide and saved with the notebook backup.

Drag the notebook/page pane's right edge or the search pane's left edge to resize. Escape cancels a drag; Left/Right move an edge in 10-pixel steps; Home or double-click restores its default. View → Reset pane widths restores all three defaults. A resize is persisted only when completed. Desktop widths compress to leave room for the page without overwriting saved preferences. On phone-width layouts, navigation or search occupies the body rather than squeezing the writing surface to a sliver. Closing search returns to the page/navigation view.

`PagePresentation`, `PageNavigationHistory`, `PageViewport` and `NavigationPaneLayout` are UI-independent editor APIs. `NavigationPreferences` is a validated Core DTO with backward-compatible defaults. `PaneResizeHandle` is a reusable Uno control with a range-value automation peer and pointer, keyboard and cancellation events. Single-page navigation within an unchanged visible outline reuses existing row controls. Previews scan at most 32 containers/4,096 UTF-16 characters, do not decode images or attachments, and may fall back to a generic label when content lies beyond that budget.

Reference workflows: [Microsoft's navigation guide](https://support.microsoft.com/en-us/onenote/onenote-help-and-learning/navigate-your-notes-with-onenote) and [OneNote keyboard shortcuts](https://support.microsoft.com/en-us/accessibility/onenote/keyboard-shortcuts-in-onenote). These features do not establish complete visual or behavioral parity, persistent cross-device visit history, or full screen-reader/touch hardware qualification.
