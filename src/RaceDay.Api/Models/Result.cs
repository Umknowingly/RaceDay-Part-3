namespace RaceDay.Api.Models;

/// <summary>At most one result per enrolment, captured by the event's organiser.</summary>
public class Result
{
    public int ResultID { get; set; }
    public int EnrolmentID { get; set; }

    /// <summary>ERD column "Time".</summary>
    public TimeSpan? FinishTime { get; set; }
    public int? Position { get; set; }
    public string Status { get; set; } = ResultStatuses.Finished;
    public string? Notes { get; set; }

    public Enrolment Enrolment { get; set; } = null!;
}
