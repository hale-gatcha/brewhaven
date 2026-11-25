using System.Security.Cryptography;
using System.Text;

namespace BrewHaven.Helpers;

public static class PasswordHasher
{
    public static string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] bytes = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return $"{Convert.ToHexString(salt)}.{Convert.ToHexString(bytes)}";
    }

    public static bool Verify(string password, string storedHash)
    {
        string[] parts = storedHash.Split('.');
        if (parts.Length != 2) return false;
        try
        {
            byte[] salt = Convert.FromHexString(parts[0]);
            byte[] expected = Convert.FromHexString(parts[1]);
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }
}
