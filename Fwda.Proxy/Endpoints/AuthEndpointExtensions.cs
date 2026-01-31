namespace Fwda.Proxy.Endpoints;

using System.Security.Claims;
using Fwda.Proxy.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Fwda.Proxy.Serialization;
using AuthenticationService = Fwda.Proxy.Authentication.AuthenticationService;
using OidcConfigurationService = Fwda.Proxy.Authentication.OidcConfigurationService;

internal static partial class AuthEndpointExtensions
{
    public static WebApplication MapAuthEndpoints(this WebApplication app)
    {
        app.MapGet(
            "/",
            static () =>
            {
                var version = typeof(AuthEndpointExtensions).Assembly.GetName().Version?.ToString(3) ?? "unknown";
                var response = new RootResponse("fwda", version, "running");
                return Results.Json(response, AuthJsonContext.Default.RootResponse);
            }
        );

        app.MapMethods(
            "/auth/{portal}",
            [HttpMethods.Get, HttpMethods.Head],
            async (string portal, HttpContext context, AuthenticationService authenticationService, OidcConfigurationService configService, ILogger<Program> logger) =>
            {
                var portalConfig = configService.GetPortal(portal);
                if (portalConfig == null)
                {
                    return Results.Unauthorized();
                }

                // Explicitly authenticate against the portal-specific cookie scheme
                var portalCookieScheme = $"cookie-{portal}";
                var authenticateResult = await context.AuthenticateAsync(portalCookieScheme);

                logger.LogAuthenticationResultForPortalPortalUsingSchemeSchemeSucceededSucceeded(
                    portal,
                    portalCookieScheme,
                    authenticateResult.Succeeded,
                    authenticateResult.Principal?.Identity?.Name
                );

                if (authenticateResult is { Succeeded: true, Principal: not null })
                {
                    // Set the authenticated principal on the context
                    context.User = authenticateResult.Principal;
                }

                if (!authenticationService.IsAuthenticated(context, portal))
                {
                    return Results.Unauthorized();
                }

                // User is authenticated - return 200 OK
                // Add user info headers for the backend
                var user = context.User;
                context.Response.Headers["X-Auth-User"] = user.FindFirst(ClaimTypes.Name)?.Value ?? user.FindFirst("preferred_username")?.Value ?? "unknown";
                context.Response.Headers["X-Auth-Email"] = user.FindFirst(ClaimTypes.Email)?.Value ?? "";
                context.Response.Headers["X-Auth-Subject"] = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

                return Results.Ok();
            }
        );

        app.MapGet("/signin/{portal}", HandleSignIn);

        // Also support /portals/{portal}/signin pattern for nginx compatibility
        app.MapGet("/portals/{portal}/signin", HandleSignIn);

        app.MapGet(
            "/callback/{portal}",
            (string portal, HttpContext context, OidcConfigurationService configService, ILogger<Program> logger) =>
            {
                var portalConfig = configService.GetPortal(portal);
                if (portalConfig == null)
                {
                    logger.LogCallbackReceivedForUnknownPortalPortal(portal);
                    return Results.NotFound($"Portal '{portal}' not found");
                }

                // Try to get returnUrl from claims first, then fall back to portal hostname
                var returnUrl = context.User.FindFirst("returnUrl")?.Value;

                if (string.IsNullOrEmpty(returnUrl))
                {
                    returnUrl = $"https://{portalConfig.Hostname}/";
                    logger.LogNoReturnUrlClaimFoundForPortalPortalRedirectingToDefaultReturnurl(portal, returnUrl);
                }
                else
                {
                    logger.LogRedirectingUserForPortalPortalToReturnUrl(portal, returnUrl);
                }

                return Results.Redirect(returnUrl);
            }
        );

        app.MapGet(
            "/signout/{portal}",
            async (string portal, HttpContext context, OidcConfigurationService configService) =>
            {
                var portalConfig = configService.GetPortal(portal);
                if (portalConfig == null)
                {
                    return Results.NotFound($"Portal '{portal}' not found");
                }

                // Sign out of the per-portal cookie scheme if present, otherwise fallback to default cookie scheme
                var portalCookieScheme = $"cookie-{portal}";
                await context.SignOutAsync(portalCookieScheme);
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

                // Note: we intentionally do not sign out of the external OIDC provider here in tests.

                return Results.Ok("Signed out successfully");
            }
        );

        return app;

        // Shared signin handler
        static async Task<IResult> HandleSignIn(string portal, HttpContext context, AuthenticationService authenticationService, OidcConfigurationService configService, string? returnUrl)
        {
            var portalConfig = configService.GetPortal(portal);
            if (portalConfig == null)
            {
                return Results.NotFound($"Portal '{portal}' not found");
            }

            if (authenticationService.IsAuthenticated(context, portal))
            {
                var redirectUrl = returnUrl ?? AuthenticationService.GetReturnUrl(context);
                return Results.Redirect(redirectUrl);
            }

            // Validate that OIDC is properly configured for this portal
            var oidc = portalConfig.Oidc;
            if (string.IsNullOrWhiteSpace(oidc?.ClientId) || string.IsNullOrWhiteSpace(oidc?.Issuer))
            {
                return Results.Problem(
                    detail: $"OIDC is not properly configured for portal '{portal}'. ClientId and Issuer are required.",
                    statusCode: StatusCodes.Status500InternalServerError
                );
            }

            // Check if the OIDC authentication scheme is actually registered
            // This prevents errors when config is hot-reloaded but schemes aren't re-registered
            var schemeName = $"oidc-{portal}";
            var schemeProvider = context.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
            var scheme = await schemeProvider.GetSchemeAsync(schemeName);

            if (scheme == null)
            {
                return Results.Problem(
                    detail: $"OIDC authentication scheme '{schemeName}' is not registered. The application may need to be restarted to register new or updated portals.",
                    statusCode: StatusCodes.Status500InternalServerError
                );
            }

            var authProps = new AuthenticationProperties
            {
                RedirectUri = returnUrl ?? AuthenticationService.GetReturnUrl(context),
                Items =
                {
                    { "portal", portal }
                }
            };

            return Results.Challenge(authProps, [schemeName]);
        }
    }

    [LoggerMessage(LogLevel.Debug, "Authentication result for portal {portal} using scheme {scheme}: Succeeded={succeeded}, Principal={principal}")]
    static partial void LogAuthenticationResultForPortalPortalUsingSchemeSchemeSucceededSucceeded(this ILogger<Program> logger, string portal, string scheme, bool succeeded, string? principal);

    [LoggerMessage(LogLevel.Warning, "Callback received for unknown portal: {Portal}")]
    static partial void LogCallbackReceivedForUnknownPortalPortal(this ILogger<Program> logger, string Portal);

    [LoggerMessage(LogLevel.Warning, "No returnUrl claim found for portal {Portal}, redirecting to default: {ReturnUrl}")]
    static partial void LogNoReturnUrlClaimFoundForPortalPortalRedirectingToDefaultReturnurl(this ILogger<Program> logger, string Portal, string ReturnUrl);

    [LoggerMessage(LogLevel.Information, "Redirecting user for portal {Portal} to: {ReturnUrl}")]
    static partial void LogRedirectingUserForPortalPortalToReturnUrl(this ILogger<Program> logger, string Portal, string ReturnUrl);
}