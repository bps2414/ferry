using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
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
    readonly System.Windows.Forms.NotifyIcon _tray = new();
    bool _loading; // preenchendo os campos por código: não conta como edição do usuário
    (string Ip, int Port)? _found; // PS5 achado na varredura, aguardando "Usar este"

    public MainWindow()
    {
        InitializeComponent();
        SettingsPanel.DataContext = _settings;
        PwBox.Password = _settings.Password;
        LoadFields();
        foreach (ComboBoxItem i in Preset.Items) if ((string)i.Tag == _settings.RemoteDir) Preset.SelectedItem = i;

        // toast do Windows via balão da bandeja
        _tray.Icon = (Environment.ProcessPath is { } exe ? System.Drawing.Icon.ExtractAssociatedIcon(exe) : null) ?? System.Drawing.SystemIcons.Application;
        _tray.Visible = true;

        _engine = new Engine(_settings, Log, AskPassword);
        _engine.Done = job => Dispatcher.BeginInvoke(() =>
        {
            if (job.Stage == Stage.Verificado) Toast("Envio concluído", $"{(job.Title != "" ? job.Title : job.Name)} concluído");
            else Toast("Erro no envio", $"Erro em {job.Name}: {Short(job.Detail)}");
        });
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
        Loaded += async (_, _) => { if (!await TestConnection(silent: true)) await Discover(silent: true); };
        Closing += (_, _) => { _stop.Cancel(); try { _settings.Save(); } catch { } _tray.Visible = false; _tray.Dispose(); };
    }

    // só avisa com a janela sem foco; chamar na thread da UI
    void Toast(string title, string text)
    {
        if (!IsActive) _tray.ShowBalloonTip(5000, title, text, System.Windows.Forms.ToolTipIcon.Info);
    }

    static string Short(string s)
    {
        var line = s.Split('\n')[0].Trim();
        return line.Length > 120 ? line[..120] + "…" : line;
    }

    void Persist()
    {
        try { _settings.Save(); }
        catch (Exception ex) { Log("Falha ao salvar as configurações: " + ex.Message); }
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
        if (NavSettings.IsChecked == true) LoadPasswords(); // o Engine pode ter acrescentado senhas
    }

    void LoadPasswords()
    {
        _loading = true;
        PwList.Text = string.Join(Environment.NewLine, _settings.KnownPasswords);
        _loading = false;
    }

    // campos que não usam binding (validam antes de gravar)
    void LoadFields()
    {
        _loading = true;
        HostBox.Text = _settings.Host;
        PortBox.Text = _settings.Port.ToString();
        FolderBox.Text = _settings.InputFolder;
        ImageBox.Text = _settings.ImageDir;
        _loading = false;
        Validate(HostBox, HostHint, null); Validate(PortBox, PortHint, null); Validate(FolderBox, FolderHint, null); Validate(ImageBox, ImageHint, null);
    }

    void Validate(TextBox box, TextBlock hint, string? error)
    {
        hint.Text = error ?? "";
        hint.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
        if (error == null) box.ClearValue(Control.BorderBrushProperty);
        else box.BorderBrush = (Brush)FindResource("Red");
    }

    void OnHostChanged(object s, TextChangedEventArgs e)
    {
        if (_loading) return;
        var t = HostBox.Text;
        var error = t.Length == 0 || t.Any(char.IsWhiteSpace) ? "Informe o IP ou o nome do PS5, sem espaços." : null;
        Validate(HostBox, HostHint, error);
        if (error != null || t == _settings.Host) return;
        _settings.Host = t; Persist(); UpdateStatusCard();
    }

    void OnPortChanged(object s, TextChangedEventArgs e)
    {
        if (_loading) return;
        var ok = int.TryParse(PortBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is >= 1 and <= 65535;
        Validate(PortBox, PortHint, ok ? null : "A porta é um número de 1 a 65535.");
        if (!ok || port == _settings.Port) return;
        _settings.Port = port; Persist(); UpdateStatusCard();
    }

    void OnFolderChanged(object s, TextChangedEventArgs e)
    {
        if (_loading) return;
        var t = FolderBox.Text;
        var ok = t.Length == 0 || Directory.Exists(t);
        Validate(FolderBox, FolderHint, ok ? null : "Essa pasta não existe.");
        if (!ok || t == _settings.InputFolder) return;
        _settings.InputFolder = t; Persist();
    }

    void OnImageDirChanged(object s, TextChangedEventArgs e)
    {
        if (_loading) return;
        var t = ImageBox.Text;
        var ok = t.StartsWith('/') && !t.Any(char.IsControl);
        Validate(ImageBox, ImageHint, ok ? null : "Caminho no PS5, começando com / (ex.: /mnt/ext1/homebrew).");
        if (!ok || t == _settings.ImageDir) return;
        _settings.ImageDir = t; Persist();
    }

    void OnPwListChanged(object s, TextChangedEventArgs e)
    {
        if (_loading) return;
        _settings.KnownPasswords = [.. PwList.Text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0)];
        Persist();
    }

    // campos com binding (usuário, destino, conexões, apagar originais)
    void OnSourceUpdated(object s, DataTransferEventArgs e) => Persist();

    void UpdateStatusCard()
    {
        StatusHost.Text = $"{_settings.Host}:{_settings.Port}";
        StatusDest.Text = _settings.RemoteDir;
    }

    async Task<bool> TestConnection(bool silent)
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
            return true;
        }
        catch (Exception ex)
        {
            StatusTitle.Text = "PS5 offline"; StatusDot.Fill = (Brush)FindResource("Red");
            TestResult.Text = "Falhou: " + ex.Message; TestResult.Foreground = (Brush)FindResource("Red");
            Log("Teste de conexão falhou: " + ex.Message);
            return false;
        }
        finally { SideTestBtn.IsEnabled = TestBtn.IsEnabled = true; }
    }

    // Varre a /24 de cada interface IPv4 ativa procurando FTP do PS5 (2121 ftpsrv, 1337 etaHEN).
    static async Task<(string Ip, int Port)?> FindPs5()
    {
        var mine = new HashSet<string>();
        var prefixes = new HashSet<string>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var a in nic.GetIPProperties().UnicastAddresses)
            {
                if (a.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var ip = a.Address.ToString();
                mine.Add(ip);
                prefixes.Add(ip[..ip.LastIndexOf('.')]);
            }
        }
        var targets = new List<(string Ip, int Port)>();
        foreach (var p in prefixes)
            for (var i = 1; i < 255; i++)
                if (!mine.Contains($"{p}.{i}")) { targets.Add(($"{p}.{i}", 2121)); targets.Add(($"{p}.{i}", 1337)); }

        (string Ip, int Port)? found = null;
        using var stop = new CancellationTokenSource();
        try
        {
            await Parallel.ForEachAsync(targets, new ParallelOptions { MaxDegreeOfParallelism = 128, CancellationToken = stop.Token }, async (t, ct) =>
            {
                try
                {
                    using var c = new TcpClient();
                    using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    limit.CancelAfter(400);
                    await c.ConnectAsync(t.Ip, t.Port, limit.Token);
                    found ??= t;
                    stop.Cancel();
                }
                catch { } // fechada, sem resposta ou cancelada
            });
        }
        catch (OperationCanceledException) { }
        return found;
    }

    // Só oferece o que achou (FoundBar); nunca troca o IP sozinho.
    async Task Discover(bool silent)
    {
        FindBtn.IsEnabled = false;
        if (!silent) { TestResult.Text = "Procurando o PS5 na rede…"; TestResult.Foreground = (Brush)FindResource("Muted"); }
        var r = await Task.Run(FindPs5);
        FindBtn.IsEnabled = true;
        if (r is { } f && !(f.Ip == _settings.Host && f.Port == _settings.Port))
        {
            _found = f;
            FoundText.Text = $"Achei um PS5 em {f.Ip}:{f.Port}.";
            FoundBar.Visibility = Visibility.Visible;
            Log($"PS5 encontrado na rede: {f.Ip}:{f.Port}");
            if (!silent) TestResult.Text = $"Encontrado {f.Ip}:{f.Port}. Clique em \"Usar este\" na barra lateral.";
        }
        else
        {
            Log(r is null ? "Nenhum PS5 encontrado na rede" : "PS5 encontrado, mas é o IP já configurado");
            if (!silent) TestResult.Text = r is null ? "Nenhum PS5 encontrado na rede." : "Um servidor FTP responde no IP e na porta já configurados.";
        }
    }

    async void OnFind(object s, RoutedEventArgs e) => await Discover(silent: false);

    async void OnUseFound(object s, RoutedEventArgs e)
    {
        if (_found is not { } f) return;
        FoundBar.Visibility = Visibility.Collapsed;
        _settings.Host = f.Ip; _settings.Port = f.Port; Persist();
        LoadFields();
        await TestConnection(silent: false);
    }

    void OnDismissFound(object s, RoutedEventArgs e) => FoundBar.Visibility = Visibility.Collapsed;

    void Log(string msg)
    {
        FileLog.Write(msg);
        Dispatcher.BeginInvoke(() =>
        {
            LogBox.AppendText($"{DateTime.Now:HH:mm:ss}  {msg}\n");
            LogBox.ScrollToEnd();
        });
    }

    Task<string?> AskPassword(Job job) => Dispatcher.InvokeAsync(() =>
    {
        Toast("Senha necessária", $"\"{job.Name}\" precisa de senha.");
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
            Filter = "Jogos compactados ou imagem|*.zip;*.rar;*.7z;*.0*;*.z0*;*.z1*;*.r0*;*.r1*;*.exfat|Todos os arquivos|*.*",
            InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        };
        if (d.ShowDialog(this) != true) return;
        _engine.AddFiles(d.FileNames);
        Log($"{d.FileNames.Length} arquivo(s) selecionado(s)");
    }

    void OnPwChanged(object s, RoutedEventArgs e)
    {
        if (_settings.Password == PwBox.Password) return;
        _settings.Password = PwBox.Password; Persist();
    }

    void Rebind() { SettingsPanel.DataContext = null; SettingsPanel.DataContext = _settings; }

    void OnPreset(object s, SelectionChangedEventArgs e)
    {
        if (Preset.SelectedItem is ComboBoxItem { Tag: string path } && _settings.RemoteDir != path) { _settings.RemoteDir = path; Rebind(); Persist(); UpdateStatusCard(); }
    }

    void OnPickFolder(object s, RoutedEventArgs e)
    {
        var d = new OpenFolderDialog { Title = "Pasta monitorada" };
        if (d.ShowDialog() == true) FolderBox.Text = d.FolderName;
    }

    async void OnTest(object s, RoutedEventArgs e) => await TestConnection(silent: false);

    void OnCopyLog(object s, RoutedEventArgs e) { try { Clipboard.SetText(LogBox.Text); } catch { } }
    void OnClearLog(object s, RoutedEventArgs e) => LogBox.Clear();
}
