using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Ferry;

public sealed class PkgInstallException(Message diagnostic, bool mayHaveSubmitted, Exception? inner = null) : Exception(diagnostic.Render(), inner)
{
    public Message Diagnostic { get; } = diagnostic;
    public bool MayHaveSubmitted { get; } = mayHaveSubmitted;
}

public static class PkgInstaller
{
    public static async Task<Message> TestAsync(Settings settings, CancellationToken ct = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using var client = new TcpClient();
        await client.ConnectAsync(settings.Host, settings.DpiPort, deadline.Token);
        return new("core.pkg.connected");
    }

    public static async Task InstallAsync(string host, int port, string path, CancellationToken ct = default)
    {
        if (port is < 1 or > 65535 || !path.StartsWith('/') || path.Split('/').Any(p => p is "." or ".." || p.Any(char.IsControl)))
            throw new PkgInstallException(new("core.pkg.path"), false);
        var request = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { url = path }));
        // DPI v1 reads into a 1024-byte buffer; retain room for its string terminator.
        if (request.Length > 1023) throw new PkgInstallException(new("core.pkg.path"), false);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using var client = new TcpClient();
        var written = false;
        try
        {
            await client.ConnectAsync(host, port, deadline.Token);
            await using var stream = client.GetStream();
            written = true; // A failed write can still have reached the peer.
            await stream.WriteAsync(request, deadline.Token);
            var buffer = new byte[4096];
            var count = 0;
            while (count < buffer.Length)
            {
                var got = await stream.ReadAsync(buffer.AsMemory(count), deadline.Token);
                if (got == 0) break;
                count += got;
                try
                {
                    using var response = JsonDocument.Parse(buffer.AsMemory(0, count));
                    if (!response.RootElement.TryGetProperty("res", out var result))
                        throw new PkgInstallException(new("core.pkg.response"), true);
                    int code;
                    if (result.ValueKind == JsonValueKind.Number && result.TryGetInt32(out code) || result.ValueKind == JsonValueKind.String && int.TryParse(result.GetString(), out code))
                    {
                        if (code != 0) throw new PkgInstallException(new("core.pkg.rejected", code), false);
                        return;
                    }
                    throw new PkgInstallException(new("core.pkg.response"), true);
                }
                catch (JsonException) { } // TCP messages may arrive in fragments.
            }
            throw new PkgInstallException(new("core.pkg.response"), true);
        }
        catch (PkgInstallException) { throw; }
        catch (Exception e)
        {
            throw new PkgInstallException(new("core.pkg.connectionFailed", e.Message), written, e);
        }
    }
}
