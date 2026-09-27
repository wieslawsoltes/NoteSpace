using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NoteSpace.Controls;
using NoteSpace.Core;

namespace NoteSpace.App;

public partial class App : Application
{
    private Window? window;
    public App() { InitializeComponent(); }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var workspace = SampleWorkspace.Create();
        window = new Window { Title = "NoteSpace", Content = new NoteCanvas { Page = workspace.Notebooks[0].Sections[0].Pages[0] } };
        window.Activate();
    }
}
