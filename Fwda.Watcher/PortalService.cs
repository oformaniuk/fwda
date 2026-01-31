namespace Fwda.Watcher
{
    using System;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Docker.DotNet;
    using Docker.DotNet.Models;
    using Fwda.Shared;
    using Fwda.Shared.Options;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    public partial class PortalService(IDockerClient client, ILogger<PortalService> logger, IOptions<WatcherOptions> options)
    {
        private readonly WatcherOptions _options = options.Value;

        public async Task<Dictionary<string, PortalOptions>> DiscoverPortals(CancellationToken ct)
        {
            var portals = new Dictionary<string, PortalOptions>();
            var containers = await client.Containers.ListContainersAsync(new ContainersListParameters(), ct);

            foreach (var container in containers)
            {
                var portalNames = ExtractPortalNames(container.Labels);
                foreach (var portalName in portalNames)
                {
                    var portalConfig = GetPortalConfigFromLabels(container.Labels, portalName);
                    if (!IsValidPortalConfig(portalConfig))
                    {
                        continue;
                    }

                    portals[portalName] = portalConfig;
                    LogDiscoveredPortal(logger, portalName, portalConfig.Hostname);
                }
            }

            return portals;
        }

        internal PortalOptions GetPortalConfigFromLabels(IDictionary<string, string> labels, string portalName)
        {
            var prefix = $"{_options.LabelPrefix}.portal.{portalName}.";

            var hostname = GetLabel($"{prefix}hostname", portalName);
            var config = new PortalOptions
            {
                Name = portalName,
                Display = ExpandEnvVars(GetLabel($"{prefix}display", portalName)),
                Hostname = ExpandEnvVars(hostname),
                CookieDomain = ExpandEnvVars(
                        GetLabel($"{prefix}cookie_domain", hostname.Split(".", 2).Last())
                    )
                    .X(static s => s.StartsWith(".") ? s : "." + s),
                Oidc = new OidcOptions
                {
                    Issuer = ExpandEnvVars(GetLabel($"{prefix}oidc.issuer")),
                    ClientId = ExpandEnvVars(GetLabel($"{prefix}oidc.clientId")),
                    ClientSecret = ExpandEnvVars(GetLabel($"{prefix}oidc.clientSecret")),
                    Scopes = ExpandEnvVars(GetLabel($"{prefix}oidc.scopes", "openid,profile,email")).Split(',').Select(s => s.Trim()).ToList()
                }
            };

            return config;

            string GetLabel(string key, string defaultValue = "") => labels.TryGetValue(key, out var value) ? value : defaultValue;
        }

        internal static string ExpandEnvVars(string value)
        {
            // Simple expansion for ${VAR}
            return VarRegex()
                .Replace(
                    value,
                    match =>
                    {
                        var varName = match.Groups[1].Value;
                        return Environment.GetEnvironmentVariable(varName) ?? match.Value;
                    }
                );
        }

        internal IEnumerable<string> ExtractPortalNames(IDictionary<string, string> labels)
        {
            var portalNames = new HashSet<string>();
            var prefix = $"{_options.LabelPrefix}.portal.";
            foreach (var key in labels.Keys)
            {
                if (key.StartsWith(prefix))
                {
                    var afterPrefix = key.Substring(prefix.Length);
                    var portalName = afterPrefix.Split('.')[0];
                    portalNames.Add(portalName);
                }
            }

            return portalNames;
        }

        internal static bool IsValidPortalConfig(PortalOptions config)
        {
            return !string.IsNullOrEmpty(config.Oidc.Issuer) &&
                   !string.IsNullOrEmpty(config.Oidc.ClientId) &&
                   !string.IsNullOrEmpty(config.Oidc.ClientSecret);
        }

        [LoggerMessage(LogLevel.Information, "Discovered portal: {portalName} (hostname: {hostname})")]
        static partial void LogDiscoveredPortal(ILogger<PortalService> logger, string portalName, string hostname);

        [GeneratedRegex(@"\$\{([^}]+)\}", RegexOptions.Compiled)]
        private static partial Regex VarRegex();
    }
}