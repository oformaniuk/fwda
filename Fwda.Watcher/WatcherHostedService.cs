namespace Fwda.Watcher;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Docker.DotNet;
using Docker.DotNet.Models;
using Fwda.Shared;
using Fwda.Shared.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public partial class WatcherHostedService(
    IOptions<WatcherOptions> options,
    IDockerClient client,
    PortalService portalService,
    ConfigService configService,
    ILogger<WatcherHostedService> logger
)
    : BackgroundService
{
    private readonly WatcherOptions _options = options.Value;
    private volatile AuthOptionsRoot? _lastConfig;
    private readonly SemaphoreSlim _semaphore = new (1, 1);
    private readonly SemaphoreSlim _restartSemaphore = new (1, 1);

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await ReloadConfiguration(cancellationToken);

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStartingConfigurationWatcher(logger);
        LogOutputPathOutputPath(logger, _options.OutputPath);

        // Initial discovery
        var portals = await portalService.DiscoverPortals(stoppingToken);
        var config = ConfigService.GenerateConfig(portals, _options.SessionSecret, _options.SessionTimeoutMinutes);
        await configService.WriteConfig(config, _options.OutputPath);
        _lastConfig = config;

        LogWatchingForLabelChanges(logger);

        var messages = new BlockingCollection<Message>();
        var progress = new Progress<Message>(message => messages.Add(message, stoppingToken));

        var box = new StrongBox<(CancellationToken token, BlockingCollection<Message> messages)>((stoppingToken, messages));
        var monitor = client.System.MonitorEventsAsync(new ContainerEventsParameters(), progress, stoppingToken);

        // Use Task.Run to correctly await the async loop instead of Task.Factory.StartNew returning a Task<Task>
        var processor = Task.Run(
            async () =>
            {
                (var token, var collection) = box.Value;

                foreach (var message in collection.GetConsumingEnumerable(token))
                {
                    await HandleContainerEvent(message, token).ConfigureAwait(false);
                }
            },
            stoppingToken
        );

        await Task.WhenAll(monitor, processor);

        messages.CompleteAdding();
    }

    private async Task HandleContainerEvent(Message message, CancellationToken token)
    {
        if (message is not { Type: "container", Action: "start" or "stop" or "die" })
        {
            return;
        }

        await ReloadConfiguration(token);
    }

    private async Task ReloadConfiguration(CancellationToken token)
    {
        try
        {
            var newPortals = await portalService.DiscoverPortals(token);
            var newConfig = ConfigService.GenerateConfig(newPortals, _options.SessionSecret, _options.SessionTimeoutMinutes);

            var shouldRestart = false;

            await _semaphore.WaitAsync(token);
            try
            {
                if (!EqualityComparer<AuthOptionsRoot>.Default.Equals(newConfig, _lastConfig))
                {
                    LogConfigurationChangedUpdating(logger);
                    await configService.WriteConfig(newConfig, _options.OutputPath);
                    // Update last config while holding the semaphore to ensure visibility
                    _lastConfig = newConfig;
                    shouldRestart = true;
                }
            }
            finally
            {
                _semaphore.Release();
            }

            if (shouldRestart)
            {
                // Perform restart outside the main semaphore to avoid blocking other handlers
                await _restartSemaphore.WaitAsync(token);
                try
                {
                    // Retrieve all containers and filter in managed code to ensure we only
                    // match the container name exactly as provided in _options.Container.
                    var containers = (await client.Containers.ListContainersAsync(
                        new ContainersListParameters
                        {
                            All = true
                        },
                        token
                    )).AsEnumerable();

                    // Docker returns container names with a leading '/'. Trim it and compare
                    // using ordinal comparison to avoid partial or culture-sensitive matches.
                    containers = containers
                        .Where(c => (c.Names ?? Array.Empty<string>())
                            .Any(n => n.TrimStart('/').Equals(_options.Container, StringComparison.Ordinal))
                        );

                    foreach (var container in containers)
                    {
                        await client.Containers.RestartContainerAsync(container.ID, new ContainerRestartParameters(), token);
                    }
                }
                finally
                {
                    _restartSemaphore.Release();
                }
            }
        }
        catch (Exception e)
        {
            LogWatcherError(logger, e);
        }
    }

    [LoggerMessage(LogLevel.Information, "Starting Configuration Watcher...")]
    static partial void LogStartingConfigurationWatcher(ILogger<WatcherHostedService> logger);

    [LoggerMessage(LogLevel.Information, "Output path: {outputPath}")]
    static partial void LogOutputPathOutputPath(ILogger<WatcherHostedService> logger, string outputPath);

    [LoggerMessage(LogLevel.Information, "Watching for label changes...")]
    static partial void LogWatchingForLabelChanges(ILogger<WatcherHostedService> logger);

    [LoggerMessage(LogLevel.Information, "Configuration changed, updating...")]
    static partial void LogConfigurationChangedUpdating(ILogger<WatcherHostedService> logger);

    [LoggerMessage(LogLevel.Error, "Watcher error")]
    static partial void LogWatcherError(ILogger<WatcherHostedService> logger, Exception exception);

    [LoggerMessage(LogLevel.Information, "Docker restart requested and completed successfully.")]
    static partial void LogDockerRestarted(ILogger<WatcherHostedService> logger);

    [LoggerMessage(LogLevel.Warning, "Docker restart requested but command failed.")]
    static partial void LogDockerRestartFailed(ILogger<WatcherHostedService> logger);

    [LoggerMessage(LogLevel.Information, "Docker restart not configured for this platform or disabled in configuration.")]
    static partial void LogDockerRestartNotConfigured(ILogger<WatcherHostedService> logger);

    [LoggerMessage(LogLevel.Error, "Docker restart error")]
    static partial void LogDockerRestartError(ILogger<WatcherHostedService> logger, Exception exception);
}