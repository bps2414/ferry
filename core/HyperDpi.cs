using System.Text.RegularExpressions;
using System.Text.Json;

namespace Ferry;

/// <summary>Instalador HyperDPI (HyperHEN): o PKG já está na pasta fixa do console; um POST vazio com finalize=1 pede a instalação e o stream SSE do WebUI acompanha.</summary>
public static class HyperDpi
{
    public const string Dir = "/user/data/HyperHEN/pkgs";
    public const int MaxName = 96;
    static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    static Uri Url(string host, int port, string path) => new($"http://{host}:{port}{path}");

    /// <summary>Nome aceito pelo servidor: 1..96 caracteres seguros, sem "..", sempre .pkg.</summary>
    public static string Name(string leaf, string suffix)
    {
        var chars = leaf.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray();
        var b = new string(chars);
        if (b.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase)) b = b[..^4];
        while (b.Contains("..")) b = b.Replace("..", "_");
        var tail = "-" + suffix + ".pkg";
        b = b[..Math.Min(b.Length, MaxName - tail.Length)];
        return (b.Length == 0 ? "pkg" : b) + tail;
    }

    public static async Task<Message> TestAsync(Settings s, CancellationToken ct = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var text = await Http.GetStringAsync(Url(s.Host, s.HyperDpiPort, "/ping"), deadline.Token);
        try
        {
            using var doc = JsonDocument.Parse(text);
            var r = doc.RootElement;
            if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String && n.GetString() == "pkg-server")
                return new("core.hyperdpi.connected", r.TryGetProperty("version", out var v) ? v.ToString() : "?", r.TryGetProperty("fw", out var f) ? f.ToString() : "?");
        }
        catch (JsonException) { }
        throw new LocalizedException(new("core.hyperdpi.notService"));
    }

    /// <summary>Devolve o content_id para acompanhar ("" = aceito, mas sem como acompanhar).</summary>
    public static async Task<string> InstallAsync(string host, int port, string remotePath, long size, CancellationToken ct = default)
    {
        var name = remotePath[(remotePath.LastIndexOf('/') + 1)..];
        if (port is < 1 or > 65535 || size <= 0 || !Regex.IsMatch(name, @"^[A-Za-z0-9._-]{1,96}$") || name.Contains(".."))
            throw new PkgInstallException(new("core.hyperdpi.name"), false);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(120)); // o servidor chama o instalador do sistema antes de responder
        var sent = false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Url(host, port, $"/install?name={Uri.EscapeDataString(name)}&offset={size}&total={size}&finalize=1")) { Content = new ByteArrayContent([]) };
            sent = true;
            using var response = await Http.SendAsync(request, deadline.Token);
            var body = await response.Content.ReadAsStringAsync(deadline.Token);
            JsonElement r;
            try { using var doc = JsonDocument.Parse(body); r = doc.RootElement.Clone(); }
            catch (JsonException) { throw new PkgInstallException(new("core.hyperdpi.response"), true); }
            if (r.ValueKind != JsonValueKind.Object) throw new PkgInstallException(new("core.hyperdpi.response"), true);
            string Field(string key) => r.TryGetProperty(key, out var v) ? v.ToString() : "";
            if (r.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True) return Field("content_id");
            var error = Field("error");
            throw new PkgInstallException(error switch
            {
                "size_gap" => new("core.hyperdpi.sizeGap", Field("expected"), Field("got")),
                "init_failed" => new("core.hyperdpi.initFailed", Field("code")),
                _ when (int)response.StatusCode == 503 || error is "initializing" or "busy" => new("core.hyperdpi.busy"),
                _ when error.StartsWith("0x") => new("core.hyperdpi.rejected", error),
                _ => new("core.hyperdpi.error", error),
            }, false);
        }
        catch (PkgInstallException) { throw; }
        catch (Exception e) { throw new PkgInstallException(new("core.hyperdpi.connectionFailed", e.Message), sent, e); }
    }

    /// <summary>Lê o /api/stream do WebUI até "complete" ou "error". Qualquer outra saída (idle, queda, limite de 30 min) é "unknown".</summary>
    public static async Task<(string Phase, string Error)> TrackAsync(string host, int webPort, string contentId, CancellationToken ct)
    {
        using var cap = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cap.CancelAfter(TimeSpan.FromMinutes(30));
        try
        {
            using var response = await Http.SendAsync(new(HttpMethod.Get, Url(host, webPort, "/api/stream")), HttpCompletionOption.ResponseHeadersRead, cap.Token);
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cap.Token));
            var started = DateTime.UtcNow;
            var seen = false;
            while (await reader.ReadLineAsync(cap.Token) is { } line)
            {
                if (!line.StartsWith("data:")) continue;
                using var doc = JsonDocument.Parse(line[5..]);
                var r = doc.RootElement;
                if (r.TryGetProperty("cid", out var c) && c.GetString() is { Length: > 0 } other && other != contentId) continue; // outra instalação
                switch (r.TryGetProperty("phase", out var p) ? p.GetString() : "")
                {
                    case "complete": return ("complete", "");
                    case "error": return ("error", r.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "");
                    case "idle":
                        if (seen || DateTime.UtcNow - started > TimeSpan.FromSeconds(15)) return ("unknown", "");
                        break;
                    default: seen = true; break;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { }
        return ("unknown", "");
    }
}
