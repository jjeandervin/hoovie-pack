using System.Reflection;
using System.Text.Json;
using HooviePack.Api.Application;
using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Application.Services;
using HooviePack.Api.Controllers;
using HooviePack.Api.Domain;
using HooviePack.Api.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace HooviePack.Api.Tests;

public sealed class DogQuoteTests
{
    [Theory]
    [InlineData(0, 25, "page")]
    [InlineData(-1, 25, "page")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(1, -1, "pageSize")]
    [InlineData(1, 101, "pageSize")]
    [InlineData(int.MaxValue, 100, "page")]
    public async Task Invalid_pagination_returns_field_validation(int page, int size, string field)
    {
        await using var db = MemoryDb();
        var error = await Assert.ThrowsAsync<ApiException>(() => new DogQuoteService(db)
            .GetPagedAsync(page, size, null, null, default));
        Assert.Equal(400, error.StatusCode);
        Assert.True(error.Errors!.ContainsKey(field));
    }

    [Fact]
    public async Task Filters_validate_trimmed_lengths_for_browse_and_random()
    {
        await using var db = MemoryDb();
        var service = new DogQuoteService(db);
        var category = new string('c', 101);
        var author = new string('a', 251);
        foreach (var call in new Func<Task>[]
        {
            () => service.GetPagedAsync(1, 25, category, null, default),
            () => service.GetRandomAsync(category, default),
            () => service.GetPagedAsync(1, 25, null, author, default)
        })
            Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(call)).StatusCode);
    }

    [Fact]
    public async Task Application_saves_trim_only_surrounding_text_and_maintain_utc_timestamps()
    {
        await using var db = MemoryDb();
        var quote = new DogQuote { Text = "  A dog,  a friend.\nAlways.  " };
        var before = DateTimeOffset.UtcNow;
        db.DogQuotes.Add(quote);
        db.SaveChanges();
        Assert.Equal("A dog,  a friend.\nAlways.", quote.Text);
        Assert.NotEqual(Guid.Empty, quote.Id);
        Assert.True(quote.IsActive);
        Assert.InRange(quote.CreatedAtUtc, before, DateTimeOffset.UtcNow);
        Assert.Equal(quote.CreatedAtUtc, quote.UpdatedAtUtc);
        var created = quote.CreatedAtUtc;
        quote.Author = "A writer";
        quote.UpdatedAtUtc = DateTimeOffset.MinValue;
        before = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        Assert.Equal(created, quote.CreatedAtUtc);
        Assert.InRange(quote.UpdatedAtUtc, before, DateTimeOffset.UtcNow);
        Assert.Equal(TimeSpan.Zero, quote.UpdatedAtUtc.Offset);
        var updated = quote.UpdatedAtUtc;
        await db.SaveChangesAsync();
        Assert.Equal(updated, quote.UpdatedAtUtc);
        quote.Text = " \t ";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public void Controller_is_authenticated_read_only_and_dto_has_only_public_fields()
    {
        var controller = typeof(DogQuotesController);
        Assert.NotNull(controller.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(controller.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal("api/dog-quotes", controller.GetCustomAttribute<RouteAttribute>()!.Template);
        var actions = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Equal(4, actions.Length);
        Assert.All(actions, action =>
        {
            Assert.NotNull(action.GetCustomAttribute<HttpGetAttribute>());
            Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
        });
        var dto = new DogQuoteDto(Guid.NewGuid(), "A quote", null, null, null, null, null);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(new[] { "author", "category", "id", "sourceUrl", "text", "work", "year" },
            json.RootElement.EnumerateObject().Select(x => x.Name).Order());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("year").ValueKind);
    }

    [PostgresFact]
    public Task Empty_migrated_table_has_expected_endpoint_responses() => WithDatabase(async db =>
    {
        var controller = Controller(db);
        var page = Ok(await controller.List(default));
        Assert.Empty(page.Items);
        Assert.Equal(1, page.Page);
        Assert.Equal(25, page.PageSize);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(0, page.TotalPages);
        Assert.Empty(Ok(await controller.Categories(default)));
        NotFound(await controller.Random(default));
        NotFound(await controller.Get(Guid.NewGuid(), default));
        Assert.False(db.Database.HasPendingModelChanges());
    });

    [PostgresFact]
    public Task Browse_filters_before_paging_and_orders_by_author_year_and_id() => WithDatabase(async db =>
    {
        var early = Quote("Groucho Marx", "Funny", 1900);
        var first = Quote("Groucho Marx", "Funny", 1920);
        var second = Quote("Groucho Marx", "Funny", 1920);
        first.Id = Guid.Parse("00000000-0000-0000-0000-000000000001");
        second.Id = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var last = Quote("Karl Marx", "Funny", 1890);
        db.DogQuotes.AddRange(last, second, early, first,
            Quote("Groucho Marx", "Funny", 1850, false), Quote("A writer", "Life"),
            Quote("Z writer", "Funny"), Quote(null, null));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var service = new DogQuoteService(db);
        foreach (var category in new[] { "Funny", "funny", "FUNNY", "  Funny  " })
        {
            var page = await service.GetPagedAsync(2, 2, category, "  mArX  ", default);
            Assert.Equal(4, page.TotalCount);
            Assert.Equal(2, page.TotalPages);
            Assert.Equal(new[] { second.Id, last.Id }, page.Items.Select(x => x.Id));
            Assert.Equal(new[] { early.Id, first.Id }, (await service.GetPagedAsync(1, 2, category, "marx", default)).Items.Select(x => x.Id));
        }
        Assert.Equal(7, (await service.GetPagedAsync(1, 100, " \t ", " ", default)).Items.Count);
        Assert.Equal(0, (await service.GetPagedAsync(1, 25, "Fun", null, default)).TotalCount);
        var beyond = await service.GetPagedAsync(5, 2, "Funny", "marx", default);
        Assert.Empty(beyond.Items);
        Assert.Equal(4, beyond.TotalCount);
        Assert.Empty(db.ChangeTracker.Entries<DogQuote>());
    });

    [PostgresFact]
    public Task Random_and_id_endpoints_return_only_eligible_quotes_and_map_nullable_metadata() => WithDatabase(async db =>
    {
        var funny = Quote("A writer", "Funny", 1923);
        funny.Work = "A book";
        funny.SourceUrl = "https://example.test/citation";
        var anonymous = Quote(null, "Life");
        var inactive = Quote("Retired", "Funny", active: false);
        db.DogQuotes.AddRange(funny, anonymous, inactive);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var controller = Controller(db);
        foreach (var category in new[] { "Funny", "funny", "FUNNY", " Funny " })
            Assert.Equal(funny.Id, Ok(await controller.Random(default, category)).Id);
        for (var attempt = 0; attempt < 20; attempt++)
            Assert.Contains(Ok(await controller.Random(default)).Id, new[] { funny.Id, anonymous.Id });
        Assert.Equal(new DogQuoteDto(funny.Id, funny.Text, funny.Author, funny.Work, funny.Year, funny.SourceUrl, funny.Category),
            Ok(await controller.Get(funny.Id, default)));
        var nullable = Ok(await controller.Get(anonymous.Id, default));
        Assert.Null(nullable.Author);
        Assert.Null(nullable.Work);
        Assert.Null(nullable.Year);
        Assert.Null(nullable.SourceUrl);
        NotFound(await controller.Get(inactive.Id, default));
        NotFound(await controller.Get(Guid.NewGuid(), default));
        NotFound(await controller.Random(default, "Missing"));
        anonymous.IsActive = false;
        db.Update(anonymous);
        await db.SaveChangesAsync();
        NotFound(await controller.Random(default, "Life"));
    });

    [PostgresFact]
    public Task Filters_treat_sql_wildcards_literally_and_accept_maximum_trimmed_lengths() => WithDatabase(async db =>
    {
        var literal = Quote(@"100%_\ Writer", @"Odd%_\");
        var maximum = Quote(new string('a', 250), new string('c', 100));
        db.DogQuotes.AddRange(literal, maximum, Quote("Other Writer", "OddAnything"));
        await db.SaveChangesAsync();
        var service = new DogQuoteService(db);
        foreach (var term in new[] { "%", "_", "\\", @"%_\" })
            Assert.Equal(literal.Id, Assert.Single((await service.GetPagedAsync(1, 25, null, term, default)).Items).Id);
        Assert.Equal(literal.Id, (await service.GetRandomAsync(@"odd%_\", default))!.Id);
        Assert.Equal(maximum.Id, Assert.Single((await service.GetPagedAsync(1, 25,
            $" {maximum.Category} ", $" {maximum.Author} ", default)).Items).Id);
    });

    [PostgresFact]
    public Task Categories_exclude_inactive_and_blank_values_and_deduplicate_case_insensitively() => WithDatabase(async db =>
    {
        db.DogQuotes.AddRange(new[] { "Love", "funny", "Funny", "Classic", " Funny ", "", " \t ", null }
            .Select(category => Quote(null, category)));
        db.DogQuotes.Add(Quote(null, "Retired", active: false));
        await db.SaveChangesAsync();
        Assert.Equal(new[] { "Classic", "Funny", "Love" }, Ok(await Controller(db).Categories(default)));
    });

    [PostgresFact]
    public Task Migration_enforces_required_text_and_lengths_allows_duplicates_and_rolls_back() => WithDatabase(async db =>
    {
        var id = Guid.NewGuid();
        // Simulate separate curation: omit IsActive to exercise its database default.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "DogQuotes" ("Id", "Text", "CreatedAtUtc", "UpdatedAtUtc")
            VALUES ({id}, {new string('q', 5000)}, {DateTimeOffset.UtcNow}, {DateTimeOffset.UtcNow})
            """);
        Assert.True((await db.DogQuotes.SingleAsync()).IsActive);
        db.DogQuotes.Add(new DogQuote { Text = new string('q', 5000) });
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.DogQuotes.CountAsync());
        var invalid = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "DogQuotes" SET "Text" = NULL WHERE "Id" = {id}
            """));
        Assert.Equal(PostgresErrorCodes.NotNullViolation, invalid.SqlState);
        foreach (var (column, length) in new[] { ("Author", 250), ("Work", 500), ("Category", 100), ("SourceUrl", 2000) })
        {
            // Column identifiers are fixed test constants; values remain parameterized.
            var sql = $"UPDATE \"DogQuotes\" SET \"{column}\" = {{0}}";
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                sql, new string('x', length + 1)));
            Assert.Equal(PostgresErrorCodes.StringDataRightTruncation, error.SqlState);
        }
        var previous = (await db.Database.GetAppliedMigrationsAsync()).Reverse().Skip(1).First();
        await db.GetService<IMigrator>().MigrateAsync(previous);
        await db.Database.MigrateAsync();
        Assert.Empty(await db.DogQuotes.AsNoTracking().ToListAsync());
    });

    private static DogQuote Quote(string? author, string? category, int? year = null, bool active = true) =>
        new() { Text = "A dog quote.", Author = author, Category = category, Year = year, IsActive = active };

    private static AppDbContext MemoryDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static DogQuotesController Controller(AppDbContext db) => new(new DogQuoteService(db));

    private static T Ok<T>(ActionResult<T> response) =>
        Assert.IsAssignableFrom<T>(Assert.IsType<OkObjectResult>(response.Result).Value);

    private static void NotFound(ActionResult<DogQuoteDto> response)
    {
        var result = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(404, result.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal(404, problem.Status);
        Assert.Equal("dog_quote_not_found", problem.Extensions["code"]);
    }

    private static async Task WithDatabase(Func<AppDbContext, Task> action)
    {
        var connection = Environment.GetEnvironmentVariable(PostgresFactAttribute.ConnectionStringVariable)!;
        var schema = $"hp_quotes_test_{Guid.NewGuid():N}";
        await using var admin = new NpgsqlConnection(connection);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin)) await create.ExecuteNonQueryAsync();
        var isolated = new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString;
        try
        {
            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(isolated, x => x.EnableRetryOnFailure()).Options);
            await db.Database.MigrateAsync();
            await action(db);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
