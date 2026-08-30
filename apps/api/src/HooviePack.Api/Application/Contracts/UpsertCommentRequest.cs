using System.ComponentModel.DataAnnotations;

namespace HooviePack.Api.Application.Contracts;

public sealed class UpsertCommentRequest
{
    [Required, StringLength(500, MinimumLength = 1)]
    public string Content { get; set; } = string.Empty;
}
