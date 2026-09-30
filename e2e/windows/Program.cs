using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Ferry;
using Localization = Ferry.Localization;

internal static class Program
{
    static int checks;

    [STAThread]
    static int Main()
    {
        var data = Path.Combine(Path.GetTempPath(), "ferry-windows-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        Environment.SetEnvironmentVariable("FERRY_DATA", data);
        try { Run(data); Console.WriteLine($"PASS: {checks} Windows checks; isolated data: {data}"); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); Console.Error.WriteLine($"FAIL; isolated data: {data}"); return 1; }
    }

    // Keep Settings' static initialization after FERRY_DATA is set, even under eager JIT initialization.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Run(string data)
    {
        Assert(Settings.AppDir == data, "isolated settings directory");
        var repo = FindRepo();
        var root = Path.Combine(data, "ftp");
        Directory.CreateDirectory(root);
        var port = FreePort();
        using var ftp = new FtpFixture(StartFtp(repo, root, port));
        WaitUntil(() => TcpReady(port), TimeSpan.FromSeconds(8), "local FTP startup");
        new Settings { Host = "127.0.0.1", Port = port, User = "ps5", Password = "ps5pass", ImageDir = "/images", Language = "en" }.Save();

        var application = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.InitializeComponent();
        var window = new MainWindow();
        application.MainWindow = window;
        window.Show();
        try
        {
            LocaleContracts();
            EditableFields(window, data);
            DpiGuide(window);
            RealTransfer(window, data, root);
            PackageTransfer(window, data, root);
            PasswordDialogs(window);
            Tray(window);
        }
        finally
        {
            window.Close();
            Assert(!window.IsVisible, "close exits the window");
            application.Shutdown();
        }
    }

    static void LocaleContracts()
    {
        Assert(WpfText.ResolveLocale("auto", "pt-PT") == "pt-BR", "Windows Portuguese maps to pt-BR");
        Assert(WpfText.ResolveLocale("auto", "en-GB") == "en", "Windows English maps to en");
        Assert(WpfText.ResolveLocale("auto", "ja-JP") == "en", "other Windows languages fall back to en");
        Assert(WpfText.ResolveLocale("en", "pt-BR") == "en" && WpfText.ResolveLocale("pt-BR", "en-US") == "pt-BR", "explicit language overrides Windows");
        var pt = Localization.Catalog("pt-BR");
        var en = Localization.Catalog("en");
        var keys = pt.Keys.Where(key => key.StartsWith("app.", StringComparison.Ordinal)).ToArray();
        Assert(keys.Length > 0, "WPF catalog loaded");
        Assert(keys.Order().SequenceEqual(en.Keys.Where(key => key.StartsWith("app.", StringComparison.Ordinal)).Order()), "WPF catalog key parity");
        foreach (var key in keys)
        {
            static string[] Parameters(string text) => Regex.Matches(text, @"(?<!\{)\{(\d+)(?:[^}]*)\}").Select(match => match.Groups[1].Value).Distinct().Order().ToArray();
            Assert(Parameters(pt[key]).SequenceEqual(Parameters(en[key])), "parameters: " + key);
        }
        WpfText.Current.Apply("pt-BR");
        Assert(WpfText.Current.T("core.size", 1572864L) == "1,5 MB", "Portuguese size formatting");
        Assert((12.5).ToString("0.0", WpfText.Current.Culture) == "12,5", "Portuguese numeric formatting");
        WpfText.Current.Apply("en");
        Assert(WpfText.Current.T("core.size", 1572864L) == "1.5 MB", "English size formatting");
        Assert((12.5).ToString("0.0", WpfText.Current.Culture) == "12.5", "English numeric formatting");
        var job = new Job { Key = "formatting", Name = "path ç.exfat", Stage = Stage.Enviando };
        job.Report(1572864, 3145728);
        typeof(Job).GetProperty(nameof(Job.SecondsRemaining))!.SetValue(job, 125.0);
        var converter = new JobTextConverter();
        foreach (var locale in new[] { "pt-BR", "en" })
        {
            WpfText.Current.Apply(locale);
            var amount = (string)converter.Convert([job], typeof(string), "Amount", WpfText.Current.Culture);
            Assert(amount.Contains(locale == "en" ? "1.5 MB" : "1,5 MB"), "amount converter follows locale: " + locale);
            Assert(Equals(converter.Convert([job], typeof(string), "EtaValue", WpfText.Current.Culture), "2:05"), "duration formatting: " + locale);
            Assert(Equals(converter.Convert([job], typeof(string), "EtaUnit", WpfText.Current.Culture), WpfText.Current.T("core.minutesRemaining")), "duration unit translated: " + locale);
            job.Detail = "550 FTP /raw/caminho ç.7z";
            Assert(Equals(converter.Convert([job], typeof(string), "Detail", WpfText.Current.Culture), job.Detail), "raw diagnostic/path preserved: " + locale);
        }
    }

