using System.Security.Cryptography;
using System.Text;

namespace Ferry;

/// <summary>
/// Senha do jogo guardada na fila. No Windows, DPAPI (só o usuário do Windows abre). Fora dele (Linux, container),
/// AES-GCM com uma chave aleatória em secret.key na pasta de dados, que só o dono lê.
/// </summary>
public static class Secret
{
    public static string KeyFile { get; set; } = Path.Combine(Settings.AppDir, "secret.key"); // o E2E aponta para outro lugar
    const string Gcm = "gcm:";
    static readonly object KeyLock = new();

    public static string Protect(string text)
    {
        var data = Encoding.UTF8.GetBytes(text);
        if (OperatingSystem.IsWindows()) return Convert.ToBase64String(ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser));
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var enc = new byte[data.Length];
        using (var aes = new AesGcm(Key(), tag.Length)) aes.Encrypt(nonce, data, enc, tag);
        return Gcm + Convert.ToBase64String([.. nonce, .. tag, .. enc]);
    }

    public static string? Unprotect(string? stored)
    {
        try
        {
            if (stored == null) return null;
            if (!stored.StartsWith(Gcm))
                return OperatingSystem.IsWindows() ? Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), null, DataProtectionScope.CurrentUser)) : null;
            var b = Convert.FromBase64String(stored[Gcm.Length..]);
            var dec = new byte[b.Length - 28];
            using (var aes = new AesGcm(Key(), 16)) aes.Decrypt(b.AsSpan(0, 12), b.AsSpan(28), b.AsSpan(12, 16), dec);
            return Encoding.UTF8.GetString(dec);
        }
        catch { return null; } // chave trocada ou outro usuário: pede a senha de novo
    }

    static byte[] Key()
    {
        lock (KeyLock)
        {
            if (File.Exists(KeyFile)) return File.ReadAllBytes(KeyFile);
            Directory.CreateDirectory(Path.GetDirectoryName(KeyFile)!);
            var key = RandomNumberGenerator.GetBytes(32);
            WritePrivate(KeyFile, key);
            return key;
        }
    }

    /// Cria um arquivo novo que só o dono lê (fora do Windows: permissão 600 já na criação).
    public static void WritePrivate(string path, byte[] data)
    {
        var o = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) o.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var fs = new FileStream(path, o);
        fs.Write(data);
    }
}
