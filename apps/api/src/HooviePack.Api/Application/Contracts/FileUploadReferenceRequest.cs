using System.ComponentModel.DataAnnotations;

namespace HooviePack.Api.Application.Contracts;

public sealed class FileUploadReferenceRequest
{
    public Guid FileId { get; set; }

    [Required, StringLength(1024, MinimumLength = 1)]
    public string UploadToken { get; set; } = string.Empty;
}
