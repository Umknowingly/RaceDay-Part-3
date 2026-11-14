namespace RaceDay.Api.Services;

/// <summary>Keys under which the logged-in user's ID and role live in the server-side session.</summary>
public static class SessionKeys
{
    public const string UserId = "UserID";
    public const string Role = "Role";
}

/// <summary>Helpers for reading and writing the logged-in user's identity in the session.</summary>
public static class RaceDaySessionExtensions
{
    public static void SignIn(this ISession session, int userId, string role)
    {
        session.SetInt32(SessionKeys.UserId, userId);
        session.SetString(SessionKeys.Role, role);
    }

    public static int? GetUserId(this ISession session) => session.GetInt32(SessionKeys.UserId);

    public static string? GetRole(this ISession session) => session.GetString(SessionKeys.Role);
}
