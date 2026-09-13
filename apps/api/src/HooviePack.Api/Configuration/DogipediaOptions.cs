namespace HooviePack.Api.Configuration;

public sealed class DogApiOptions
{
    public const string SectionName = "DogApi";
    public string BaseUrl { get; set; } = "https://dogapi.dog/api/v2/";
    public int PageSize { get; set; } = 1000;
    public int TimeoutSeconds { get; set; } = 30;
}

public sealed class DogipediaSyncOptions
{
    public const string SectionName = "Dogipedia:Sync";
    public bool Enabled { get; set; } = true;
    public double FreshnessHours { get; set; } = 24;
    public double CheckIntervalMinutes { get; set; } = 60;
}
