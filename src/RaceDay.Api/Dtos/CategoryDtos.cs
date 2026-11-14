using System.ComponentModel.DataAnnotations;
using RaceDay.Api.Models;

namespace RaceDay.Api.Dtos;

/// <summary>Request body for adding a category to an event.</summary>
public class CategoryRequest
{
    /// <summary>The category name, e.g. "10km", "Under 20" or "Senior 60+". Must be unique within the event.</summary>
    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    /// <summary>Distance in kilometres for a distance category. Leave out for an age category.</summary>
    [Range(0.01, 9999.99)]
    public decimal? DistanceKm { get; set; }

    /// <summary>The most participants allowed in this category. Defaults to 100.</summary>
    [Range(1, 100000)]
    public int? MaxParticipants { get; set; }
}

/// <summary>A category as returned by the API. EnrolmentCount excludes cancelled enrolments.</summary>
public record CategoryResponse(
    int CategoryID,
    int EventID,
    string Name,
    string? Description,
    decimal? DistanceKm,
    int MaxParticipants,
    int EnrolmentCount)
{
    public static CategoryResponse From(Category c, int enrolmentCount) => new(
        c.CategoryID, c.EventID, c.Name, c.Description, c.DistanceKm, c.MaxParticipants, enrolmentCount);
}
