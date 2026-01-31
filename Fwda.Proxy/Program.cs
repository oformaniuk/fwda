using Fwda.Proxy.Authentication;
using Fwda.Proxy.Endpoints;
using Fwda.Shared.Encryption;
using Fwda.Shared.Options;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Logging;
using StackExchange.Redis;
using YamlDotNet.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Enable PII logging in development for better debugging
if (builder.Environment.IsDevelopment())
{
    IdentityModelEventSource.ShowPII = true;
}

builder.Configuration.AddEnvironmentVariables();

// Add services
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddSingleton<OidcConfigurationService>();

builder.Configuration.AddInMemoryCollection(
    new Dictionary<string, string?>
    {
        ["ASPNETCORE_FORWARDEDHEADERS_ENABLED"] = "true",
        ["ServiceOptions:Endpoint"] = Environment.GetEnvironmentVariable("SERVICE_ENDPOINT"),
        ["Kestrel:Endpoints:Http:Url"] = $"{Environment.GetEnvironmentVariable("LISTEN_ADDRESS") ?? "0.0.0.0"}:{Environment.GetEnvironmentVariable("LISTEN_PORT") ?? "5005" }",
    }
);

// Add YAML configuration with hot-reload support
var configPath = builder.Configuration["ConfigPath"] ?? "/config/config.yaml";

var deserializer = new DeserializerBuilder()
    .WithTypeConverter(new EncryptedStringConverter())
    .Build();

AuthOptions initialConfig;
using (var configuration = File.OpenText(configPath))
{
    initialConfig = deserializer.Deserialize<AuthOptionsRoot>(configuration).Auth;
}

// Configure Options pattern with change tracking
builder.Services.AddSingleton(initialConfig);
builder.Services.Configure<AuthOptions>(options =>
    {
        // Copy properties from initialConfig to the options instance
        options.SessionTimeoutMinutes = initialConfig.SessionTimeoutMinutes;
        options.Portals = initialConfig.Portals;
    }
);

// Data Protection configuration - must be done before any cookie/auth/ticket-store registrations
var redisConn = builder.Configuration["REDIS_CONNECTION_STRING"] ?? Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING");
if (!string.IsNullOrEmpty(redisConn))
{
    // register connection multiplexer for redis
    var multiplexer = await ConnectionMultiplexer.ConnectAsync(redisConn);
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ => multiplexer);

    // persist data protection keys to Redis
    builder.Services.AddDataProtection()
        .SetApplicationName("fwda")
        .PersistKeysToStackExchangeRedis(multiplexer, "DataProtection-Keys:fwda");
}
else
{
    // fallback to file system path (must be a shared volume across instances in production)
    var keyFolder = new DirectoryInfo(builder.Configuration["DP_KEYS_PATH"] ?? "/keys/dataprotection");
    Directory.CreateDirectory(keyFolder.FullName);

    builder.Services.AddDataProtection()
        .SetApplicationName("fwda")
        .PersistKeysToFileSystem(keyFolder);
}

// Register distributed cache (Redis if configured, otherwise in-memory for single-node)
if (!string.IsNullOrEmpty(redisConn))
{
    // Use StackExchange Redis cache
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConn;
        // Optionally: configure instance name from config
        options.InstanceName = builder.Configuration["REDIS_INSTANCE_NAME"] ?? Environment.GetEnvironmentVariable("REDIS_INSTANCE_NAME") ?? "FwdaForwardAuth:";
    });
}
else
{
    // fallback to in-memory distributed cache
    builder.Services.AddDistributedMemoryCache();
}

// Register the ticket store that persists authentication tickets server-side
builder.Services.AddSingleton<DistributedCacheTicketStore>(sp =>
{
    var cache = sp.GetRequiredService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>();
    var dataProtection = sp.GetRequiredService<IDataProtectionProvider>();
    // Use same expiration as session timeout
    var expiration = TimeSpan.FromMinutes(sp.GetRequiredService<IOptions<AuthOptions>>().Value.SessionTimeoutMinutes);
    return new DistributedCacheTicketStore(cache, expiration, dataProtection);
});

builder.Services.AddSingleton<ITicketStore>(sp => sp.GetRequiredService<DistributedCacheTicketStore>());

// Register CookieOptionsConfigurator - must be done before AddAuthentication
// The configurator is registered for both IConfigureOptions and IConfigureNamedOptions
// to ensure it's invoked for all cookie authentication schemes (default and named)
builder.Services.AddSingleton<CookieOptionsConfigurator>();
builder.Services.AddSingleton<IConfigureOptions<CookieAuthenticationOptions>>(sp =>
    sp.GetRequiredService<CookieOptionsConfigurator>()
);
builder.Services.AddSingleton<IConfigureNamedOptions<CookieAuthenticationOptions>>(sp =>
    sp.GetRequiredService<CookieOptionsConfigurator>()
);

// Remove runtime scheme management; schemes are registered at startup based on current config
builder.Services.AddAuthorization();

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// Enable detailed OIDC logging in development
if (builder.Environment.IsDevelopment())
{
    builder.Logging.AddFilter("Microsoft.AspNetCore.Authentication", LogLevel.Debug);
    builder.Logging.AddFilter("Microsoft.AspNetCore.Authentication.OpenIdConnect", LogLevel.Trace);
}


// Configure cookie authentication
builder.Services.AddAuthentication(static options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        }
    )
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme)
    .ConfigureOidcSchemes(initialConfig);

var app = builder.Build();

// Force /auth responses to stay 401 (never redirect)
app.Use(async (context, next) =>
    {
        await next();

        if (context.Request.Path.StartsWithSegments("/auth"))
        {
            // If anything turned this into a redirect, convert back to 401 with no Location
            if (context.Response.StatusCode is StatusCodes.Status302Found or StatusCodes.Status303SeeOther)
            {
                context.Response.Headers.Remove("Location");
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentLength = 0;
            }
        }
    }
);

// Configure middleware
app
    .UseRouting()
    .UseAuthentication()
    .UseAuthorization();

// Minimal APIs
app
    .MapAuthEndpoints()
    .MapHealthEndpoints();

app.Run();

namespace Fwda.Proxy
{
    public partial class Program;
}
