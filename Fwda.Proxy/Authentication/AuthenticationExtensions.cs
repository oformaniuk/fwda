namespace Fwda.Proxy.Authentication
{
    using System;
    using System.Security.Claims;
    using System.Threading.Tasks;
    using Fwda.Shared.Options;
    using Microsoft.AspNetCore.Authentication;
    using Microsoft.AspNetCore.Authentication.OpenIdConnect;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;

    internal static partial class AuthenticationExtensions
    {
        // Configure OIDC authentication for each portal
        // Helper is declared as a local function to stay within the top-level program scope
        public static AuthenticationBuilder ConfigureOidcSchemes(
            this AuthenticationBuilder authenticationBuilder,
            AuthOptions initialConfig
        )
        {
            // Registers a per-portal cookie scheme and the corresponding OIDC handler (only when configured)
            foreach ((var portalName, var portalConfig) in initialConfig.Portals)
            {
                // register cookie per portal
                authenticationBuilder.AddCookie($"cookie-{portalName}");

                var oidc = portalConfig.Oidc;
                if (!ShouldRegisterOidc(oidc))
                {
                    continue;
                }

                var schemeName = $"oidc-{portalName}";
                RegisterOpenIdConnectForPortal(authenticationBuilder, schemeName, oidc!, portalName);
            }

            return authenticationBuilder;
        }

        private static bool ShouldRegisterOidc(OidcOptions? oidc)
            =>
                // only register OIDC handler when required settings are present
                !(string.IsNullOrWhiteSpace(oidc?.ClientId) || string.IsNullOrWhiteSpace(oidc?.Issuer));

        private static void RegisterOpenIdConnectForPortal(
            AuthenticationBuilder authenticationBuilder,
            string schemeName,
            OidcOptions oidc,
            string portalName
        )
        {
            authenticationBuilder.AddOpenIdConnect(
                schemeName,
                options =>
                {
                    ConfigureIssuer(options, oidc.Issuer);

                    options.ClientId = oidc.ClientId;
                    options.ClientSecret = oidc.ClientSecret;
                    options.ResponseType = "code";
                    // Do NOT persist tokens into the authentication properties/cookie by default - this bloats the cookie
                    options.SaveTokens = false;
                    // Keep this configurable in your portal config if you truly need tokens persisted
                    options.GetClaimsFromUserInfoEndpoint = true;
                    options.CallbackPath = $"/callback/{portalName}";

                    // Use a per-portal cookie scheme for sign-in to keep sessions separate when multiple portals exist
                    options.SignInScheme = $"cookie-{portalName}";

                    ConfigureScopes(options, oidc);

                    options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                    {
                        NameClaimType = "preferred_username",
                        RoleClaimType = "roles"
                    };

                    options.Events = new OpenIdConnectEvents
                    {
                        OnMessageReceived = context =>
                        {
                            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                            logger.LogOnMessageReceivedForPortalPortalProcessingCallback(portalName);
                            return Task.CompletedTask;
                        },
                        OnTokenValidated = context =>
                        {
                            // Reduce the claims persisted into the cookie by creating a minimal identity.
                            // Keep only the essential claims: name/username, name identifier, roles (if any), portal and returnUrl.
                            var incoming = context.Principal;

                            var identity = new ClaimsIdentity(
                                context.Principal?.Identity?.AuthenticationType ?? "",
                                ClaimTypes.Name,
                                ClaimTypes.Role
                            );

                            // Preferred username / name
                            var preferred = incoming?.FindFirst("preferred_username")?.Value ?? incoming?.FindFirst(ClaimTypes.Name)?.Value;
                            if (!string.IsNullOrEmpty(preferred))
                            {
                                identity.AddClaim(new Claim(ClaimTypes.Name, preferred));
                            }

                            // NameIdentifier
                            var sub = incoming?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? incoming?.FindFirst("sub")?.Value;
                            if (!string.IsNullOrEmpty(sub))
                            {
                                identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, sub));
                            }

                            // Roles
                            var roles = incoming?.FindAll(identity.RoleClaimType);
                            if (roles != null)
                            {
                                foreach (var r in roles)
                                {
                                    identity.AddClaim(new Claim(identity.RoleClaimType, r.Value));
                                }
                            }

                            // Portal claim
                            identity.AddClaim(new Claim("portal", portalName));

                            // ReturnUrl from properties if present
                            var returnUrl = context.Properties?.RedirectUri;
                            if (!string.IsNullOrEmpty(returnUrl))
                            {
                                identity.AddClaim(new Claim("returnUrl", returnUrl));
                            }

                            // Replace principal with the trimmed-down principal
                            context.Principal = new ClaimsPrincipal(identity);

                            return Task.CompletedTask;
                        },
                        OnRemoteFailure = context =>
                        {
                            // Handle the error gracefully
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            context.HandleResponse();

                            return Task.CompletedTask;
                        }
                    };
                }
            );
        }

        private static void ConfigureIssuer(OpenIdConnectOptions options, string? issuer)
        {
            if (string.IsNullOrEmpty(issuer)) return;

            if (issuer.Contains("/.well-known/openid-configuration", StringComparison.OrdinalIgnoreCase))
            {
                options.MetadataAddress = issuer;
            }
            else
            {
                options.Authority = issuer;
            }

            options.RequireHttpsMetadata = issuer.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        private static void ConfigureScopes(OpenIdConnectOptions options, OidcOptions oidc)
        {
            options.Scope.Clear();
            if (oidc.Scopes is not { Count: > 0 })
            {
                return;
            }

            foreach (var scope in oidc.Scopes)
            {
                options.Scope.Add(scope);
            }
        }

        [LoggerMessage(LogLevel.Information, "OnMessageReceived for portal {portal}: Processing callback")]
        static partial void LogOnMessageReceivedForPortalPortalProcessingCallback(this ILogger<Program> logger, string portal);

        [LoggerMessage(LogLevel.Information, "OnRedirectToIdentityProvider for portal {portal}: Original={original}, Modified={modified}")]
        static partial void LogOnRedirectToIdentityProviderForPortalPortalOriginalOriginalModified(this ILogger<Program> logger, string portal, string original, string modified);
    }
}