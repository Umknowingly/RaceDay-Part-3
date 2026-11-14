namespace RaceDay.Api.Models;

/// <summary>
/// An age or distance category within an event (e.g. "Under 20", "10km").
/// EventID is an addition: the approved endpoint plan manages categories per event.
/// </summary>
public class Category
{
    public int CategoryID { get; set; }

    /// <summary>The organiser who created the category.</summary>
    public int UserID { get; set; }
    public int EventID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Null for age-based categories.</summary>
    public decimal? DistanceKm { get; set; }
    public int MaxParticipants { get; set; } = 100;

    public AppUser Creator { get; set; } = null!;
    public Event Event { get; set; } = null!;
    public ICollection<Enrolment> Enrolments { get; set; } = new List<Enrolment>();
}
