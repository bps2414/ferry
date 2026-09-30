using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Ferry;

static class WebhookChecks
{
    public static async Task RunAsync()
    {
        var checks = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("Webhook: " + name);
            checks++;
            Console.WriteLine("OK " + name);
        }
        var folder = Path.Combine(Path.GetTempPath(), "ferry-webhook-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var original = Settings.FilePath;
        try
        {
            Settings.FilePath = Path.Combine(folder, "settings.json");
            File.WriteAllText(Settings.FilePath, "{\"Host\":\"127.0.0.1\"}");
            var settings = Settings.Load();
            Check(!settings.WebhookEnabled && settings.WebhookUrl == "" && settings.WebhookAutoLocale == "en" && settings.Host == "127.0.0.1", "legacy configuration is disabled and preserved");
            foreach (var value in new[] { "", "ftp://localhost/topic", "file:///tmp/topic", "http://user:pass@localhost/topic", "http://localhost/a#fragment", "http://localhost/a\r\nX:evil", "relative/path" })
                Check(!Webhooks.IsValidUrl(value), "reject invalid URL " + value.Replace('\r', ' ').Replace('\n', ' '));
            Check(Webhooks.IsValidUrl("http://127.0.0.1:12345/topic") && Webhooks.IsValidUrl("https://ntfy.sh/topic"), "HTTP local and HTTPS allowed");
            Dictionary<string, Message> Update(object value) => Webhooks.UpdateSettings(settings, JsonSerializer.SerializeToElement(value));
            Check(Update(new { webhookEnabled = true }).ContainsKey("webhookEnabled") && !settings.WebhookEnabled, "cannot enable without a URL");
            Check(Update(new { webhookUrl = "file:///bad", webhookKind = "discord" }).ContainsKey("webhookUrl") && settings.WebhookUrl == "" && settings.WebhookKind == "generic", "invalid update preserves the entire previous configuration");
            Check(Update(new { webhookKind = "unsupported" }).ContainsKey("webhookKind") && Update(new { webhookAutoLocale = "ja" }).ContainsKey("webhookAutoLocale") && Update(new { webhookEnabled = "true" }).ContainsKey("webhookEnabled"), "provider, locale and switch types are validated");

            await using var receiver = new WebhookReceiver();
            settings.WebhookUrl = receiver.Url + "topic?token=url-secret";
            Check(Update(new { webhookEnabled = true, webhookUrl = settings.WebhookUrl, webhookKind = "ntfy" }).Count == 0 && settings.WebhookEnabled, "atomic update is independent of property order");
            settings.WebhookEnabled = false;
            settings.WebhookKind = "generic";
            settings.Password = "ftp-secret";
            settings.KnownPasswords = ["known-secret"];
            var logs = new ConcurrentQueue<Message>();
            await using var webhook = new Webhooks(settings, logs.Enqueue);
            var job = new Job { Key = "test-game", Name = "Ação @everyone.rar", Title = "Título", TitleId = "PPSA12345", Stage = Stage.Verificado, ArchivePassword = "archive-secret" };
            webhook.OnDone(job);
            await Task.Delay(80);
            Check(receiver.Count == 0, "disabled webhook makes no request");

            foreach (var status in new[] { 401, 429, 500, 302 })
            {
                receiver.Reply(status);
                var result = await webhook.TestAsync();
                await receiver.NextAsync();
                Check(!result.Ok && result.Message.Key == "core.webhook.httpFailed" && Equals(result.Message.Args[0], status), "HTTP " + status + " fails without retry or redirect (" + result.Message.Key + ")");
            }
            await using (var timed = new Webhooks(settings, logs.Enqueue))
            {
                var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                receiver.Reply(200, gate: hold.Task);
                var attempt = timed.TestAsync();
                await receiver.NextAsync();
                var result = await attempt;
                Check(!result.Ok && result.Message.Key == "core.webhook.timeout", "timeout returns its specific failure");
                hold.SetResult();
            }
            Check(logs.All(m => !m.Render().Contains("url-secret")), "logs never include URL credentials");

            foreach (var locale in new[] { "pt-BR", "en" })
            {
                settings.Language = locale;
                settings.WebhookEnabled = true;
                settings.WebhookKind = "generic";
                webhook.OnDone(job);
                var request = await receiver.NextAsync();
                using var json = JsonDocument.Parse(request.Body);
                var payload = json.RootElement;
                Check(payload.GetProperty("event").GetString() == "completed" && payload.GetProperty("language").GetString() == locale, "completion event and locale " + locale);
                Check(payload.GetProperty("schemaVersion").GetInt32() == 1 && payload.GetProperty("job").GetProperty("titleId").GetString() == "PPSA12345", "generic schema and game metadata");
                Check(payload.GetProperty("title").GetString() == (locale == "en" ? "Transfer complete" : "Envio concluído"), "localized title " + locale);
            }
            settings.Language = "auto";
            foreach (var locale in new[] { "pt-BR", "en" })
            {
                settings.Language = locale;
                var package = new Job { Key = "package", Name = "Homebrew.pkg", Stage = Stage.InstalacaoSolicitada };
                webhook.OnDone(package);
                using var json = JsonDocument.Parse((await receiver.NextAsync()).Body);
                Check(json.RootElement.GetProperty("event").GetString() == "installation_requested"
                    && json.RootElement.GetProperty("title").GetString() == (locale == "en" ? "Installation requested" : "Instalação solicitada"),
                    "package acceptance has a distinct honest event " + locale);
                var count = receiver.Count;
                package.Stage = Stage.VerifiqueNoPs5;
                webhook.OnDone(package);
                package.Stage = Stage.PacotePronto;
                webhook.OnDone(package);
                await Task.Delay(80);
                Check(receiver.Count == count, "unknown or prepared package never emits completion " + locale);
            }
            settings.Language = "auto";
            settings.WebhookAutoLocale = "pt-BR";
            var test = await webhook.TestAsync();
            using (var json = JsonDocument.Parse((await receiver.NextAsync()).Body))
                Check(test.Ok && json.RootElement.GetProperty("language").GetString() == "pt-BR" && json.RootElement.GetProperty("job").ValueKind == JsonValueKind.Null, "automatic language and explicit test without a game");
            settings.Save();
            var restored = Settings.Load();
            Check(restored.WebhookEnabled && restored.WebhookUrl == settings.WebhookUrl && restored.WebhookAutoLocale == "pt-BR", "configuration survives restart");
            settings.WebhookAutoLocale = "en";
            await using (var windows = new Webhooks(settings, logs.Enqueue, automaticLocale: "pt-BR"))
            {
                await windows.TestAsync();
                using (var json = JsonDocument.Parse((await receiver.NextAsync()).Body))
                    Check(json.RootElement.GetProperty("language").GetString() == "pt-BR", "Windows automatic language is Portuguese");
                settings.Language = "en";
                await windows.TestAsync();
                using (var json = JsonDocument.Parse((await receiver.NextAsync()).Body))
                    Check(json.RootElement.GetProperty("language").GetString() == "en", "explicit language overrides Windows automatic fallback");
            }
            settings.Language = "auto";
            settings.WebhookAutoLocale = "pt-BR";

            job.ArchivePassword = null;
            webhook.OnPassword(job);
            using (var json = JsonDocument.Parse((await receiver.NextAsync()).Body))
                Check(json.RootElement.GetProperty("event").GetString() == "password_required", "first manual password request notifies");
            var before = receiver.Count;
            job.ArchivePassword = "incorrect-secret";
            webhook.OnPassword(job);
            job.Stage = Stage.Pausado;
            webhook.OnDone(job);
            await Task.Delay(80);
            Check(receiver.Count == before, "incorrect passwords and pause do not notify");
            job.Stage = Stage.Erro;
            job.SetDetail(new("core.raw", "ftp-secret known-secret archive-secret " + settings.WebhookUrl));
            webhook.OnDone(job);
            var error = await receiver.NextAsync();
            Check(error.Body.Contains("\"event\":\"error\"") && !error.Body.Contains("secret") && !error.Body.Contains("http://"), "error reports game without raw diagnostics or credentials");

            settings.WebhookKind = "discord";
            settings.WebhookUrl = receiver.Url + "api/webhooks/id/token?wait=false&thread_id=123";
            job.Stage = Stage.Verificado;
            job.Title = new string('界', 3000);
            webhook.OnDone(job);
            var discord = await receiver.NextAsync();
            using (var json = JsonDocument.Parse(discord.Body))
                Check(discord.Path.Contains("wait=true") && !discord.Path.Contains("wait=false") && discord.Path.Contains("thread_id=123") && json.RootElement.GetProperty("allowed_mentions").GetProperty("parse").GetArrayLength() == 0 && json.RootElement.GetProperty("content").GetString()!.Length <= 2000, "Discord confirmation, limits and disabled mentions");
            settings.WebhookKind = "ntfy";
            settings.WebhookUrl = receiver.Url + "topic";
            webhook.OnDone(job);
            var ntfy = await receiver.NextAsync();
            Check(ntfy.Method == "POST" && ntfy.ContentType.StartsWith("text/plain") && Encoding.UTF8.GetByteCount(ntfy.Body) <= 4096 && ntfy.Body.Contains("Envio concluído"), "ntfy topic receives bounded UTF-8 text");

            var failedResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            receiver.Reply(500, gate: failedResponse.Task);
            webhook.OnDone(job);
            await receiver.NextAsync();
            Check(!failedResponse.Task.IsCompleted, "enqueue returns while HTTP is still waiting");
            var failuresBefore = logs.Count(m => m.Key == "core.webhook.httpFailed");
            failedResponse.SetResult();
            await WaitUntilAsync(() => logs.Count(m => m.Key == "core.webhook.httpFailed") > failuresBefore);
            Check(job.Stage == Stage.Verificado, "webhook failure does not change game completion");

            var snapshotSettings = new Settings { WebhookEnabled = true, WebhookKind = "generic", WebhookUrl = receiver.Url, Language = "en" };
            await using (var bounded = new Webhooks(snapshotSettings, logs.Enqueue, capacity: 1))
            {
                var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                receiver.Reply(200, gate: hold.Task);
                bounded.OnDone(job);
                await receiver.NextAsync();
                job.Title = "Snapshot";
                bounded.OnDone(job);
                snapshotSettings.Language = "pt-BR";
                job.Title = "Changed later";
                bounded.OnDone(job);
                Check(logs.Any(m => m.Key == "core.webhook.queueFull"), "full queue drops with a safe diagnostic instead of blocking");
                hold.SetResult();
                using var queued = JsonDocument.Parse((await receiver.NextAsync()).Body);
                Check(queued.RootElement.GetProperty("language").GetString() == "en" && queued.RootElement.GetProperty("job").GetProperty("title").GetString() == "Snapshot", "queued messages keep immutable game and locale snapshots");
            }

            using var stopped = new CancellationTokenSource();
            await using var shutdown = new Webhooks(settings, logs.Enqueue, stopped.Token);
            receiver.Reply(200, gate: new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task);
            shutdown.OnDone(job);
            await receiver.NextAsync();
            stopped.Cancel();
            await shutdown.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
            Check(true, "shutdown cancels in-flight HTTP promptly");

            var unavailable = receiver.Url;
            await receiver.DisposeAsync();
            settings.WebhookUrl = unavailable;
            Check(!(await webhook.TestAsync()).Ok, "connection refused returns a safe failure");
            Check(logs.All(m => !m.Render().Contains("url-secret") && !m.Render().Contains("ftp-secret")), "all logs remain credential-free");
            Console.WriteLine($"Webhook: {checks} checks passed.");
        }
        finally
        {
            Settings.FilePath = original;
            Directory.Delete(folder, true);
        }
    }
    static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!condition()) await Task.Delay(20, stop.Token);
    }
}

