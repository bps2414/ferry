using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Ferry;

public partial class MainWindow : Window
{
    readonly Settings _settings = Settings.Load();
    readonly Engine _engine;
    readonly TransferPowerSession _powerSession;
    bool _renderingPower;
    bool _countdownShown;
    readonly Webhooks _webhooks;
    readonly CancellationTokenSource _stop = new();
    readonly System.Windows.Forms.NotifyIcon _tray = new();
    readonly List<(DateTime Time, Message Message)> _logs = [];
    WindowState _restoredState = WindowState.Normal;
    bool _loading; // preenchendo os campos por código: não conta como edição do usuário
    (string Ip, int Port)? _found; // PS5 achado na varredura, aguardando "Usar este"

    public MainWindow() : this(new WindowsPowerService()) { }

    public MainWindow(IPowerService power, TimeProvider? clock = null, bool startEngine = true)
    {
        _loading = true;
        WpfText.Current.Apply(_settings.Language);
        InitializeComponent();
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(WpfText.Current.Locale);
        SettingsPanel.DataContext = _settings;
        PwBox.Password = _settings.Password;
        LoadFields();
        foreach (ComboBoxItem i in Preset.Items) if ((string)i.Tag == _settings.RemoteDir) Preset.SelectedItem = i;
        _loading = true;
        foreach (ComboBoxItem item in LanguageBox.Items) if ((string)item.Tag == _settings.Language) LanguageBox.SelectedItem = item;
        _loading = false;

        // toast do Windows via balão da bandeja
        _tray.Icon = (Environment.ProcessPath is { } exe ? System.Drawing.Icon.ExtractAssociatedIcon(exe) : null) ?? System.Drawing.SystemIcons.Application;
        _tray.Visible = true;
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add(T("app.trayOpen"), null, (_, _) => Dispatcher.Invoke(RestoreWindow));
        menu.Items.Add(T("app.trayExit"), null, (_, _) => Dispatcher.Invoke(() => Close()));
        _tray.ContextMenuStrip = menu;
        _tray.Text = "Ferry";
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreWindow);
        _tray.BalloonTipClicked += (_, _) => Dispatcher.Invoke(RestoreWindow);
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized) Hide();
            else _restoredState = WindowState;
        };
        WpfText.Current.PropertyChanged += OnPresentationChanged;

        _webhooks = new Webhooks(_settings, Log, _stop.Token, automaticLocale: WpfText.Current.AutomaticLocale);
        _engine = new Engine(_settings, Log, job => { _webhooks.OnPassword(job); return AskPassword(job); }) { MessageLog = Log };
        _engine.Done = job =>
        {
            _webhooks.OnDone(job);
            Dispatcher.BeginInvoke(() =>
            {
                if (job.Stage == Stage.Verificado) Toast(new("core.webhook.completedTitle"), new("core.webhook.completedText", job.Title != "" ? job.Title : job.Name));
                else if (job.Stage == Stage.PacotePronto) Toast(new("ui.packageReadyTitle"), job.DetailMessage ?? new("ui.packageReadyText", job.Title != "" ? job.Title : job.Name));
                else if (job.Stage == Stage.InstalacaoSolicitada) Toast(new("ui.installationTitle"), new("ui.installationText", job.Title != "" ? job.Title : job.Name));
                else if (job.Stage == Stage.VerifiqueNoPs5) Toast(new("ui.installationUnknownTitle"), new("ui.installationUnknownText", job.Title != "" ? job.Title : job.Name));
                else Toast(new("core.webhook.errorTitle"), new("core.webhook.errorText", job.Name, Short(job.DetailMessage is { } detail ? WpfText.Current.Render(detail) : job.Detail)));
            });
        };
        BindingOperations.EnableCollectionSynchronization(_engine.Jobs, _engine.Lock);
        JobList.ItemsSource = _engine.Jobs;
        _engine.Restore();
        _powerSession = new(_engine.Jobs, _engine.Lock, power, clock, _engine.ObserveWork);

        // resumo da fila (contagens, velocidade total) 2x por segundo
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => RefreshSummary();
        timer.Start();
        RefreshSummary();
        UpdateStatusCard();

        if (startEngine) Task.Run(() => _engine.RunAsync(_stop.Token));
        RoutedEventHandler? initialLoad = null;
        initialLoad = async (_, _) => { Loaded -= initialLoad; if (!await TestConnection(silent: true)) await Discover(silent: true); };
        if (startEngine) Loaded += initialLoad;
        Closing += (_, _) =>
        {
            timer.Stop();
            _powerSession.Dispose();
            WpfText.Current.PropertyChanged -= OnPresentationChanged;
            _stop.Cancel(); _ = _webhooks.DisposeAsync();
            try { _settings.Save(); } catch { }
            _tray.Visible = false; _tray.Dispose(); menu.Dispose();
        };
    }

    static string T(string key, params object?[] args) => WpfText.Current.T(key, args);
    static void SetText(TextBlock block, string key, params object?[] args) => SetText(block, new Message(key, args));
    static void SetText(TextBlock block, Message message) => WpfText.Bind(block, TextBlock.TextProperty, message);

    void RestoreWindow()
    {
        WindowState = _restoredState;
        Show();
        Activate();
        foreach (Window owned in OwnedWindows) if (owned.IsVisible) owned.Activate();
    }

    void OnLanguageChanged(object s, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageBox.SelectedItem is not ComboBoxItem { Tag: string mode }) return;
        lock (_settings)
        {
            _settings.Language = mode;
            _settings.WebhookAutoLocale = WpfText.Current.AutomaticLocale;
        }
        WpfText.Current.Apply(mode);
        Persist();
    }

    void OnPresentationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WpfText.Locale)) return;
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(WpfText.Current.Locale);
        _tray.ContextMenuStrip!.Items[0].Text = T("app.trayOpen");
        _tray.ContextMenuStrip.Items[1].Text = T("app.trayExit");
        RefreshSummary();
        RenderLog();
    }

    // só avisa com a janela sem foco; chamar na thread da UI
    void Toast(Message title, Message text)
    {
        if (!IsActive) _tray.ShowBalloonTip(5000, WpfText.Current.Render(title), WpfText.Current.Render(text), System.Windows.Forms.ToolTipIcon.Info);
    }

    static string Short(string s)
    {
        var line = s.Split('\n')[0].Trim();
        return line.Length > 120 ? line[..120] + "…" : line;
    }

    void Persist()
    {
        try { _settings.Save(); }
        catch (Exception ex) { Log(new Message("app.saveFailed", Localization.ExceptionMessage(ex))); }
    }

    void RefreshSummary()
    {
        RefreshPower();
        List<Job> jobs;
        lock (_engine.Lock) jobs = [.. _engine.Jobs];
        int Count(params Stage[] s) => jobs.Count(j => s.Contains(j.Stage));
        var sending = Count(Stage.Extraindo, Stage.Enviando);
        var queued = Count(Stage.NaFila, Stage.AguardandoPartes, Stage.Pausado);
        var done = Count(Stage.Verificado);
        var requested = Count(Stage.InstalacaoSolicitada);
        var ready = Count(Stage.PacotePronto);
        var submitting = Count(Stage.SolicitandoInstalacao);
        var unknown = Count(Stage.VerifiqueNoPs5);
        var errors = Count(Stage.Erro);
        var parts = new List<string>();
        if (sending > 0) parts.Add(T("ui.sending", sending));
        if (queued > 0) parts.Add(T("ui.queued", queued));
        if (done > 0) parts.Add(T(done == 1 ? "ui.doneOne" : "ui.doneMany", done));
        if (requested > 0) parts.Add(T("ui.installationsRequested", requested));
        if (ready > 0) parts.Add(T("ui.packagesReady", ready));
        if (submitting > 0) parts.Add(T("ui.installationsSubmitting", submitting));
        if (unknown > 0) parts.Add(T("ui.installationsUnknown", unknown));
        if (errors > 0) parts.Add(T(errors == 1 ? "ui.errorsOne" : "ui.errorsMany", errors));
        SummaryText.Text = parts.Count == 0 ? T("ui.emptyQueue") : string.Join(" · ", parts);

        var rate = jobs.Where(j => j.IsActive).Sum(j => j.Rate);
        SpeedPill.Visibility = rate > 0 ? Visibility.Visible : Visibility.Collapsed;
        var sz = WpfText.Current.Size((long)rate).Split(' ');
        (SpeedText.Text, SpeedUnit.Text) = (sz[0], " " + sz[1] + "/s");
        ClearBtn.Visibility = done + requested > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyHint.Visibility = jobs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var pending = jobs.Count - done - requested;
        QueueBadge.Visibility = pending > 0 ? Visibility.Visible : Visibility.Collapsed;
        QueueBadgeText.Text = pending.ToString(WpfText.Current.Culture);
    }

    void RefreshPower()
    {
        _powerSession.Tick();
        _engine.HoldAutoClear = _powerSession.Armed;
        _renderingPower = true;
        ShutdownAfterBox.IsChecked = _powerSession.Armed;
        ShutdownAfterBox.IsEnabled = _powerSession.Armed || _powerSession.CanArm;
        KeepAwakeBox.IsChecked = _powerSession.KeepAwake;
        _renderingPower = false;
        SetText(PowerStatusText, _powerSession.StatusKey, _powerSession.RemainingSeconds);
        SetText(BannerText, _powerSession.StatusKey, _powerSession.RemainingSeconds);
        PowerBanner.Visibility = _powerSession.Armed ? Visibility.Visible : Visibility.Collapsed;
        if (_powerSession.RemainingSeconds != null && (!_countdownShown || !IsVisible)) RestoreWindow();
        _countdownShown = _powerSession.RemainingSeconds != null;
    }

    void OnShutdownAfterChanged(object s, RoutedEventArgs e)
    {
        if (_loading || _renderingPower) return;
        if (ShutdownAfterBox.IsChecked == true) _powerSession.Arm(); else _powerSession.Cancel();
        RefreshPower();
    }

    void OnKeepAwakeChanged(object s, RoutedEventArgs e)
    {
        if (_loading || _renderingPower) return;
        _powerSession.KeepAwake = KeepAwakeBox.IsChecked == true;
        RefreshPower();
    }

    void OnCancelShutdown(object s, RoutedEventArgs e) { _powerSession.Cancel(); RefreshPower(); }

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
        PkgBox.Text = _settings.PkgDir;
        DpiPortBox.Text = _settings.DpiPort.ToString(CultureInfo.InvariantCulture);
        WebhookUrlBox.Password = _settings.WebhookUrl;
        WebhookEnabledBox.IsChecked = _settings.WebhookEnabled;
        foreach (ComboBoxItem item in WebhookKindBox.Items) if ((string)item.Tag == _settings.WebhookKind) WebhookKindBox.SelectedItem = item;
        _loading = false;
        Validate(HostBox, HostHint, null); Validate(PortBox, PortHint, null); Validate(FolderBox, FolderHint, null); Validate(ImageBox, ImageHint, null);
        Validate(PkgBox, PkgHint, null); Validate(DpiPortBox, DpiPortHint, null);
    }

    void Validate(TextBox box, TextBlock hint, string? error)
    {
        SetText(hint, error ?? "core.raw", error == null ? [""] : []);
        hint.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
        if (error == null) box.ClearValue(Control.BorderBrushProperty);
        else box.BorderBrush = (Brush)FindResource("Red");
    }

    void OnHostChanged(object s, TextChangedEventArgs e)
    {
        if (_loading) return;
        var t = HostBox.Text;
        var error = t.Length == 0 || t.Any(char.IsWhiteSpace) ? "app.hostInvalid" : null;
        Validate(HostBox, HostHint, error);
        if (error != null || t == _settings.Host) return;
        _settings.Host = t; Persist(); UpdateStatusCard();
    }

    void OnPortChanged(object s, TextChangedEventArgs e)
    {
        if (_loading) return;
        var ok = int.TryParse(PortBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is >= 1 and <= 65535;
        Validate(PortBox, PortHint, ok ? null : "app.portInvalid");
        if (!ok || port == _settings.Port) return;
        _settings.Port = port; Persist(); UpdateStatusCard();
    }

    void OnFolderChanged(object s, TextChangedEventArgs e)
    {
        if (_loading) return;
        var t = FolderBox.Text;
        var ok = t.Length == 0 || Directory.Exists(t);
        Validate(FolderBox, FolderHint, ok ? null : "app.folderInvalid");
        if (!ok || t == _settings.InputFolder) return;
        _settings.InputFolder = t; Persist();
    }

    void OnImageDirChanged(object s, TextChangedEventArgs e)
    {
        if (_loading) return;
        var t = ImageBox.Text;
        var ok = t.StartsWith('/') && !t.Any(char.IsControl);
        Validate(ImageBox, ImageHint, ok ? null : "app.imageDirInvalid");
        if (!ok || t == _settings.ImageDir) return;
        _settings.ImageDir = t; Persist();
    }

    void OnPkgDirChanged(object s, TextChangedEventArgs e)
    {
        if (_loading) return;
        var path = PkgBox.Text;
        var ok = path.StartsWith('/') && !path.Any(char.IsControl) && !path.Split('/').Any(x => x is "." or "..");
        Validate(PkgBox, PkgHint, ok ? null : "ui.pkgDirInvalid");
        if (!ok || path == _settings.PkgDir) return;
        _settings.PkgDir = path; Persist();
    }

    void OnDpiPortChanged(object s, TextChangedEventArgs e)
    {
        if (_loading) return;
        var ok = int.TryParse(DpiPortBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is >= 1 and <= 65535;
        Validate(DpiPortBox, DpiPortHint, ok ? null : "app.portInvalid");
        if (!ok || port == _settings.DpiPort) return;
        _settings.DpiPort = port; Persist();
    }

    async void OnDpiTest(object s, RoutedEventArgs e)
    {
        if (HostHint.Visibility == Visibility.Visible || DpiPortHint.Visibility == Visibility.Visible)
        { SetText(DpiTestResult, "ui.dpiSaveFirst"); return; }
        DpiTestBtn.IsEnabled = false;
        SetText(DpiTestResult, "ui.dpiTesting");
        try
        {
            var result = await PkgInstaller.TestAsync(_settings, _stop.Token);
            SetText(DpiTestResult, result);
            DpiTestResult.Foreground = (Brush)FindResource("Green");
            Log(result);
        }
        catch (Exception ex)
        {
            SetText(DpiTestResult, "ui.dpiTestFailed", Localization.ExceptionMessage(ex));
            DpiTestResult.Foreground = (Brush)FindResource("Red");
        }
        finally { DpiTestBtn.IsEnabled = true; }
    }

    void OnRequestInstall(object s, RoutedEventArgs e)
    {
        if ((s as FrameworkElement)?.DataContext is not Job job || !job.CanRequestInstall) return;
        var unknown = job.Stage == Stage.VerifiqueNoPs5;
        if (unknown && MessageBox.Show(this, T("ui.installConfirm", job.Title != "" ? job.Title : job.Name),
            T("ui.installationUnknownTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        if (!_engine.RequestInstall(job, unknown)) Log(new Message("ui.installRequestFailed"));
    }

    void OnPwListChanged(object s, TextChangedEventArgs e)
    {
        if (_loading) return;
        _settings.KnownPasswords = [.. PwList.Text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0)];
        Persist();
    }

    bool SaveWebhook()
    {
        if (_loading || WebhookKindBox?.SelectedItem is not ComboBoxItem item || WebhookUrlBox == null) return false;
        var errors = Webhooks.UpdateSettings(_settings, JsonSerializer.SerializeToElement(new
        {
            webhookEnabled = WebhookEnabledBox.IsChecked == true,
            webhookKind = (string)item.Tag,
            webhookUrl = WebhookUrlBox.Password.Trim(),
            webhookAutoLocale = WpfText.Current.AutomaticLocale
        }));
        SetText(WebhookHint, errors.Values.FirstOrDefault() ?? new Message("core.raw", ""));
        WebhookHint.Visibility = errors.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        SetText(WebhookTestResult, "core.raw", "");
        if (errors.Count > 0) return false;
        try { _settings.Save(); return true; }
        catch
        {
            SetText(WebhookHint, "app.webhookSaveFailed");
            WebhookHint.Visibility = Visibility.Visible;
            return false;
        }
    }
    void OnWebhookUrlChanged(object s, RoutedEventArgs e) => SaveWebhook();
    void OnWebhookEnabledChanged(object s, RoutedEventArgs e) => SaveWebhook();
    void OnWebhookKindChanged(object s, SelectionChangedEventArgs e) => SaveWebhook();
    async void OnWebhookTest(object s, RoutedEventArgs e)
    {
        if (!SaveWebhook()) return;
        WebhookTestBtn.IsEnabled = false;
        SetText(WebhookTestResult, "ui.webhookTesting");
        try
        {
            var result = await _webhooks.TestAsync(_stop.Token);
            SetText(WebhookTestResult, result.Message);
            WebhookTestResult.Foreground = (Brush)FindResource(result.Ok ? "Green" : "Red");
        }
        finally { WebhookTestBtn.IsEnabled = true; }
    }

    // campos com binding (usuário, destino, conexões, apagar originais)
    void OnSourceUpdated(object s, DataTransferEventArgs e) => Persist();

    void UpdateStatusCard()
    {
        StatusHost.Text = $"{_settings.Host}:{_settings.Port}";
        StatusDest.Text = _settings.RemoteDir;
        EmptyDest.Text = $"PS5 · {_settings.Host}";
    }

    async Task<bool> TestConnection(bool silent)
    {
        UpdateStatusCard();
        SideTestBtn.IsEnabled = TestBtn.IsEnabled = false;
        SetText(StatusTitle, "ui.testing"); StatusDot.Fill = (Brush)FindResource("Amber");
        if (!silent) SetText(TestResult, "ui.testing");
        try
        {
            var msg = await Ftp.TestMessageAsync(_settings);
            SetText(StatusTitle, "ui.online"); StatusDot.Fill = (Brush)FindResource("Green");
            SetText(TestResult, msg); TestResult.Foreground = (Brush)FindResource("Green");
            Log(new Message("app.connectionTestLog", msg));
            return true;
        }
        catch (Exception ex)
        {
            SetText(StatusTitle, "ui.offline"); StatusDot.Fill = (Brush)FindResource("Red");
            SetText(TestResult, "app.connectionFailed", Localization.ExceptionMessage(ex)); TestResult.Foreground = (Brush)FindResource("Red");
            Log(new Message("app.connectionFailedLog", Localization.ExceptionMessage(ex)));
            return false;
        }
        finally { SideTestBtn.IsEnabled = TestBtn.IsEnabled = true; }
    }

    // Só oferece o que achou (FoundBar); nunca troca o IP sozinho.
    async Task Discover(bool silent)
    {
        FindBtn.IsEnabled = false;
        if (!silent) { SetText(TestResult, "ui.searching"); TestResult.Foreground = (Brush)FindResource("Muted"); }
        var r = await Task.Run(Discovery.FindPs5);
        FindBtn.IsEnabled = true;
        if (r is { } f && !(f.Ip == _settings.Host && f.Port == _settings.Port))
        {
            _found = f;
            SetText(FoundText, "ui.found", $"{f.Ip}:{f.Port}");
            FoundBar.Visibility = Visibility.Visible;
            Log(new Message("app.foundLog", $"{f.Ip}:{f.Port}"));
            if (!silent) SetText(TestResult, "app.foundHint", $"{f.Ip}:{f.Port}");
        }
        else
        {
            Log(new Message(r is null ? "app.notFound" : "app.foundConfiguredLog"));
            if (!silent) SetText(TestResult, r is null ? "app.notFound" : "app.foundConfigured");
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

    void Log(string msg) => Log(new Message("core.raw", msg));

    void Log(Message message)
    {
        var time = DateTime.Now;
        FileLog.Write(WpfText.Current.Render(message));
        Dispatcher.BeginInvoke(() =>
        {
            _logs.Add((time, message));
            LogBox.AppendText($"{time.ToString("HH:mm:ss", WpfText.Current.Culture)}  {WpfText.Current.Render(message)}\n");
            LogBox.ScrollToEnd();
        });
    }

    void RenderLog()
    {
        var start = LogBox.SelectionStart;
        var length = LogBox.SelectionLength;
        var offset = LogBox.VerticalOffset;
        LogBox.Text = string.Concat(_logs.Select(entry => $"{entry.Time.ToString("HH:mm:ss", WpfText.Current.Culture)}  {WpfText.Current.Render(entry.Message)}\n"));
        LogBox.Select(Math.Min(start, LogBox.Text.Length), Math.Min(length, Math.Max(0, LogBox.Text.Length - start)));
        LogBox.ScrollToVerticalOffset(offset);
    }

    async Task<string?> AskPassword(Job job)
    {
        _powerSession.PasswordPending = true;
        try { return await Dispatcher.InvokeAsync(() =>
    {
        Toast(new("core.webhook.passwordTitle"), new("core.webhook.passwordText", job.Name));
        RestoreWindow();
        var wrong = job.ArchivePassword != null;
        var box = new PasswordBox { Margin = new Thickness(0, 12, 0, 18) };
        var ok = new Button { IsDefault = true, MinWidth = 110, Style = (Style)FindResource("Primary") };
        var cancel = new Button { IsCancel = true, MinWidth = 110, Margin = new Thickness(8, 0, 0, 0), Style = (Style)FindResource("Btn") };
        WpfText.Bind(ok, ContentControl.ContentProperty, new("ui.extract"));
        WpfText.Bind(cancel, ContentControl.ContentProperty, new("ui.cancel"));
        var heading = new TextBlock { FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource(wrong ? "Red" : "Text") };
        var hint = new TextBlock { Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        SetText(heading, wrong ? "ui.wrongPassword" : "ui.protected");
        SetText(hint, wrong ? "ui.wrongPasswordHint" : "ui.passwordHint", job.Name);
        var w = new Window
        {
            Owner = this, Width = 440, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = (Brush)FindResource("Bg"),
            Content = new StackPanel
            {
                Margin = new Thickness(22),
                Children =
                {
                    heading, hint,
                    box,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
                },
            },
        };
        WpfText.Bind(w, Window.TitleProperty, new("ui.archivePassword"));
        ok.Click += (_, _) => w.DialogResult = true;
        w.Loaded += (_, _) => box.Focus();
        return w.ShowDialog() == true ? box.Password : null;
        }).Task; }
        finally { _powerSession.PasswordPending = false; }
    }

    static Job JobOf(object sender) => (Job)((FrameworkElement)sender).DataContext;
    void OnPause(object s, RoutedEventArgs e) => _engine.Pause(JobOf(s));
    void OnResume(object s, RoutedEventArgs e) => _engine.Resume(JobOf(s));
    void OnCancel(object s, RoutedEventArgs e) => _engine.Cancel(JobOf(s));
    void OnRetry(object s, RoutedEventArgs e) => _engine.Retry(JobOf(s));
    void OnRemove(object s, RoutedEventArgs e) => _engine.Remove(JobOf(s));
    void OnSendNow(object s, RoutedEventArgs e) => _engine.SendNow(JobOf(s));

    // barra desliza até o valor novo (o Job reporta 4x/s); voltar (tentar de novo) é imediato
    void OnProgress(object s, DataTransferEventArgs e)
    {
        if (s is not ProgressBar { Tag: double v } bar) return;
        var ms = v < bar.Value ? 0 : 350;
        bar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty,
            new System.Windows.Media.Animation.DoubleAnimation(v, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
    }
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
        Log(new Message("app.filesDropped", files.Length));
    }

    void OnPickFiles(object s, RoutedEventArgs e)
    {
        var d = new OpenFileDialog
        {
            Title = T("app.pickFiles"),
            Multiselect = true,
            Filter = T("app.fileFilter"),
            InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        };
        if (d.ShowDialog(this) != true) return;
        _engine.AddFiles(d.FileNames);
        Log(new Message("app.filesSelected", d.FileNames.Length));
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
        var d = new OpenFolderDialog { Title = T("ui.watchFolder") };
        if (d.ShowDialog() == true) FolderBox.Text = d.FolderName;
    }

    async void OnTest(object s, RoutedEventArgs e) => await TestConnection(silent: false);

    void OnCopyLog(object s, RoutedEventArgs e) { try { Clipboard.SetText(LogBox.Text); } catch { } }
    void OnClearLog(object s, RoutedEventArgs e) { _logs.Clear(); LogBox.Clear(); }
}
