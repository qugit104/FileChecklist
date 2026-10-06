using System.IO;
using System.Windows;
using System.Globalization;

namespace FileChecklist.Desktop;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args.ToList();
        int languageIndex = args.IndexOf("--lang");
        if (languageIndex >= 0)
        {
            if (languageIndex + 1 >= args.Count || args[languageIndex + 1] is not ("en" or "zh"))
            { MessageBox.Show("Use --lang en or --lang zh.", "FileChecklist"); Shutdown(64); return; }
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(args[languageIndex + 1] == "zh" ? "zh-CN" : "en-US");
            args.RemoveRange(languageIndex, 2);
        }
        var window = new MainWindow(); MainWindow = window;
        window.Show();
        if (args.Contains("--smoke")) window.Loaded += async (_, _) => await window.RunSmoke(args.Last());
        else if (args.Contains("--demo")) window.Loaded += (_, _) => window.StartDemo();
        else if (args.Count == 1 && File.Exists(args[0])) window.Loaded += (_, _) => window.OpenTask(args[0]);
    }
}
