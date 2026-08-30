using System.ComponentModel.DataAnnotations;

namespace HooviePack.Api.Application.Contracts;

public sealed class UpsertDogRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(100)]
    public string? Breed { get; set; }

    public DateOnly? Birthday { get; set; }

    [Range(0, 40)]
    public int? ApproximateAgeYears { get; set; }

    [StringLength(500)]
    public string? Bio { get; set; }

    [StringLength(200)]
    public string? FavoriteThing { get; set; }

    public Guid? OwnerMembershipId { get; set; }

    public FileUploadReferenceRequest? PhotoFile { get; set; }

    public bool RemovePhoto { get; set; }
}
