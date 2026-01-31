namespace Fwda.Tests;

using Fwda.Shared;
using Fwda.Shared.Encryption;
using Xunit;

[Collection(nameof(EnvVarCollection))]
public class CryptoHelperTests
{
    private const string TestKey = "testkey"; // Simple passphrase for testing

    private static void WithEnvKey(string? key, Action action)
    {
        var previous = Environment.GetEnvironmentVariable("CONFIG_ENCRYPTION_KEY");
        try
        {
            Environment.SetEnvironmentVariable("CONFIG_ENCRYPTION_KEY", key);
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable("CONFIG_ENCRYPTION_KEY", previous);
        }
    }

    [Fact]
    public void Encrypt_WithKey_ReturnsEncryptedString()
    {
        WithEnvKey(TestKey, () =>
        {
            var plainText = "test value";
            var result = CryptoHelper.Encrypt(plainText);
            Assert.NotEqual(plainText, result);
            Assert.StartsWith("ENC:", result);
        });
    }

    [Fact]
    public void Decrypt_WithKey_ReturnsOriginalString()
    {
        WithEnvKey(TestKey, () =>
        {
            var plainText = "test value";
            var encrypted = CryptoHelper.Encrypt(plainText);
            var decrypted = CryptoHelper.Decrypt(encrypted);
            Assert.Equal(plainText, decrypted);
        });
    }

    [Fact]
    public void Encrypt_WithoutKey_ReturnsPlainText()
    {
        WithEnvKey(null, () =>
        {
            var plainText = "test value";
            var result = CryptoHelper.Encrypt(plainText);
            Assert.Equal(plainText, result);
        });
    }

    [Fact]
    public void Decrypt_WithoutKey_ReturnsInput()
    {
        WithEnvKey(null, () =>
        {
            var input = "some text";
            var result = CryptoHelper.Decrypt(input);
            Assert.Equal(input, result);
        });
    }

    [Fact]
    public void Decrypt_PlainText_ReturnsPlainText()
    {
        WithEnvKey(TestKey, () =>
        {
            var plainText = "plain text";
            var result = CryptoHelper.Decrypt(plainText);
            Assert.Equal(plainText, result);
        });
    }

    [Fact]
    public void Decrypt_InvalidBase64_ThrowsException()
    {
        WithEnvKey(TestKey, () =>
        {
            var invalidEncrypted = "ENC:invalidbase64!!!";
            Assert.Throws<FormatException>(() => CryptoHelper.Decrypt(invalidEncrypted));
        });
    }

    [Theory]
    [InlineData("short")]
    [InlineData("a longer test value with spaces")]
    [InlineData("special chars: !@#$%^&*()")]
    [InlineData("")]
    public void EncryptDecrypt_RoundTrip(string value)
    {
        WithEnvKey(TestKey, () =>
        {
            var encrypted = CryptoHelper.Encrypt(value);
            var decrypted = CryptoHelper.Decrypt(encrypted);
            Assert.Equal(value, decrypted);
        });
    }
}