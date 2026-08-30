using System.ComponentModel.DataAnnotations;

namespace HooviePack.Api.Application.Contracts;

public sealed class CreateInviteRequest
{
    [Range(1, 30)]
    public int ExpiresInDays { get; set; } = 7;
}
