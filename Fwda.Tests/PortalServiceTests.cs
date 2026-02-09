namespace Fwda.Tests;

using Docker.DotNet;
using Docker.DotNet.Models;
using Fwda.Shared;
using Fwda.Shared.Options;
using Fwda.Watcher;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

public class PortalServiceTests
{
    private readonly IDockerClient _dockerClient = Substitute.For<IDockerClient>();
    private readonly ILogger<PortalService> _logger = Substitute.For<ILogger<PortalService>>();
    private readonly IOptions<WatcherOptions> _options = Options.Create(new WatcherOptions { LabelPrefix = "fwda.auth" });

    [Fact]
    public async Task DiscoverPortals_ReturnsEmptyDictionary_WhenNoContainers()
    {
        // Arrange
        var containers = new List<ContainerListResponse>();
        _dockerClient.Containers.ListContainersAsync(Arg.Any<ContainersListParameters>(), Arg.Any<CancellationToken>())
            .Returns(containers);

        var service = new PortalService(_dockerClient, _logger, _options);

        // Act
        var result = await service.DiscoverPortals(CancellationToken.None);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task DiscoverPortals_ReturnsPortals_WhenValidLabelsPresent()
    {
        // Arrange
        var labels = new Dictionary<string, string>
        {
            ["fwda.auth.myportal.oidc.issuer"] = "https://issuer.com",
            ["fwda.auth.myportal.oidc.client_id"] = "clientId",
            ["fwda.auth.myportal.oidc.client_secret"] = "secret",
            ["fwda.auth.myportal.hostname"] = "myportal.com",
            ["fwda.auth.myportal.display"] = "My Portal"
        };
        var containers = new List<ContainerListResponse>
        {
            new() { Labels = labels }
        };
        _dockerClient.Containers.ListContainersAsync(Arg.Any<ContainersListParameters>(), Arg.Any<CancellationToken>())
            .Returns(containers);

        var service = new PortalService(_dockerClient, _logger, _options);

        // Act
        var result = await service.DiscoverPortals(CancellationToken.None);

        // Assert
        Assert.Single(result);
        Assert.True(result.ContainsKey("myportal"));
        var portal = result["myportal"];
        Assert.Equal("myportal", portal.Name);
        Assert.Equal("My Portal", portal.Display);
        Assert.Equal("myportal.com", portal.Hostname);
        Assert.Equal("https://issuer.com", portal.Oidc.Issuer);
        Assert.Equal("clientId", portal.Oidc.ClientId);
        Assert.Equal("secret", portal.Oidc.ClientSecret.Value);
    }

    [Fact]
    public async Task DiscoverPortals_SkipsInvalidPortals()
    {
        // Arrange
        var validLabels = new Dictionary<string, string>
        {
            ["fwda.auth.valid.oidc.issuer"] = "https://issuer.com",
            ["fwda.auth.valid.oidc.client_id"] = "clientId",
            ["fwda.auth.valid.oidc.client_secret"] = "secret"
        };
        var invalidLabels = new Dictionary<string, string>
        {
            ["fwda.auth.invalid.oidc.issuer"] = "https://issuer.com",
            // Missing clientId and clientSecret
        };
        var containers = new List<ContainerListResponse>
        {
            new() { Labels = validLabels },
            new() { Labels = invalidLabels }
        };
        _dockerClient.Containers.ListContainersAsync(Arg.Any<ContainersListParameters>(), Arg.Any<CancellationToken>())
            .Returns(containers);

        var service = new PortalService(_dockerClient, _logger, _options);

        // Act
        var result = await service.DiscoverPortals(CancellationToken.None);

        // Assert
        Assert.Single(result);
        Assert.True(result.ContainsKey("valid"));
    }

    [Fact]
    public void ExtractPortalNames_ReturnsUniquePortalNames()
    {
        // Arrange
        var labels = new Dictionary<string, string>
        {
            ["fwda.auth.portal1.oidc.issuer"] = "value1",
            ["fwda.auth.portal2.oidc.client_id"] = "value2",
            ["fwda.auth.portal1.hostname"] = "value3",
            ["fwda1.other.label"] = "value4"
        };
        var service = new PortalService(_dockerClient, _logger, _options);

        // Act
        var result = service.ExtractPortalNames(labels).ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("portal1", result);
        Assert.Contains("portal2", result);
    }

    [Fact]
    public void GetPortalConfigFromLabels_BuildsConfigCorrectly()
    {
        // Arrange
        var labels = new Dictionary<string, string>
        {
            ["fwda.auth.myportal.display"] = "Display Name",
            ["fwda.auth.myportal.hostname"] = "hostname.com",
            ["fwda.auth.myportal.oidc.issuer"] = "https://issuer.com",
            ["fwda.auth.myportal.oidc.client_id"] = "clientId",
            ["fwda.auth.myportal.oidc.client_secret"] = "secret",
            ["fwda.auth.myportal.oidc.scopes"] = "openid, profile"
        };
        var service = new PortalService(_dockerClient, _logger, _options);

        // Act
        var result = service.GetPortalConfigFromLabels(labels, "myportal");

        // Assert
        Assert.Equal("myportal", result.Name);
        Assert.Equal("Display Name", result.Display);
        Assert.Equal("hostname.com", result.Hostname);
        Assert.Equal("https://issuer.com", result.Oidc.Issuer);
        Assert.Equal("clientId", result.Oidc.ClientId);
        Assert.Equal("secret", result.Oidc.ClientSecret.Value);
        Assert.Equal(["openid", "profile"], result.Oidc.Scopes);
    }

    [Fact]
    public void GetPortalConfigFromLabels_UsesDefaults()
    {
        // Arrange
        var labels = new Dictionary<string, string>
        {
            ["fwda.auth.myportal.oidc.issuer"] = "https://issuer.com",
            ["fwda.auth.myportal.oidc.client_id"] = "clientId",
            ["fwda.auth.myportal.oidc.client_secret"] = "secret"
        };
        var service = new PortalService(_dockerClient, _logger, _options);

        // Act
        var result = service.GetPortalConfigFromLabels(labels, "myportal");

        // Assert
        Assert.Equal("myportal", result.Name);
        Assert.Equal("myportal", result.Display); // default to portalName
        Assert.Equal("myportal", result.Hostname); // default to portalName
        Assert.Equal(["openid", "profile", "email"], result.Oidc.Scopes); // default
    }

    [Fact]
    public void ExpandEnvVars_ExpandsVariables()
    {
        // Arrange
        Environment.SetEnvironmentVariable("TEST_VAR", "expanded");

        // Act
        var result = PortalService.ExpandEnvVars("prefix ${TEST_VAR} suffix");

        // Assert
        Assert.Equal("prefix expanded suffix", result);

        // Cleanup
        Environment.SetEnvironmentVariable("TEST_VAR", null);
    }

    [Fact]
    public void ExpandEnvVars_LeavesUnexpandedIfNotSet()
    {
        // Arrange
        // Ensure var is not set
        Environment.SetEnvironmentVariable("NONEXISTENT_VAR", null);

        // Act
        var result = PortalService.ExpandEnvVars("prefix ${NONEXISTENT_VAR} suffix");

        // Assert
        Assert.Equal("prefix ${NONEXISTENT_VAR} suffix", result);
    }

    [Fact]
    public void IsValidPortalConfig_ReturnsTrue_WhenAllRequiredFieldsPresent()
    {
        // Arrange
        var config = new PortalOptions
        {
            Oidc = new OidcOptions
            {
                Issuer = "issuer",
                ClientId = "clientId",
                ClientSecret = "secret"
            }
        };

        // Act
        var result = PortalService.IsValidPortalConfig(config);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValidPortalConfig_ReturnsFalse_WhenIssuerMissing()
    {
        // Arrange
        var config = new PortalOptions
        {
            Oidc = new OidcOptions
            {
                ClientId = "clientId",
                ClientSecret = "secret"
            }
        };

        // Act
        var result = PortalService.IsValidPortalConfig(config);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidPortalConfig_ReturnsFalse_WhenClientIdMissing()
    {
        // Arrange
        var config = new PortalOptions
        {
            Oidc = new OidcOptions
            {
                Issuer = "issuer",
                ClientSecret = "secret"
            }
        };

        // Act
        var result = PortalService.IsValidPortalConfig(config);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidPortalConfig_ReturnsFalse_WhenClientSecretMissing()
    {
        // Arrange
        var config = new PortalOptions
        {
            Oidc = new OidcOptions
            {
                Issuer = "issuer",
                ClientId = "clientId"
            }
        };

        // Act
        var result = PortalService.IsValidPortalConfig(config);

        // Assert
        Assert.False(result);
    }
}
