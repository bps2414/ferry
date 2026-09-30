using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ferry;

// Real Engine + 7-Zip + local FTP. Only the console's DPI endpoint is simulated.
static class PkgChecks
{
    static int _checks;
    static string _root = "", _work = "", _sevenZip = "";

    public static async Task RunAsync(string? section = null)
    {
        _root = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(_root, "e2e"))) _root = Path.GetDirectoryName(_root)!;
        _work = Path.Combine(Path.GetTempPath(), "ferry-pkg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_work);
        Environment.SetEnvironmentVariable("FERRY_DATA", Path.Combine(_work, "data"));
        Settings.FilePath = Path.Combine(_work, "settings.json");
        FileLog.FilePath = Path.Combine(_work, "log.txt");
        Secret.KeyFile = Path.Combine(_work, "secret.key");
        if (OperatingSystem.IsWindows()) Archives.SevenZipPath = Path.Combine(_root, "app", "tools", "7z.exe");
        _sevenZip = Archives.SevenZip();
        Console.WriteLine("PKG isolated work directory: " + _work);

        // Failure contracts are exercised before success cases. Sections permit focused reruns.
        (string name, Func<Task> run)[] sections = [("inputs", InvalidInputs), ("archives", ArchiveFailures),
            ("publication", PublicationFailures), ("formats", PackagesAndImages), ("dpi", DpiFailuresAndRetry),
            ("persistence", PersistenceAndConcurrency), ("pause", PauseResumeIdentity)];
        if (section != null && !sections.Any(s => s.name == section)) throw new ArgumentException("Unknown PKG section: " + section);
        foreach (var test in sections.Where(s => section == null || section == s.name)) await test.run();
        Console.WriteLine($"PKG {section ?? "all"} checks passed: {_checks}. Fake FTP/DPI only; no PS5 installation claimed.");
    }

    static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("PKG: " + name);
        _checks++;
        Console.WriteLine("OK " + name);
    }

    static byte[] Payload(string magic = "CNT", int size = 1_048_576)
    {
        var bytes = new byte[size];
        new Random(601).NextBytes(bytes);
        bytes[0] = 0x7f;
        Encoding.ASCII.GetBytes(magic).CopyTo(bytes, 1);
        if (magic == "CNT" && size >= 0x438) BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(0x430, 8), (ulong)size);
        return bytes;
    }

    static string Put(string dir, string name, byte[] bytes)
    {
        var path = Path.Combine(dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    static async Task InvalidInputs()
    {
        foreach (var (name, bytes) in new[]
        {
            ("invalid.pkg", Encoding.ASCII.GetBytes("not a package")),
            ("truncated.pkg", new byte[] { 0x7f, 0x43, 0x4e, 0x54 }),
            ("bad-length.pkg", Payload())
        })
        {
            if (name == "bad-length.pkg") BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(0x430, 8), (ulong)bytes.Length + 1);
            await using var s = await Scenario.Create(name);
            var path = Put(s.Input, name, bytes);
            s.Engine.AddFiles([path]);
            var job = await s.WaitJob();
            Check(job.Stage == Stage.Erro, name + " rejected");
            Check(s.RemoteFiles().Length == 0 && s.Dpi.Count == 0, name + " never transfers or submits");
        }
    }

    static async Task ArchiveFailures()
    {
        foreach (var kind in new[] { "multiple", "mixed-image", "mixed-dump" })
        {
            await using var s = await Scenario.Create(kind);
            var src = Directory.CreateDirectory(Path.Combine(s.Folder, "source")).FullName;
            Put(src, "Game.pkg", Payload());
            if (kind == "multiple") Put(src, "Update.pkg", Payload("FIH"));
            if (kind == "mixed-image") Put(src, "Shadow.ffpkg", Payload("FIH"));
            if (kind == "mixed-dump")
            {
                Put(src, "PPSA12345/eboot.bin", [1, 2, 3]);
                Put(src, "PPSA12345/sce_sys/param.json", Encoding.UTF8.GetBytes("{\"titleId\":\"PPSA12345\"}"));
            }
            var archive = Path.Combine(s.Input, kind + ".zip");
            await Tool(_sevenZip, src, "a", "-tzip", "-mx0", archive, ".");
            s.Engine.AddFiles([archive]);
            var job = await s.WaitJob();
            Check(job.Stage == Stage.Erro && job.Detail.Length > 0, kind + " explicit rejection");
            Check(s.RemoteFiles().Length == 0 && s.Dpi.Count == 0, kind + " no selected payload sent");
        }
        await using (var s = await Scenario.Create("password-refused", _ => Task.FromResult<string?>(null)))
        {
            var src = Directory.CreateDirectory(Path.Combine(s.Folder, "source")).FullName;
            Put(src, "Game.pkg", Payload());
            var archive = Path.Combine(s.Input, "encrypted.7z");
            await Tool(_sevenZip, src, "a", "-t7z", "-psecret", "-mhe=on", archive, ".");
            s.Engine.AddFiles([archive]);
            var job = await s.WaitJob();
            Check(job.Stage is Stage.Cancelado or Stage.Erro, "password refusal stops package");
            Check(s.RemoteFiles().Length == 0 && s.Dpi.Count == 0, "password refusal never transfers or submits");
        }
        await using (var s = await Scenario.Create("missing-volume"))
        {
            var src = Directory.CreateDirectory(Path.Combine(s.Folder, "source")).FullName;
            Put(src, "Game.pkg", Payload());
            await Tool(_sevenZip, src, "a", "-t7z", "-mx0", "-v200000b", Path.Combine(s.Input, "split.7z"), ".");
            File.Delete(Path.Combine(s.Input, "split.7z.002"));
            s.Engine.AddFiles(Directory.GetFiles(s.Input));
            await s.Wait(j => j.Stage == Stage.AguardandoPartes && j.Detail.Length > 0);
            Check(s.RemoteFiles().Length == 0 && s.Dpi.Count == 0, "missing volume stays waiting without upload or DPI");
        }
    }

    static async Task PackagesAndImages()
    {
        foreach (var magic in new[] { "CNT", "FIH" })
        {
            await using var s = await Scenario.Create("loose-" + magic);
            s.Settings.DeleteOriginal = true;
            s.Dpi.Reply = "fragmented";
            var path = Put(s.Input, "Game " + magic + ".PKG", Payload(magic));
            var original7z = Archives.SevenZipPath;
            Archives.SevenZipPath = Path.Combine(s.Folder, "7z-deliberately-absent.exe");
            try
            {
                s.Engine.AddFiles([path]);
                var job = await s.WaitJob();
                Check(job.Stage == Stage.InstalacaoSolicitada && !job.Installed, magic + " requests installation without claiming installed");
                AssertPublished(s, path);
                Check(File.Exists(path), "DeleteOriginal preserves loose " + magic);
            }
            finally { Archives.SevenZipPath = original7z; }
        }
        foreach (var kind in new[] { "zip", "7z", "7z-encrypted-split", "zip-split", "rar", "rar-split" })
        {
            await using var s = await Scenario.Create("archive-" + kind, _ => Task.FromResult<string?>("secret"));
            s.Settings.DeleteOriginal = true;
            var src = Directory.CreateDirectory(Path.Combine(s.Folder, "source")).FullName;
            var original = Put(src, "Sub/Game.pkg", Payload());
            Put(src, "README.txt", Encoding.UTF8.GetBytes("A companion is not a second game."));
            Put(src, "cover.png", [1, 2, 3, 4]);
            if (kind.StartsWith("rar"))
            {
                var rar = Path.Combine(_root, "e2e", "tools", OperatingSystem.IsWindows() ? "Rar.exe" : "rar");
                if (!File.Exists(rar)) throw new InvalidOperationException("RAR fixture generator unavailable: " + rar);
                var options = new List<string> { "a", "-m0", "-r", "-idq" };
                if (kind == "rar-split") options.Add("-v200000b");
                options.Add(Path.Combine(s.Input, "Game.rar")); options.Add(".");
                await Tool(rar, src, [.. options]);
            }
            else if (kind == "zip-split") SplitZip.Write(src, Path.Combine(s.Input, "Game"), 200_000);
            else
            {
                var ext = kind.StartsWith("zip") ? "zip" : "7z";
                var options = new List<string> { "a", "-t" + ext, "-mx0" };
                if (kind.Contains("encrypted")) options.AddRange(["-psecret", "-mhe=on", "-v200000b"]);
                options.Add(Path.Combine(s.Input, "Game." + ext)); options.Add(".");
                await Tool(_sevenZip, src, [.. options]);
            }
            var parts = Directory.GetFiles(s.Input);
            s.Engine.AddFiles(parts);
            var job = await s.WaitJob();
            Check(job.Stage == Stage.InstalacaoSolicitada, kind + " accepted from extraction stream");
            AssertPublished(s, original);
            Check(parts.All(File.Exists), "DeleteOriginal preserves " + kind + " volumes");
            Check(Directory.GetFiles(s.Input, "*.pkg", SearchOption.AllDirectories).Length == 0, kind + " does not extract PKG beside archive");
        }
        foreach (var ext in new[] { "ffpkg", "ffpfs", "ffpfsc" })
        foreach (var archived in new[] { false, true })
        {
            await using var s = await Scenario.Create("image-" + ext + "-" + archived);
            var original = Put(archived ? Path.Combine(s.Folder, "source") : s.Input, "Shadow." + ext, Payload("FIH"));
            var added = original;
            if (archived)
            {
                added = Path.Combine(s.Input, "Shadow.zip");
                await Tool(_sevenZip, Path.GetDirectoryName(original)!, "a", "-tzip", "-mx0", added, Path.GetFileName(original));
            }
            s.Engine.AddFiles([added]);
            var job = await s.WaitJob();
            var target = Path.Combine(s.FtpRoot, "images", Path.GetFileName(original));
            Check(job.Stage == Stage.Verificado && File.Exists(target) && Hash(original) == Hash(target), ext + (archived ? " archive" : " loose") + " image uses ImageDir and preserves bytes");
            Check(s.Dpi.Count == 0 && !s.RemoteFiles().Any(p => p.EndsWith(Engine.PartSuffix)), "image publishes without DPI or partial leftovers");
        }
    }

    static async Task PublicationFailures()
    {
        await using (var s = await Scenario.Create("archive-crc"))
        {
            var src = Directory.CreateDirectory(Path.Combine(s.Folder, "source")).FullName;
            Put(src, "Game.pkg", Payload());
            var zip = Path.Combine(s.Input, "Corrupt.zip");
            await Tool(_sevenZip, src, "a", "-tzip", "-mx0", zip, ".");
            var bytes = File.ReadAllBytes(zip);
            var payloadAt = bytes.AsSpan().IndexOf(new byte[] { 0x7f, 0x43, 0x4e, 0x54 });
            if (payloadAt < 0) throw new InvalidOperationException("Stored ZIP fixture lost package header");
            bytes[payloadAt + 8192] ^= 0x5a; File.WriteAllBytes(zip, bytes);
            s.Engine.AddFiles([zip]);
            var job = await s.WaitJob();
            Check(job.Stage == Stage.Erro && s.Dpi.Count == 0 && !s.RemoteFiles().Any(p => p.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase)), "extractor CRC failure never publishes or installs");
        }
        await using (var s = await Scenario.Create("rename-rejected"))
        {
            string? target = null;
            s.Engine.Jobs.CollectionChanged += (_, ev) =>
            {
                if (ev.NewItems == null) return;
                foreach (Job job in ev.NewItems)
                    job.PropertyChanged += (_, change) =>
                    {
                        if (change.PropertyName != nameof(Job.Stage) || job.Stage != Stage.Enviando || target != null) return;
                        target = Path.Combine(s.FtpRoot, job.Package!.RemotePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(target); // Inject before bytes start; no timing-dependent network race.
                    };
            };
            s.Engine.AddFiles([Put(s.Input, "Game.pkg", Payload())]);
            await s.Wait(j => j.Stage == Stage.Erro);
            Check(target != null && Directory.Exists(target) && !File.Exists(target) && s.Dpi.Count == 0, "failed final rename never sends DPI");
        }
    }

    static void AssertPublished(Scenario s, string original)
    {
        var files = s.RemoteFiles();
        Check(files.Length == 1 && files[0].EndsWith(".pkg", StringComparison.OrdinalIgnoreCase) && Hash(files[0]) == Hash(original), "FTP publishes exact PKG bytes only");
        Check(files[0].StartsWith(Path.Combine(s.FtpRoot, "packages"), StringComparison.OrdinalIgnoreCase), "PKG uses PkgDir");
        Check(s.Dpi.Count == 1 && s.Dpi.PublicationValid && s.Dpi.HashAtRequest == Hash(original), "DPI observes final file and hash after rename, never partial");
        Check(s.Dpi.DurableSubmitting, "submitting state is durable before DPI receives request");
    }

    static async Task DpiFailuresAndRetry()
    {
        foreach (var reply in new[] { "rejected", "malformed", "lost", "oversized", "timeout", "string" })
        {
            await using var s = await Scenario.Create("dpi-" + reply);
            s.Settings.DeleteOriginal = true;
            s.Dpi.Reply = reply;
            var path = Put(s.Input, "Game.pkg", Payload());
            s.Engine.AddFiles([path]);
            var job = await s.WaitJob();
            var expected = reply == "string" ? Stage.InstalacaoSolicitada : reply == "rejected" ? Stage.PacotePronto : Stage.VerifiqueNoPs5;
            Check(job.Stage == expected, "DPI " + reply + " yields " + expected + " (actual " + job.Stage + ")");
            Check(s.Dpi.Count == 1 && File.Exists(path) && s.RemoteFiles().Length == 1 && Hash(s.RemoteFiles()[0]) == Hash(path), "DPI " + reply + " preserves prepared bytes and DeleteOriginal source");
            if (reply == "string") continue;
            Check(job.CanRequestInstall, "DPI " + reply + " offers explicit installation retry");
            if (expected == Stage.VerifiqueNoPs5)
                Check(!s.Engine.RequestInstall(job), "uncertain DPI requires confirmation");
            var logBefore = s.UploadCount;
            s.Dpi.Reply = "success";
            s.Settings.PkgDir = "/changed-packages";
            s.Settings.DpiPort = FreePort();
            Check(s.Engine.RequestInstall(job, confirmUnknown: true), "explicit DPI retry queued");
            await s.Wait(j => j.Stage == Stage.InstalacaoSolicitada);
            Check(s.Dpi.Count == 2 && s.UploadCount == logBefore, "DPI retry uses frozen console and never retransfers");
        }
        await using (var s = await Scenario.Create("dpi-closed"))
        {
            s.Settings.DpiPort = FreePort();
            s.Engine.AddFiles([Put(s.Input, "Game.pkg", Payload())]);
            var job = await s.WaitJob();
            Check(job.Stage == Stage.PacotePronto && job.CanRequestInstall && s.RemoteFiles().Length == 1, "closed DPI port retains retryable prepared PKG");
        }
        await using (var s = await Scenario.Create("dpi-probe"))
        {
            var message = await PkgInstaller.TestAsync(s.Settings);
            await Task.Delay(100);
            Check(message.Key == "core.pkg.connected" && s.Dpi.Count == 0, "DPI availability test sends no installation request");
            try { await PkgInstaller.InstallAsync("127.0.0.1", s.Dpi.Port, "/data/../Game.pkg"); throw new InvalidOperationException("Traversal was accepted"); }
            catch (PkgInstallException ex) { Check(!ex.MayHaveSubmitted && s.Dpi.Count == 0, "installer rejects traversal before connecting"); }
            try { await PkgInstaller.InstallAsync("127.0.0.1", s.Dpi.Port, "/data/" + new string('a', 1100) + ".pkg"); throw new InvalidOperationException("Oversized request was accepted"); }
            catch (PkgInstallException ex) { Check(!ex.MayHaveSubmitted && s.Dpi.Count == 0, "DPI v1 request limit rejects oversized JSON before connecting"); }
        }
    }

    static async Task PersistenceAndConcurrency()
    {
        await using (var s = await Scenario.Create("unknown-retry-failure"))
        {
            s.Dpi.Reply = "lost";
            s.Engine.AddFiles([Put(s.Input, "Game.pkg", Payload())]);
            var job = await s.WaitJob(); Check(job.Stage == Stage.VerifiqueNoPs5, "lost response records uncertain remote effect");
            s.Dpi.Reply = "rejected";
            Check(s.Engine.RequestInstall(job, confirmUnknown: true), "confirmed uncertain retry is queued");
            job = await s.WaitJob();
            Check(job.Stage == Stage.VerifiqueNoPs5 && s.Dpi.Count == 2 && !s.Engine.RequestInstall(job), "explicit retry rejection preserves original uncertainty and confirmation requirement");
            s.Dpi.Close();
            Check(s.Engine.RequestInstall(job, confirmUnknown: true), "confirmed retry after DPI closes is queued");
            job = await s.WaitJob();
            Check(job.Stage == Stage.VerifiqueNoPs5 && s.Dpi.Count == 2, "connection failure after uncertain request preserves original uncertainty");
        }
        await using (var s = await Scenario.Create("automatic-manual-race"))
        {
            var clicks = 0;
            s.Engine.Jobs.CollectionChanged += (_, ev) =>
            {
                if (ev.NewItems == null) return;
                foreach (Job job in ev.NewItems)
                    job.PropertyChanged += (_, change) =>
                    {
                        if (change.PropertyName == nameof(Job.Stage) && job.Stage == Stage.PacotePronto && Interlocked.Increment(ref clicks) == 1)
                            s.Engine.RequestInstall(job);
                    };
            };
            s.Engine.AddFiles([Put(s.Input, "Game.pkg", Payload())]);
            await s.Wait(j => j.Stage == Stage.InstalacaoSolicitada);
            await Task.Delay(1500);
            Check(s.Dpi.Count == 1, "manual click at publication and automatic install share one remote submission");
        }
        await using (var s = await Scenario.Create("restart-missing-source"))
        {
            var path = Put(s.Input, "Game.pkg", Payload());
            s.Engine.AddFiles([path]);
            await s.Wait(j => j.Stage == Stage.InstalacaoSolicitada);
            await s.Stop();
            File.Delete(path);
            var restarted = s.NewEngine();
            restarted.Restore();
            await s.Run(restarted);
            await s.Wait(j => j.Stage == Stage.InstalacaoSolicitada);
            await Task.Delay(1500);
            Check(s.Dpi.Count == 1 && s.Engine.Jobs.Count == 1, "restart retains submitted PKG with missing origin and never resubmits");
        }
        await using (var s = await Scenario.Create("unknown-restart"))
        {
            s.Dpi.Reply = "lost";
            s.Engine.AddFiles([Put(s.Input, "Game.pkg", Payload())]);
            await s.Wait(j => j.Stage == Stage.VerifiqueNoPs5);
            await s.Stop();
            var restarted = s.NewEngine(); restarted.Restore(); await s.Run(restarted);
            await s.Wait(j => j.Stage == Stage.VerifiqueNoPs5);
            await Task.Delay(1500);
            Check(s.Dpi.Count == 1, "unknown survives restart without duplicate remote request");
        }
        foreach (var state in new[] { "submitting", "transferring" })
        await using (var s = await Scenario.Create("crash-window-" + state))
        {
            s.Engine.AddFiles([Put(s.Input, "Game.pkg", Payload())]);
            await s.Wait(j => j.Stage == Stage.InstalacaoSolicitada); await s.Stop();
            RewritePackageLedger(s.Engine.QueueFile, p => p["State"] = state);
            var restarted = s.NewEngine(); restarted.Restore(); await s.Run(restarted);
            await s.Wait(j => j.Stage == (state == "submitting" ? Stage.VerifiqueNoPs5 : Stage.PacotePronto));
            await Task.Delay(1000);
            Check(s.Dpi.Count == 1, state == "submitting" ? "crash during submission restores unknown without resubmitting" : "crash after rename recovers own final as prepared without resubmitting");
        }
        await using (var s = await Scenario.Create("durable-submission"))
        {
            s.Dpi.Reply = "rejected";
            s.Engine.AddFiles([Put(s.Input, "Game.pkg", Payload())]);
            var job = await s.WaitJob();
            Check(job.Stage == Stage.PacotePronto, "rejected package ready for persistence test");
            var blocked = Path.Combine(s.Folder, "blocked-queue"); Directory.CreateDirectory(blocked);
            s.Engine.QueueFile = blocked;
            var before = s.Dpi.Count;
            s.Engine.RequestInstall(job);
            await Task.Delay(1200);
            Check(s.Dpi.Count == before, "failed durable queue write prevents DPI request");
        }
        await using (var s = await Scenario.Create("double-click"))
        {
            s.Dpi.Reply = "rejected";
            s.Engine.AddFiles([Put(s.Input, "Game.pkg", Payload())]);
            var job = await s.WaitJob();
            s.Dpi.Reply = "held";
            var accepted = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => s.Engine.RequestInstall(job))));
            await s.Wait(j => j.Stage == Stage.SolicitandoInstalacao);
            await Until(() => s.Dpi.Count == 2);
            Check(accepted.Count(v => v) == 1 && s.Dpi.Count == 2, "concurrent installation clicks create one remote request");
            s.Dpi.Release.TrySetResult();
            await s.Wait(j => j.Stage == Stage.InstalacaoSolicitada);
        }
        await using (var s = await Scenario.Create("removed-pending-request"))
        {
            s.Dpi.Reply = "rejected";
            s.Engine.AddFiles([Put(s.Input, "Game.pkg", Payload())]);
            var job = await s.WaitJob(); await s.Stop();
            Check(s.Engine.RequestInstall(job), "installation can be queued before removal");
            s.Engine.Remove(job); await s.Run(s.Engine); await Task.Delay(1500);
            Check(s.Dpi.Count == 1 && s.Engine.Jobs.Count == 0, "removed pending installation never reaches DPI");
        }
        await using (var s = await Scenario.Create("restored-header-change"))
        {
            s.Dpi.Reply = "rejected";
            var path = Put(s.Input, "Game.pkg", Payload());
            s.Engine.AddFiles([path]);
            await s.WaitJob(); await s.Stop();
            var oldRemote = s.RemoteFiles().Single(); var oldHash = Hash(oldRemote);
            var time = File.GetLastWriteTimeUtc(path); var bytes = File.ReadAllBytes(path);
            bytes[100] ^= 0x5a; File.WriteAllBytes(path, bytes); File.SetLastWriteTimeUtc(path, time);
            s.Dpi.Reply = "success";
            var restarted = s.NewEngine(); restarted.Restore(); await s.Run(restarted);
            await Until(() => s.Dpi.Count == 2, 30_000);
            await s.Wait(j => j.Stage == Stage.InstalacaoSolicitada);
            var finalFiles = s.RemoteFiles().Where(p => p.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase)).ToArray();
            Check(finalFiles.Length == 2 && Hash(oldRemote) == oldHash && finalFiles.Any(p => Hash(p) == Hash(path)), "restart detects same-size/mtime changed header and preserves historical remote package");
        }
        foreach (var response in new[] { "lost", "success" })
        await using (var s = await Scenario.Create("immutable-history-" + response))
        {
            s.Dpi.Reply = response;
            var path = Put(s.Input, "Game.pkg", Payload()); s.Engine.AddFiles([path]);
            var job = await s.WaitJob(); var historicalStage = job.Stage;
            var historicalPath = job.Package!.RemotePath; var uploads = s.UploadCount;
            await s.Stop();
            var bytes = File.ReadAllBytes(path); bytes[100] ^= 0x5a;
            File.WriteAllBytes(path, bytes); File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(2));
            var restarted = s.NewEngine(); restarted.Restore(); await s.Run(restarted);
            await Task.Delay(2500);
            lock (restarted.Lock) job = restarted.Jobs.Single();
            Check(job.Stage == historicalStage && job.Package?.RemotePath == historicalPath && s.Dpi.Count == 1 && s.UploadCount == uploads,
                response + " source mutation preserves submitted/unknown history without automatic transfer or installation");
        }
    }

    static void RewritePackageLedger(string path, Action<JsonNode> update)
    {
        var ledger = JsonNode.Parse(File.ReadAllText(path))!;
        foreach (var preparation in ledger["Packages"]!.AsObject()) update(preparation.Value!);
        File.WriteAllText(path, ledger.ToJsonString());
    }

    static async Task PauseResumeIdentity()
    {
        foreach (var kind in new[] { "same", "mutated", "foreign" })
        {
            var mutate = kind == "mutated";
            await using var s = await Scenario.Create("pause-identity-" + kind, rate: 2);
            var path = Put(s.Input, "Large.pkg", Payload(size: 16_777_216));
            s.Engine.AddFiles([path]);
            var job = await s.Wait(j => j.Stage == Stage.Enviando && j.DoneBytes > 500_000 && j.Progress < 90);
            s.Engine.Pause(job);
            await s.Wait(j => j.Stage == Stage.Pausado);
            // Stage switches immediately; wait for cancellation to release its open source.
            await Until(() =>
            {
                try { using var unlocked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return true; }
                catch (IOException) { return false; }
            });
            if (mutate)
            {
                var bytes = File.ReadAllBytes(path); bytes[100] ^= 0x5a; File.WriteAllBytes(path, bytes);
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(2));
            }
            if (kind == "foreign")
            {
                await s.Stop();
                RewritePackageLedger(s.Engine.QueueFile, p => p["PartialStarted"] = false);
                var restarted = s.NewEngine(); restarted.Restore(); await s.Run(restarted);
            }
            else s.Engine.Resume(job);
            await s.Wait(j => j.Stage == Stage.InstalacaoSolicitada, 45_000);
            Check(Hash(s.RemoteFiles().Single(p => p.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase))) == Hash(path), "pause/resume " + kind + " publishes exact current bytes");
            Check(kind != "same" ? s.AppendCount == 0 : s.AppendCount > 0, kind == "same" ? "owned unchanged partial resumes with APPE" : kind + " origin never appends untrusted partial");
            Check(s.Dpi.Count == 1, "pause/resume submits only after completed transfer");
        }
    }

    static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    static int FreePort() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
    static async Task Until(Func<bool> condition, int timeout = 20_000)
    {
        var watch = Stopwatch.StartNew();
        while (!condition()) { if (watch.ElapsedMilliseconds > timeout) throw new TimeoutException("PKG condition timed out"); await Task.Delay(40); }
    }
    static async Task Tool(string exe, string cwd, params string[] args)
    {
        var info = new ProcessStartInfo(exe) { WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        if (!OperatingSystem.IsWindows()) info.Environment["LC_ALL"] = "C.UTF-8";
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException(exe + ": " + await output + await error);
    }

    sealed class Scenario : IAsyncDisposable
    {
        public required string Folder, Input, FtpRoot;
        public required Settings Settings;
        public required Engine Engine;
        public required FakeDpi Dpi;
        public required Process Ftp;
        public ConcurrentQueue<string> FtpLogs = new();
        readonly ConcurrentQueue<string> _engineLogs = new();
        Func<Job, Task<string?>> _password = _ => Task.FromResult<string?>(null);
        CancellationTokenSource? _stop;
        Task? _run;
        public int UploadCount => FtpLogs.Count(line => line.Contains(" STOR ") || line.Contains(" APPE "));
        public int AppendCount => FtpLogs.Count(line => line.Contains(" APPE "));
        public string[] RemoteFiles() => Directory.GetFiles(FtpRoot, "*", SearchOption.AllDirectories);

        public static async Task<Scenario> Create(string name, Func<Job, Task<string?>>? password = null, int rate = 40)
        {
            var folder = Path.Combine(_work, name); var input = Path.Combine(folder, "input"); var ftpRoot = Path.Combine(folder, "ftp");
            Directory.CreateDirectory(input); Directory.CreateDirectory(ftpRoot);
            var port = FreePort(); var dpi = new FakeDpi(ftpRoot);
            var settings = new Settings { Host = "127.0.0.1", Port = port, DpiPort = dpi.Port, User = "ps5", Password = "ps5pass", InputFolder = "", RemoteDir = "/dumps", ImageDir = "/images", PkgDir = "/packages", Connections = 1 };
            var info = new ProcessStartInfo(OperatingSystem.IsWindows() ? "python" : "python3") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var arg in new[] { Path.Combine(_root, "e2e", "ftpserver.py"), port.ToString(), ftpRoot, "appe", rate.ToString() }) info.ArgumentList.Add(arg);
            var ftp = Process.Start(info)!;
            var s = new Scenario { Folder = folder, Input = input, FtpRoot = ftpRoot, Settings = settings, Engine = null!, Dpi = dpi, Ftp = ftp, _password = password ?? (_ => Task.FromResult<string?>(null)) };
            ftp.ErrorDataReceived += (_, ev) => { if (ev.Data != null) s.FtpLogs.Enqueue(ev.Data); }; ftp.BeginErrorReadLine();
            ftp.OutputDataReceived += (_, ev) => { if (ev.Data != null) s.FtpLogs.Enqueue(ev.Data); }; ftp.BeginOutputReadLine();
            try
            {
                await Until(() =>
                {
                    if (ftp.HasExited) throw new InvalidOperationException("FTP exited: " + string.Join("\n", s.FtpLogs));
                    try { using var c = new TcpClient(); c.Connect("127.0.0.1", port); return true; } catch (SocketException) { return false; }
                });
                await s.Run(s.NewEngine()); return s;
            }
            catch { await s.DisposeAsync(); throw; }
        }
        public Engine NewEngine() => new(Settings, _engineLogs.Enqueue, _password) { StableSeconds = 0, QueueFile = Path.Combine(Folder, "queue.json") };
        public Task Run(Engine engine) { Engine = engine; _stop = new(); _run = engine.RunAsync(_stop.Token); return Task.CompletedTask; }
        public async Task Stop() { if (_stop == null) return; _stop.Cancel(); if (_run != null) await _run.WaitAsync(TimeSpan.FromSeconds(20)); _stop.Dispose(); _stop = null; }
        public Task<Job> WaitJob() => Wait(j => j.Stage is Stage.Erro or Stage.Cancelado or Stage.InstalacaoSolicitada or Stage.VerifiqueNoPs5 or Stage.Verificado
            || j.Stage == Stage.PacotePronto && j.DetailMessage?.Key == "core.concat");
        public async Task<Job> Wait(Func<Job, bool> predicate, int timeout = 20_000)
        {
            Job? result = null;
            try { await Until(() => { lock (Engine.Lock) result = Engine.Jobs.FirstOrDefault(predicate); return result != null; }, timeout); }
            catch (TimeoutException) { throw new TimeoutException("PKG " + Path.GetFileName(Folder) + ": " + string.Join("; ", Engine.Jobs.Select(j => j.Stage + ": " + j.Detail)) + "\n" + string.Join("\n", _engineLogs)); }
            return result!;
        }
        public async ValueTask DisposeAsync()
        {
            Dpi.Release.TrySetResult();
            await Stop(); await Dpi.DisposeAsync();
            if (!Ftp.HasExited) Ftp.Kill(true);
            await Ftp.WaitForExitAsync(); Ftp.Dispose();
        }
    }

    sealed class FakeDpi : IAsyncDisposable
    {
        readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        readonly CancellationTokenSource _stop = new();
        readonly ConcurrentQueue<string> _requests = new();
        readonly string _ftpRoot;
        readonly Task _run;
        public string Reply = "success";
        public bool PublicationValid = true;
        public bool DurableSubmitting = true;
        public string HashAtRequest = "";
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Count => _requests.Count;
        public int Port { get; }
        public FakeDpi(string ftpRoot)
        {
            _ftpRoot = ftpRoot; _listener.Start(); Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _run = Task.Run(Accept);
        }
        public void Close() { _stop.Cancel(); _listener.Stop(); }
        async Task Accept()
        {
            while (!_stop.IsCancellationRequested)
            {
                try { using var client = await _listener.AcceptTcpClientAsync(_stop.Token); await Handle(client); }
                catch (OperationCanceledException) { break; }
                catch (Exception e) when (_stop.IsCancellationRequested || e is IOException or SocketException) { }
            }
        }
        async Task Handle(TcpClient client)
        {
            var stream = client.GetStream(); var buffer = new byte[4096]; var body = new MemoryStream();
            JsonDocument? doc = null;
            while (doc == null)
            {
                var count = await stream.ReadAsync(buffer, _stop.Token);
                if (count == 0) return; // Availability probe sends no installation request.
                body.Write(buffer, 0, count);
                try { doc = JsonDocument.Parse(body.ToArray()); } catch (JsonException) { if (body.Length > 4096) throw; }
            }
            using (doc)
            {
                var url = doc.RootElement.GetProperty("url").GetString()!;
                _requests.Enqueue(url);
                var remote = Path.Combine(_ftpRoot, url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                PublicationValid &= url.StartsWith('/') && !url.Contains("..") && File.Exists(remote) && !File.Exists(remote + Engine.PartSuffix);
                if (File.Exists(remote)) HashAtRequest = Hash(remote);
                using var ledger = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(_ftpRoot)!, "queue.json")));
                DurableSubmitting &= ledger.RootElement.GetProperty("Packages").EnumerateObject().Any(p =>
                    p.Value.GetProperty("RemotePath").GetString() == url && p.Value.GetProperty("State").GetString() == "submitting");
            }
            var reply = Reply;
            if (reply == "lost") return;
            if (reply == "timeout") { await Task.Delay(10_500, _stop.Token); return; }
            if (reply == "held") await Release.Task.WaitAsync(_stop.Token);
            var response = reply switch { "rejected" => "{\"res\":-7}", "malformed" => "not-json", "oversized" => new string('x', 5000), "string" => "{\"res\":\"0\"}", _ => "{\"res\":0}" };
            var bytes = Encoding.UTF8.GetBytes(response);
            if (reply == "fragmented")
            {
                foreach (var value in bytes) { await stream.WriteAsync(new byte[] { value }, _stop.Token); await Task.Delay(15, _stop.Token); }
            }
            else await stream.WriteAsync(bytes, _stop.Token);
        }
        public async ValueTask DisposeAsync() { _stop.Cancel(); _listener.Stop(); await _run; _stop.Dispose(); }
    }
}
