using System.Security.Cryptography;
using System.Text.Json;
using System.IO;

namespace MetricBot;

public static class PasswordService
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 210_000;

    private static readonly string SecurityDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MetricBot");

    private static readonly string SecurityPath = Path.Combine(SecurityDirectory, "security.json");

    public static bool IsPasswordSet => File.Exists(SecurityPath) && TryRead(out _);

    public static bool Verify(string password)
    {
        if (!TryRead(out var data))
            return false;

        try
        {
            var salt = Convert.FromBase64String(data.Salt);
            var expected = Convert.FromBase64String(data.Hash);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                data.Iterations,
                HashAlgorithmName.SHA256,
                expected.Length);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    public static void Set(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password cannot be empty.", nameof(password));

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSize);

        var data = new PasswordData
        {
            Salt = Convert.ToBase64String(salt),
            Hash = Convert.ToBase64String(hash),
            Iterations = Iterations,
        };

        Directory.CreateDirectory(SecurityDirectory);
        File.WriteAllText(SecurityPath, JsonSerializer.Serialize(data));
    }

    public static void Remove()
    {
        if (File.Exists(SecurityPath))
            File.Delete(SecurityPath);
    }

    private static bool TryRead(out PasswordData data)
    {
        data = new PasswordData();
        try
        {
            var parsed = JsonSerializer.Deserialize<PasswordData>(File.ReadAllText(SecurityPath));
            if (parsed == null || parsed.Iterations <= 0 ||
                string.IsNullOrWhiteSpace(parsed.Salt) || string.IsNullOrWhiteSpace(parsed.Hash))
                return false;

            data = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class PasswordData
    {
        public string Salt { get; set; } = "";
        public string Hash { get; set; } = "";
        public int Iterations { get; set; }
    }
}
