using HooviePack.Api.Domain;

namespace HooviePack.Api.Application.Contracts;

public sealed class UpdateMemberRoleRequest
{
    public FamilyRole Role { get; set; }
}
