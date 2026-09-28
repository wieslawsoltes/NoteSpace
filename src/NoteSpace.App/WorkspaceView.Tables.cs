using NoteSpace.Core;

namespace NoteSpace.App;

public sealed partial class WorkspaceView
{
    private async Task<bool> ExecuteTableCommandAsync(string command)
    {
        if (!command.StartsWith("table-", StringComparison.Ordinal)) return false;
        {
            switch (command)
            {
                case "table-edit-cell": surface.EditSelectedTableCell(); return true;
                case "table-edit-all": await TableDialogAsync(surface.SelectedBlockId); return true;
                case "table-copy":
                    var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    package.SetText(surface.CopyTableText()); Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package); return true;
                case "table-paste":
                    var targetPage = CurrentPage?.Id; var targetBlock = surface.SelectedBlockId; var targetCell = surface.SelectedTableCell;
                    var content = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
                    if (!content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text)) throw new InvalidOperationException("The clipboard does not contain text.");
                    var tsv = await content.GetTextAsync();
                    if (CurrentPage?.Id != targetPage || surface.SelectedBlockId != targetBlock || surface.SelectedTableCell != targetCell) throw new InvalidOperationException("The paste target changed. Select the destination again.");
                    surface.PasteTableText(tsv); return true;
                default: surface.ApplyTableCommand(command[6..]); return true;
            }
        }
    }
}
