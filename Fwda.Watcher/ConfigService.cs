namespace Fwda.Watcher
{
    using Fwda.Shared;
    using Fwda.Shared.Encryption;
    using Fwda.Shared.Options;
    using Microsoft.Extensions.Logging;
    using YamlDotNet.Serialization;

    public class ConfigService(ILogger<ConfigService> logger)
    {
        private static readonly ISerializer YamlSerializer = new SerializerBuilder()
            .WithTypeConverter(new EncryptedStringConverter())
            .Build();

        public static AuthOptionsRoot GenerateConfig(Dictionary<string, PortalOptions> portals, string sessionSecret, int sessionTimeout) => new()
        {
            Auth = new AuthOptions
            {
                SessionSecret = sessionSecret,
                SessionTimeoutMinutes = sessionTimeout,
                Portals = portals
            }
        };

        public async Task WriteConfig(AuthOptionsRoot config, string path)
        {
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                await File.WriteAllTextAsync(path, YamlSerializer.Serialize(config));

                logger.LogInformation("Configuration written to {Path}", path);
                logger.LogInformation("Total portals configured: {Count}", config.Auth.Portals.Count);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to write config");
            }
        }
    }
}