namespace Fwda.Proxy.Endpoints;

using Fwda.Proxy.Models;
using Microsoft.AspNetCore.Http;
using Fwda.Proxy.Serialization;
using OidcConfigurationService = Fwda.Proxy.Authentication.OidcConfigurationService;

internal static class HealthEndpointExtensions
{
    public static WebApplication MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet(
            "/health",
            (OidcConfigurationService configService) =>
            {
                var config = configService.GetCurrentConfig();
                var response = new HealthResponse(
                    "healthy",
                    config.Portals.Count,
                    config.Portals.Keys.ToArray()
                );

                return Results.Json(response, AuthJsonContext.Default.HealthResponse);
            }
        );

        return app;
    }
}