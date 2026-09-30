namespace API.Auth;

/// <summary>
/// Persists the random key used to sign login JWTs, so sessions survive a restart. Stored next to
/// settings.json but never returned by any endpoint (unlike TrangaSettings).
/// </summary>
public static class AuthKeyProvider
{
    private static readonly string KeyFilePath = Path.Join(TrangaSettings.WorkingDirectory, ".authkey");

    public static byte[] GetOrCreateSigningKey()
    {
        if (File.Exists(KeyFilePath))
        {
            byte[] existing = Convert.FromBase64String(File.ReadAllText(KeyFilePath));
            if (existing.Length >= 32)
                return existing;
        }

        byte[] key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        File.WriteAllText(KeyFilePath, Convert.ToBase64String(key));
        return key;
    }
}
