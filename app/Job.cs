using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PS5Sender;

public enum Stage { AguardandoPartes, NaFila, Extraindo, Enviando, Verificado, Pausado, Cancelado, Erro }

public class Job : INotifyPropertyChanged
{
    public required string Key { get; init; }       // nome-base do grupo (sem sufixo de volume)
    public required string Name { get; init; }
    public string MainFile { get; set; } = "";      // arquivo que o 7z abre
    public List<string> Parts { get; set; } = [];
    public string? ArchivePassword { get; set; }
    public CancellationTokenSource? Cts { get; set; }
    public bool Installed { get; set; } // destino já tinha o jogo publicado (param.json/sfo com nome final)
    public bool Force { get; set; }     // usuário mandou reenviar por cima do jogo instalado

    Stage _stage;
    public Stage Stage
    {
        get => _stage;
        set
        {
            _stage = value;
            foreach (var p in new[] { nameof(Stage), nameof(StageText), nameof(CanPause), nameof(CanResume), nameof(CanCancel), nameof(CanRetry), nameof(IsActive) }) Changed(p);
        }
    }

    public string StageText => Stage switch
    {
        Stage.AguardandoPartes => "Aguardando partes",
        Stage.NaFila => "Na fila",
        Stage.Extraindo => "Lendo arquivo",
        Stage.Enviando => "Enviando",
        Stage.Verificado => "Concluído",
        Stage.Pausado => "Pausado",
        Stage.Cancelado => "Cancelado",
        _ => "Erro",
    };

    public bool IsActive => Stage is Stage.Extraindo or Stage.Enviando;
    public bool CanPause => Stage is Stage.NaFila or Stage.Extraindo or Stage.Enviando;
    public bool CanResume => Stage == Stage.Pausado;
    public bool CanCancel => Stage is Stage.AguardandoPartes or Stage.NaFila or Stage.Extraindo or Stage.Enviando or Stage.Pausado;
    public bool CanRetry => Stage is Stage.Erro or Stage.Cancelado;

    double _progress; public double Progress { get => _progress; set { _progress = value; Changed(); } }
    string _detail = ""; public string Detail { get => _detail; set { _detail = value; Changed(); } }
    string _stats = ""; public string Stats { get => _stats; set { _stats = value; Changed(); } }
    string _file = ""; public string CurrentFile { get => _file; set { _file = value; Changed(); } }
    // do sce_sys/param.sfo e icon0.png de dentro do arquivo ("" / null = não deu para ler)
    string _title = ""; public string Title { get => _title; set { _title = value; Changed(); } }
    string _titleId = ""; public string TitleId { get => _titleId; set { _titleId = value; Changed(); } }
    byte[]? _icon; public byte[]? Icon { get => _icon; set { _icon = value; Changed(); } }
    public double Rate { get; private set; } // bytes/s, lido pelo resumo da janela

    // Chamado de várias threads e a cada poucos KB: atualiza a UI no máximo 4x/s (senão o dispatcher do WPF afoga).
    readonly object _gate = new();
    long _lastBytes; DateTime _lastTime = DateTime.UtcNow, _lastUi;
    public void Report(long done, long total)
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastUi).TotalMilliseconds < 250 && done < total) return;
            _lastUi = now;
            var dt = (now - _lastTime).TotalSeconds;
            if (dt >= 1)
            {
                var inst = Math.Max(0, done - _lastBytes) / dt;
                Rate = Rate == 0 ? inst : Rate * 0.7 + inst * 0.3;
                _lastBytes = done; _lastTime = now;
            }
            Progress = total > 0 ? 100.0 * done / total : 0;
            var eta = Rate > 0 ? " · " + Time((total - done) / Rate) + " restantes" : "";
            Stats = $"{Size(done)} de {Size(total)}" + (Rate > 0 ? $" · {Size((long)Rate)}/s" : "") + eta;
        }
    }

    public void ResetRate() { lock (_gate) { _lastBytes = 0; Rate = 0; _lastTime = DateTime.UtcNow; _lastUi = default; } Stats = ""; CurrentFile = ""; }

    public void Finish(string detail) { Rate = 0; Progress = 100; Stats = ""; CurrentFile = ""; Detail = detail; }

    public static string Size(long b) => b switch
    {
        >= 1L << 30 => $"{b / (double)(1L << 30):0.00} GB",
        >= 1L << 20 => $"{b / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{b / 1024.0:0} KB",
        _ => $"{b} B",
    };

    static string Time(double s) { var t = TimeSpan.FromSeconds(Math.Min(s, 359999)); return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}min" : t.TotalMinutes >= 1 ? $"{t.Minutes}min {t.Seconds:00}s" : $"{t.Seconds}s"; }

    public event PropertyChangedEventHandler? PropertyChanged;
    void Changed([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new(p));
}
