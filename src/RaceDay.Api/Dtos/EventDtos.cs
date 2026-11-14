using System.ComponentModel.DataAnnotations;
using RaceDay.Api.Models;

namespace RaceDay.Api.Dtos;

/// <summary>Request body for creating (POST) or updating (PUT) an event.</summary>
public class EventRequest
{
    /// <summary>The event's name, e.g. "Comrades Marathon Training Run".</summary>
    [Required, StringLength(150)]
    public string Name { get; set; } = string.Empty;

    /// <summary>The event date (yyyy-MM-dd). Must not be in the past when creating an event.</summary>
    [Required]
    public DateOnly? Date { get; set; }

    /// <summary>Where the event takes place (stored as the venue).</summary>
    [Required, StringLength(150)]
    public string Location { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    /// <summary>Optional notes about the route.</summary>
    [StringLength(2000)]
    public string? RouteInfo { get; set; }

    /// <summary>The event's headline distance in kilometres (greater than 0).</summary>
    [Range(0.01, 9999.99)]
    public decimal DistanceKm { get; set; }

    /// <summary>"Run", "Walk" or "Cycle".</summary>
    [Required]
    public string EventType { get; set; } = string.Empty;
}

/// <summary>An event as returned by the API.</summary>
public record EventResponse(
    int EventID,
    int OrganiserID,
    string OrganiserName,
    string Name,
    DateOnly Date,
    string Location,
    string? Description,
    string? RouteInfo,
    decimal DistanceKm,
    string EventType)
{
    /// <summary>The event's Organiser navigation property must be loaded.</summary>
    public static EventResponse From(Event e) => new(
        e.EventID, e.UserID, e.Organiser.Name, e.Name, e.EventDate, e.Venue,
        e.Description, e.RouteInfo, e.DistanceKm, e.EventType);
}

/// <summary>One row of the Organiser dashboard: an event with its counts.</summary>
public record OrganiserEventSummary(
    int EventID,
    string Name,
    DateOnly Date,
    string Location,
    decimal DistanceKm,
    string EventType,
    int CategoryCount,
    int EnrolmentCount,
    int ResultCount);
