using System.Linq.Expressions;
using HooviePack.Api.Application.Contracts;
using HooviePack.Api.Domain;
using HooviePack.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HooviePack.Api.Application.Services;

public interface IDogQuoteService
{
    Task<DogQuoteDto?> GetRandomAsync(string? category, CancellationToken cancellationToken);
    Task<PagedResponse<DogQuoteDto>> GetPagedAsync(int page, int pageSize, string? category, string? author, CancellationToken cancellationToken);
    Task<DogQuoteDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken);
}

public sealed class DogQuoteService(AppDbContext db) : IDogQuoteService
{
    private static readonly Expression<Func<DogQuote, DogQuoteDto>> ToDto = x =>
        new(x.Id, x.Text, x.Author, x.Work, x.Year, x.SourceUrl, x.Category);

    public Task<DogQuoteDto?> GetRandomAsync(string? category, CancellationToken cancellationToken) =>
        ActiveQuotes(category).OrderBy(x => EF.Functions.Random()).Select(ToDto).FirstOrDefaultAsync(cancellationToken);

    public async Task<PagedResponse<DogQuoteDto>> GetPagedAsync(
        int page, int pageSize, string? category, string? author, CancellationToken cancellationToken)
    {
        if (page < 1) throw ApiException.BadRequest("Page must be at least 1.", "page");
        if (pageSize is < 1 or > 100) throw ApiException.BadRequest("Page size must be between 1 and 100.", "pageSize");
        var offset = (long)(page - 1) * pageSize;
        if (offset > int.MaxValue) throw ApiException.BadRequest("Page is too large.", "page");

        var query = ActiveQuotes(category);
        author = NormalizeFilter(author, 250, "author");
        if (author is not null)
        {
            var pattern = $"%{EscapeLike(author)}%";
            query = query.Where(x => x.Author != null && EF.Functions.ILike(x.Author, pattern, "\\"));
        }

        var count = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.Author).ThenBy(x => x.Year).ThenBy(x => x.Id)
            .Skip((int)offset).Take(pageSize).Select(ToDto).ToListAsync(cancellationToken);
        return new(items, page, pageSize, count);
    }

    public Task<DogQuoteDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        ActiveQuotes(null).Where(x => x.Id == id).Select(ToDto).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        var categories = await ActiveQuotes(null).Where(x => x.Category != null && x.Category != "")
            .Select(x => x.Category!).Distinct().ToListAsync(cancellationToken);
        // Preserve a deterministic display value when curated rows differ only in casing.
        return categories.Select(x => x.Trim()).Where(x => x.Length > 0)
            .OrderBy(x => x, StringComparer.Ordinal)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private IQueryable<DogQuote> ActiveQuotes(string? category)
    {
        category = NormalizeFilter(category, 100, "category");
        var query = db.DogQuotes.AsNoTracking().Where(x => x.IsActive);
        if (category is not null)
        {
            var pattern = EscapeLike(category);
            query = query.Where(x => x.Category != null && EF.Functions.ILike(x.Category, pattern, "\\"));
        }
        return query;
    }

    private static string? NormalizeFilter(string? value, int maxLength, string field)
    {
        value = value?.Trim();
        if (value?.Length > maxLength)
            throw ApiException.BadRequest($"{field} must be at most {maxLength} characters.", field);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
