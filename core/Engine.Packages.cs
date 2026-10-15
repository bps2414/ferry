using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Ferry;

public sealed class PackagePreparation
{
    public string Identity { get; set; } = "";
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string MainFile { get; set; } = "";
    public string EntryPath { get; set; } = "";
    public string SourceSignature { get; set; } = "";
    public string HeaderHash { get; set; } = "";
    public string Format { get; set; } = "";
    public List<string> Parts { get; set; } = [];
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public int DpiPort { get; set; }
    /// "" = etaHEN DPI v1 (fila antiga)
    public string Installer { get; set; } = "";
    public int HyperDpiPort { get; set; }
    public int HyperDpiWebPort { get; set; }
    public string User { get; set; } = "";
    public string ProtectedPassword { get; set; } = "";
    public string RemotePath { get; set; } = "";
    public string PartialPath { get; set; } = "";
    public long Size { get; set; }
    public string State { get; set; } = "transferring";
    public bool PartialStarted { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public partial class Engine
{
    Dictionary<string, PackagePreparation> _packages = new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentQueue<Job> _installRequests = new();
    readonly HashSet<string> _checkedPackages = new(StringComparer.OrdinalIgnoreCase);

    void RestorePackages()
    {
        List<PackagePreparation> visible;
        lock (_dropped) visible = _packages.Values.Where(p => !_removed.Contains(p.Key))
            .GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(g => g.OrderByDescending(p => p.CreatedUtc).First()).ToList();
        lock (Lock)
            foreach (var p in visible.Where(p => p.State != "transferring" || File.Exists(p.MainFile)))
            {
                var job = Jobs.FirstOrDefault(j => j.Key.Equals(p.Key, StringComparison.OrdinalIgnoreCase));
                if (job != null) continue;
                job = new Job { Key = p.Key, Name = p.Name }; Jobs.Add(job);
                job.Package = p; job.MainFile = p.MainFile; job.Parts = [.. p.Parts];
                lock (_dropped) job.ArchivePassword = Secret.Unprotect(_passwords.GetValueOrDefault(p.Key));
                SetPackageStage(job);
            }
    }

    async Task ValidateRestoredPackagesAsync()
    {
        Job[] candidates;
        lock (Lock) candidates = Jobs.Where(j => j.Package is { SourceSignature.Length: > 0 } p && p.State == "prepared"
            && p.Parts.Count > 0 && p.Parts.All(File.Exists)
            && (!_checkedPackages.Contains(p.Identity) || p.SourceSignature != Sig(p.Parts))).ToArray();
        foreach (var job in candidates)
        {
            var p = job.Package!;
            var changed = p.SourceSignature != Sig(p.Parts);
            if (!changed)
            {
                try
                {
                    var loose = Archives.IsPackage(p.MainFile);
                    Entry? entry = loose ? new(Path.GetFileName(p.MainFile), new FileInfo(p.MainFile).Length, false, false)
                        : (await Archives.ListAsync(p.MainFile, job.ArchivePassword)).entries.FirstOrDefault(e => e.Path == p.EntryPath);
                    changed = entry == null || Convert.ToHexString(SHA256.HashData(await ReadPackageHeaderAsync(job, entry, loose, CancellationToken.None))) != p.HeaderHash
                        || p.SourceSignature != Sig(p.Parts);
                }
                catch { changed = true; }
            }
            lock (Lock)
            {
                _checkedPackages.Add(p.Identity);
                // The historical preparation and remote file remain immutable. Only the
                // current source card returns to inspection for its new identity.
                if (changed && Jobs.Contains(job) && job.Stage != Stage.SolicitandoInstalacao)
                {
                    job.Package = null; job.Progress = 0; job.Stage = Stage.AguardandoPartes;
                    job.SetDetail(new("core.pkg.changed")); _stable.Remove(job.Key);
                }
            }
        }
    }

    static void SetPackageStage(Job job)
    {
        job.Stage = job.Package?.State switch
        {
            "prepared" => Stage.PacotePronto,
            "submitted" => Stage.InstalacaoSolicitada,
            "installed" => Stage.Instalado,
            "submitting" or "unknown" => Stage.VerifiqueNoPs5,
            _ => Stage.AguardandoPartes,
        };
        if (job.Stage == Stage.InstalacaoSolicitada) job.Finish(new Message("core.pkg.requested"));
        else if (job.Stage == Stage.Instalado) job.Finish(new Message("core.hyperdpi.installed"));
        else if (job.Stage == Stage.VerifiqueNoPs5) job.Finish(new Message("core.pkg.unknown"));
        else if (job.Stage == Stage.PacotePronto) job.Finish(PackageReadyMessage(job.Package!));
    }

    static Message PackageReadyMessage(PackagePreparation package) => new("core.pkg.prepared",
        package.RemotePath, package.RemotePath[..package.RemotePath.LastIndexOf('/')]);

    public bool RequestInstall(Job job, bool confirmUnknown = false)
    {
        lock (Lock)
        {
            if (!Jobs.Contains(job) || !job.CanRequestInstall || job.Package == null || job.Stage == Stage.VerifiqueNoPs5 && !confirmUnknown) return false;
            job.Stage = Stage.SolicitandoInstalacao;
            _installRequests.Enqueue(job);
            return true;
        }
    }

    async Task SubmitPackageAsync(Job job)
    {
        lock (Lock) if (!Jobs.Contains(job) || job.Stage != Stage.SolicitandoInstalacao || job.Package?.State is not ("prepared" or "unknown")) return;
        var p = job.Package!;
        var previouslyUnknown = p.State == "unknown";
        try
        {
            // This durable transition is mandatory before any possible remote effect.
            lock (_dropped)
            {
                var previous = p.State;
                p.State = "submitting";
                try { SaveQueueRequired(); }
                catch { p.State = previous; throw; }
            }
            job.Stage = Stage.SolicitandoInstalacao;
            job.SetDetail(new("core.pkg.submitting"));
            string? contentId = null;
            if (p.Installer == "hyperdpi") contentId = await HyperDpi.InstallAsync(p.Host, p.HyperDpiPort, p.RemotePath, p.Size);
            else await PkgInstaller.InstallAsync(p.Host, p.DpiPort, p.RemotePath);
            lock (_dropped)
            {
                p.State = "submitted";
                try { SaveQueueRequired(); }
                catch { p.State = "unknown"; throw; }
            }
            if (!string.IsNullOrEmpty(contentId))
            {
                // O card continua ativo até o resultado; o motor segue com as outras transferências.
                job.SetDetail(new("core.hyperdpi.tracking"));
                _ = Task.Run(() => TrackHyperDpiAsync(job, p, contentId));
                return;
            }
            job.Stage = Stage.InstalacaoSolicitada;
            job.Finish(new Message("core.pkg.requested"));
            JobLog(job, new("core.pkg.requested"));
            NotifyPackageDone(job);
        }
        catch (Exception e)
        {
            if (e is PkgInstallException dpi) p.State = dpi.MayHaveSubmitted || previouslyUnknown ? "unknown" : "prepared";
            // Persistence failure after success stays unknown; before sending stays prepared.
            try { SaveQueueRequired(); } catch { }
            job.Stage = p.State == "unknown" ? Stage.VerifiqueNoPs5 : Stage.PacotePronto;
            var diagnostic = e is PkgInstallException failure ? failure.Diagnostic : Localization.ExceptionMessage(e);
            job.Finish(new Message("core.concat", diagnostic, new Message(p.State == "unknown" ? "core.pkg.unknownHint" : "core.pkg.retryHint")));
            JobLog(job, diagnostic);
            if (p.State == "unknown") NotifyPackageDone(job);
        }
    }

    async Task TrackHyperDpiAsync(Job job, PackagePreparation p, string contentId)
    {
        try
        {
            var (phase, error) = await HyperDpi.TrackAsync(p.Host, p.HyperDpiWebPort, contentId, _stop);
            Message detail;
            if (phase == "complete")
            {
                lock (_dropped) { p.State = "installed"; try { SaveQueueRequired(); } catch { } }
                // Instalação confirmada: o PKG na pasta do console não serve mais. Falha ao apagar só vai para o log.
                var removed = true;
                try { await Ftp.DeleteAsync(new Settings { Host = p.Host, Port = p.Port, User = p.User, Password = Secret.Unprotect(p.ProtectedPassword) ?? "" }, p.RemotePath, _stop); }
                catch (Exception e) when (e is not OperationCanceledException) { removed = false; JobLog(job, Localization.ExceptionMessage(e)); }
                job.Stage = Stage.Instalado;
                detail = new(removed ? "core.hyperdpi.installed" : "core.hyperdpi.installedKept");
                job.Finish(detail);
            }
            else if (phase == "error")
            {
                lock (_dropped) { p.State = "prepared"; try { SaveQueueRequired(); } catch { } }
                job.Stage = Stage.PacotePronto;
                detail = new("core.concat", new Message("core.hyperdpi.failed", error), new Message("core.pkg.retryHint"));
                job.Finish(detail);
            }
            else
            {
                lock (_dropped) { p.State = "unknown"; try { SaveQueueRequired(); } catch { } }
                job.Stage = Stage.VerifiqueNoPs5;
                detail = new("core.pkg.unknown");
                job.Finish(detail);
            }
            JobLog(job, detail);
            NotifyPackageDone(job);
        }
        catch (OperationCanceledException) { } // app fechando: segue "submitted" e volta como solicitada
        catch (Exception e) { Emit(new("core.error", Localization.ExceptionMessage(e))); }
    }

    void NotifyPackageDone(Job job)
    {
        try { Done?.Invoke(job); }
        catch (Exception e) { Emit(new("core.error", Localization.ExceptionMessage(e))); }
    }

    static string SafePackageName(string path)
    {
        var leaf = path.Replace('\\', '/').Split('/')[^1];
        if (leaf.Any(char.IsControl) || leaf is "" or "." or "..") throw new LocalizedException(new("core.pkg.path"));
        var name = string.Concat(leaf.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));
        return name.Length > 160 ? name[..150] + ".pkg" : name;
    }

    static string PackageDirectory(string path)
    {
        var dir = path.TrimEnd('/');
        if (!dir.StartsWith('/') || dir.Split('/').Any(p => p is "." or ".." || p.Any(char.IsControl)) || dir.Contains('\\'))
            throw new LocalizedException(new("core.pkg.path"));
        return dir;
    }

    async Task<byte[]> ReadPackageHeaderAsync(Job job, Entry entry, bool loose, CancellationToken ct)
    {
        if (entry.Size < PackageHeader.ProbeSize) throw new LocalizedException(new("core.pkg.header"));
        string? list = null;
        Process? process = null;
        try
        {
            if (!loose)
            {
                list = Path.Combine(Path.GetTempPath(), $"ferry-pkg-{Guid.NewGuid():N}.txt");
                await File.WriteAllLinesAsync(list, [Archives.Native(entry.Path)], ct);
                process = Archives.OpenStream(job.MainFile, job.ArchivePassword, list);
                _ = process.StandardError.ReadToEndAsync();
            }
            using var stream = process?.StandardOutput.BaseStream ?? File.OpenRead(job.MainFile);
            using var cancel = ct.Register(() => { try { process?.Kill(true); } catch { } });
            var bytes = new byte[PackageHeader.ProbeSize];
            await stream.ReadExactlyAsync(bytes, ct);
            PackageHeader.Validate(bytes, entry.Size);
            return bytes;
        }
        catch (EndOfStreamException e) { throw new LocalizedException(new("core.pkg.header"), e); }
        finally
        {
            if (process != null) { try { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); } catch { } process.Dispose(); }
            if (list != null) try { File.Delete(list); } catch { }
        }
    }

