using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace GameHelper.Services;

/// <summary>
/// Читает POESESSID из локальной базы куков Chrome/Yandex Browser.
/// Поддерживает Chrome v80+ (шифрование AES-256-GCM через DPAPI).
/// </summary>
public static class BrowserCookieReader
{
    private static readonly (string Name, string UserDataPath)[] KnownBrowsers =
    [
        ("Yandex Browser",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Yandex\YandexBrowser\User Data")),
        ("Google Chrome",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Google\Chrome\User Data")),
        ("Microsoft Edge",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\Edge\User Data")),
    ];

    /// <summary>
    /// Ищет POESESSID во всех известных браузерах.
    /// Возвращает (browserName, value) первого найденного или null.
    /// </summary>
    public static (string Browser, string Value)? TryReadPoeSessId()
    {
        foreach (var (name, userDataPath) in KnownBrowsers)
        {
            try
            {
                var value = TryReadFromBrowser(userDataPath);
                if (!string.IsNullOrWhiteSpace(value))
                    return (name, value);
            }
            catch { /* пробуем следующий */ }
        }
        return null;
    }

    private static string? TryReadFromBrowser(string userDataPath)
    {
        if (!Directory.Exists(userDataPath)) return null;

        var aesKey = ReadAesKey(userDataPath);
        if (aesKey is null) return null;

        // Ищем куку во всех профилях (Default, Profile 1, ...)
        foreach (var profile in new[] { "Default", "Profile 1", "Profile 2" })
        {
            var cookieDb = Path.Combine(userDataPath, profile, "Network", "Cookies");
            if (!File.Exists(cookieDb))
                cookieDb = Path.Combine(userDataPath, profile, "Cookies");
            if (!File.Exists(cookieDb)) continue;

            var value = QueryCookieDb(cookieDb, aesKey);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }

    private static byte[]? ReadAesKey(string userDataPath)
    {
        var localStatePath = Path.Combine(userDataPath, "Local State");
        if (!File.Exists(localStatePath)) return null;

        var localState = JsonDocument.Parse(File.ReadAllText(localStatePath));
        if (!localState.RootElement.TryGetProperty("os_crypt", out var osCrypt)) return null;
        if (!osCrypt.TryGetProperty("encrypted_key", out var encKeyEl)) return null;

        var encKeyB64 = encKeyEl.GetString();
        if (string.IsNullOrEmpty(encKeyB64)) return null;

        var encKeyWithPrefix = Convert.FromBase64String(encKeyB64);
        // Первые 5 байт — "DPAPI" (ASCII)
        var encKey = encKeyWithPrefix.Skip(5).ToArray();

        return ProtectedData.Unprotect(encKey, null, DataProtectionScope.CurrentUser);
    }

    private static string? QueryCookieDb(string dbPath, byte[] aesKey)
    {
        // Chrome держит файл открытым — копируем во временный
        var tmpPath = Path.Combine(
            Path.GetTempPath(),
            $"gh_cookies_{Path.GetRandomFileName()}.db");

        try
        {
            // FileShare.ReadWrite позволяет читать файл пока браузер держит его открытым
            using (var src = new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var dst = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
                src.CopyTo(dst);

            using var conn = new SqliteConnection($"Data Source={tmpPath};Mode=ReadOnly");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT encrypted_value
                FROM cookies
                WHERE host_key LIKE '%pathofexile.com'
                  AND name = 'POESESSID'
                LIMIT 1
                """;

            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;

            var encryptedValue = (byte[])reader[0];
            return DecryptCookieValue(encryptedValue, aesKey);
        }
        finally
        {
            try { File.Delete(tmpPath); } catch { }
        }
    }

    private static string? DecryptCookieValue(byte[] encryptedValue, byte[] aesKey)
    {
        // Chrome v80+: "v10" или "v11" + 12 байт nonce + ciphertext + 16 байт tag
        if (encryptedValue.Length < 3 + 12 + 16) return null;

        var prefix = Encoding.ASCII.GetString(encryptedValue, 0, 3);
        if (prefix != "v10" && prefix != "v11") return null;

        var nonce      = encryptedValue[3..15];
        var ciphertext = encryptedValue[15..^16];
        var tag        = encryptedValue[^16..];

        using var aes = new AesGcm(aesKey, 16);
        var plaintext = new byte[ciphertext.Length];
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }
}
