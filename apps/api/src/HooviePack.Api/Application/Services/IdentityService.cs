using System.Security.Claims;
using HooviePack.Api.Domain;
using HooviePack.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HooviePack.Api.Application.Services;

public interface IIdentityService
{
    Task<AppUser> GetCurrentUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
}

public sealed class IdentityService(AppDbContext db) : IIdentityService
{
    public async Task<AppUser> GetCurrentUserAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var subject = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(subject))
        {
            throw ApiException.Unauthorized();
        }

        var now = DateTimeOffset.UtcNow;
        var user = await db.Users.SingleOrDefaultAsync(
            x => x.AuthProviderUserId == subject,
            cancellationToken);

        if (user is null)
        {
            var email = GetClaim(principal, "email", ClaimTypes.Email) ?? string.Empty;
            var displayName = GetClaim(principal, "name", "preferred_username", ClaimTypes.Name)
                ?? (email.Length > 0 ? email.Split('@')[0] : "Pack member");

            user = new AppUser
            {
                AuthProviderUserId = subject,
                Email = Truncate(email, 320),
                DisplayName = Truncate(displayName, 100),
                CreatedAt = now,
                UpdatedAt = now,
                LastSeenAt = now
            };

            db.Users.Add(user);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return user;
            }
            catch (DbUpdateException)
            {
                db.Entry(user).State = EntityState.Detached;
                user = await db.Users.SingleAsync(x => x.AuthProviderUserId == subject, cancellationToken);
            }
        }

        if (now - user.LastSeenAt >= TimeSpan.FromMinutes(5))
        {
            user.LastSeenAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        return user;
    }

    private static string? GetClaim(ClaimsPrincipal principal, params string[] claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var value = principal.FindFirstValue(claimType);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
