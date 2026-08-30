using System.ComponentModel.DataAnnotations;

namespace HooviePack.Api.Application.Contracts;

public sealed class InitializeMediaUploadRequest
{
    [Required, StringLength(255, MinimumLength = 1)]
    public string FileName { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 1)]
    public string ContentType { get; set; } = string.Empty;

    public long Size { get; set; }

    public UploadPurpose Purpose { get; set; }

    public Guid? FamilyId { get; set; }
}
