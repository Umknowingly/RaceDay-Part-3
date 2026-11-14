using Microsoft.AspNetCore.Mvc;

namespace RaceDay.Api.Services;

/// <summary>Builds consistent RFC 7807 error responses.</summary>
public static class ApiProblems
{
    private static ObjectResult Create(int status, string title, string detail) =>
        new ObjectResult(new ProblemDetails { Status = status, Title = title, Detail = detail })
        {
            StatusCode = status
        };

    // these are the most common ones, but you can also use Create() to make any other RFC 7807 response
    public static ObjectResult Unauthorized(string detail = "You must be logged in to use this endpoint.") =>
        Create(StatusCodes.Status401Unauthorized, "Unauthorized", detail);

    public static ObjectResult Forbidden(string detail) =>
        Create(StatusCodes.Status403Forbidden, "Forbidden", detail);

    public static ObjectResult NotFound(string detail) =>
        Create(StatusCodes.Status404NotFound, "Not Found", detail);

    public static ObjectResult Conflict(string detail) =>
        Create(StatusCodes.Status409Conflict, "Conflict", detail);

    public static ObjectResult BadRequest(string detail) =>
        Create(StatusCodes.Status400BadRequest, "Bad Request", detail);
}
