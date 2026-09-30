using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PS5Sender;

public partial class MainWindow : Window
{
    readonly Settings _settings = Settings.Load();
    readonly Engine _engine;
    readonly CancellationTokenSource _stop = new();

    public MainWindow()
    {
        InitializeComponent();
        SettingsPanel.DataContext = _settings;
        PwBox.Password = _settings.Password;
        foreach (ComboBoxItem i in Preset.Items) if ((string)i.Tag == _settings.RemoteDir) Preset.SelectedItem = i;

        _engine = new Engine(_settings, Log, AskPassword);
        BindingOperations.EnableCollectionSynchronization(_engine.Jobs, _engine.Lock);
        JobList.ItemsSource = _engine.Jobs;
        _engine.Restore();

        // resumo da fila (contagens, velocidade total) 2x por segundo
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => RefreshSummary();
        timer.Start();
        RefreshSummary();
        UpdateStatusCard();

        Task.Run(() => _engine.RunAsync(_stop.Token));
        Loaded += async (_, _) => await TestConnection(silent: true);
        Closing += (_, _) => { _stop.Cancel(); try { _settings.Save(); } catch { } };
    }

    void RefreshSummary()
    {
        List<Job> jobs;
        lock (_engine.Lock) jobs = [.. _engine.Jobs];
        int Count(params Stage[] s) => jobs.Count(j => s.Contains(j.Stage));
        var sending = Count(Stage.Extraindo, Stage.Enviando);
        var queued = Count(Stage.NaFila, Stage.AguardandoPartes, Stage.Pausado);
        var done = Count(Stage.Verificado);
        var errors = Count(Stage.Erro);
        var parts = new List<string>();
        if (sending > 0) parts.Add($"{sending} enviando");
        if (queued > 0) parts.Add($"{queued} na fila");
        if (done > 0) parts.Add($"{done} concluído(s)");
        if (errors > 0) parts.Add($"{errors} com erro");
        SummaryText.Text = parts.Count == 0 ? "Nenhum jogo na fila" : string.Join(" · ", parts);

        var rate = jobs.Where(j => j.IsActive).Sum(j => j.Rate);
        SpeedPill.Visibility = rate > 0 ? Visibility.Visible : Visibility.Collapsed;
        SpeedText.Text = $"{Job.Size((long)rate)}/s";
        ClearBtn.Visibility = done > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyHint.Visibility = jobs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var pending = jobs.Count - done;
        QueueBadge.Visibility = pending > 0 ? Visibility.Visible : Visibility.Collapsed;
        QueueBadgeText.Text = pending.ToString();
    }

