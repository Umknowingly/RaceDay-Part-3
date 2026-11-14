using System.ComponentModel.DataAnnotations;

namespace RaceDay.Api.Dtos;

/// <summary>Request body for enrolling in a category.</summary>
public class EnrolRequest
{
    [Required, StringLength(100)]
    public string EmergencyContactName { get; set; } = string.Empty;

    [Required, StringLength(20)]
    public string EmergencyContactNumber { get; set; } = string.Empty;
}

/// <summary>Request body for an Organiser changing an enrolment's status.</summary>
public class UpdateEnrolmentStatusRequest
{
    /// <summary>"Pending", "Confirmed" or "Cancelled".</summary>
    [Required]
    public string Status { get; set; } = string.Empty;
}

/// <summary>An enrolment with the event, category and participant details a screen needs.</summary>
public record EnrolmentResponse(
    int EnrolmentID,
    int EventID,
    string EventName,
    DateOnly EventDate,
    int CategoryID,
    string CategoryName,
    int ParticipantID,
    string ParticipantName,
    DateTime EnrolDate,
    string Status,
    string EmergencyContactName,
    string EmergencyContactNumber,
    bool HasResult);
