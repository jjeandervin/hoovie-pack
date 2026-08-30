using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using HooviePack.Api.Application;
using HooviePack.Api.Application.Services;
using HooviePack.Api.Configuration;
using HooviePack.Api.Infrastructure.Data;
using HooviePack.Api.Infrastructure.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MediaStorageOptions>(
    builder.Configuration.GetSection(MediaStorageOptions.SectionName));
builder.Services.Configure<FileServiceOptions>(
    builder.Configuration.GetSection(FileServiceOptions.SectionName));

var fileServiceOptions = builder.Configuration
    .GetSection(FileServiceOptions.SectionName)
    .Get<FileServiceOptions>() ?? new FileServiceOptions();
if (!Uri.TryCreate(fileServiceOptions.BaseUrl, UriKind.Absolute, out var fileServiceBaseUri) ||
    (fileServiceBaseUri.Scheme != Uri.UriSchemeHttp && fileServiceBaseUri.Scheme != Uri.UriSchemeHttps))
{
    throw new InvalidOperationException("FileService:BaseUrl must be an absolute HTTP(S) URL.");
}

if (fileServiceOptions.TimeoutSeconds is < 1 or > 60)
{
    throw new InvalidOperationException("FileService:TimeoutSeconds must be between 1 and 60.");
}

var normalizedFileServiceBaseUri = new Uri(fileServiceBaseUri.AbsoluteUri.TrimEnd('/') + "/");
void ConfigureFileServiceClient(HttpClient client)
{
    client.BaseAddress = normalizedFileServiceBaseUri;
    client.Timeout = TimeSpan.FromSeconds(fileServiceOptions.TimeoutSeconds);
    if (!string.IsNullOrWhiteSpace(fileServiceOptions.ApiKey))
    {
        client.DefaultRequestHeaders.Add("X-Internal-Api-Key", fileServiceOptions.ApiKey);
    }
}

builder.Services.AddHttpClient<IFileServiceClient, FileServiceClient>(ConfigureFileServiceClient);
builder.Services.AddHttpClient<FileServiceHealthCheck>(ConfigureFileServiceClient);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");
}
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure()));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "HooviePack API",
        Version = "v1",
        Description = "Private family social feed, dog profiles, and media API."
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Paste a Keycloak access token."
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document, null)] = []
    });
});

var authenticationOptions = builder.Configuration
    .GetSection(AuthenticationOptions.SectionName)
    .Get<AuthenticationOptions>() ?? new AuthenticationOptions();
var allowInternalHttpMetadata = IsTrustedInternalHttpMetadata(authenticationOptions.MetadataAddress);
if (authenticationOptions.RequireHttpsMetadata &&
    Uri.TryCreate(authenticationOptions.MetadataAddress, UriKind.Absolute, out var configuredMetadataUri) &&
    configuredMetadataUri.Scheme == Uri.UriSchemeHttp &&
    !allowInternalHttpMetadata)
{
    throw new InvalidOperationException(
        "Authentication:MetadataAddress must use HTTPS unless it targets the loopback interface or the Docker-internal 'keycloak' service.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = authenticationOptions.Authority.TrimEnd('/');
        options.Audience = authenticationOptions.Audience;
        // Containers fetch discovery through an isolated HTTP service name while
        // tokens retain the externally visible HTTPS issuer. Issuer validation
        // remains enabled against ValidIssuer below.
        options.RequireHttpsMetadata = authenticationOptions.RequireHttpsMetadata && !allowInternalHttpMetadata;
        options.MapInboundClaims = false;
        if (!string.IsNullOrWhiteSpace(authenticationOptions.MetadataAddress))
        {
            options.MetadataAddress = authenticationOptions.MetadataAddress;
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = (authenticationOptions.ValidIssuer ?? authenticationOptions.Authority).TrimEnd('/'),
            ValidateAudience = true,
            ValidAudience = authenticationOptions.Audience,
            ValidateLifetime = true,
            NameClaimType = "preferred_username",
            RoleClaimType = "role",
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:4200"];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod());
});

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("API process is running."), tags: ["live"])
    .AddCheck<PostgresHealthCheck>(
        "postgres",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"],
        timeout: TimeSpan.FromSeconds(4))
    .AddCheck<FileServiceHealthCheck>(
        "files-api",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"],
        timeout: TimeSpan.FromSeconds(Math.Min(fileServiceOptions.TimeoutSeconds, 10)));

builder.Services.AddScoped<IIdentityService, IdentityService>();
builder.Services.AddScoped<IFamilyAccessService, FamilyAccessService>();
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<IFamilyService, FamilyService>();
builder.Services.AddScoped<IDogService, DogService>();
builder.Services.AddScoped<IPostService, PostService>();
builder.Services.AddScoped<ICommentService, CommentService>();
builder.Services.AddScoped<IReactionService, ReactionService>();
builder.Services.AddScoped<IMediaService, MediaService>();
builder.Services.AddScoped<IApplicationHealthService, ApplicationHealthService>();
builder.Services.AddScoped<IMediaCleanupService, MediaCleanupService>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger(options => options.RouteTemplate = "swagger/{documentName}/openapi.json");
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/openapi.json", "HooviePack API v1");
        options.RoutePrefix = "swagger";
        options.DisplayRequestDuration();
        options.EnablePersistAuthorization();
    });
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

await app.ApplyMigrationsAsync();
await app.RunAsync();

static bool IsTrustedInternalHttpMetadata(string? metadataAddress)
{
    if (!Uri.TryCreate(metadataAddress, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp)
    {
        return false;
    }

    if (string.Equals(uri.Host, "keycloak", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    return IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address);
}

public partial class Program;
