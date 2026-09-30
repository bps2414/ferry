using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Ferry;

/// <summary>Session-only opt-in. Tick and Dispose run on the WPF dispatcher; events only invalidate state.</summary>
public sealed class TransferPowerSession : IDisposable
{
    readonly ObservableCollection<Job> _jobs;
    readonly object _sync;
    readonly IPowerService _power;
    readonly TimeProvider _clock;
    readonly Func<WorkObservation>? _observe;
    readonly HashSet<Job> _subscribed = [];
    long? _countdown;
    long _revision;
    WorkObservation? _armedWork;
    bool _closed, _passwordPending, _sleepHeld;
    public bool Armed { get; private set; }
    public bool KeepAwake { get; set; }
    public int? RemainingSeconds { get; private set; }
    public string StatusKey { get; private set; } = "app.powerOff";

    public TransferPowerSession(ObservableCollection<Job> jobs, object sync, IPowerService power, TimeProvider? clock = null, Func<WorkObservation>? observe = null)
    {
        _jobs = jobs; _sync = sync; _power = power; _clock = clock ?? TimeProvider.System; _observe = observe;
        lock (_sync) { Subscribe(); _jobs.CollectionChanged += CollectionChanged; }
    }

    static bool Successful(Job job) => job.Stage is Stage.Verificado or Stage.PacotePronto or Stage.InstalacaoSolicitada;
    static bool Pending(Job job) => job.Stage is Stage.AguardandoPartes or Stage.NaFila or Stage.Extraindo or Stage.Enviando or Stage.Pausado or Stage.SolicitandoInstalacao;

    public bool CanArm
    {
        get { lock (_sync) return !_closed && _jobs.Any(Pending); }
    }

    public bool PasswordPending
    {
        get { lock (_sync) return _passwordPending; }
        set { lock (_sync) { _passwordPending = value; if (value) ResetCountdown(); } }
    }

    public bool Arm()
    {
        lock (_sync)
        {
            if (!CanArm) return false;
            try
            {
                var work = _observe?.Invoke();
                if (work?.HasUnobservedWork == true) { StatusKey = "app.powerUnobserved"; return false; }
                _revision = work?.Revision ?? 0;
                _armedWork = work;
            }
            catch { StatusKey = "app.powerUnobserved"; return false; }
            Armed = true; ResetCountdown(); StatusKey = "app.powerWaiting";
            return true;
        }
    }

    public void Cancel()
    {
        lock (_sync) Disarm("app.powerCancelled");
    }

    void Disarm(string key) { Armed = false; ResetCountdown(); StatusKey = key; }
    void ResetCountdown() { _countdown = null; RemainingSeconds = null; }

    bool SourcesMatch(WorkObservation? current)
    {
        if (_armedWork?.SourceFiles is not { } armed || current?.SourceFiles is not { } now)
            return current?.SourceSignature == _armedWork?.SourceSignature;
        if (current.SourceScope != _armedWork.SourceScope) return false;
        if (now.Any(file => !armed.TryGetValue(file.Key, out var stamp) || stamp != file.Value)) return false;
        return armed.All(file => now.ContainsKey(file.Key)
            || current.AutomaticallyDeletedFiles?.TryGetValue(file.Key, out var deleted) == true && deleted == file.Value);
    }

    void Subscribe()
    {
        foreach (var job in _subscribed.Where(j => !_jobs.Contains(j)).ToArray()) { job.PropertyChanged -= StageChanged; _subscribed.Remove(job); }
        foreach (var job in _jobs) if (_subscribed.Add(job)) job.PropertyChanged += StageChanged;
    }

    void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        lock (_sync)
        {
            Subscribe();
            if (Armed && e.Action != NotifyCollectionChangedAction.Move) Disarm("app.powerQueueChanged");
        }
    }

    void StageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Job.Stage)) return;
        lock (_sync)
            if (Armed && sender is Job job && !Successful(job)) { ResetCountdown(); StatusKey = "app.powerWaiting"; }
    }

    public void Tick()
    {
        lock (_sync)
        {
            if (_closed) return;
            try
            {
                if (!Armed) return;
                var work = _observe?.Invoke(); // filesystem + AddFiles evidence, even when cards are unchanged
                if (work != null && work.Revision != _revision) { Disarm("app.powerQueueChanged"); return; }
                if (!SourcesMatch(work)) { Disarm("app.powerQueueChanged"); return; }
                if (work?.HasUnobservedWork == true) { ResetCountdown(); StatusKey = "app.powerUnobserved"; return; }
                if (_jobs.Count == 0 || _passwordPending || !_jobs.All(Successful))
                { ResetCountdown(); StatusKey = "app.powerWaiting"; return; }
                if (_countdown == null)
                { _countdown = _clock.GetTimestamp(); }
                RemainingSeconds = Math.Max(0, (int)Math.Ceiling(60 - _clock.GetElapsedTime(_countdown.Value).TotalSeconds));
                StatusKey = "app.powerCountdown";
                if (RemainingSeconds > 0) return;

                // Fresh evidence at the action boundary. Never infer success from an old UI summary.
                var final = _observe?.Invoke();
                if (!Armed || _closed || _passwordPending || _jobs.Count == 0 || !_jobs.All(Successful)
                    || final?.HasUnobservedWork == true || final?.Revision != work?.Revision || !SourcesMatch(final))
                { Disarm("app.powerQueueChanged"); return; }
                Disarm("app.powerRequested"); // one-shot, including failures
                _power.ShutdownThisPc();
            }
            catch
            {
                Disarm("app.powerFailed");
                KeepAwake = false;
            }
            finally
            {
                try
                {
                    var hold = !_passwordPending && (KeepAwake && _jobs.Any(j => j.IsActive) || Armed && _countdown != null);
                    if (_sleepHeld != hold) { _power.PreventSleep(hold); _sleepHeld = hold; }
                }
                catch { Disarm("app.powerFailed"); KeepAwake = false; ReleaseSleep(); }
            }
        }
    }

    void ReleaseSleep()
    {
        if (!_sleepHeld) return;
        try { _power.PreventSleep(false); _sleepHeld = false; }
        catch { StatusKey = "app.powerFailed"; }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_closed) return;
            _closed = true; Disarm("app.powerCancelled"); KeepAwake = false; ReleaseSleep();
            _jobs.CollectionChanged -= CollectionChanged;
            foreach (var job in _subscribed) job.PropertyChanged -= StageChanged;
            _subscribed.Clear();
        }
    }
}
