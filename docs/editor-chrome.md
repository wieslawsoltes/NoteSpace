# Simplified ribbon and selection tools

## Ribbon presentation

Use the **Simplified ribbon / Classic ribbon** button at the end of the tab strip to change the presentation. The same `RibbonTab`, `RibbonGroup` and `RibbonCommand` definitions drive both modes. The single-row presentation uses icon buttons with full command tooltips. Commands that do not fit remain in **More ribbon commands**, in the same group/order, with their current selected/enabled states. No separate implementation of text editing or document commands exists in the compact view.

The chosen ribbon mode is saved in `WorkspaceSettings.Navigation.SimplifiedRibbon`. Old backups default to the classic presentation. **Ctrl+F1** collapses or restores the ribbon in either mode. Collapsing does not discard the chosen mode. Changing width within the same overflow breakpoint reuses the existing command controls and menu rather than rebuilding them on every pixel.

`RibbonControl.SetSimplified` is independently reusable; the existing three-argument `Configure` overload remains supported. `EditorChromeLayout.VisibleCommands` supplies the UI-independent overflow calculation. `VisibleCommandCount`, `OverflowCommandCount` and `CompactRebuilds` describe control work, not rendering frame rates.

## Selection mini-toolbar

Completing a pointer text selection shows a small toolbar above or below the selected text when enough writing-surface space exists. Font/size, bold, italic, underline and highlight are directly available; More exposes character scripts, font color, clear formatting, format copying and selected-text copy/cut. Actions use the existing editor command handlers and retain the original selection. The toolbar does not create an independent undo stack.

**Alt+F10** opens and focuses selection tools for a nonempty active rich-text selection. Escape dismisses the toolbar and returns to the text input. Typing, scrolling, starting another pointer selection, leaving the surface or closing the editor dismisses the transient toolbar. View → Selection toolbar enables/disables automatic and keyboard presentation, with the preference saved in notebook backups. It is enabled by default for old backups.

`SelectionToolbar` is a reusable Uno control raising command IDs without owning a document. `NoteSurface` supplies its selection, placement, state and host command forwarding. `EditorChromeLayout.PlaceToolbar` is the headless placement helper; it keeps the toolbar within the writing viewport, tries above/below the selection, and hides it instead of covering the selection when neither fits. State, focus and placement are transient; only the enabled preference is persisted.

## Boundaries and verification

The strip is not a pixel-identical Microsoft ribbon, custom command pinning or a complete Office keyboard-tip system. The mini-toolbar formats one active text container, not arbitrary objects, table-cell ranges or cross-container selections. Native text/IME and rich clipboard limitations remain those described in [WYSIWYG editing](wysiwyg.md). Hardware stylus, full touch selection and screen-reader qualification remain separate work.

Portable tests cover overflow accounting, randomized fit invariants, placement edges/invalid inputs and backward-compatible preference round trips. Browser tests use real pointer/keyboard input, inspect saved content and verify selection retention, overflow commands, keyboard dismissal, layout reuse and preference reload. Existing organization, table, search and rich editing suites remain enabled. Results are qualified by their individual CI run; adding a test is not a claim that it passed.

Microsoft references: [OneNote's optional simplified ribbon](https://techcommunity.microsoft.com/blog/microsoft_365blog/refreshing-the-onenote-app-on-windows/3401914), [more room for OneNote content](https://techcommunity.microsoft.com/blog/microsoft_365blog/more-room-for-your-notes-in-onenote/4411729) and the [Office mini-toolbar workflow](https://support.microsoft.com/en-au/word/use-the-mini-toolbar-to-format-text). These inform familiar interactions, not a claim of complete OneNote behavior or visual equivalence.
