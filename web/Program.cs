using System.Globalization;
using System.Text.Json;
using Ferry;
using Ferry.Web;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

// Números como no app Windows ("62,4"), sem depender do ICU do sistema (InvariantGlobalization).
var br = (CultureInfo)CultureInfo.InvariantCulture.Clone();
(br.NumberFormat.NumberDecimalSeparator, br.NumberFormat.NumberGroupSeparator) = (",", ".");
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = br;

var port = int.TryParse(Environment.GetEnvironmentVariable("FERRY_PORT"), out var p) && p is > 0 and < 65536 ? p : 8021;
var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.Logging.SetMinimumLevel(LogLevel.Warning);
// chaves do cookie na pasta de dados: o login sobrevive a reiniciar o container
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Settings.AppDir, "keys"))).SetApplicationName("Ferry");
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "ferry";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // rede de casa é http
    o.ExpireTimeSpan = TimeSpan.FromDays(30);
    o.SlidingExpiration = true;
    // API: 401 em vez de redirecionar para uma página de login
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNameCaseInsensitive = true);
var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

// ---------- Engine ----------
var firstRun = !File.Exists(Settings.FilePath);
var settings = Settings.Load();
// container: a pasta /games (FERRY_GAMES) já vem como pasta monitorada
if (firstRun && Environment.GetEnvironmentVariable("FERRY_GAMES") is { Length: > 0 } games && Directory.Exists(games))
{
    settings.InputFolder = games;
    settings.Save();
}
var hub = new Hub(settings);
var engine = new Engine(settings, hub.Log, hub.AskPassword) { Done = hub.OnDone };
hub.Engine = engine;
engine.Restore();
var stop = app.Lifetime.ApplicationStopping;
var run = Task.Run(() => engine.RunAsync(stop));
app.Lifetime.ApplicationStopped.Register(() => { try { settings.Save(); } catch { } });
hub.Log($"Ferry {typeof(Hub).Assembly.GetName().Version?.ToString(3)} na porta {port} · dados em {Settings.AppDir}");
_ = Task.Run(async () => { if (!await hub.TestConnection()) await hub.Discover(); });

// ---------- API ----------
Auth.Map(app);
var api = app.MapGroup("/api").RequireAuthorization();

api.MapPost("/jobs/{id}/{action}", (string id, string action) => hub.Act(id, action) ? Results.Ok() : Results.NotFound());
api.MapPost("/jobs/clear-finished", () => { engine.ClearFinished(); return Results.Ok(); });
api.MapPost("/jobs/{id}/password", (string id, PasswordAnswer body) => hub.Answer(id, body.Password) ? Results.Ok() : Results.NotFound());
api.MapGet("/jobs/{id}/icon", (string id) => hub.Find(id)?.Icon is { } icon ? Results.File(icon, "image/png") : Results.NotFound());

api.MapGet("/settings", () => Results.Json(settings));
// salva campo a campo, como a janela: o que é inválido volta com a mensagem e não é gravado
api.MapPut("/settings", (JsonElement body) =>
{
    var errors = new Dictionary<string, string>();
    foreach (var f in body.EnumerateObject())
    {
        var v = f.Value;
        string? Str() => v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        switch (f.Name.ToLowerInvariant())
        {
            case "host":
                if (Str() is { Length: > 0 } h && !h.Any(char.IsWhiteSpace)) settings.Host = h;
                else errors["host"] = "Informe o IP ou o nome do PS5, sem espaços.";
                break;
            case "port":
                if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && n is >= 1 and <= 65535) settings.Port = n;
                else errors["port"] = "A porta é um número de 1 a 65535.";
                break;
            case "user": settings.User = Str() ?? ""; break;
            case "password": settings.Password = Str() ?? ""; break;
            case "inputfolder":
                if (Str() is { } d && (d == "" || Directory.Exists(d))) settings.InputFolder = d;
                else errors["inputFolder"] = "Essa pasta não existe no servidor.";
                break;
            case "remotedir":
                if (Str() is { Length: > 0 } r && r.StartsWith('/')) settings.RemoteDir = r;
                else errors["remoteDir"] = "Caminho no PS5, começando com /.";
                break;
            case "imagedir":
                if (Str() is { } i && i.StartsWith('/') && !i.Any(char.IsControl)) settings.ImageDir = i;
                else errors["imageDir"] = "Caminho no PS5, começando com / (ex.: /mnt/ext1/homebrew).";
                break;
            case "connections":
                if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var c) && c is >= 1 and <= 8) settings.Connections = c;
                else errors["connections"] = "De 1 a 8 conexões.";
                break;
            case "deleteoriginal":
                if (v.ValueKind is JsonValueKind.True or JsonValueKind.False) settings.DeleteOriginal = v.GetBoolean();
                break;
            case "knownpasswords":
                if (v.ValueKind == JsonValueKind.Array) settings.KnownPasswords = [.. v.EnumerateArray().Select(x => x.GetString()?.Trim() ?? "").Where(x => x.Length > 0)];
                break;
        }
    }
    try { settings.Save(); }
    catch (Exception e) { hub.Log("Falha ao salvar as configurações: " + e.Message); errors["save"] = e.Message; }
    return Results.Json(new { errors, settings });
});

