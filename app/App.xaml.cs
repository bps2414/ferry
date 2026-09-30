namespace Ferry;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        Settings.Migrate(Settings.OldAppDir, Settings.AppDir); // antes da MainWindow (StartupUri) ler as configurações
        base.OnStartup(e);
    }
}
