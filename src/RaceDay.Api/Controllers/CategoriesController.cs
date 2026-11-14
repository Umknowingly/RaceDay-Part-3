using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Data;
using RaceDay.Api.Dtos;
using RaceDay.Api.Models;
using RaceDay.Api.Services;

namespace RaceDay.Api.Controllers;

// here is the controller for managing categories. It has endpoints for creating, listing and deleting categories. All endpoints
// require a session cookie to be sent with the request, and only organisers can create and delete categories. The list endpoint is
// public and can be accessed by anyone.
[Route("api")]
[Produces("application/json")]
public class CategoriesController : ControllerBase
{
    private readonly RaceDayDbContext _db;

    public CategoriesController(RaceDayDbContext db) => _db = db;

    //here's how to create a category. It requires a session cookie to be sent with the request, and only organisers can create
    //categories. The request body must contain the name, description, distance and max participants for the category. The response
    //will contain the created category.
    [HttpPost("events/{eventId:int}/categories")]
    [SessionAuthorize(Roles = RoleNames.Organiser)]
    [ProducesResponseType(typeof(CategoryResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateCategory(int eventId, [FromBody] CategoryRequest request)
    {
        var ev = await _db.Events.AsNoTracking().FirstOrDefaultAsync(e => e.EventID == eventId);
        if (ev == null)
        {
            return ApiProblems.NotFound($"No event with ID {eventId}.");
        }

        var userId = HttpContext.Session.GetUserId()!.Value;
        if (ev.UserID != userId)
        {
            return ApiProblems.Forbidden("You can only add categories to events that you organise.");
        }

        var name = request.Name.Trim();
        if (await _db.Categories.AnyAsync(c => c.EventID == eventId && c.Name == name))
        {
            return ApiProblems.Conflict($"This event already has a category called '{name}'.");
        }

        var category = new Category
        {
            UserID = userId,
            EventID = eventId,
            Name = name,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            DistanceKm = request.DistanceKm,
            MaxParticipants = request.MaxParticipants ?? 100
        };

        _db.Categories.Add(category);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetEventCategories), new { eventId }, CategoryResponse.From(category, 0));
    }

    /// <summary>Lists all categories for an event.</summary>   
    [HttpGet("events/{eventId:int}/categories")]
    [ProducesResponseType(typeof(IEnumerable<CategoryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEventCategories(int eventId)
    {
        if (!await _db.Events.AnyAsync(e => e.EventID == eventId))
        {
            return ApiProblems.NotFound($"No event with ID {eventId}.");
        }

        var categories = await _db.Categories.AsNoTracking()
            .Where(c => c.EventID == eventId)
            .OrderBy(c => c.Name)
            .Select(c => new CategoryResponse(
                c.CategoryID, c.EventID, c.Name, c.Description, c.DistanceKm, c.MaxParticipants,
                c.Enrolments.Count(en => en.Status != EnrolmentStatuses.Cancelled)))
            .ToListAsync();

        return Ok(categories);
    }

    /// <summary>Deletes a category.</summary>
    [HttpDelete("categories/{id:int}")]
    [SessionAuthorize(Roles = RoleNames.Organiser)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteCategory(int id)
    {
        var category = await _db.Categories.Include(c => c.Event).FirstOrDefaultAsync(c => c.CategoryID == id);
        if (category == null)
        {
            return ApiProblems.NotFound($"No category with ID {id}.");
        }

        if (category.Event.UserID != HttpContext.Session.GetUserId())
        {
            return ApiProblems.Forbidden("You can only remove categories from events that you organise.");
        }

        if (await _db.Enrolments.AnyAsync(en => en.CategoryID == id))
        {
            return ApiProblems.Conflict("This category already has enrolments and cannot be removed.");
        }

        _db.Categories.Remove(category);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
