using Microsoft.UI.Xaml;

namespace NoteSpace.App;

public partial class App : Application
{
    private Window? window;
    public App() { InitializeComponent(); }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var workspace = new WorkspaceView(new PlatformServices());
        window = new Window { Title = "NoteSpace", Content = workspace };
        window.Closed += (_, _) => workspace.Dispose();
        window.Activate();
    }
}
