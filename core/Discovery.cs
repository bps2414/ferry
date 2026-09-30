using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Ferry;

public static class Discovery
{
    // Varre a /24 de cada interface IPv4 ativa procurando FTP do PS5 (2121 ftpsrv, 1337 etaHEN).
    public static async Task<(string Ip, int Port)?> FindPs5()
    {
        var mine = new HashSet<string>();
        var prefixes = new HashSet<string>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var a in nic.GetIPProperties().UnicastAddresses)
            {
                if (a.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var ip = a.Address.ToString();
                mine.Add(ip);
                prefixes.Add(ip[..ip.LastIndexOf('.')]);
            }
        }
        var targets = new List<(string Ip, int Port)>();
        foreach (var p in prefixes)
            for (var i = 1; i < 255; i++)
                if (!mine.Contains($"{p}.{i}")) { targets.Add(($"{p}.{i}", 2121)); targets.Add(($"{p}.{i}", 1337)); }

        (string Ip, int Port)? found = null;
        using var stop = new CancellationTokenSource();
        try
        {
            await Parallel.ForEachAsync(targets, new ParallelOptions { MaxDegreeOfParallelism = 128, CancellationToken = stop.Token }, async (t, ct) =>
            {
                try
                {
                    using var c = new TcpClient();
                    using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    limit.CancelAfter(400);
                    await c.ConnectAsync(t.Ip, t.Port, limit.Token);
                    found ??= t;
                    stop.Cancel();
                }
                catch { } // fechada, sem resposta ou cancelada
            });
        }
        catch (OperationCanceledException) { }
        return found;
    }
}