    static void EditableFields(MainWindow window, string data)
    {
        Field<TextBox>(window, "HostBox").Text = "invalid host";
        Field<TextBox>(window, "PortBox").Text = "invalid-port";
        Field<TextBox>(window, "FolderBox").Text = Path.Combine(data, "does-not-exist");
        Field<TextBox>(window, "ImageBox").Text = "invalid-relative";
        Field<TextBox>(window, "PkgBox").Text = "../invalid-package-folder";
        Field<TextBox>(window, "DpiPortBox").Text = "not-a-port";
        Field<PasswordBox>(window, "WebhookUrlBox").Password = "invalid webhook secret";
        Field<TextBox>(window, "PwList").Text = "known one\nknown two";
        var settings = Field<Settings>(window, "_settings");
        var originalHost = settings.Host;
        foreach (var locale in new[] { "pt-BR", "en" })
        {
            SelectLanguage(window, locale);
            Assert(Field<TextBox>(window, "HostBox").Text == "invalid host" && settings.Host == originalHost, "invalid host edit preserved: " + locale);
            Assert(Field<TextBox>(window, "PortBox").Text == "invalid-port" && settings.Port > 0, "invalid port edit preserved: " + locale);
            Assert(Field<TextBox>(window, "FolderBox").Text.EndsWith("does-not-exist"), "invalid folder edit preserved: " + locale);
            Assert(Field<TextBox>(window, "ImageBox").Text == "invalid-relative", "invalid image path edit preserved: " + locale);
            Assert(Field<TextBox>(window, "PkgBox").Text == "../invalid-package-folder" && settings.PkgDir == "/data/etaHEN/pkgs", "invalid PKG path edit preserved: " + locale);
            Assert(Field<TextBox>(window, "DpiPortBox").Text == "not-a-port" && settings.DpiPort == 9090, "invalid DPI port edit preserved: " + locale);
            Assert(Field<PasswordBox>(window, "PwBox").Password == "ps5pass", "FTP password preserved: " + locale);
            Assert(Field<PasswordBox>(window, "WebhookUrlBox").Password == "invalid webhook secret", "webhook secret edit preserved: " + locale);
            Assert(Field<TextBox>(window, "PwList").Text == "known one\nknown two", "known passwords preserved: " + locale);
            Assert(Settings.Load().Language == locale, "language preference persisted: " + locale);
            var hint = Field<TextBlock>(window, "PortHint").Text;
            Assert(locale == "en" ? hint.Contains("port", StringComparison.OrdinalIgnoreCase) : hint.Contains("porta", StringComparison.OrdinalIgnoreCase), "validation translated immediately: " + locale);
        }
        Field<TextBox>(window, "ImageBox").Text = "/images";
        Field<TextBox>(window, "PkgBox").Text = "/data/etaHEN/pkgs";
        Field<TextBox>(window, "DpiPortBox").Text = "9090";
        SelectLanguage(window, "auto");
        Assert(Settings.Load().Language == "auto" && WpfText.Current.Locale == WpfText.ResolveLocale("auto", WpfText.Current.SystemLocale), "Automatic persisted and uses Windows culture");
    }

    static void DpiGuide(MainWindow window)
    {
        Field<RadioButton>(window, "NavSettings").IsChecked = true;
        var dpiHelp = Field<Expander>(window, "DpiHelp");
        dpiHelp.IsExpanded = true;
        foreach (var locale in new[] { "pt-BR", "en" })
        {
            SelectLanguage(window, locale);
            Pump();
            Assert(Equals(dpiHelp.Header, locale == "en" ? "How do I enable DPI?" : "Como ativar o DPI?"), "DPI activation guide title translated: " + locale);
            var guideText = string.Join("\n", Visuals<TextBlock>(dpiHelp).Select(b => b.Text));
            Assert(guideText.Contains("Toolbox > Services", StringComparison.Ordinal) && guideText.Contains("Direct Package Installer (9090)", StringComparison.Ordinal)
                && guideText.Contains("/data/etaHEN/config.ini", StringComparison.Ordinal) && guideText.Contains("DPI=1", StringComparison.Ordinal)
                && guideText.Contains("12800", StringComparison.Ordinal), "DPI activation guide contains console steps and separate V2: " + locale);
        }
        dpiHelp.IsExpanded = false;
        Field<RadioButton>(window, "NavQueue").IsChecked = true;
    }

