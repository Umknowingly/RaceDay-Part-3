using Microsoft.AspNetCore.Mvc.Filters;

namespace RaceDay.Api.Services;

/// <summary>
/// Protects an endpoint using the server-side session.
/// No session (not logged in) returns 401. A logged-in user whose role is not in
/// <see cref="Roles"/> returns 403. Leave Roles empty to allow any logged-in user.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class SessionAuthorizeAttribute : Attribute, IAuthorizationFilter
{
    /// <summary>Comma-separated list of allowed roles, e.g. "Organiser".</summary>
    public string? Roles { get; set; }

    // this is called for every request to an endpoint with this attribute
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var session = context.HttpContext.Session;
        var userId = session.GetUserId();
        var role = session.GetRole();

        if (userId == null || role == null)
        {
            context.Result = ApiProblems.Unauthorized();
            return;
        }

        if (!string.IsNullOrWhiteSpace(Roles))
        {
            var allowed = Roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!allowed.Contains(role))
            {
                context.Result = ApiProblems.Forbidden(
                    $"This endpoint is only available to: {string.Join(", ", allowed)}.");
            }
        }
    }
}
