namespace Fwda.Tests
{
    using Fwda.Shared;
    using Fwda.Shared.Encryption;
    using YamlDotNet.Serialization;
    using YamlDotNet.Serialization.NamingConventions;

    [Collection(nameof(EnvVarCollection))]
    public class EncryptedStringConverterTests
    {
        private const string TestKey = "testkey";

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
        public void Accepts_ReturnsTrueForEncryptedString()
        {
            var converter = new EncryptedStringConverter();
            Assert.True(converter.Accepts(typeof(EncryptedString)));
            Assert.False(converter.Accepts(typeof(string)));
        }

        [Theory]
        [InlineData(TestKey)]
        [InlineData(null)]
        public void RoundTrip_SerializationDeserialization(string? key)
        {
            WithEnvKey(key, () =>
            {
                var serializer = new SerializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .WithTypeConverter(new EncryptedStringConverter())
                    .Build();

                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .WithTypeConverter(new EncryptedStringConverter())
                    .Build();

                var testClass = new TestClass { Secret = "test secret value" };

                // Serialize
                var yaml = serializer.Serialize(testClass);
                if (key != null)
                {
                    Assert.Contains("ENC:", yaml); // Should be encrypted
                }

                // Deserialize
                var deserialized = deserializer.Deserialize<TestClass>(yaml);
                Assert.Equal("test secret value", deserialized.Secret.Value);
            });
        }

        private class TestClass
        {
            public EncryptedString Secret { get; set; } = new();
        }
    }
}