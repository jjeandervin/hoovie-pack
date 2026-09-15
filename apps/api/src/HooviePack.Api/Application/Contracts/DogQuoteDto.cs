namespace HooviePack.Api.Application.Contracts;

public sealed record DogQuoteDto(
    Guid Id,
    string Text,
    string? Author,
    string? Work,
    int? Year,
    string? SourceUrl,
    string? Category);
