namespace RaceDay.Api.Models;

public class Event
{
    public int EventID { get; set; }

    /// <summary>The organiser (an AppUser with role Organiser) who owns this event.</summary>
    public int UserID { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly EventDate { get; set; }

    /// <summary>ERD column "venue"; exposed by the API as "location".</summary>
    public string Venue { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? RouteInfo { get; set; }

    /// <summary>Headline distance in kilometres (required by the Part 2 brief).</summary>
    public decimal DistanceKm { get; set; }

    /// <summary>Run, Walk or Cycle (required by the Part 2 brief).</summary>
    public string EventType { get; set; } = string.Empty;

    public AppUser Organiser { get; set; } = null!;
    public ICollection<Category> Categories { get; set; } = new List<Category>();
    public ICollection<Enrolment> Enrolments { get; set; } = new List<Enrolment>();
}
