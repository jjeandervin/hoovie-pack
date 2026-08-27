namespace HooviePack.Api.Configuration;

public sealed class AuthenticationOptions
{
    public const string SectionName = "Authentication";

    public string Authority { get; set; } = "http://localhost:8081/realms/hooviepack";
    public string? MetadataAddress { get; set; }
    public string? ValidIssuer { get; set; }
    public string Audience { get; set; } = "hooviepack-api";
    public bool RequireHttpsMetadata { get; set; } = true;
}

public sealed class MediaStorageOptions
{
    public const string SectionName = "MediaStorage";

    public string RootPath { get; set; } = "media";
    public long MaxImageBytes { get; set; } = 10 * 1024 * 1024;
}
