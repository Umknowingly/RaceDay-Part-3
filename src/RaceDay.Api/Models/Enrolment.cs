namespace RaceDay.Api.Models;

/// <summary>
/// Records the link between a Participant, the Event and the selected Category,
/// as the Part 2 brief requires.
/// </summary>
public class Enrolment
{
    public int EnrolmentID { get; set; }
    public int ParticipantID { get; set; }
    public int EventID { get; set; }
    public int CategoryID { get; set; }
    public DateTime EnrolDate { get; set; }
    public string Status { get; set; } = EnrolmentStatuses.Pending;
    public string EmergencyContactName { get; set; } = string.Empty;
    public string EmergencyContactNumber { get; set; } = string.Empty;

    public Participant Participant { get; set; } = null!;
    public Event Event { get; set; } = null!;
    public Category Category { get; set; } = null!;
    public Result? Result { get; set; }
}
