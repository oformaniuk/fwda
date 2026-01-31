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
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            { "WatcherOptions:SessionSecret", Environment.GetEnvironmentVariable("WATCHER_SESSION_SECRET") },
                            { "WatcherOptions:SessionTimeoutMinutes", Environment.GetEnvironmentVariable("WATCHER_SESSION_TIMEOUT_MINUTES") },
                            { "WatcherOptions:OutputPath", Environment.GetEnvironmentVariable("WATCHER_OUTPUT_PATH") },
                            { "WatcherOptions:LabelPrefix", Environment.GetEnvironmentVariable("WATCHER_LABEL_PREFIX") },
                            { "WatcherOptions:Container", Environment.GetEnvironmentVariable("WATCHER_CONTAINER") }
                        }
                    );
                    config.AddCommandLine(args);
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.Configure<WatcherOptions>(hostContext.Configuration);
                    services.AddSingleton(DockerClientFactory.CreateDockerClient());
                    services.AddSingleton<PortalService>();
                    services.AddSingleton<ConfigService>();
                    services.AddHostedService<WatcherHostedService>();
                })
                .Build()
                .RunAsync();
        }
    }
}