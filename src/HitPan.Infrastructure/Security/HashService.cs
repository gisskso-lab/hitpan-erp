using System.Security.Cryptography;
using System.Text;

namespace HitPan.Infrastructure.Security;

public interface IHashService
{
    string Hash(string value, string tenantId);
    bool Verify(string value, string tenantId, string hash);
}

public sealed class HashService : IHashService
{
    public string Hash(string value, string tenantId)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("tenantId is required.", nameof(tenantId));
        }

        var key = Encoding.UTF8.GetBytes(tenantId);
        using var hmac = new HMACSHA256(key);
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
        return Convert.ToBase64String(hashBytes);
    }

    public bool Verify(string value, string tenantId, string hash)
    {
        if (hash is null)
        {
            return false;
        }

        var expectedHash = Hash(value, tenantId);
        var hashBytes = Convert.FromBase64String(hash);
        var expectedHashBytes = Convert.FromBase64String(expectedHash);

        return CryptographicOperations.FixedTimeEquals(hashBytes, expectedHashBytes);
    }
}
