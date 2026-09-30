using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Ferry;

/// <summary>Avisos best-effort: nunca espera HTTP nas callbacks da Engine nem altera a fila de jogos.</summary>
public sealed class Webhooks : IAsyncDisposable
{
    public record Result(bool Ok, Message Message);
    record Options(string Kind, string Url, string Locale);
    record Game(string Name, string Title, string TitleId);
    record Payload(int SchemaVersion, string Event, DateTimeOffset OccurredAt, string Language, Game? Job, string Title, string Message);
    record Pending(Options Options, Payload Payload);

    readonly Settings _settings;
    readonly string? _automaticLocale;
    readonly Action<Message> _log;
    readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    readonly CancellationTokenSource _stop;
    readonly CancellationToken _token;
    readonly TimeSpan _timeout;
    readonly Channel<Pending> _queue;
    readonly Task _worker;
    int _disposed;

    public Webhooks(Settings settings, Action<Message> log, CancellationToken stop = default, TimeSpan? timeout = null, int capacity = 64, string? automaticLocale = null)
    {
        _settings = settings;
        _automaticLocale = automaticLocale;
        _log = log;
        _stop = CancellationTokenSource.CreateLinkedTokenSource(stop);
        _token = _stop.Token;
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
        _queue = Channel.CreateBounded<Pending>(new BoundedChannelOptions(capacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        _worker = WorkAsync();
    }

    public static bool IsValidKind(string? kind) => kind is "generic" or "discord" or "ntfy";
    public static bool IsValidUrl(string? value) => !string.IsNullOrEmpty(value) && !value.Any(char.IsWhiteSpace) && !value.Any(char.IsControl)
        && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
        && uri.Host.Length > 0 && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;

    Options Snapshot()
    {
        lock (_settings) return new(_settings.WebhookKind, _settings.WebhookUrl,
            _settings.Language is "pt-BR" or "en" ? _settings.Language : Localization.NormalizeLocale(_automaticLocale ?? _settings.WebhookAutoLocale));
    }

    /// Validate the whole webhook update before applying it, independent of JSON property order.
    public static Dictionary<string, Message> UpdateSettings(Settings settings, JsonElement body)
    {
        var fields = body.EnumerateObject().GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.OrdinalIgnoreCase);
        var errors = new Dictionary<string, Message>();
        if (!fields.Keys.Any(k => k.StartsWith("webhook", StringComparison.OrdinalIgnoreCase))) return errors;
        lock (settings)
        {
            string Read(string key, string fallback, string error)
            {
                if (!fields.TryGetValue(key, out var value)) return fallback;
                if (value.ValueKind == JsonValueKind.String) return value.GetString()!;
                errors[key] = new(error);
                return fallback;
            }
            var enabled = settings.WebhookEnabled;
            if (fields.TryGetValue("webhookEnabled", out var flag))
            {
                if (flag.ValueKind is JsonValueKind.True or JsonValueKind.False) enabled = flag.GetBoolean();
                else errors["webhookEnabled"] = new("core.webhook.invalidEnabled");
            }
            var kind = Read("webhookKind", settings.WebhookKind, "core.webhook.invalidKind");
            var url = Read("webhookUrl", settings.WebhookUrl, "core.webhook.invalidUrl");
            var locale = Read("webhookAutoLocale", settings.WebhookAutoLocale, "core.webhook.invalidLocale");
            if (!IsValidKind(kind)) errors["webhookKind"] = new("core.webhook.invalidKind");
            if (url != "" && !IsValidUrl(url)) errors["webhookUrl"] = new("core.webhook.invalidUrl");
            if (enabled && url == "") errors["webhookEnabled"] = new("core.webhook.urlRequired");
            if (locale is not ("pt-BR" or "en")) errors["webhookAutoLocale"] = new("core.webhook.invalidLocale");
            if (errors.Count == 0) (settings.WebhookEnabled, settings.WebhookKind, settings.WebhookUrl, settings.WebhookAutoLocale) = (enabled, kind, url, locale);
        }
        return errors;
    }

    public void OnDone(Job job)
    {
        if (job.Stage is Stage.Verificado or Stage.Erro) Enqueue(job.Stage == Stage.Verificado ? "completed" : "error", job);
        else if (job.Stage == Stage.InstalacaoSolicitada) Enqueue("installation_requested", job);
    }
    public void OnPassword(Job job)
    {
        // Engine zera a senha antes do primeiro diálogo; tentativas incorretas não repetem o aviso.
        if (job.ArchivePassword == null) Enqueue("password_required", job);
    }
    void Enqueue(string kind, Job job)
    {
        if (!_settings.WebhookEnabled || _token.IsCancellationRequested) return;
        try
        {
            var options = Snapshot();
            if (Invalid(options) is { } invalid) { Log(invalid); return; }
            if (!_queue.Writer.TryWrite(new(options, CreatePayload(options, kind, job)))) Log(new("core.webhook.queueFull"));
        }
        catch { Log(new("core.webhook.unavailable")); }
    }

    Payload CreatePayload(Options options, string kind, Job? job)
    {
        string Clean(string value, int length)
        {
            // Never export passwords, even if a game title contains one accidentally.
            foreach (var secret in _settings.KnownPasswords.Concat(new[] { _settings.Password, job?.ArchivePassword, options.Url }).OfType<string>().Where(s => s.Length > 0).OrderByDescending(s => s.Length))
                value = value.Replace(secret, "[redacted]", StringComparison.Ordinal);
            return Limit(new string(value.Where(c => !char.IsControl(c)).ToArray()), length);
        }
        var game = job == null ? null : new Game(Clean(job.Name, 500), Clean(job.Title, 500), Clean(job.TitleId, 32));
        var name = game?.Title is { Length: > 0 } title ? title : game?.Name ?? "";
        var message = kind switch
        {
            "completed" => new Message("core.webhook.completedText", name),
            "installation_requested" => new Message("core.webhook.installationRequestedText", name),
            "password_required" => new Message("core.webhook.passwordText", game!.Name),
            "error" => new Message("core.webhook.errorText", name, SafeError(job!)),
            _ => new Message("core.webhook.testText")
        };
        var titleKey = kind switch { "completed" => "core.webhook.completedTitle", "installation_requested" => "core.webhook.installationRequestedTitle", "error" => "core.webhook.errorTitle", "password_required" => "core.webhook.passwordTitle", _ => "core.webhook.testTitle" };
        return new(1, kind, DateTimeOffset.UtcNow, options.Locale, game,
            new Message(titleKey).Render(options.Locale), Limit(message.Render(options.Locale), 1000));
    }
    static Message SafeError(Job job) => job.DetailMessage?.Key switch
    {
        "core.passwordMissing" or "core.corruptArchive" or "core.noGame" => new(job.DetailMessage.Key),
        "core.installed" => new("core.webhook.installed"),
        "core.archive.failed" => new("core.webhook.archiveError"),
        _ => new("core.webhook.errorDetail") // FTP/system diagnostics may contain paths or credentials.
    };
    static string Limit(string value, int length)
    {
        if (value.Length <= length) return value;
        var end = length - 1;
        if (char.IsHighSurrogate(value[end - 1])) end--;
        return value[..end] + "…";
    }
    static Message? Invalid(Options options) => !IsValidKind(options.Kind) ? new("core.webhook.invalidKind")
        : !IsValidUrl(options.Url) ? new("core.webhook.invalidUrl") : null;

    /// Explicit test bypasses the enabled switch, but uses exactly the saved provider/URL/locale.
    public async Task<Result> TestAsync(CancellationToken cancellationToken = default)
    {
        if (_token.IsCancellationRequested) return new(false, new("core.webhook.cancelled"));
        var options = Snapshot();
        if (Invalid(options) is { } invalid) return new(false, invalid);
        var result = await SendAsync(new(options, CreatePayload(options, "test", null)), cancellationToken);
        Log(result.Message);
        return result;
    }
    async Task WorkAsync()
    {
        try
        {
            await foreach (var pending in _queue.Reader.ReadAllAsync(_token))
            {
                var result = await SendAsync(pending, _token);
                if (!_token.IsCancellationRequested) Log(result.Message);
            }
        }
        catch (OperationCanceledException) when (_token.IsCancellationRequested) { }
    }
    async Task<Result> SendAsync(Pending pending, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_token, cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            var (options, payload) = pending;
            var url = options.Url;
            if (options.Kind == "discord")
            {
                var builder = new UriBuilder(url);
                var query = builder.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Where(p => !Uri.UnescapeDataString(p.Split('=')[0]).Equals("wait", StringComparison.OrdinalIgnoreCase));
                builder.Query = string.Join('&', query.Append("wait=true"));
                url = builder.Uri.AbsoluteUri;
            }
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = options.Kind switch
            {
                "discord" => JsonContent.Create(new { content = Limit(payload.Title + "\n" + payload.Message, 2000), allowed_mentions = new { parse = Array.Empty<string>() } }),
                "ntfy" => new StringContent(payload.Title + "\n" + payload.Message, Encoding.UTF8, "text/plain"),
                _ => JsonContent.Create(payload, options: JsonSerializerOptions.Web)
            };
            if (options.Kind == "ntfy") request.Headers.Add("Title", "Ferry");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return response.IsSuccessStatusCode ? new(true, new("core.webhook.accepted")) : new(false, new("core.webhook.httpFailed", (int)response.StatusCode));
        }
        catch (OperationCanceledException) { return new(false, new(_token.IsCancellationRequested || cancellationToken.IsCancellationRequested ? "core.webhook.cancelled" : "core.webhook.timeout")); }
        catch { return new(false, new("core.webhook.unavailable")); }
    }
    void Log(Message message) { try { _log(message); } catch { } }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _queue.Writer.TryComplete();
        await _stop.CancelAsync();
        await _worker;
        _http.Dispose();
        _stop.Dispose();
    }
}