    void OnNav(object s, RoutedEventArgs e)
    {
        if (QueuePage == null) return; // ainda carregando o XAML
        QueuePage.Visibility = NavQueue.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = NavSettings.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        LogPage.Visibility = NavLog.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    void UpdateStatusCard()
    {
        StatusHost.Text = $"{_settings.Host}:{_settings.Port}";
        StatusDest.Text = _settings.RemoteDir;
    }

    async Task TestConnection(bool silent)
    {
        UpdateStatusCard();
        SideTestBtn.IsEnabled = TestBtn.IsEnabled = false;
        StatusTitle.Text = "Testando…"; StatusDot.Fill = (Brush)FindResource("Amber");
        if (!silent) TestResult.Text = "Testando…";
        try
        {
            var msg = await Ftp.TestAsync(_settings);
            StatusTitle.Text = "PS5 online"; StatusDot.Fill = (Brush)FindResource("Green");
            TestResult.Text = msg; TestResult.Foreground = (Brush)FindResource("Green");
            Log("Teste de conexão: " + msg);
        }
        catch (Exception ex)
        {
            StatusTitle.Text = "PS5 offline"; StatusDot.Fill = (Brush)FindResource("Red");
            TestResult.Text = "Falhou: " + ex.Message; TestResult.Foreground = (Brush)FindResource("Red");
            Log("Teste de conexão falhou: " + ex.Message);
        }
        SideTestBtn.IsEnabled = TestBtn.IsEnabled = true;
    }

    void Log(string msg) => Dispatcher.BeginInvoke(() =>
    {
        LogBox.AppendText($"{DateTime.Now:HH:mm:ss}  {msg}\n");
        LogBox.ScrollToEnd();
    });

    Task<string?> AskPassword(Job job) => Dispatcher.InvokeAsync(() =>
    {
        var wrong = job.ArchivePassword != null;
        var box = new PasswordBox { Margin = new Thickness(0, 12, 0, 18) };
        var ok = new Button { Content = "Extrair", IsDefault = true, MinWidth = 110, Style = (Style)FindResource("AccentButtonStyle") };
        var cancel = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 110, Margin = new Thickness(8, 0, 0, 0) };
        var w = new Window
        {
            Title = "Senha do arquivo", Owner = this, Width = 440, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = (Brush)FindResource("Bg"),
            Content = new StackPanel
            {
                Margin = new Thickness(22),
                Children =
                {
                    new TextBlock { Text = wrong ? "Senha incorreta" : "Arquivo protegido por senha", FontSize = 16, FontWeight = FontWeights.SemiBold,
                                    Foreground = (Brush)FindResource(wrong ? "Red" : "Text") },
                    new TextBlock { Text = wrong ? $"A senha não abriu \"{job.Name}\". Tente de novo." : $"Digite a senha de \"{job.Name}\".",
                                    Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) },
                    box,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
                },
            },
        };
        ok.Click += (_, _) => w.DialogResult = true;
        w.Loaded += (_, _) => box.Focus();
        return w.ShowDialog() == true ? box.Password : null;
    }).Task;

    static Job JobOf(object sender) => (Job)((FrameworkElement)sender).DataContext;
    void OnPause(object s, RoutedEventArgs e) => _engine.Pause(JobOf(s));
    void OnResume(object s, RoutedEventArgs e) => _engine.Resume(JobOf(s));
    void OnCancel(object s, RoutedEventArgs e) => _engine.Cancel(JobOf(s));
    void OnRetry(object s, RoutedEventArgs e) => _engine.Retry(JobOf(s));
    void OnRemove(object s, RoutedEventArgs e) => _engine.Remove(JobOf(s));
    void OnClearFinished(object s, RoutedEventArgs e) => _engine.ClearFinished();

    void OnDragEnter(object s, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        NavQueue.IsChecked = true;
        DropOverlay.Visibility = Visibility.Visible;
    }

    void OnDragOver(object s, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void OnDragLeave(object s, DragEventArgs e)
    {
        // DragLeave dispara ao passar por elementos filhos; só esconde se o mouse saiu da janela
        var p = e.GetPosition(this);
        if (p.X <= 0 || p.Y <= 0 || p.X >= ActualWidth || p.Y >= ActualHeight) DropOverlay.Visibility = Visibility.Collapsed;
    }

    void OnDrop(object s, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        _engine.AddFiles(files);
        Log($"{files.Length} arquivo(s) adicionado(s) por arrastar e soltar");
    }

    void OnPickFiles(object s, RoutedEventArgs e)
    {
        var d = new OpenFileDialog
        {
            Title = "Escolher jogos (selecione todas as partes)",
            Multiselect = true,
            Filter = "Jogos compactados|*.zip;*.rar;*.7z;*.0*;*.z0*;*.z1*;*.r0*;*.r1*|Todos os arquivos|*.*",
            InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        };
        if (d.ShowDialog(this) != true) return;
        _engine.AddFiles(d.FileNames);
        Log($"{d.FileNames.Length} arquivo(s) selecionado(s)");
    }

    void OnPwChanged(object s, RoutedEventArgs e) => _settings.Password = PwBox.Password;

    void Rebind() { SettingsPanel.DataContext = null; SettingsPanel.DataContext = _settings; }

    void OnPreset(object s, SelectionChangedEventArgs e)
    {
        if (Preset.SelectedItem is ComboBoxItem { Tag: string path } && _settings.RemoteDir != path) { _settings.RemoteDir = path; Rebind(); }
    }

    void OnPickFolder(object s, RoutedEventArgs e)
    {
        var d = new OpenFolderDialog { Title = "Pasta monitorada" };
        if (d.ShowDialog() == true) { _settings.InputFolder = d.FolderName; Rebind(); }
    }

    async void OnSave(object s, RoutedEventArgs e)
    {
        _settings.Save();
        Log("Configurações salvas");
        await TestConnection(silent: false);
    }

    async void OnTest(object s, RoutedEventArgs e) => await TestConnection(silent: false);

    void OnCopyLog(object s, RoutedEventArgs e) { try { Clipboard.SetText(LogBox.Text); } catch { } }
    void OnClearLog(object s, RoutedEventArgs e) => LogBox.Clear();
}
