namespace Fwda.Shared.Encryption;

using System.Buffers;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Helper class for encrypting and decrypting configuration values
/// </summary>
public static class CryptoHelper
{
    private const string EnvKeyName = "CONFIG_ENCRYPTION_KEY";
    private const string EncryptionPrefix = "ENC:";

    // Keep legacy salt for backward compatibility with values encrypted before the rename.
    private static readonly byte[] LegacySalt = "nginx-forward-auth-salt"u8.ToArray();
    private static readonly byte[] CurrentSalt = "fwda-forward-auth-salt"u8.ToArray();

    /// <summary>
    /// Encrypts a plain text string using AES
    /// </summary>
    public static string Encrypt(string plainText)
    {
        var key = GetKey();
        if (key == null)
        {
            return plainText; // No key, return plain text
        }

        var maxByteCount = Encoding.UTF8.GetMaxByteCount(plainText.Length);
        var plainBytes = ArrayPool<byte>.Shared.Rent(maxByteCount);
        try
        {
            var plainTextSpan = plainText.AsSpan();
            var plainBytesSpan = plainBytes.AsSpan();
            var plainLen = Encoding.UTF8.GetBytes(plainTextSpan, plainBytesSpan);
            using var aes = Aes.Create();
            aes.Key = key;
            aes.GenerateIV();
            using var encryptor = aes.CreateEncryptor();
            var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainLen);
            var resultLen = aes.IV.Length + encryptedBytes.Length;
            Span<byte> resultSpan = stackalloc byte[resultLen];
            aes.IV.AsSpan().CopyTo(resultSpan);
            encryptedBytes.AsSpan().CopyTo(resultSpan.Slice(aes.IV.Length));
            ReadOnlySpan<byte> finalResult = resultSpan.Slice(0, resultLen);
            var base64 = Convert.ToBase64String(finalResult);
            return EncryptionPrefix + base64;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(plainBytes);
        }
    }

    /// <summary>
    /// Decrypts an encrypted string using AES, or returns as is if not encrypted or no key
    /// </summary>
    public static string Decrypt(string encryptedText)
    {
        // If no key is configured, we intentionally leave the value unchanged.
        var keyInput = Environment.GetEnvironmentVariable(EnvKeyName);
        if (string.IsNullOrEmpty(keyInput))
        {
            return encryptedText;
        }

        if (!encryptedText.StartsWith(EncryptionPrefix))
        {
            return encryptedText; // Not encrypted, return as is
        }

        var encrypted = encryptedText[EncryptionPrefix.Length..];
        var maxLen = (encrypted.Length * 3 + 3) / 4;
        var encryptedBytes = ArrayPool<byte>.Shared.Rent(maxLen);
        try
        {
            var encryptedBytesSpan = encryptedBytes.AsSpan();
            if (!Convert.TryFromBase64String(encrypted, encryptedBytesSpan, out var encryptedLen))
            {
                throw new FormatException("Invalid base64 in encrypted data");
            }

            // Try current salt first, then fall back to legacy salt for backward compatibility.
            foreach (var key in GetCandidateKeys(keyInput))
            {
                try
                {
                    using var aes = Aes.Create();
                    aes.Key = key;
                    Span<byte> iv = stackalloc byte[16];
                    encryptedBytes.AsSpan(0, 16).CopyTo(iv);
                    aes.IV = iv.ToArray();
                    using var decryptor = aes.CreateDecryptor();
                    var cipherLen = encryptedLen - 16;
                    var cipherBytes = ArrayPool<byte>.Shared.Rent(cipherLen);
                    try
                    {
                        var cipherSpan = cipherBytes.AsSpan(0, cipherLen);
                        encryptedBytes.AsSpan(16, cipherLen).CopyTo(cipherSpan);
                        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherLen);
                        return Encoding.UTF8.GetString(plainBytes.AsSpan());
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(cipherBytes);
                    }
                }
                catch (CryptographicException)
                {
                    // Wrong key (likely because of salt mismatch) — try the next candidate.
                }
            }

            // If neither key worked, surface a deterministic failure.
            throw new CryptographicException("Unable to decrypt value with provided key.");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(encryptedBytes);
        }
    }

    private static byte[]? GetKey()
    {
        var keyInput = Environment.GetEnvironmentVariable(EnvKeyName);
        if (string.IsNullOrEmpty(keyInput))
        {
            return null; // No key set, default to unencrypted
        }

        // Prefer current salt for encryption / normal operation.
        return DeriveKey(keyInput, CurrentSalt);
    }

    private static IEnumerable<byte[]> GetCandidateKeys(string keyInput)
    {
        yield return DeriveKey(keyInput, CurrentSalt);
        yield return DeriveKey(keyInput, LegacySalt);
    }

    private static byte[] DeriveKey(string keyInput, byte[] salt)
    {
        try
        {
            var key = Convert.FromBase64String(keyInput);
            if (key.Length == 32)
            {
                return key; // Valid 32-byte key
            }
        }
        catch
        {
            // Not valid base64, treat as passphrase
        }

        // Derive key from passphrase using PBKDF2
        return Rfc2898DeriveBytes.Pbkdf2(
            keyInput,
            salt,
            iterations: 10000,
            HashAlgorithmName.SHA256,
            outputLength: 32
        );
    }
}