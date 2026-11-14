namespace RaceDay.Api.Models;

/// <summary>
/// The PARTICIPANT table from the Part 1 ERD. UserID (an addition) links the participant
/// to the login account they authenticate with.
/// </summary>
public class Participant
{
    public int ParticipantID { get; set; }
    public int UserID { get; set; }
    public string Name { get; set; } = string.Empty;

    public AppUser User { get; set; } = null!;
    public ICollection<Enrolment> Enrolments { get; set; } = new List<Enrolment>();
}
