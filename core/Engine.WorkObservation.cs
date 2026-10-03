using System.IO;

namespace Ferry;

/// <summary>Read-only discovery evidence for the Windows session power guard.</summary>
public sealed record WorkObservation(long Revision, string SourceSignature, bool HasUnobservedWork)
{
    public string SourceScope { get; init; } = "";
    public IReadOnlyDictionary<string, string>? SourceFiles { get; init; }
    public IReadOnlyDictionary<string, string>? AutomaticallyDeletedFiles { get; init; }
}

public partial class Engine
{
    long _workRevision;
    readonly Dictionary<string, string> _automaticallyDeletedFiles = new(StringComparer.OrdinalIgnoreCase);

    static string FileStamp(string path)
    {
        if (Directory.Exists(path)) return Archives.DirStamp(path);
        var file = new FileInfo(path);
        return file.Exists ? $"{file.Length}:{file.LastWriteTimeUtc.Ticks}" : "missing";
    }

    // Record exactly the original revision removed by the existing successful-transfer cleanup.
    // The same lock makes deletion/evidence atomic to ObserveWork; no change to deletion policy.
    void DeleteOriginalObserved(string path)
    {
        lock (Lock)
        {
            var stamp = FileStamp(path);
            File.Delete(path);
            _automaticallyDeletedFiles[Path.GetFullPath(path)] = stamp;
        }
    }

    /// <summary>Includes sources not yet turned into cards. Failure to inspect must block power actions.</summary>
    public WorkObservation ObserveWork()
    {
        lock (Lock)
        {
            if (settings.InputFolder.Length > 0 && !Directory.Exists(settings.InputFolder))
                throw new IOException("Input folder is unavailable.");
            var before = Interlocked.Read(ref _workRevision);
            var groups = CurrentGroups();
            lock (_dropped) foreach (var key in _removed) groups.Remove(key);
            var signature = settings.InputFolder + "\n" + string.Join("\n", groups.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Key + ":" + Sig(g.Value.Parts.Order(StringComparer.OrdinalIgnoreCase).ToList())));
            var sources = groups.Values.SelectMany(g => g.Parts).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(path => path, FileStamp, StringComparer.OrdinalIgnoreCase);
            var unseen = groups.Any(g => !Jobs.Any(j => j.Key.Equals(g.Key, StringComparison.OrdinalIgnoreCase)
                && j.Parts.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(g.Value.Parts.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase)));
            var after = Interlocked.Read(ref _workRevision);
            return new(after, signature, unseen || before != after)
            {
                SourceScope = settings.InputFolder, SourceFiles = sources,
                AutomaticallyDeletedFiles = new Dictionary<string, string>(_automaticallyDeletedFiles, StringComparer.OrdinalIgnoreCase)
            };
        }
    }
}
