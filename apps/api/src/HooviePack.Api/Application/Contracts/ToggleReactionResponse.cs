namespace HooviePack.Api.Application.Contracts;

public sealed record ToggleReactionResponse(
    bool Added,
    ReactionSummaryResponse Reactions);
