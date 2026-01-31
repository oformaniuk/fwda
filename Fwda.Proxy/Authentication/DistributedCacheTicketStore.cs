namespace Fwda.Proxy.Authentication;

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;

// Simple ITicketStore implementation that stores authentication tickets in IDistributedCache.
// Cookies will contain only a reference key, drastically reducing cookie size and avoiding chunking.
// This version protects the serialized ticket bytes with IDataProtector before storing in the cache.
public class DistributedCacheTicketStore(
    IDistributedCache cache,
    TimeSpan expiration,
    IDataProtectionProvider dataProtectionProvider
)
    : ITicketStore
{
    private readonly IDistributedCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("TicketStore") ?? throw new ArgumentNullException(nameof(dataProtectionProvider));

    public async Task RemoveAsync(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        await _cache.RemoveAsync(key);
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentNullException(nameof(key));
        }

        var bytes = TicketSerializer.Default.Serialize(ticket);

        // protect bytes before storing
        var protectedBytes = _protector.Protect(bytes);

        var options = new DistributedCacheEntryOptions
        {
            SlidingExpiration = expiration
        };

        await _cache.SetAsync(key, protectedBytes, options);
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        var data = await _cache.GetAsync(key);

        if (data == null)
        {
            return null;
        }

        try
        {
            var unprotected = _protector.Unprotect(data);
            return TicketSerializer.Default.Deserialize(unprotected);
        }
        catch
        {
            // If unprotect fails (tampering, key rotation, etc.) treat as missing
            return null;
        }
    }

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = "AuthSession:" + Guid.NewGuid().ToString("N");
        await RenewAsync(key, ticket);
        return key;
    }
}