namespace Fwda.Proxy.Authentication;

using Fwda.Shared.Options;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public partial class CookieOptionsConfigurator(
    IOptionsMonitor<AuthOptions> authOptionsMonitor,
    ILogger<CookieOptionsConfigurator> logger,
    ITicketStore ticketStore) : IConfigureNamedOptions<CookieAuthenticationOptions>
{
    public void Configure(string? name, CookieAuthenticationOptions options)
    {
        var authOptions = authOptionsMonitor.CurrentValue;

        // options.Events.OnRedirectToAccessDenied = static context =>
        // {
        //     context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        //     return Task.CompletedTask;
        // };
        
        // If no name provided or default, configure global cookie defaults
        if (string.IsNullOrEmpty(name) || name == CookieAuthenticationDefaults.AuthenticationScheme)
        {
            options.Cookie.Name = "fwda";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(authOptions.SessionTimeoutMinutes);
            options.SlidingExpiration = true;
            options.LoginPath = "/signin";
            options.SessionStore = ticketStore;

            return;
        }

        // Support named cookie schemes in the form: cookie-{portal}
        const string prefix = "cookie-";
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var portalName = name[prefix.Length..];

        // Try to get portal-specific settings and fall back to defaults
        authOptions.Portals.TryGetValue(portalName, out var portalConfig);
        portalConfig ??= new PortalOptions();

        options.Cookie.Name = $"fwda-{portalName}";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(authOptions.SessionTimeoutMinutes);
        options.SlidingExpiration = true;
        options.SessionStore = ticketStore;
        options.Cookie.Domain = portalConfig.CookieDomain;
    }

    public void Configure(CookieAuthenticationOptions options) => Configure(Options.DefaultName, options);
}