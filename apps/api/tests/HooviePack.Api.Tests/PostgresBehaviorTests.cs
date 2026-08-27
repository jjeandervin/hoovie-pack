using System.Security.Claims;
using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Application.Services;
using HooviePack.Api.Domain;
using HooviePack.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HooviePack.Api.Tests;

public sealed class PostgresBehaviorTests
{
    [PostgresFact]
    public async Task Join_uses_retrying_execution_strategy_and_is_idempotent_after_redemption()
    {
        await WithIsolatedSchemaAsync(async connectionString =>
        {
            var seed = await SeedAsync(connectionString);
            await using var db = CreateDb(connectionString);
            var service = new FamilyService(db, new IdentityService(db), new FamilyAccessService(db));
            var ownerPrincipal = Principal(seed.OwnerSubject);
            var joinerPrincipal = Principal(seed.JoinerSubject);
            var invite = await service.CreateInviteAsync(
                ownerPrincipal,
                seed.FamilyId,
                new CreateInviteRequest { ExpiresInDays = 7 });

            var first = await service.JoinAsync(
                joinerPrincipal,
                new JoinFamilyRequest { InviteCode = Assert.IsType<string>(invite.InviteCode) });
            var repeated = await service.JoinAsync(
                joinerPrincipal,
                new JoinFamilyRequest { InviteCode = invite.InviteCode! });

            Assert.Equal(seed.FamilyId, first.Id);
            Assert.Equal(FamilyRole.Member, first.Role);
            Assert.Equal(seed.FamilyId, repeated.Id);
            Assert.Equal(1, await db.FamilyMemberships.CountAsync(
                membership => membership.FamilyId == seed.FamilyId && membership.UserId == seed.JoinerId));
            var redeemedInvite = await db.FamilyInvites.SingleAsync(value => value.Id == invite.Id);
            Assert.NotNull(redeemedInvite.RedeemedAt);
            Assert.Equal(seed.JoinerId, redeemedInvite.RedeemedByUserId);
        });
    }

    [PostgresFact]
    public async Task Concurrent_reaction_toggles_are_serialized_without_unique_key_failures()
    {
        await WithIsolatedSchemaAsync(async connectionString =>
        {
            var seed = await SeedAsync(connectionString);
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<ToggleReactionResponse> ToggleAsync()
            {
                await using var db = CreateDb(connectionString);
                var service = new ReactionService(db, new IdentityService(db), new FamilyAccessService(db));
                await gate.Task;
                return await service.ToggleAsync(Principal(seed.OwnerSubject), seed.PostId, "paw");
            }

            var firstTask = ToggleAsync();
            var secondTask = ToggleAsync();
            gate.SetResult();
            var results = await Task.WhenAll(firstTask, secondTask);

            Assert.Contains(results, result => result.Added);
            Assert.Contains(results, result => !result.Added);
            await using var verificationDb = CreateDb(connectionString);
            Assert.False(await verificationDb.Reactions.AnyAsync(
                reaction => reaction.PostId == seed.PostId &&
                    reaction.UserId == seed.OwnerId &&
                    reaction.Type == ReactionType.Paw));
        });
    }

    private static async Task<TestSeed> SeedAsync(string connectionString)
    {
        await using var db = CreateDb(connectionString);
        await db.Database.MigrateAsync();
        var owner = CreateUser($"postgres-owner-{Guid.NewGuid():N}");
        var joiner = CreateUser($"postgres-joiner-{Guid.NewGuid():N}");
        var family = new Family
        {
            Name = "PostgreSQL Pack",
            Slug = $"postgres-pack-{Guid.NewGuid():N}",
            CreatedByUserId = owner.Id,
            CreatedByUser = owner
        };
        var post = new Post
        {
            FamilyId = family.Id,
            Family = family,
            AuthorUserId = owner.Id,
            AuthorUser = owner,
            Content = "Concurrent paws"
        };
        db.Users.AddRange(owner, joiner);
        db.Families.Add(family);
        db.FamilyMemberships.Add(new FamilyMembership
        {
            FamilyId = family.Id,
            Family = family,
            UserId = owner.Id,
            User = owner,
            Role = FamilyRole.Owner
        });
        db.Posts.Add(post);
        await db.SaveChangesAsync();
        return new TestSeed(
            owner.Id,
            owner.AuthProviderUserId,
            joiner.Id,
            joiner.AuthProviderUserId,
            family.Id,
            post.Id);
    }

    private static AppDbContext CreateDb(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure())
            .Options;
        return new AppDbContext(options);
    }

    private static async Task WithIsolatedSchemaAsync(Func<string, Task> action)
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(PostgresFactAttribute.ConnectionStringVariable)
            ?? throw new InvalidOperationException("PostgreSQL test connection string is unavailable.");
        var schema = $"hp_test_{Guid.NewGuid():N}";
        await ExecuteSchemaCommandAsync(baseConnectionString, $"CREATE SCHEMA \"{schema}\"");
        var testConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            SearchPath = schema
        }.ConnectionString;

        try
        {
            await action(testConnectionString);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteSchemaCommandAsync(baseConnectionString, $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE");
        }
    }

    private static async Task ExecuteSchemaCommandAsync(string connectionString, string commandText)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(commandText, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static AppUser CreateUser(string subject) => new()
    {
        AuthProviderUserId = subject,
        Email = $"{subject}@example.test",
        DisplayName = subject,
        LastSeenAt = DateTimeOffset.UtcNow
    };

    private static ClaimsPrincipal Principal(string subject) => new(
        new ClaimsIdentity([new Claim("sub", subject)], "test"));

    private sealed record TestSeed(
        Guid OwnerId,
        string OwnerSubject,
        Guid JoinerId,
        string JoinerSubject,
        Guid FamilyId,
        Guid PostId);
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public const string ConnectionStringVariable = "HOOVIEPACK_TEST_POSTGRES";

    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringVariable)))
        {
            Skip = $"Set {ConnectionStringVariable} to run PostgreSQL integration tests.";
        }
    }
}
