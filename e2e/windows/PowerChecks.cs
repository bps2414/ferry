using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ferry;

internal static partial class Program
{
    sealed class FakePowerService : IPowerService
    {
        public int Shutdowns;
        public bool Awake;
        public bool Fail;
        public void ShutdownThisPc() { if (Fail) throw new InvalidOperationException("fake failure"); Shutdowns++; }
        public void PreventSleep(bool enabled) { Awake = enabled; }
    }

    sealed class PowerClock : TimeProvider
    {
        long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        public void Advance(int seconds) => ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }

    static Job PowerJob(Stage stage = Stage.Enviando) => new() { Key = Guid.NewGuid().ToString(), Name = "Power fixture", Stage = stage };

    static void RunPower(string data)
    {
        // Failure contracts FIRST. No native power service is constructed in this harness.
        foreach (var stage in new[] { Stage.Verificado, Stage.PacotePronto, Stage.InstalacaoSolicitada })
        {
            var fake = new FakePowerService(); var clock = new PowerClock();
            var jobs = new ObservableCollection<Job>();
            using var session = new TransferPowerSession(jobs, new object(), fake, clock);
            Assert(!session.Arm(), "empty queue cannot arm");
            jobs.Add(PowerJob(stage));
            Assert(!session.Arm(), "restored terminal history cannot arm: " + stage);
            clock.Advance(120); session.Tick();
            Assert(fake.Shutdowns == 0, "history never shuts down");
        }
        foreach (var stage in new[] { Stage.Pausado, Stage.Erro, Stage.Cancelado, Stage.AguardandoPartes, Stage.NaFila, Stage.Extraindo, Stage.Enviando, Stage.SolicitandoInstalacao, Stage.VerifiqueNoPs5 })
        {
            var fake = new FakePowerService(); var clock = new PowerClock(); var job = PowerJob();
            var jobs = new ObservableCollection<Job> { job };
            using var session = new TransferPowerSession(jobs, new object(), fake, clock);
            Assert(session.Arm(), "live work can arm"); job.Stage = Stage.Verificado; session.Tick();
            clock.Advance(60); job.Stage = stage; session.Tick();
            Assert(fake.Shutdowns == 0 && session.RemainingSeconds == null, "final recheck blocks: " + stage);
        }
        foreach (var mutation in new[] { "remove", "clear", "add", "replace", "cancel", "close", "password", "resume", "request" })
        {
            var fake = new FakePowerService(); var clock = new PowerClock(); var job = PowerJob();
            var jobs = new ObservableCollection<Job> { job };
            var session = new TransferPowerSession(jobs, new object(), fake, clock);
            Assert(session.Arm(), "arm before " + mutation); job.Stage = Stage.Verificado; session.Tick();
            Assert(session.RemainingSeconds == 60, "full grace period"); clock.Advance(60);
            switch (mutation)
            {
                case "remove": jobs.Remove(job); break;
                case "clear": jobs.Clear(); break;
                case "add": jobs.Add(PowerJob(Stage.Verificado)); break;
                case "replace": jobs[0] = PowerJob(Stage.Verificado); break;
                case "cancel": session.Cancel(); break;
                case "close": session.Dispose(); break;
                case "password": session.PasswordPending = true; break;
                case "resume": job.Stage = Stage.NaFila; job.Stage = Stage.Verificado; break;
                case "request": job.Stage = Stage.SolicitandoInstalacao; job.Stage = Stage.InstalacaoSolicitada; break;
            }
            session.Tick();
            Assert(fake.Shutdowns == 0, "mutation invalidates elapsed countdown: " + mutation);
            if (mutation is "resume" or "request") Assert(session.RemainingSeconds == 60, "transient work restarts full 60 seconds");
            session.Dispose();
        }
        {
            var fake = new FakePowerService(); var clock = new PowerClock(); var a = PowerJob(); var b = PowerJob(Stage.Pausado);
            var jobs = new ObservableCollection<Job> { a, b };
            using var session = new TransferPowerSession(jobs, new object(), fake, clock);
            session.Arm(); a.Stage = Stage.Verificado; session.Tick(); clock.Advance(100); session.Tick();
            Assert(fake.Shutdowns == 0 && session.RemainingSeconds == null, "every observed job must succeed");
            b.Stage = Stage.Verificado; session.PasswordPending = true; session.Tick();
            Assert(session.RemainingSeconds == null, "password dialog independently blocks terminal queue");
            session.PasswordPending = false; session.Tick();
            Assert(session.RemainingSeconds == 60, "password resolved starts fresh countdown");
        }
        foreach (var stage in new[] { Stage.Verificado, Stage.PacotePronto, Stage.InstalacaoSolicitada })
        {
            var fake = new FakePowerService(); var clock = new PowerClock(); var job = PowerJob();
            var jobs = new ObservableCollection<Job> { job };
            using var session = new TransferPowerSession(jobs, new object(), fake, clock);
            Assert(!session.Armed, "default off"); session.Arm(); job.Stage = stage; session.Tick();
            clock.Advance(59); session.Tick(); Assert(fake.Shutdowns == 0 && session.RemainingSeconds == 1, "cannot act before 60s: " + stage);
            clock.Advance(1); session.Tick(); session.Tick();
            Assert(fake.Shutdowns == 1 && !session.Armed, "successful work acts once via fake: " + stage);
        }
        {
            var fake = new FakePowerService(); var job = PowerJob(); var clock = new PowerClock();
            var jobs = new ObservableCollection<Job> { job };
            using var session = new TransferPowerSession(jobs, new object(), fake, clock);
            session.KeepAwake = true; session.Tick(); Assert(fake.Awake, "sending inhibits sleep via fake");
            foreach (var stage in new[] { Stage.Pausado, Stage.Erro, Stage.Cancelado, Stage.AguardandoPartes, Stage.Verificado, Stage.SolicitandoInstalacao })
            { job.Stage = stage; session.Tick(); Assert(!fake.Awake, "sleep restored: " + stage); job.Stage = Stage.Enviando; session.Tick(); }
            session.KeepAwake = false; session.Tick(); Assert(!fake.Awake, "unchecking restores sleep");
            session.KeepAwake = true; session.Tick(); session.Dispose(); Assert(!fake.Awake, "closing restores sleep");
        }
        {
            var fake = new FakePowerService { Fail = true }; var job = PowerJob(); var clock = new PowerClock();
            using var session = new TransferPowerSession(new ObservableCollection<Job> { job }, new object(), fake, clock);
            session.Arm(); job.Stage = Stage.Verificado; session.Tick(); clock.Advance(60); session.Tick();
            Assert(fake.Shutdowns == 0 && !session.Armed && session.StatusKey == "app.powerFailed", "power failure is visible and never retries");
        }

        foreach (var arrival in new[] { "drop", "watch", "part", "unreadable" })
        {
            var folder = System.IO.Path.Combine(data, arrival); System.IO.Directory.CreateDirectory(folder);
            var settings = new Settings { InputFolder = arrival == "drop" ? "" : folder };
            var engine = new Engine(settings, _ => { }, _ => Task.FromResult<string?>(null));
            engine.QueueFile = System.IO.Path.Combine(folder, "queue.json");
            var file = System.IO.Path.Combine(folder, arrival == "part" ? "live.zip.001" : "live.exfat"); System.IO.File.WriteAllText(file, "fixture");
            var job = PowerJob(); job.Parts = [file];
            if (arrival != "drop") job = new Job { Key = Archives.Group([file]).Keys.Single(), Name = "source", Parts = [file], Stage = Stage.Enviando };
            engine.Jobs.Add(job);
            var sourcePower = new FakePowerService(); var clock = new PowerClock();
            using var session = new TransferPowerSession(engine.Jobs, engine.Lock, sourcePower, clock, engine.ObserveWork);
            Assert(session.Arm(), "arm source observation: " + arrival); job.Stage = Stage.Verificado; session.Tick(); clock.Advance(60);
            // Arrival immediately before the final check, without ever running ScanAsync.
            if (arrival == "unreadable") { System.IO.Directory.Delete(folder, true); }
            else
            {
                var incoming = System.IO.Path.Combine(folder, arrival == "part" ? "live.zip.002" : "incoming.exfat");
                System.IO.File.WriteAllText(incoming, "new work");
                if (arrival == "drop") engine.AddFiles([incoming]);
            }
            session.Tick();
            Assert(sourcePower.Shutdowns == 0, "invisible/unreadable source blocks deadline: " + arrival);
        }
        {
            var observation = new WorkObservation(0, "source", false); var fake = new FakePowerService(); var job = PowerJob(); var clock = new PowerClock();
            using var session = new TransferPowerSession(new ObservableCollection<Job> { job }, new object(), fake, clock, () => observation);
            session.Arm(); job.Stage = Stage.Verificado; session.Tick(); clock.Advance(60);
            observation = observation with { HasUnobservedWork = true };
            session.Tick(); Assert(fake.Shutdowns == 0, "final discovery check independently blocks");
        }
        {
            var folder = System.IO.Path.Combine(data, "terminal-overwrite"); System.IO.Directory.CreateDirectory(folder);
            var file = System.IO.Path.Combine(folder, "history.exfat"); System.IO.File.WriteAllText(file, "old source");
            var engine = new Engine(new Settings { InputFolder = folder }, _ => { }, _ => Task.FromResult<string?>(null));
            var history = new Job { Key = Archives.Group([file]).Keys.Single(), Name = "historical", Parts = [file], Stage = Stage.Verificado };
            var pending = PowerJob(); engine.Jobs.Add(history); engine.Jobs.Add(pending);
            var fake = new FakePowerService(); var clock = new PowerClock();
            using var session = new TransferPowerSession(engine.Jobs, engine.Lock, fake, clock, engine.ObserveWork);
            session.Arm(); System.IO.File.AppendAllText(file, "changed before countdown"); pending.Stage = Stage.Verificado;
            session.Tick(); clock.Advance(60); session.Tick();
            Assert(fake.Shutdowns == 0 && !session.Armed, "same-path terminal source changed after arming cannot masquerade as historical success");
        }
        {
            var fake = new FakePowerService(); var clock = new PowerClock(); var job = PowerJob();
            using var session = new TransferPowerSession(new ObservableCollection<Job> { job }, new object(), fake, clock);
            session.Arm(); job.Stage = Stage.Verificado; session.Tick();
            Assert(fake.Awake && !session.KeepAwake, "armed countdown keeps PC awake even without sending opt-in");
            session.Cancel(); session.Tick(); Assert(!fake.Awake, "cancel releases countdown sleep hold");
            job.Stage = Stage.Enviando; session.Arm(); job.Stage = Stage.Verificado; session.Tick();
            clock.Advance(60); session.Tick(); Assert(fake.Shutdowns == 1 && !fake.Awake, "final fake action releases countdown sleep hold");
        }
        {
            var folder = System.IO.Path.Combine(data, "automatic-original-deletion"); System.IO.Directory.CreateDirectory(folder);
            var root = System.IO.Path.Combine(folder, "ftp"); System.IO.Directory.CreateDirectory(root);
            var port = FreePort(); using var ftp = new FtpFixture(StartFtp(FindRepo(), root, port));
            WaitUntil(() => TcpReady(port), TimeSpan.FromSeconds(8), "power fixture local FTP startup");
            var source = System.IO.Path.Combine(folder, "original.exfat"); System.IO.File.WriteAllBytes(source, new byte[32768]);
            var settings = new Settings { Host = "127.0.0.1", Port = port, User = "ps5", Password = "ps5pass", ImageDir = "/images", DeleteOriginal = true };
            var engine = new Engine(settings, _ => { }, _ => Task.FromResult<string?>(null)) { StableSeconds = 0, QueueFile = System.IO.Path.Combine(folder, "queue.json") };
            engine.AddFiles([source]);
            ((Task)Invoke(engine, "ScanAsync")!).GetAwaiter().GetResult();
            ((Task)Invoke(engine, "ScanAsync")!).GetAwaiter().GetResult();
            var job = engine.Jobs.Single(); var fake = new FakePowerService(); var clock = new PowerClock();
            using var session = new TransferPowerSession(engine.Jobs, engine.Lock, fake, clock, engine.ObserveWork);
            Assert(session.Arm(), "arm before real local transfer with DeleteOriginal");
            var transfer = (Task)Invoke(engine, "ProcessAsync", job)!;
            WaitUntil(() => transfer.IsCompleted, TimeSpan.FromSeconds(15), "power fixture verified transfer and automatic deletion");
            transfer.GetAwaiter().GetResult();
            Assert(job.Stage == Stage.Verificado && !System.IO.File.Exists(source), "existing automatic DeleteOriginal preserved");
            session.Tick(); Assert(session.Armed && session.RemainingSeconds == 60, "automatic original deletion does not cancel countdown");
            clock.Advance(60); session.Tick(); Assert(fake.Shutdowns == 1 && !fake.Awake, "verified deleted original permits only fake shutdown");
        }
        foreach (var change in new[] { "revision", "source", "unseen" })
        {
            var fake = new FakePowerService(); var job = PowerJob(); var clock = new PowerClock();
            var due = false; var reads = 0;
            WorkObservation Observe()
            {
                var baseline = new WorkObservation(0, "source", false);
                if (!due || ++reads != 2) return baseline;
                return change switch
                {
                    "revision" => baseline with { Revision = 1 },
                    "source" => baseline with { SourceSignature = "new arrival" },
                    _ => baseline with { HasUnobservedWork = true }
                };
            }
            using var session = new TransferPowerSession(new ObservableCollection<Job> { job }, new object(), fake, clock, Observe);
            session.Arm(); job.Stage = Stage.Verificado; session.Tick(); clock.Advance(60); due = true;
            session.Tick(); Assert(reads == 2 && fake.Shutdowns == 0, "arrival between summary and action blocks: " + change);
        }

        new Settings { Host = "127.0.0.1", Language = "en" }.Save();
        var application = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown }; application.InitializeComponent();
        var power = new FakePowerService(); var time = new PowerClock();
        var window = new MainWindow(power, time, startEngine: false);
        application.MainWindow = window; window.Show();
        try
        {
            LocaleContracts();
            var engine = Field<Engine>(window, "_engine");
            var arm = Field<CheckBox>(window, "ShutdownAfterBox");
            var awake = Field<CheckBox>(window, "KeepAwakeBox");
            var cancel = Field<Button>(window, "CancelShutdownBtn");
            Assert(arm.IsChecked != true && !arm.IsEnabled && awake.IsChecked != true, "WPF session defaults and empty queue");
            var job = PowerJob(Stage.Pausado); lock (engine.Lock) engine.Jobs.Add(job);
            Invoke(window, "RefreshSummary"); arm.IsChecked = true;
            job.Stage = Stage.Verificado; Invoke(window, "RefreshSummary");
            foreach (var locale in new[] { "pt-BR", "en" })
            {
                SelectLanguage(window, locale);
                Assert(arm.IsChecked == true && cancel.Visibility == Visibility.Visible, "language preserves armed session and cancel: " + locale);
                var status = Field<TextBlock>(window, "PowerStatusText").Text;
                Assert(status.Contains("60") && status.Contains(locale == "en" ? "this PC" : "este PC", StringComparison.OrdinalIgnoreCase), "localized countdown identifies host: " + locale);
                Assert(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(cancel)), "cancel has accessible name");
                window.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                var capture = System.IO.Path.Combine(data, "power-" + locale + ".png");
                using (var output = System.IO.File.Create(capture)) encoder.Save(output);
                Console.WriteLine("CAPTURE fake-power countdown: " + capture);
            }
            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); time.Advance(120); Invoke(window, "RefreshSummary");
            Assert(power.Shutdowns == 0 && arm.IsChecked != true, "WPF cancel disarms");
            job.Stage = Stage.Enviando; Invoke(window, "RefreshSummary"); awake.IsChecked = true;
            Assert(power.Awake, "WPF keep awake uses injected fake");
            job.Stage = Stage.Pausado; Invoke(window, "RefreshSummary"); Assert(!power.Awake, "WPF pause releases sleep inhibition");
            arm.IsChecked = true; job.Stage = Stage.Verificado; Invoke(window, "RefreshSummary");
            window.WindowState = WindowState.Minimized; Pump();
            Invoke(window, "RefreshSummary"); Assert(window.IsVisible, "countdown restores hidden window for cancellation");
        }
        finally { window.Close(); time.Advance(120); Assert(power.Shutdowns == 0 && !power.Awake, "closing cancels power action and restores sleep"); application.Shutdown(); }
    }
}