sealed class WebhookReceiver : IAsyncDisposable
{
    public record Request(string Method, string Path, string ContentType, string Body);
    readonly HttpListener _listener = new();
    readonly CancellationTokenSource _stop = new();
    readonly Channel<Request> _requests = Channel.CreateUnbounded<Request>();
    readonly ConcurrentQueue<(int Status, TimeSpan Delay, Task? Gate)> _replies = new();
    readonly ConcurrentBag<Task> _handlers = [];
    readonly Task _loop;
    int _count, _disposed;
    public string Url { get; }
    public int Count => Volatile.Read(ref _count);
    public WebhookReceiver()
    {
        var port = new TcpListener(IPAddress.Loopback, 0);
        port.Start();
        Url = $"http://127.0.0.1:{((IPEndPoint)port.LocalEndpoint).Port}/";
        port.Stop();
        _listener.Prefixes.Add(Url);
        _listener.Start();
        _loop = ListenAsync();
    }
    public void Reply(int status, TimeSpan delay = default, Task? gate = null) => _replies.Enqueue((status, delay, gate));
    public async Task<Request> NextAsync() => await _requests.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));
    async Task ListenAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested) _handlers.Add(HandleAsync(await _listener.GetContextAsync().WaitAsync(_stop.Token)));
        }
        catch (Exception) when (_stop.IsCancellationRequested) { }
    }
    async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync(_stop.Token);
            var reply = _replies.TryDequeue(out var scripted) ? scripted : (Status: 200, Delay: TimeSpan.Zero, Gate: (Task?)null);
            Interlocked.Increment(ref _count);
            await _requests.Writer.WriteAsync(new(context.Request.HttpMethod, context.Request.RawUrl!, context.Request.ContentType ?? "", body));
            if (reply.Gate is { } gate) await gate.WaitAsync(_stop.Token);
            await Task.Delay(reply.Delay, _stop.Token);
            context.Response.StatusCode = reply.Status;
            if (reply.Status == 302) context.Response.RedirectLocation = Url + "redirected";
            context.Response.Close();
        }
        catch (Exception) when (_stop.IsCancellationRequested) { }
        catch (HttpListenerException) { } // the caller may have timed out
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        _listener.Close();
        await _loop;
        await Task.WhenAll(_handlers);
        _stop.Dispose();
    }
}
