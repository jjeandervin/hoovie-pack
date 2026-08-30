using HooviePack.Api.Domain;

namespace HooviePack.Api.Application.Contracts;

public sealed record ReactionSummaryResponse(
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyCollection<ReactionType> MyReactions);
