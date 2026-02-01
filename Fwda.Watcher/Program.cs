namespace Fwda.Watcher
{
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;

    internal static class Program
    {
        private static async Task Main(string[] args)
        {
            await Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((context, config) =>
                {
                    // Explicitly add configuration sources for clarity
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                    config.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true);
                    config.AddEnvironmentVariables();
                    config.AddInMemoryConfig();
                    config.AddCommandLine(args);
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.Configure<WatcherOptions>(hostContext.Configuration.GetSection("WatcherOptions"));
                    services.AddSingleton(DockerClientFactory.CreateDockerClient());
                    services.AddSingleton<PortalService>();
                    services.AddSingleton<ConfigService>();
                    services.AddHostedService<WatcherHostedService>();
                })
                .Build()
                .RunAsync();
        }

        private static IConfigurationBuilder AddInMemoryConfig(this IConfigurationBuilder config)
        {
            var dictionary = new Dictionary<string, string?>();
                    
            if (Environment.GetEnvironmentVariable("WATCHER_SESSION_SECRET") != null)
            {
                dictionary["WatcherOptions:SessionSecret"] = Environment.GetEnvironmentVariable("WATCHER_SESSION_SECRET");
            }
                    
            if (Environment.GetEnvironmentVariable("WATCHER_SESSION_TIMEOUT_MINUTES") != null)
            {
                dictionary["WatcherOptions:SessionTimeoutMinutes"] = Environment.GetEnvironmentVariable("WATCHER_SESSION_TIMEOUT_MINUTES");
            }

            if (Environment.GetEnvironmentVariable("WATCHER_OUTPUT_PATH") != null)
            {
                dictionary["WatcherOptions:OutputPath"] = Environment.GetEnvironmentVariable("WATCHER_OUTPUT_PATH");
            }

            if (Environment.GetEnvironmentVariable("WATCHER_LABEL_PREFIX") != null)
            {
                dictionary["WatcherOptions:LabelPrefix"] = Environment.GetEnvironmentVariable("WATCHER_LABEL_PREFIX");
            }

            if (Environment.GetEnvironmentVariable("WATCHER_CONTAINER") != null)
            {
                dictionary["WatcherOptions:Container"] = Environment.GetEnvironmentVariable("WATCHER_CONTAINER");
            }

            return config.AddInMemoryCollection(dictionary);
        }
    }
}