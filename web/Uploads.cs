using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ferry.Web;

/// <summary>
/// Upload pelo navegador em blocos, com retomada: caiu a conexão ou recarregou a página, continua do byte onde parou.
/// O parcial fica em .ferry-upload/ (a Engine só olha a raiz da pasta) e só ganha o nome final inteiro:
/// assim um .exfat pela metade nunca entra na fila.
/// Destino: a pasta monitorada, se existir (a Engine acha sozinha); senão uploads/ na pasta de dados, adicionada à fila.
/// </summary>
public static class Uploads
{
    public const int MaxChunk = 64 << 20;

    record Meta(string Name, long Size);
    public record Start(string? Name, long Size, long LastModified);

    static string Dir(Settings s) => s.InputFolder is { Length: > 0 } f && Directory.Exists(f) ? f : Path.Combine(Settings.AppDir, "uploads");
    static string Temp(Settings s) => Path.Combine(Dir(s), ".ferry-upload");

    public static void Map(RouteGroupBuilder api, Hub hub)
    {
        api.MapPost("/uploads", (Start body) =>
        {
            var name = body.Name?.Trim() ?? "";
            if (name == "" || name != Path.GetFileName(name) || name.StartsWith('.') || name.Any(char.IsControl) || body.Size < 0)
                return Results.BadRequest(new { error = "Nome de arquivo inválido." });
            var s = hub.Settings;
            var final = Path.Combine(Dir(s), name);
            if (new FileInfo(final) is { Exists: true } fi && fi.Length == body.Size)
            {
                hub.Engine.AddFiles([final]);
                return Results.Ok(new { id = "", offset = body.Size, done = true });
            }
            // mesmo arquivo (nome, tamanho, data) = mesmo id: reenviar continua o parcial
            var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{name}|{body.Size}|{body.LastModified}")))[..16].ToLowerInvariant();
            Directory.CreateDirectory(Temp(s));
            File.WriteAllText(Path.Combine(Temp(s), id + ".json"), JsonSerializer.Serialize(new Meta(name, body.Size)));
            var part = new FileInfo(Path.Combine(Temp(s), id + ".part"));
            return Results.Ok(new { id, offset = part.Exists ? part.Length : 0, done = false });
        });

        api.MapPut("/uploads/{id}", async (HttpContext c, string id, long offset) =>
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[0-9a-f]{16}$")) return Results.NotFound();
            var s = hub.Settings;
            var metaFile = Path.Combine(Temp(s), id + ".json");
            if (!File.Exists(metaFile)) return Results.NotFound();
            var meta = JsonSerializer.Deserialize<Meta>(File.ReadAllText(metaFile))!;
            var partPath = Path.Combine(Temp(s), id + ".part");
            long length;
            await using (var fs = new FileStream(partPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
            {
                // o navegador acha que está em outro ponto (bloco repetido ou perdido): diz onde continuar
                if (fs.Length != offset) return Results.Json(new { offset = fs.Length }, statusCode: 409);
                fs.Seek(0, SeekOrigin.End);
                await c.Request.Body.CopyToAsync(fs, c.RequestAborted);
                length = fs.Length;
            }
            if (length > meta.Size) { File.Delete(partPath); return Results.BadRequest(new { error = "Recebido mais que o tamanho do arquivo." }); }
            if (length < meta.Size) return Results.Ok(new { offset = length, done = false });

            var final = Path.Combine(Dir(s), meta.Name);
            File.Move(partPath, final, true);
            File.Delete(metaFile);
            hub.Log($"{meta.Name} recebido pelo navegador ({Job.Size(meta.Size)})");
            hub.Engine.AddFiles([final]); // fora da pasta monitorada entra na fila; dentro, volta mesmo se tinha sido removido
            return Results.Ok(new { offset = length, done = true });
        }).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(MaxChunk));
    }
}