    static void RealTransfer(MainWindow window, string data, string root)
    {
        var source = Path.Combine(data, "idioma e acentuação.exfat");
        var bytes = new byte[16 * 1024 * 1024];
        new Random(451).NextBytes(bytes);
        File.WriteAllBytes(source, bytes);
        var engine = Field<Engine>(window, "_engine");
        engine.StableSeconds = 0;
        engine.AddFiles([source]);
        Job? job = null;
        WaitUntil(() => { lock (engine.Lock) job = engine.Jobs.FirstOrDefault(); return job is { Stage: Stage.Enviando, DoneBytes: > 0 }; }, TimeSpan.FromSeconds(15), "real FTP transfer active");
        var original = job!;
        foreach (var locale in new[] { "pt-BR", "en" })
        {
            var progress = original.Progress;
            SelectLanguage(window, locale);
            Assert(ReferenceEquals(original, engine.Jobs.Single()), "same queue job during language change: " + locale);
            Assert(original.Stage == Stage.Enviando && original.Progress >= progress, "active transfer and progress preserved: " + locale);
            window.UpdateLayout();
            var text = string.Join("\n", Visuals<TextBlock>(window).Select(block => block.Text));
            Assert(text.Contains(locale == "en" ? "Sending" : "Enviando", StringComparison.OrdinalIgnoreCase), "active stage rendered in selected language: " + locale);
        }
        WaitUntil(() => original.Stage is Stage.Verificado or Stage.Erro, TimeSpan.FromSeconds(25), "real FTP transfer completion");
        Assert(original.Stage == Stage.Verificado, "transfer verified: " + original.Detail);
        var destination = Path.Combine(root, "images", Path.GetFileName(source));
        Assert(File.Exists(destination) && SHA256.HashData(File.ReadAllBytes(destination)).SequenceEqual(SHA256.HashData(bytes)), "FTP destination hash after switching languages");
        Assert(!Directory.EnumerateFiles(root, "*.ferry-part", SearchOption.AllDirectories).Any(), "atomic FTP publication preserved");
        Assert(File.ReadAllText(FileLog.FilePath).Contains("226 Transfer complete.", StringComparison.Ordinal), "raw FTP diagnostics preserved");
    }