    async Task ProcessPackageAsync(Job job, Entry entry, bool loose, CancellationToken ct)
    {
        var before = Sig(job.Parts);
        var header = await ReadPackageHeaderAsync(job, entry, loose, ct);
        if (before != Sig(job.Parts)) throw new LocalizedException(new("core.pkg.changed"));
        var hyper = settings.Installer == "hyperdpi";
        var dir = hyper ? HyperDpi.Dir : PackageDirectory(settings.PkgDir);
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{before}|{entry.Path}|{entry.Size}|{Convert.ToHexString(header)}|{settings.Host}|{settings.Port}|{settings.DpiPort}|{dir}" + (hyper ? $"|hyperdpi|{settings.HyperDpiPort}|{settings.HyperDpiWebPort}" : ""))));
        PackagePreparation p;
        lock (_dropped)
        {
            if (!_packages.TryGetValue(identity, out p!))
            {
                p = new PackagePreparation { Identity = identity, Key = job.Key, Name = job.Name, MainFile = job.MainFile,
                    EntryPath = entry.Path, SourceSignature = before, HeaderHash = Convert.ToHexString(SHA256.HashData(header)),
                    Format = PackageHeader.Validate(header, entry.Size) == "CNT" ? "PS4 / CNT" : "PS5 / FIH",
                    Parts = [.. job.Parts], Host = settings.Host, Port = settings.Port, DpiPort = settings.DpiPort,
                    Installer = hyper ? "hyperdpi" : "", HyperDpiPort = settings.HyperDpiPort, HyperDpiWebPort = settings.HyperDpiWebPort,
                    User = settings.User, ProtectedPassword = Secret.Protect(settings.Password), Size = entry.Size,
                    RemotePath = dir + "/" + (hyper ? HyperDpi.Name(Path.GetFileName(entry.Path.Replace('\\', '/')), identity[..16].ToLowerInvariant())
                        : Path.GetFileNameWithoutExtension(SafePackageName(entry.Path)) + "-" + identity.ToLowerInvariant() + ".pkg"),
                    // etaHEN matches any filename containing .pkg, including .pkg.ferry-part.
                    PartialPath = dir + "/" + identity.ToLowerInvariant() + PartSuffix };
                _packages[identity] = p;
            }
            // Instalado e apagado do console: adicionar de novo envia o PKG outra vez.
            if (p.State == "installed") { p.State = "transferring"; p.PartialStarted = false; }
            job.Package = p;
            SaveQueueRequired();
        }
        lock (Lock) _checkedPackages.Add(p.Identity);
        if (p.State != "transferring") { SetPackageStage(job); return; }
        var snapshot = new Settings { Host = p.Host, Port = p.Port, DpiPort = p.DpiPort, User = p.User,
            Password = Secret.Unprotect(p.ProtectedPassword) ?? "", Connections = settings.Connections };
        var partial = p.PartialPath.Length > 0 ? p.PartialPath : p.RemotePath + PartSuffix;
        var final = await Ftp.RemoteStateAsync(snapshot, [p.RemotePath], ct);
        // An exclusive final path can only have been published after successful extraction and SIZE.
        if (final.have[0] == p.Size)
        {
            lock (_dropped) { p.State = "prepared"; SaveQueueRequired(); }
            job.Stage = Stage.PacotePronto; job.Finish(PackageReadyMessage(p));
            return; // Recovery never initiates an installation request.
        }
        if (final.have[0] >= 0) throw new LocalizedException(new("core.pkg.remoteConflict"));
        var noAppend = false;
        for (var attempt = 0; ; attempt++)
        {
            var (have, append, _) = await Ftp.RemoteStateAsync(snapshot, [partial], ct, noAppend);
            if (!p.PartialStarted) have[0] = -1;
            // A full partial may come from an extractor that failed after its final byte.
            // Re-run extraction rather than treating SIZE as successful CRC validation.
            if (have[0] == entry.Size) have[0] = -1;
            lock (_dropped) { p.PartialStarted = true; SaveQueueRequired(); }
            job.Stage = Stage.Enviando; job.ResetRate(); job.Detail = p.RemotePath;
            var list = Path.Combine(Path.GetTempPath(), $"ferry-pkg-{Guid.NewGuid():N}.txt");
            try
            {
                await File.WriteAllLinesAsync(list, [Archives.Native(entry.Path)], ct);
                using var process = loose ? null : Archives.OpenStream(job.MainFile, job.ArchivePassword, list);
                using var src = process?.StandardOutput.BaseStream ?? File.OpenRead(job.MainFile);
                using var cancel = ct.Register(() => { try { process?.Kill(true); } catch { } });
                var errors = process?.StandardError.ReadToEndAsync();
                Exception? failed = null;
                try
                {
                    await Ftp.StreamAsync(snapshot, src, [entry], [partial], have, append, [0], job.Report,
                        _ => job.CurrentFile = entry.Path, _ => { }, _ => false, log, ct, m => JobLog(job, m));
                }
                catch (Exception e) { failed = e; try { process?.Kill(true); } catch { } }
                if (process != null) await process.WaitForExitAsync();
                ct.ThrowIfCancellationRequested();
                if (process is { ExitCode: not 0 } && (failed == null || failed is LocalizedException { MessageData.Key: "core.archive.outputShort" or "core.archive.outputLong" }))
                    throw new LocalizedException(new("core.archive.failed", (await errors!).Trim()));
                if (failed is LocalizedException { MessageData.Key: "core.ftp.sendFailed" } ftp && append && !noAppend && Equals(ftp.MessageData.Args[1], "APPE"))
                { noAppend = true; continue; }
                if (failed != null && attempt < 3 && Transient(failed)) { await Task.Delay(1000, ct); continue; }
                if (failed != null) throw failed;
                break;
            }
            finally { try { File.Delete(list); } catch { } }
        }
        if (before != Sig(job.Parts)) throw new LocalizedException(new("core.pkg.changed"));
        var complete = await Ftp.RemoteStateAsync(snapshot, [partial], ct);
        if (complete.have[0] != entry.Size) throw new LocalizedException(new("core.ftp.size", partial, entry.Size, complete.have[0]));
        await Ftp.RenameAsync(snapshot, partial, p.RemotePath, ct);
        var published = await Ftp.RemoteStateAsync(snapshot, [p.RemotePath], ct);
        if (published.have[0] != entry.Size) throw new LocalizedException(new("core.ftp.size", p.RemotePath, entry.Size, published.have[0]));
        lock (_dropped) { p.State = "prepared"; SaveQueueRequired(); }
        ct.ThrowIfCancellationRequested();
        job.Stage = Stage.PacotePronto; job.Finish(PackageReadyMessage(p));
        JobLog(job, PackageReadyMessage(p));
        if (settings.AutoInstallPackages) RequestInstall(job);
        else if (job.Stage == Stage.PacotePronto) NotifyPackageDone(job);
    }
}