api.MapPost("/ps5/test", async () => { var ok = await hub.TestConnection(); return Results.Json(new { ok, message = hub.TestMessage }); });
api.MapPost("/ps5/discover", async () => Results.Json(new { message = await hub.Discover() }));
api.MapPost("/ps5/use-found", async () =>
{
    if (hub.Found is not { } f) return Results.NotFound();
    hub.Found = null;
    (settings.Host, settings.Port) = (f.Ip, f.Port);
    settings.Save();
    await hub.TestConnection();
    return Results.Ok();
});
api.MapPost("/ps5/dismiss-found", () => { hub.Found = null; return Results.Ok(); });

api.MapGet("/log", () => Results.Text(string.Join("\n", hub.LogSince(0).Select(l => l.Text))));
api.MapPost("/log/clear", () => { hub.ClearLog(); return Results.Ok(); });

Uploads.Map(api, hub);

// Progresso ao vivo (SSE): fila quando muda (até 4x/s, como o Job.Report), linhas novas do log e avisos.
api.MapGet("/events", async (HttpContext c) =>
{
    c.Response.Headers.ContentType = "text/event-stream";
    c.Response.Headers.CacheControl = "no-cache";
    c.Response.Headers["X-Accel-Buffering"] = "no"; // proxy reverso não segura o stream
    var ct = c.RequestAborted;
    string? last = null;
    long seq = hub.LastSeq, logSeq = hub.LogSince(0).LastOrDefault()?.Seq ?? 0;
    var ping = DateTime.UtcNow;
    async Task Send(string ev, string data) { await c.Response.WriteAsync($"event: {ev}\ndata: {data}\n\n", ct); }
    try
    {
        while (!ct.IsCancellationRequested)
        {
            var snap = hub.Snapshot();
            if (snap != last) { await Send("state", snap); last = snap; }
            foreach (var l in hub.LogSince(logSeq)) { await Send("log", JsonSerializer.Serialize(l.Text)); logSeq = l.Seq; }
            foreach (var n in hub.NoticesSince(seq)) { await Send("notice", JsonSerializer.Serialize(new { n.Kind, n.Title, n.Text }, JsonSerializerOptions.Web)); seq = n.Seq; }
            if (DateTime.UtcNow - ping > TimeSpan.FromSeconds(20)) { await c.Response.WriteAsync(": ping\n\n", ct); ping = DateTime.UtcNow; }
            await c.Response.Body.FlushAsync(ct);
            await Task.Delay(250, ct);
        }
    }
    catch (OperationCanceledException) { }
});

// ---------- interface (embutida no binário) ----------
var ui = typeof(Hub).Assembly;
app.MapGet("/{**path}", (string? path) =>
{
    var name = "ui/" + (string.IsNullOrEmpty(path) ? "index.html" : path);
    if (ui.GetManifestResourceStream(name) is not { } s) return Results.NotFound();
    var type = Path.GetExtension(name) switch
    {
        ".html" => "text/html; charset=utf-8", ".css" => "text/css; charset=utf-8", ".js" => "text/javascript; charset=utf-8",
        ".svg" => "image/svg+xml", ".ttf" => "font/ttf", ".png" => "image/png", ".ico" => "image/x-icon", _ => "application/octet-stream",
    };
    return Results.Stream(s, type);
}).AllowAnonymous();

app.Run();
await run;

record PasswordAnswer(string? Password);
