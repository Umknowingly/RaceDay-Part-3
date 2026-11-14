namespace RaceDay.Api.Models;

/// <summary>
/// The USER table from the Part 1 ERD: the login account for both Organisers and Participants.
/// Role and ContactNumber are additions (see the README deviations table).
/// </summary>
public class AppUser
{
    public int UserID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /// <summary>PBKDF2 hash. The plain-text password is never stored.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>"Organiser" or "Participant". Drives role-based access.</summary>
    public string Role { get; set; } = string.Empty;
    public string? ContactNumber { get; set; }

    /// <summary>Present only for users who registered as a Participant.</summary>
    public Participant? Participant { get; set; }
    public ICollection<Event> OrganisedEvents { get; set; } = new List<Event>();
    public ICollection<Category> CreatedCategories { get; set; } = new List<Category>();
}
