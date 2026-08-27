using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace HooviePack.Api.Application;

public sealed class ApiException(
    int statusCode,
    string title,
    string detail,
    IReadOnlyDictionary<string, string[]>? errors = null) : Exception(detail)
{
    public int StatusCode { get; } = statusCode;
    public string Title { get; } = title;
    public string Detail { get; } = detail;
    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;

    public static ApiException BadRequest(string detail, string field = "request") =>
        new(StatusCodes.Status400BadRequest, "Validation failed", detail,
            new Dictionary<string, string[]> { [field] = [detail] });

    public static ApiException Unauthorized(string detail = "The access token does not contain a valid subject.") =>
        new(StatusCodes.Status401Unauthorized, "Unauthorized", detail);

    public static ApiException Forbidden(string detail = "You do not have permission to perform this action.") =>
        new(StatusCodes.Status403Forbidden, "Forbidden", detail);

    public static ApiException NotFound(string detail = "The requested resource was not found.") =>
        new(StatusCodes.Status404NotFound, "Not found", detail);

    public static ApiException Conflict(string detail) =>
        new(StatusCodes.Status409Conflict, "Conflict", detail);

    public static ApiException ServiceUnavailable(string detail) =>
        new(StatusCodes.Status503ServiceUnavailable, "Service unavailable", detail);
}

public sealed class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            if (!httpContext.Response.HasStarted)
            {
                httpContext.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            }

            return true;
        }

        if (exception is ApiException apiException)
        {
            var problem = apiException.Errors is null
                ? new ProblemDetails
                {
                    Status = apiException.StatusCode,
                    Title = apiException.Title,
                    Detail = apiException.Detail,
                    Instance = httpContext.Request.Path
                }
                : new HttpValidationProblemDetails(apiException.Errors)
                {
                    Status = apiException.StatusCode,
                    Title = apiException.Title,
                    Detail = apiException.Detail,
                    Instance = httpContext.Request.Path
                };
            return await WriteProblemAsync(httpContext, exception, problem);
        }

        if (exception is BadHttpRequestException or InvalidDataException)
        {
            var isTooLarge = IsPayloadTooLarge(exception);
            return await WriteProblemAsync(
                httpContext,
                exception,
                new ProblemDetails
                {
                    Status = isTooLarge ? StatusCodes.Status413PayloadTooLarge : StatusCodes.Status400BadRequest,
                    Title = isTooLarge ? "Payload too large" : "Malformed request",
                    Detail = isTooLarge
                        ? "The request body exceeds the configured upload limit."
                        : "The request body could not be read.",
                    Instance = httpContext.Request.Path
                });
        }

        if (exception is OperationCanceledException)
        {
            return await WriteProblemAsync(
                httpContext,
                exception,
                new ProblemDetails
                {
                    Status = StatusCodes.Status503ServiceUnavailable,
                    Title = "Request canceled",
                    Detail = "The request was canceled before it could be completed.",
                    Instance = httpContext.Request.Path
                });
        }

        logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        return await WriteProblemAsync(
            httpContext,
            exception,
            new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred",
                Detail = "The request could not be completed.",
                Instance = httpContext.Request.Path
            });
    }

    private async ValueTask<bool> WriteProblemAsync(
        HttpContext httpContext,
        Exception exception,
        ProblemDetails problem)
    {
        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;
        var written = await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
        if (!written && !HttpMethods.IsHead(httpContext.Request.Method))
        {
            httpContext.Response.ContentType = "application/problem+json";
            await httpContext.Response.WriteAsJsonAsync(problem, CancellationToken.None);
        }

        return true;
    }

    private static bool IsPayloadTooLarge(Exception exception) =>
        exception is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } ||
        exception is InvalidDataException invalidDataException &&
        (invalidDataException.Message.Contains("length limit", StringComparison.OrdinalIgnoreCase) ||
            invalidDataException.Message.Contains("request body too large", StringComparison.OrdinalIgnoreCase));
}
