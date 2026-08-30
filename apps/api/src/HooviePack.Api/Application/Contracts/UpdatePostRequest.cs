using System.ComponentModel.DataAnnotations;

namespace HooviePack.Api.Application.Contracts;

public sealed class UpdatePostRequest
{
    [StringLength(2000)]
    public string? Content { get; set; }

    [Required]
    public List<FileUploadReferenceRequest> PhotoFiles { get; set; } = [];

    [Required]
    public List<Guid> RemovedPhotoIds { get; set; } = [];
}
