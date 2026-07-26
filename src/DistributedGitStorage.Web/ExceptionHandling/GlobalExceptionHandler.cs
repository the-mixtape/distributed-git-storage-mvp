using DistributedGitStorage.Services.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DistributedGitStorage.Web.ExceptionHandling;

internal sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            InvalidRepositoryNameException or ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request"),
            RepositoryNotFoundException => (StatusCodes.Status404NotFound, "Repository not found"),
            RepositoryAlreadyExistsException => (StatusCodes.Status409Conflict, "Repository already exists"),
            HttpRequestException => (StatusCodes.Status503ServiceUnavailable, "Git storage cluster is unavailable"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected server error")
        };

        logger.Log(status >= 500 ? LogLevel.Error : LogLevel.Warning, exception,
            "Request failed with status code {StatusCode}", status);
        context.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = status < 500 ? exception.Message : null
            },
            Exception = exception
        });
    }
}
