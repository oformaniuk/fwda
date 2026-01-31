namespace Fwda.Proxy.Authentication;

using Fwda.Shared.Options;

/// <summary>
/// Service that monitors configuration changes and provides access to portal configurations
/// </summary>
public partial class OidcConfigurationService(
    AuthOptions authOptions,
    ILogger<OidcConfigurationService> logger
)
{
    public AuthOptions GetCurrentConfig() => authOptions;

    public PortalOptions? GetPortal(string portalName)
    {
        var config = GetCurrentConfig();
        return config.Portals.GetValueOrDefault(portalName);
    }

    [LoggerMessage(LogLevel.Information, "OIDC Configuration Service initialized with {count} portals")]
    partial void LogOidcConfigurationServiceInitializedWithCountPortals(int count);

    [LoggerMessage(LogLevel.Information, "Configuration changed detected. New portal count: {count}")]
    partial void LogConfigurationChangedDetectedNewPortalCountCount(int count);

    [LoggerMessage(LogLevel.Information, "Portal available: {name} ({display}) - {hostname}")]
    partial void LogPortalAvailableNameDisplayHostname(string name, string display, string hostname);
}
