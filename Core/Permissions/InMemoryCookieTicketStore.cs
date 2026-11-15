using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DiscordBot.Core.Permissions;

/// <summary>
/// Server-side in-memory ticket store so SignOutAsync immediately revokes cookie tickets.
/// </summary>
public sealed class InMemoryCookieTicketStore(TimeProvider timeProvider) : ITicketStore
{
    private sealed record StoredTicket(AuthenticationTicket Ticket, DateTimeOffset ExpiresUtc);

    private readonly ConcurrentDictionary<string, StoredTicket> _tickets = new();

    public Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        PruneExpired();
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expiresUtc = ticket.Properties.ExpiresUtc ?? timeProvider.GetUtcNow().AddDays(7);
        _tickets[key] = new StoredTicket(ticket, expiresUtc);
        return Task.FromResult(key);
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        var expiresUtc = ticket.Properties.ExpiresUtc ?? timeProvider.GetUtcNow().AddDays(7);
        _tickets[key] = new StoredTicket(ticket, expiresUtc);
        return Task.CompletedTask;
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        if (!_tickets.TryGetValue(key, out var entry)) return Task.FromResult<AuthenticationTicket?>(null);

        if (entry.ExpiresUtc <= timeProvider.GetUtcNow())
        {
            _tickets.TryRemove(key, out _);
            return Task.FromResult<AuthenticationTicket?>(null);
        }

        return Task.FromResult<AuthenticationTicket?>(entry.Ticket);
    }

    public Task RemoveAsync(string key)
    {
        _tickets.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    private void PruneExpired()
    {
        var now = timeProvider.GetUtcNow();
        foreach (var kvp in _tickets)
        {
            if (kvp.Value.ExpiresUtc <= now)
            {
                _tickets.TryRemove(kvp.Key, out _);
            }
        }
    }
}
