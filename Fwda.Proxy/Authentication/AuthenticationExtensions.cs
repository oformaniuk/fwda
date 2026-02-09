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
                        // Prefer X-Forwarded headers (host/proto/prefix) when present to build the redirect_uri;
                        // otherwise fall back to the request's Host/Scheme/PathBase. This works well when
                        // the app is behind a proxy that sets these headers (preferred) but still handles
                        // direct connections to the app.
                        OnRedirectToIdentityProvider = context =>
                        {
                            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                            var original = context.ProtocolMessage.RedirectUri ?? string.Empty;

                            var req = context.Request;
                            var headers = req.Headers;

                            // Prefer X-Forwarded-Proto and X-Forwarded-Host (take first value if comma-separated)
                            string? xfProto = headers.ContainsKey("X-Forwarded-Proto") ? headers["X-Forwarded-Proto"].ToString().Split(',')[0].Trim() : null;
                            string? xfHost = headers.ContainsKey("X-Forwarded-Host") ? headers["X-Forwarded-Host"].ToString().Split(',')[0].Trim() : null;
                            string? xfPrefix = headers.ContainsKey("X-Forwarded-Prefix") ? headers["X-Forwarded-Prefix"].ToString().Split(',')[0].Trim() : null;

                            // Support the standard Forwarded header (RFC 7239) as an additional fallback
                            if (string.IsNullOrEmpty(xfProto) || string.IsNullOrEmpty(xfHost))
                            {
                                if (headers.ContainsKey("Forwarded"))
                                {
                                    // Take the first forwarded value: "for=..., proto=https; host=example.com"
                                    var first = headers["Forwarded"].ToString().Split(',')[0];
                                    var parts = first.Split(';', StringSplitOptions.RemoveEmptyEntries);
                                    foreach (var p in parts)
                                    {
                                        var pair = p.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
                                        if (pair.Length != 2) continue;
                                        var key = pair[0].Trim();
                                        var val = pair[1].Trim().Trim('"');

                                        if (string.Equals(key, "proto", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(xfProto))
                                        {
                                            xfProto = val;
                                        }

                                        if (string.Equals(key, "host", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(xfHost))
                                        {
                                            xfHost = val;
                                        }
                                    }
                                }
                            }

                            var scheme = !string.IsNullOrEmpty(xfProto) ? xfProto : req.Scheme;
                            var hostHeader = !string.IsNullOrEmpty(xfHost) ? xfHost : (req.Host.HasValue ? req.Host.Value : string.Empty);
                            var basePath = !string.IsNullOrEmpty(xfPrefix) ? xfPrefix : (req.PathBase.HasValue ? req.PathBase.Value : string.Empty);

                            // Normalize basePath (remove trailing slash if present) to avoid '//' when concatenating
                            if (!string.IsNullOrEmpty(basePath) && basePath.EndsWith('/'))
                            {
                                basePath = basePath.TrimEnd('/');
                            }

                            var callback = options.CallbackPath.HasValue ? options.CallbackPath.Value : "/signin-oidc";

                            // Parse hostHeader into HostString to extract host and optional port safely
                            var hostString = new HostString(hostHeader);

                            var uriBuilder = new UriBuilder
                            {
                                Scheme = scheme,
                                Host = hostString.Host ?? string.Empty,
                                Path = string.IsNullOrEmpty(basePath) ? callback : (basePath + callback)
                            };

                            if (hostString.Port.HasValue)
                            {
                                uriBuilder.Port = hostString.Port.Value;
                            }
                            else
                            {
                                // If no explicit port in header, ensure default ports are omitted
                                uriBuilder.Port = -1;
                            }

                            var redirectUri = uriBuilder.Uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.UriEscaped);

                            context.ProtocolMessage.RedirectUri = redirectUri;

                            logger.LogOnRedirectToIdentityProviderForPortalPortalOriginalOriginalModified(portalName, original, redirectUri);

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
