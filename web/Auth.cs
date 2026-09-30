using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Ferry.Web;

/// <summary>
/// Um usuário só, criado na primeira abertura (auth.json na pasta de dados, senha com PBKDF2).
/// Sessão por cookie HttpOnly. Esqueceu a senha: apague auth.json e crie de novo.
/// </summary>
public static class Auth
{
    public static string FilePath { get; set; } = Path.Combine(Settings.AppDir, "auth.json");
    const int Iterations = 210_000; // PBKDF2-SHA512 (recomendação OWASP)
    const int MaxFails = 5;
    static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    static readonly ConcurrentDictionary<string, (int fails, DateTime since)> Fails = [];

    record Account(string User, string Salt, string Hash);
    public record Credentials(string? User, string? Password);

    static Account? Load()
    {
        try { return JsonSerializer.Deserialize<Account>(File.ReadAllText(FilePath)); }
        catch { return null; }
    }

    static string Hash(string password, byte[] salt) =>
        Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA512, 32));

    public static void Map(WebApplication app, Settings settings)
    {
        var auth = app.MapGroup("/api/auth").AllowAnonymous();

        auth.MapGet("/state", (HttpContext c) => Results.Json(new { setup = Load() == null, user = c.User.Identity?.IsAuthenticated == true ? c.User.Identity.Name : null, language = settings.Language }));

        // primeira abertura: cria o usuário (só se ainda não existe) e já entra
        auth.MapPost("/setup", async (HttpContext c, Credentials body) =>
        {
            if (Load() != null) return Results.Conflict(WebText.Error(new("web.accountExists")));
            if (Invalid(body) is { } error) return Results.BadRequest(WebText.Error(error));
            var salt = RandomNumberGenerator.GetBytes(16);
            var json = JsonSerializer.Serialize(new Account(body.User!.Trim(), Convert.ToBase64String(salt), Hash(body.Password!, salt)));
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            Secret.WritePrivate(FilePath, Encoding.UTF8.GetBytes(json));
            await SignIn(c, body.User.Trim());
            return Results.Ok();
        });

        auth.MapPost("/login", async (HttpContext c, Credentials body) =>
        {
            var ip = c.Connection.RemoteIpAddress?.ToString() ?? "?";
            if (Fails.TryGetValue(ip, out var f) && f.fails >= MaxFails && DateTime.UtcNow - f.since < Window)
                return Results.Json(WebText.Error(new("web.tooManyAttempts")), statusCode: 429);
            var acc = Load();
            var ok = acc != null && body.User?.Trim() == acc.User && body.Password is { } pw
                && CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(Hash(pw, Convert.FromBase64String(acc.Salt))), Convert.FromBase64String(acc.Hash));
            if (!ok)
            {
                Fails.AddOrUpdate(ip, _ => (1, DateTime.UtcNow), (_, o) => DateTime.UtcNow - o.since < Window ? (o.fails + 1, o.since) : (1, DateTime.UtcNow));
                return Results.Json(WebText.Error(new("web.badCredentials")), statusCode: 401);
            }
            Fails.TryRemove(ip, out _);
            await SignIn(c, acc!.User);
            return Results.Ok();
        });

        auth.MapPost("/logout", async (HttpContext c) => { await c.SignOutAsync(); return Results.Ok(); });
    }

    static Message? Invalid(Credentials b) =>
        string.IsNullOrWhiteSpace(b.User) ? new("web.userRequired")
        : b.Password is not { Length: >= 8 } ? new("web.shortPassword")
        : null;

    static Task SignIn(HttpContext c, string user) =>
        c.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, user)], CookieAuthenticationDefaults.AuthenticationScheme)),
            new AuthenticationProperties { IsPersistent = true });
}
