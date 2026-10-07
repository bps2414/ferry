using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Ferry;

static class ResumeChecks
{
    static int checks;
    static void Check(bool ok, string name)
    {
        if (!ok) throw new InvalidOperationException("Resume: " + name);
        checks++;
        Console.WriteLine("OK " + name);
    }

    public static async Task RunAsync(string? section = null)
    {
        var root = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(root, "e2e"))) root = Path.GetDirectoryName(root)!;
        var work = Path.Combine(Path.GetTempPath(), "ferry-resume-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        Environment.SetEnvironmentVariable("FERRY_DATA", Path.Combine(work, "data"));
        Settings.FilePath = Path.Combine(work, "settings.json");
        FileLog.FilePath = Path.Combine(work, "log.txt");
        Secret.KeyFile = Path.Combine(work, "secret.key");
        Console.WriteLine("Isolated resume checks: " + work);
        if (section == null || section == "rate") await RateBaseline();
        foreach (var fault in new[] { "reset", "reject", "temporary", "denied", "noappe", "foreign", "replay", "cancel-replay" }.Where(f => section == null || f == section))
            await Recovery(root, work, fault);
        Console.WriteLine($"Resume checks passed: {checks}. Local FTP only.");
    }

    static async Task Recovery(string root, string work, string fault)
    {
        var folder = Directory.CreateDirectory(Path.Combine(work, fault)).FullName;
        var input = Directory.CreateDirectory(Path.Combine(folder, "input")).FullName;
        var remoteRoot = Directory.CreateDirectory(Path.Combine(folder, "ftp")).FullName;
        var remote = Directory.CreateDirectory(Path.Combine(remoteRoot, "images")).FullName;
        var source = Path.Combine(input, "Image.exfat");
        var payload = new byte[16 << 20];
        new Random(719).NextBytes(payload);
        File.WriteAllBytes(source, payload);
        var partial = Path.Combine(remote, "Image.exfat" + Engine.PartSuffix);
        File.WriteAllBytes(partial, payload[..(4 << 20)]);
        var partialPath = "/images/Image.exfat" + Engine.PartSuffix;
        var queue = Path.Combine(folder, "queue.json");
        File.WriteAllText(queue, JsonSerializer.Serialize(new
        {
            Dropped = new[] { source }, Removed = Array.Empty<string>(),
            Started = fault == "foreign" ? new Dictionary<string, Dictionary<string, long>>()
                : new Dictionary<string, Dictionary<string, long>> { [source] = new() { [partialPath] = payload.Length } }
        }));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var info = new ProcessStartInfo(OperatingSystem.IsWindows() ? "python" : "python3")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in new[] { Path.Combine(root, "e2e", "resume_ftpserver.py"), port.ToString(), remoteRoot, fault }) info.ArgumentList.Add(arg);
        using var server = Process.Start(info)!;
        var errors = server.StandardError.ReadToEndAsync();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        Task? runner = null;
        var logs = new List<string>();
        try
        {
            while (true)
            {
                stop.Token.ThrowIfCancellationRequested();
                try { using var client = new TcpClient(); await client.ConnectAsync(IPAddress.Loopback, port, stop.Token); break; }
                catch (SocketException) { await Task.Delay(50, stop.Token); }
            }
            var commandFile = Path.Combine(remoteRoot, "commands.txt");
            await Until(() => File.Exists(commandFile) && File.ReadAllLines(commandFile).LastOrDefault() == "CLOSE 0", stop.Token);
            var settings = new Settings { Host = "127.0.0.1", Port = port, User = "ps5", Password = "ps5pass", InputFolder = input, ImageDir = "/images", Connections = 3, DeleteOriginal = false };
            if (fault is "replay" or "cancel-replay")
            {
                await Replay(settings, payload, partial, partialPath, remoteRoot, fault == "cancel-replay", stop.Token);
                return;
            }
            var engine = new Engine(settings, m => { lock (logs) logs.Add(m); }, _ => Task.FromResult<string?>(null)) { QueueFile = queue };
            engine.Restore();
            runner = engine.RunAsync(stop.Token);
            Job? job;
            do
            {
                await Task.Delay(50, stop.Token);
                lock (engine.Lock) job = engine.Jobs.SingleOrDefault();
            } while (job?.Stage is not (Stage.Verificado or Stage.Erro));
            if (fault == "denied")
            {
                Check(job.Stage == Stage.Erro && File.Exists(partial) && File.ReadAllBytes(partial).SequenceEqual(payload[..(4 << 20)]), "permission denial preserves partial and reports error");
            }
            else
            {
                Check(job.Stage == Stage.Verificado, fault + " transfer completes");
                Check(SHA256.HashData(File.ReadAllBytes(Path.Combine(remote, "Image.exfat"))).SequenceEqual(SHA256.HashData(payload)), fault + " final content hash matches");
            }
            await Until(() => File.ReadAllLines(Path.Combine(remoteRoot, "commands.txt")).LastOrDefault() == "CLOSE 0", stop.Token);
            var commands = File.ReadAllLines(Path.Combine(remoteRoot, "commands.txt"));
            var stor = commands.Count(l => l.StartsWith("STOR "));
            var appe = commands.Count(l => l.StartsWith("APPE "));
            if (fault is "reset" or "temporary")
            {
                if (fault == "reset") Check(commands.Count(l => l.StartsWith("RESET ")) == 2, "two actual socket resets occurred during APPE");
                Check(stor == 0 && appe == 3, fault + " resumes every retry with APPE, never truncates with STOR");
                lock (logs) Check(!logs.Any(l => l.Contains("recusou APPE") || l.Contains("rejected APPE")), fault + " is not classified as unsupported append");
            }
            else if (fault == "denied") Check(stor == 0 && appe == 1, "permission denial never falls back to overwrite");
            else Check(stor == 1, fault + " safely falls back to full STOR");
            Check(commands.Where(l => l.StartsWith("OPEN ")).All(l => int.Parse(l[5..]) <= settings.Connections), fault + " retries do not leak idle FTP connections");
            Check(File.Exists(source), fault + " original preserved");
        }
        finally
        {
            stop.Cancel();
            if (runner != null) await runner;
            server.Kill(true); await server.WaitForExitAsync();
            var stderr = await errors;
            if (stderr.Contains("Traceback")) Console.WriteLine(stderr);
        }
    }

    static async Task Until(Func<bool> predicate, CancellationToken ct)
    {
        while (!predicate()) await Task.Delay(50, ct);
    }

    static async Task RateBaseline()
    {
        var job = new Job { Key = "rate", Name = "rate" };
        job.ResetRate();
        job.Report(10L << 30, 20L << 30);
        Check(job.Rate == 0 && job.DoneBytes == 10L << 30, "existing remote bytes are visible without invented transfer speed");
        await Task.Delay(1100);
        job.Report((10L << 30) + (1 << 20), 20L << 30);
        Check(job.Rate is > 0 and < 2 << 20, "speed measures only new bytes after resuming");
    }

    static async Task Replay(Settings settings, byte[] payload, string partial, string path, string remoteRoot, bool cancel, CancellationToken stop)
    {
        using var src = new GatedStream(payload);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stop);
        long reported = -1, reread = -1;
        var work = Ftp.StreamAsync(settings, src, [new Entry("Image.exfat", payload.Length, false, false)], [path], [4 << 20], true,
            [0], (done, _) => reported = done, _ => { }, _ => { }, _ => false, _ => { }, cts.Token,
            replayProgress: (done, _) => reread = done);
        try
        {
            await src.Reading.Task.WaitAsync(stop);
            Check(reported == 4 << 20, "partial bytes are credited before archive replay starts");
            Check(reread == 0, "archive replay reports preparation before blocking on decompression");
            var commands = File.ReadAllLines(Path.Combine(remoteRoot, "commands.txt"));
            Check(commands.Last() == "CLOSE 0", "archive replay opens no idle FTP sessions");
            if (cancel)
            {
                cts.Cancel();
                try { await work; throw new InvalidOperationException("Resume: replay ignored cancellation"); }
                catch (OperationCanceledException) { checks++; Console.WriteLine("OK archive replay remains cancellable"); }
                Check(File.ReadAllBytes(partial).SequenceEqual(payload[..(4 << 20)]), "cancel during replay leaves remote partial unchanged");
            }
            else
            {
                src.Release.TrySetResult();
                await work;
                Check(reread == 4 << 20 && reported == payload.Length, "replay reports completion without double-counting saved bytes");
                Check(File.ReadAllBytes(partial).SequenceEqual(payload), "non-seekable replay appends exact remaining content");
            }
        }
        finally { cts.Cancel(); src.Release.TrySetResult(); try { await work; } catch { } }
    }

    sealed class GatedStream(byte[] data) : MemoryStream(data)
    {
        public TaskCompletionSource Reading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanSeek => false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            Reading.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return await base.ReadAsync(buffer, ct);
        }
    }
}
