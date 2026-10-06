using System.IO;
using System.Windows;

namespace FileChecklist.Desktop;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow(); MainWindow = window;
        window.Show();
        if (e.Args.Contains("--smoke")) window.Loaded += async (_, _) => await window.RunSmoke(e.Args.Last());
        else if (e.Args.Contains("--demo")) window.Loaded += (_, _) => window.StartDemo();
        else if (e.Args.Length == 1 && File.Exists(e.Args[0])) window.Loaded += (_, _) => window.OpenTask(e.Args[0]);
    }
}
