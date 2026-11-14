using System.ComponentModel.DataAnnotations;
using RaceDay.Api.Models;

namespace RaceDay.Api.Dtos;

/// <summary>Request body for POST /api/auth/register.</summary>
public class RegisterRequest
{
    /// <summary>The user's full name.</summary>
    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Unique email address, used to log in.</summary>
    [Required, EmailAddress, StringLength(150)]
    public string Email { get; set; } = string.Empty;

    /// <summary>At least 8 characters. Stored hashed, never in plain text.</summary>
    [Required, MinLength(8), StringLength(100)]
    public string Password { get; set; } = string.Empty;

    /// <summary>Either "Organiser" or "Participant".</summary>
    [Required]
    public string Role { get; set; } = string.Empty;
}

/// <summary>Request body for POST /api/auth/login.</summary>
public class LoginRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

/// <summary>Request body for PUT /api/users/{id}.</summary>
public class UpdateProfileRequest
{
    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(150)]
    public string Email { get; set; } = string.Empty;

    [StringLength(20)]
    public string? ContactNumber { get; set; }
}

/// <summary>A user's own profile. Never includes the password or its hash.</summary>
public record UserSummary(int UserID, string Name, string Email, string Role, string? ContactNumber)
{
    public static UserSummary From(AppUser user) =>
        new(user.UserID, user.Name, user.Email, user.Role, user.ContactNumber);
}

/// <summary>What other logged-in users can see about someone: no email or contact details.</summary>
public record PublicProfile(int UserID, string Name, string Role)
{
    public static PublicProfile From(AppUser user) => new(user.UserID, user.Name, user.Role);
}
