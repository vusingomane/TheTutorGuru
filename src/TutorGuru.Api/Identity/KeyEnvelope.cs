using System.Security.Cryptography;
using System.Text;

namespace TutorGuru.Api.Identity;

// AES-GCM protects persisted Data Protection XML using the existing workspace
// secret. Nonces are fresh for every write and the envelope version is bound as AAD.
public static class KeyEnvelope
{
    private const string Prefix = "aesgcm-v1:";
    private static readonly byte[] Purpose = Encoding.UTF8.GetBytes("TheTutorGuru.Identity.Keys.v1");
    private static byte[] RootKey()
    {
        var secret = Environment.GetEnvironmentVariable("SESSION_SECRET")
            ?? throw new InvalidOperationException("SESSION_SECRET is required to protect Identity keys.");
        if (secret.Length < 32) throw new InvalidOperationException("SESSION_SECRET must contain at least 32 characters.");
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(secret), 32, info: Purpose);
    }
    public static bool Encrypted(string xml) => xml.StartsWith(Prefix, StringComparison.Ordinal);
    public static string Protect(string xml)
    {
        var key = RootKey();
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(12);
            var plain = Encoding.UTF8.GetBytes(xml);
            var cipher = new byte[plain.Length];
            var tag = new byte[16];
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plain, cipher, tag, Purpose);
            return Prefix + Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    public static string Unprotect(string envelope)
    {
        if (!Encrypted(envelope)) throw new InvalidOperationException("Unprotected Identity keys found. Run the explicit Identity migration before serving requests.");
        var payload = Convert.FromBase64String(envelope[Prefix.Length..]);
        if (payload.Length < 28) throw new CryptographicException("Invalid Identity key envelope.");
        var key = RootKey();
        try
        {
            var plain = new byte[payload.Length - 28];
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(payload.AsSpan(0, 12), payload.AsSpan(28), payload.AsSpan(12, 16), plain, Purpose);
            return Encoding.UTF8.GetString(plain);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
