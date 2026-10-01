# Live text editing and layout

## Editing behavior

Double-click a text note to start editing. Skia draws mixed formatting while a native text control supplies Unicode and plain-text clipboard input. Click or drag inside the active note to position the caret or select a range; double-click to select a word. Escape commits and leaves the editor. Autosave commits without closing the draft.

Character commands apply to the selected range. At a caret, with no selection, they change future typing only. Moving the caret inherits the surrounding style. With no active editor, formatting applies to the whole selected container. Alignment, lists and flow settings apply to the entire container, not independently selected paragraphs.

The Layout tab exposes superscript/subscript, Copy Format / Paste Format, indentation, line spacing, Text Layout, Size & Position, and container ordering. Ctrl+M / Ctrl+Shift+M change indentation; Ctrl+Shift+C / Ctrl+Shift+V copy and apply the current character format. Ribbon state reflects the current selection or typing style.

Home/End use visual lines; Ctrl+Home/End use the note boundaries. Up/Down and Page Up/Down preserve the desired horizontal position through the shared rendered layout. Shift extends selection. Backspace/Delete remove complete Unicode text elements. Tab inserts a regular tab interval. Enter starts a paragraph; wrapped list lines do not create additional list numbers.

Ribbon Copy/Cut retains a selected rich fragment within the current surface/session. Ribbon Paste inserts it into an active editor. Outside editing, these commands retain note-container behavior. Native clipboard shortcuts transfer plain text; external HTML/RTF formatting is not imported.

## Reusable APIs

`TextEditingBuffer` in Editor is independent of Uno and Skia. It owns a detached block, directional selection, future-typing style and content version. The host supplies persistence and undo:

```csharp
var draft = new TextEditingBuffer(block);
draft.Select(0, 5);
draft.Format(style => style.Bold = true);
draft.Select(draft.Block.Text.Length, draft.Block.Text.Length);
draft.Format(style => style.Baseline = 1);
draft.ReplaceSelection("2");
session.EditPage(pageId, "Edit rich text", page =>
    draft.CopyContentTo(page.Blocks.First(b => b.Id == draft.Block.Id)));
```

Use buffer methods to change draft content, rather than mutating `Block` directly: cached grapheme boundaries and attribute runs are invalidated by the buffer's versioned operations. `CurrentFormat` returns a detached copy. Attribute queries share resolved runs per content version. Native full-value reconciliation prefers a valid splice at the existing selection before falling back to a minimal string difference, preserving style locations in repeated text.

`RichText.CopyRange` and `PasteRange` preserve canonical style runs and hyperlinks. `TextFlowSettings` belongs to Core, is validated and serialized, and defaults safely for old schema-one files. Baseline values are -1 (subscript), 0 and 1 (superscript).

The renderer's source-indexed fragments and visual lines drive `CaretBounds`, `SelectionBounds`, `HitTestText`, `MoveTextLine` and `TextLineEdge`. Their coordinates are container-local logical pixels. `RenderOptions.TextEdit` accepts transient draft content and adorners. Live and committed text use the same renderer; caret blinking changes neither content nor layout.

`NoteSurface` combines these components with native input. It reconciles current input before save/navigation and keeps failed drafts. Host-owned keyboard actions are ordered before subsequent input/format commands so dispatcher scheduling does not reorder a selection and its formatting. Selection, caret, copied fragments and future-typing styles are transient session state.

## Scope and limitations

Indents/spacing use logical pixels; line spacing multiplies the tallest nominal font on a visual line. First-line indentation applies to a hard paragraph, not a wrapped continuation. Script text is rendered at 70% nominal size with a shifted baseline. Existing geometry, range, aggregate-size and history limits remain enforced.

Not implemented: independently formatted paragraph runs, hanging indents, nested lists, arbitrary tab stops, a separate soft-break model, bidirectional paragraph navigation, ligature-aware caret positions, full script shaping, cross-container selections or rich/merged table cells. The table editor remains plain text.

Complete IME composition/candidate presentation, spell-check decoration, screen-reader rich-text ranges, touch selection handles and physical keyboard/device behavior need qualification. No Microsoft font files are bundled or pixel identity with OneNote asserted.

HTML export retains implemented alignment, indentation, line spacing and script styles, but is not a complete free-form layout export. PNG uses the shared renderer. Native .notespace backups preserve implemented model fields; .one/.onepkg interchange remains unsupported.

## Verification

Portable tests cover drafts, typing styles, Unicode edits, repeated-text offsets, rich clipboard, validation and history. Rendering tests cover caret/range geometry, wrapping, tabs, spacing and pixel equality between live drafts and committed text. Real-input browser tests check visible live formatting, native input, selection, layout and persistence. Results and test counts must be read from the particular CI run; this document does not imply unexecuted platform qualification.
