using System.ComponentModel.DataAnnotations;
using RaceDay.Api.Models;

namespace RaceDay.Api.Dtos;

/// <summary>Request body for capturing a result.</summary>
public class ResultRequest
{
    /// <summary>Finish time as hh:mm:ss, for example "00:52:30". Required when the status is Finished.</summary>
    public TimeSpan? FinishTime { get; set; }

    /// <summary>Finishing position within the participant's category. Required when the status is Finished.</summary>
    [Range(1, 1000000)]
    public int? Position { get; set; }

    /// <summary>"Finished" (default), "DNF" or "DSQ".</summary>
    public string Status { get; set; } = ResultStatuses.Finished;

    [StringLength(500)]
    public string? Notes { get; set; }
}

/// <summary>
/// A result with its event, category and participant. TotalFinishers is the number of
/// participants who finished in the same category, so a screen can show "47th of 312".
/// </summary>
public record ResultResponse(
    int ResultID,
    int EnrolmentID,
    int EventID,
    string EventName,
    DateOnly EventDate,
    int CategoryID,
    string CategoryName,
    int ParticipantID,
    string ParticipantName,
    TimeSpan? FinishTime,
    int? Position,
    int TotalFinishers,
    string Status,
    string? Notes);
