namespace HooviePack.FileMigration;

public sealed class LegacyMediaOptions
{
    public const string SectionName = "LegacyMedia";

    public string RootPath { get; set; } = "/legacy-media";
}
