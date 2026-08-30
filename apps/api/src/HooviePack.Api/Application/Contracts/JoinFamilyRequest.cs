using System.ComponentModel.DataAnnotations;

namespace HooviePack.Api.Application.Contracts;

public sealed class JoinFamilyRequest
{
    [Required, StringLength(256, MinimumLength = 10)]
    public string InviteCode { get; set; } = string.Empty;
}
