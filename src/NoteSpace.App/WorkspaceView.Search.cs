using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NoteSpace.Controls;
using NoteSpace.Editor;

namespace NoteSpace.App;

public sealed partial class WorkspaceView
{
    private readonly DispatcherTimer searchTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private void ConfigureSearch()
    {
        searchTimer.Tick += (_, _) => { searchTimer.Stop(); UpdateSearch(); };
        search.Options.Changed += (_, _) => ScheduleSearch();
    }
    private void ScheduleSearch()
    {
        searchTimer.Stop(); if (searchOpen) searchTimer.Start();
    }
    private NoteSearchQuery Query(string text, SearchOptionsControl options)
    {
        var selected = session.Pages.FirstOrDefault(x => x.Page.Id == CurrentPage?.Id);
        var id = options.Scope switch {
            NoteSearchScope.Notebook => selected.Notebook?.Id,
            NoteSearchScope.Section => CurrentSection?.Id,
            NoteSearchScope.Page => CurrentPage?.Id,
            _ => null
        };
        // A removed/empty scope should find nothing, never silently widen to all notes.
        return new(text) { Scope = options.Scope, ScopeId = id ?? "missing-scope", Filter = options.Filter,
            MatchCase = options.MatchCase, WholeWord = options.WholeWord, MaximumResults = 201 };
    }
    private void NavigateSearchResult(SearchHit hit)
    {
        Navigate(hit.PageId, hit.BlockId); if (surface.HasPendingText) return;
        surface.RevealSearchResult(hit); Report();
    }
    private async Task ReplaceTextAsync()
    {
        surface.EndEditing(); if (surface.HasPendingText) return;
        var find = new TextBox { Header = "Find", Text = search.QueryBox.Text, MinWidth = 310, MaxLength = 100000 };
        var replacement = new TextBox { Header = "Replace with", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 100, MaxLength = 100000 };
        AutomationProperties.SetAutomationId(find, "replace-find"); AutomationProperties.SetAutomationId(replacement, "replace-value");
        var options = new SearchOptionsControl(false) { Scope = search.Options.Scope, MatchCase = search.Options.MatchCase, WholeWord = search.Options.WholeWord };
        var content = new StackPanel { Spacing = 12, MaxWidth = 440 };
        content.Children.Add(find); content.Children.Add(replacement); content.Children.Add(options);
        content.Children.Add(new TextBlock { Text = "Replace note text and table cells. Page titles, tags and attachments stay unchanged. The entire replacement is one undoable action.", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        var dialog = new ContentDialog { Title = "Replace text", Content = new ScrollViewer { Content = content, MaxHeight = 540 }, PrimaryButtonText = "Replace all", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary, IsPrimaryButtonEnabled = find.Text.Length > 0 };
        find.TextChanging += (_, _) => dialog.IsPrimaryButtonEnabled = find.Text.Length > 0;
        dialog.Opened += (_, _) => { find.Focus(FocusState.Programmatic); find.SelectAll(); };
        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary || find.Text.Length == 0) return;
        var result = session.ReplaceAll(Query(find.Text, options), replacement.Text, reflow: block => block.Height = Math.Clamp(surface.Renderer.MeasureHeight(block), 40, 20000));
        UpdateSearch();
        await MessageAsync("Replace complete", $"{result.Matches} matches; updated {result.ChangedContainers} text containers or table cells on {result.ChangedPages} pages.");
    }
}
