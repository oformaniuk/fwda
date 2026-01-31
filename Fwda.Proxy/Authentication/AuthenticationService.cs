namespace Fwda.Proxy.Authentication;

public partial class AuthenticationService(ILogger<AuthenticationService> logger)
{
    public bool IsAuthenticated(HttpContext context, string portal)
    {
        var authResult = context.User.Identity?.IsAuthenticated ?? false;
        
        LogIsAuthenticated(logger, portal, authResult, context.User.Identity?.Name, context.User.Identity?.AuthenticationType);

        if (!authResult)
        {
            LogUserIsNotAuthenticatedForPortalPortal(logger, portal);
            return authResult;
        }

        // Check if user has the portal claim
        var portalClaim = context.User.FindFirst("portal")?.Value;
        LogPortalClaimValuePortalclaimExpectedExpectedportal(logger, portalClaim, portal);
        
        if (portalClaim == portal)
        {
            LogUserAuthenticatedSuccessfullyForPortalPortal(logger, portal);
            return authResult;
        }

        LogUserAuthenticatedButPortalMismatchExpectedPortalGotPortalClaim(logger, portal, portalClaim);
        return false;
    }

    public static string GetReturnUrl(HttpContext context)
    {
        var scheme = context.Request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? context.Request.Scheme;
        var host = context.Request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? context.Request.Host.ToString();
        var path = context.Request.Headers["X-Forwarded-Uri"].FirstOrDefault() ?? context.Request.Path.ToString();
        
        return $"{scheme}://{host}{path}";
    }

    [LoggerMessage(LogLevel.Warning, "User authenticated but portal mismatch. Expected: {portal}, Got: {portalClaim}")]
    static partial void LogUserAuthenticatedButPortalMismatchExpectedPortalGotPortalClaim(ILogger<AuthenticationService> logger, string? portal, string? portalClaim);

    [LoggerMessage(LogLevel.Debug, "IsAuthenticated check for portal {Portal}: User.Identity.IsAuthenticated={IsAuth}, Identity.Name={Name}, Identity.AuthenticationType={AuthType}")]
    static partial void LogIsAuthenticated(ILogger<AuthenticationService> logger, string Portal, bool IsAuth, string Name, string AuthType);

    [LoggerMessage(LogLevel.Debug, "User is not authenticated for portal {Portal}")]
    static partial void LogUserIsNotAuthenticatedForPortalPortal(ILogger<AuthenticationService> logger, string Portal);

    [LoggerMessage(LogLevel.Debug, "Portal claim value: {PortalClaim}, Expected: {ExpectedPortal}")]
    static partial void LogPortalClaimValuePortalclaimExpectedExpectedportal(ILogger<AuthenticationService> logger, string PortalClaim, string ExpectedPortal);

    [LoggerMessage(LogLevel.Debug, "User authenticated successfully for portal {Portal}")]
    static partial void LogUserAuthenticatedSuccessfullyForPortalPortal(ILogger<AuthenticationService> logger, string Portal);
}
