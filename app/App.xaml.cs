using System.IO;

namespace Ferry;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        Settings.Migrate(Settings.OldAppDir, Settings.AppDir); // antes da MainWindow (StartupUri) ler as configurações
        Archives.SevenZipPath = Extract7z();
        base.OnStartup(e);
    }

    // 7z.exe + 7z.dll embutidos no exe, extraídos ao lado dele na 1ª vez
    static string Extract7z()
    {
        var dir = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "7z");
        Directory.CreateDirectory(dir);
        foreach (var name in new[] { "7z.exe", "7z.dll" })
        {
            using var src = typeof(App).Assembly.GetManifestResourceStream(name)!;
            var dst = Path.Combine(dir, name);
            if (File.Exists(dst) && new FileInfo(dst).Length == src.Length) continue;
            using var fs = File.Create(dst); src.CopyTo(fs);
        }
        return Path.Combine(dir, "7z.exe");
    }
}