    static void PackageTransfer(MainWindow window, string data, string root)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var stop = new CancellationTokenSource();
        var dpiPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = new System.Collections.Concurrent.ConcurrentQueue<(string Path, byte[] Hash)>();
        var loseResponse = false;
        var receiver = Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    using var client = await listener.AcceptTcpClientAsync(stop.Token);
                    var stream = client.GetStream();
                    var buffer = new byte[1024];
                    var count = await stream.ReadAsync(buffer, stop.Token);
                    if (count == 0) continue;
                    using var json = System.Text.Json.JsonDocument.Parse(buffer.AsMemory(0, count));
                    var path = json.RootElement.GetProperty("url").GetString()!;
                    var file = Path.Combine(root, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                    received.Enqueue((file, SHA256.HashData(File.ReadAllBytes(file))));
                    if (!loseResponse) await stream.WriteAsync("{\"res\":\"0\"}"u8.ToArray(), stop.Token);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (SocketException) when (stop.IsCancellationRequested) { }
        });
        try
        {
            var engine = Field<Engine>(window, "_engine");
            var settings = Field<Settings>(window, "_settings");
            Field<TextBox>(window, "DpiPortBox").Text = dpiPort.ToString(CultureInfo.InvariantCulture);
            Assert(settings.DpiPort == dpiPort && Settings.Load().DpiPort == dpiPort, "DPI port auto-saves in WPF");
            settings.DeleteOriginal = true;
            var bytes = new byte[1_000_000];
            new Random(611).NextBytes(bytes);
            bytes[0] = 0x7f; bytes[1] = (byte)'C'; bytes[2] = (byte)'N'; bytes[3] = (byte)'T';
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(0x430, 8), (ulong)bytes.Length);
            var source = Path.Combine(data, "Windows package.pkg");
            File.WriteAllBytes(source, bytes);
            engine.AddFiles([source]);
            Job? job = null;
            WaitUntil(() => { lock (engine.Lock) job = engine.Jobs.FirstOrDefault(j => j.Name == "Windows package"); return job?.Stage is Stage.PacotePronto or Stage.Erro; }, TimeSpan.FromSeconds(25), "WPF package upload");
            Assert(job!.Stage == Stage.PacotePronto && received.IsEmpty && !settings.AutoInstallPackages, "WPF defaults to upload only, zero DPI requests");
            var readyFile = Path.Combine(root, job.Package!.RemotePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            Assert(Path.GetDirectoryName(readyFile) == Path.Combine(root, "data", "etaHEN", "pkgs") && File.Exists(readyFile), "WPF publishes PKG directly in etaHEN default folder");
            Assert(job.Detail.Contains(job.Package.RemotePath, StringComparison.Ordinal) && job.Detail.Contains("Custom PKG Search Path", StringComparison.Ordinal), "WPF ready detail gives full path and manual installer guidance");
            foreach (var locale in new[] { "pt-BR", "en" })
            {
                SelectLanguage(window, locale);
                window.UpdateLayout();
                Assert(Visuals<TextBlock>(window).Any(b => b.Text == (locale == "en" ? "PKG uploaded" : "PKG enviado")), "manual ready state translated in WPF: " + locale);
            }
            Field<CheckBox>(window, "AutoInstallPackagesBox").IsChecked = true;
            Pump();
            Assert(settings.AutoInstallPackages && Settings.Load().AutoInstallPackages, "WPF automatic DPI mode saves explicitly");
            SelectLanguage(window, "pt-BR"); SelectLanguage(window, "en");
            Assert(Field<CheckBox>(window, "AutoInstallPackagesBox").IsChecked == true && settings.AutoInstallPackages, "automatic DPI mode survives language switch");
            var requestButton = new Button { DataContext = job };
            Invoke(window, "OnRequestInstall", requestButton, new RoutedEventArgs());
            WaitUntil(() => job.Stage is Stage.InstalacaoSolicitada or Stage.Erro, TimeSpan.FromSeconds(15), "explicit WPF DPI request");
            Assert(job!.Stage == Stage.InstalacaoSolicitada, "WPF package accepted: " + job.Detail);
            Assert(received.Count == 1 && received.Single().Hash.SequenceEqual(SHA256.HashData(bytes)), "WPF package hash before DPI");
            Assert(File.Exists(source), "WPF retains PKG original despite DeleteOriginal");
            foreach (var locale in new[] { "pt-BR", "en" })
            {
                SelectLanguage(window, locale);
                window.UpdateLayout();
                Assert(Visuals<TextBlock>(window).Any(b => b.Text == (locale == "en" ? "Installation requested" : "Instalação solicitada")), "honest package state in WPF: " + locale);
                Assert(!job.CanPause && !job.CanCancel && !job.CanRetry && !job.CanRequestInstall, "accepted package disables transfer/retry actions: " + locale);
            }
            loseResponse = true;
            var archive = Path.Combine(data, "Windows archive.zip");
            using (var zip = System.IO.Compression.ZipFile.Open(archive, System.IO.Compression.ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("inside.pkg", System.IO.Compression.CompressionLevel.NoCompression);
                using var output = entry.Open(); output.Write(bytes);
            }
            engine.AddFiles([archive]);
            Job? archived = null;
            WaitUntil(() => { lock (engine.Lock) archived = engine.Jobs.FirstOrDefault(j => j.Name == "Windows archive"); return archived?.Stage is Stage.VerifiqueNoPs5 or Stage.Erro; }, TimeSpan.FromSeconds(25), "WPF uncertain package archive");
            Assert(archived!.Stage == Stage.VerifiqueNoPs5 && archived.CanRequestInstall && !archived.CanPause && !archived.CanCancel, "lost DPI response exposes confirmed manual action");
            Assert(!engine.RequestInstall(archived), "uncertain request requires explicit confirmation");
            Assert(received.Count == 2 && received.Last().Hash.SequenceEqual(SHA256.HashData(bytes)) && File.Exists(archive), "WPF streams archive without losing the original");
            settings.DeleteOriginal = false;
        }
        finally
        {
            stop.Cancel();
            listener.Stop();
            receiver.GetAwaiter().GetResult();
        }
    }

    static void PasswordDialogs(MainWindow window)
    {
        foreach (var wrong in new[] { false, true })
        {
            SelectLanguage(window, "pt-BR");
            window.WindowState = WindowState.Minimized;
            Pump();
            var job = new Job { Key = "password-test", Name = "arquivo ç.7z", ArchivePassword = wrong ? "wrong" : null };
            Exception? callbackError = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += (_, _) =>
            {
                var dialog = window.OwnedWindows.Cast<Window>().FirstOrDefault();
                if (dialog == null) return;
                timer.Stop();
                try
                {
                    Assert(window.IsVisible && window.WindowState != WindowState.Minimized, "password request restores tray window");
                    var input = Visuals<PasswordBox>(dialog).Single();
                    input.Password = "digitada ç 123";
                    var title = dialog.Title;
                    SelectLanguage(window, "en");
                    Assert(ReferenceEquals(dialog, window.OwnedWindows.Cast<Window>().Single()), "same open password dialog");
                    Assert(input.Password == "digitada ç 123", "typed archive password preserved");
                    Assert(dialog.Title != title && dialog.Title.Contains("password", StringComparison.OrdinalIgnoreCase), "open password dialog title translated");
                    Assert(Visuals<Button>(dialog).Any(button => Equals(button.Content, "Cancel")), "open password dialog buttons translated");
                    SelectLanguage(window, "pt-BR");
                    Assert(input.Password == "digitada ç 123" && dialog.Title == title, "password preserved on reverse switch");
                    dialog.DialogResult = true;
                }
                catch (Exception error) { callbackError = error; dialog.Close(); }
            };
            timer.Start();
            var answer = (Task<string?>)Invoke(window, "AskPassword", job)!;
            WaitUntil(() => answer.IsCompleted, TimeSpan.FromSeconds(6), "password dialog completion");
            timer.Stop();
            if (callbackError != null) throw callbackError;
            Assert(answer.GetAwaiter().GetResult() == "digitada ç 123", "password result preserved: wrong=" + wrong);
        }
    }

    static void Tray(MainWindow window)
    {
        foreach (var state in new[] { WindowState.Normal, WindowState.Maximized })
        {
            window.WindowState = state;
            Pump();
            window.WindowState = WindowState.Minimized;
            Pump();
            Assert(!window.IsVisible, "minimize hides window: " + state);
            Invoke(window, "RestoreWindow");
            Pump();
            Assert(window.IsVisible && window.WindowState == state, "tray restores original window state: " + state);
        }
    }

    static void SelectLanguage(MainWindow window, string locale)
    {
        var combo = Field<ComboBox>(window, "LanguageBox");
        combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().Single(item => Equals(item.Tag, locale));
        Pump();
        Assert(WpfText.Current.Locale == WpfText.ResolveLocale(locale, WpfText.Current.SystemLocale), "selected locale applied: " + locale);
    }

    static T Field<T>(object instance, string name) => (T)(instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(instance) ?? throw new InvalidOperationException("Missing field " + name));
    static object? Invoke(object instance, string name, params object[] args) => instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(instance, args);
    static IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Visuals<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    static void Assert(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        checks++;
        if (!description.StartsWith("parameters:", StringComparison.Ordinal)) Console.WriteLine("PASS " + description);
    }
    static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    static void WaitUntil(Func<bool> predicate, TimeSpan timeout, string description)
    {
        var watch = Stopwatch.StartNew();
        while (!predicate())
        {
            if (watch.Elapsed > timeout) throw new TimeoutException(description);
            Pump();
            Thread.Sleep(20);
        }
        Pump();
    }
    static string FindRepo()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "e2e", "ftpserver.py"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Run from the repository root");
    }
    static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
    static bool TcpReady(int port)
    {
        try { using var client = new TcpClient(); client.Connect(IPAddress.Loopback, port); return true; }
        catch (SocketException) { return false; }
    }
    static Process StartFtp(string repo, string root, int port)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("FERRY_PYTHON") ?? "python") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { Path.Combine(repo, "e2e", "ftpserver.py"), port.ToString(CultureInfo.InvariantCulture), root, "appe", "2" }) start.ArgumentList.Add(argument);
        var process = Process.Start(start)!;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    sealed class FtpFixture(Process process) : IDisposable
    {
        public void Dispose()
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            finally { process.Dispose(); }
        }
    }
}
