using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Data;
using RaceDay.Api.Dtos;
using RaceDay.Api.Models;
using RaceDay.Api.Services;

namespace RaceDay.Api.Controllers;

/// <summary>Create, view, update and delete events. Viewing is public; changes need an Organiser.</summary>
[ApiController]
[Route("api/events")]
[Produces("application/json")]
public class EventsController : ControllerBase
{
    private readonly RaceDayDbContext _db;

    public EventsController(RaceDayDbContext db) => _db = db;

    /// <summary>Lists upcoming events, optionally filtered by location, date or event type.</summary>
    /// <remarks>Public endpoint. Only events happening today or later are returned, soonest first.</remarks>
    /// <param name="location">Optional. Matches events whose location contains this text (not case sensitive).</param>
    /// <param name="date">Optional. Only events on this date (yyyy-MM-dd).</param>
    /// <param name="eventType">Optional. "Run", "Walk" or "Cycle".</param>
    /// <response code="200">The matching events (possibly an empty list).</response>
    /// <response code="400">The date or event type filter is invalid.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<EventResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetEvents(
        [FromQuery] string? location, [FromQuery] DateOnly? date, [FromQuery] string? eventType)
    {
        if (!string.IsNullOrWhiteSpace(eventType) && !EventTypes.IsValid(eventType))
        {
            ModelState.AddModelError(nameof(eventType), "Event type must be 'Run', 'Walk' or 'Cycle'.");
            return ValidationProblem(ModelState);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var query = _db.Events.AsNoTracking().Include(e => e.Organiser).Where(e => e.EventDate >= today);

        if (!string.IsNullOrWhiteSpace(location))
        {
            var term = location.Trim().ToLowerInvariant();
            query = query.Where(e => e.Venue.ToLower().Contains(term));
        }

        if (date.HasValue)
        {
            query = query.Where(e => e.EventDate == date.Value);
        }

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            query = query.Where(e => e.EventType == eventType);
        }

        var events = await query.OrderBy(e => e.EventDate).ThenBy(e => e.Name).ToListAsync();
        return Ok(events.Select(EventResponse.From));
    }

    /// <summary>Lists every event the logged-in Organiser has created, with enrolment counts.</summary>
    /// <remarks>
    /// Organiser only. Includes past events, soonest first, and counts the categories, the enrolments
    /// (cancelled ones excluded) and the captured results for each event. Used by the Organiser dashboard.
    /// </remarks>
    /// <response code="200">The Organiser's events with their counts.</response>
    /// <response code="401">Not logged in.</response>
    /// <response code="403">The logged-in user is not an Organiser.</response>
    [HttpGet("mine")]
    [SessionAuthorize(Roles = RoleNames.Organiser)]
    [ProducesResponseType(typeof(IEnumerable<OrganiserEventSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMyEvents()
    {
        var userId = HttpContext.Session.GetUserId()!.Value;

        var events = await _db.Events.AsNoTracking()
            .Where(e => e.UserID == userId)
            .OrderBy(e => e.EventDate).ThenBy(e => e.Name)
            .Select(e => new OrganiserEventSummary(
                e.EventID, e.Name, e.EventDate, e.Venue, e.DistanceKm, e.EventType,
                e.Categories.Count(),
                e.Enrolments.Count(en => en.Status != EnrolmentStatuses.Cancelled),
                e.Enrolments.Count(en => en.Result != null)))
            .ToListAsync();

        return Ok(events);
    }

    /// <summary>Retrieves the full details of one event.</summary>
    /// <remarks>Public endpoint. Works for past events too.</remarks>
    /// <param name="id">The event's ID.</param>
    /// <response code="200">The event.</response>
    /// <response code="404">No event with that ID.</response>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(EventResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEvent(int id)
    {
        var ev = await _db.Events.AsNoTracking().Include(e => e.Organiser)
            .FirstOrDefaultAsync(e => e.EventID == id);

        return ev == null
            ? ApiProblems.NotFound($"No event with ID {id}.")
            : Ok(EventResponse.From(ev));
    }

    /// <summary>Creates a new event owned by the logged-in Organiser.</summary>
    /// <remarks>Organiser only.</remarks>
    /// <param name="request">name, date, location, distanceKm and eventType are required; description and routeInfo are optional.</param>
    /// <response code="201">The event was created. Returns the event.</response>
    /// <response code="400">Validation failed, the event type is not Run/Walk/Cycle, or the date is in the past.</response>
    /// <response code="401">Not logged in.</response>
    /// <response code="403">The logged-in user is not an Organiser.</response>
    [HttpPost]
    [SessionAuthorize(Roles = RoleNames.Organiser)]
    [ProducesResponseType(typeof(EventResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateEvent([FromBody] EventRequest request)
    {
        if (!IsValid(request, requireFutureDate: true))
        {
            return ValidationProblem(ModelState);
        }

        var ev = new Event { UserID = HttpContext.Session.GetUserId()!.Value };
        Apply(ev, request);

        _db.Events.Add(ev);
        await _db.SaveChangesAsync();
        await _db.Entry(ev).Reference(e => e.Organiser).LoadAsync();

        return CreatedAtAction(nameof(GetEvent), new { id = ev.EventID }, EventResponse.From(ev));
    }

    /// <summary>Updates an event's details.</summary>
    /// <remarks>Organiser only, and only for events they own.</remarks>
    /// <param name="id">The event's ID.</param>
    /// <param name="request">The new values for all event fields.</param>
    /// <response code="200">The updated event.</response>
    /// <response code="400">Validation failed or the event type is not Run/Walk/Cycle.</response>
    /// <response code="401">Not logged in.</response>
    /// <response code="403">Not an Organiser, or the event belongs to another Organiser.</response>
    /// <response code="404">No event with that ID.</response>
    [HttpPut("{id:int}")]
    [SessionAuthorize(Roles = RoleNames.Organiser)]
    [ProducesResponseType(typeof(EventResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateEvent(int id, [FromBody] EventRequest request)
    {
        var ev = await _db.Events.Include(e => e.Organiser).FirstOrDefaultAsync(e => e.EventID == id);
        if (ev == null)
        {
            return ApiProblems.NotFound($"No event with ID {id}.");
        }

        if (ev.UserID != HttpContext.Session.GetUserId())
        {
            return ApiProblems.Forbidden("You can only change events that you organise.");
        }

        if (!IsValid(request, requireFutureDate: false))
        {
            return ValidationProblem(ModelState);
        }

        Apply(ev, request);
        await _db.SaveChangesAsync();
        return Ok(EventResponse.From(ev));
    }

    /// <summary>Deletes an event together with its categories.</summary>
    /// <remarks>Organiser only, and only for events they own. An event that already has enrolments cannot be deleted.</remarks>
    /// <param name="id">The event's ID.</param>
    /// <response code="204">The event was deleted.</response>
    /// <response code="401">Not logged in.</response>
    /// <response code="403">Not an Organiser, or the event belongs to another Organiser.</response>
    /// <response code="404">No event with that ID.</response>
    /// <response code="409">The event already has enrolments.</response>
    [HttpDelete("{id:int}")]
    [SessionAuthorize(Roles = RoleNames.Organiser)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteEvent(int id)
    {
        var ev = await _db.Events.Include(e => e.Categories).FirstOrDefaultAsync(e => e.EventID == id);
        if (ev == null)
        {
            return ApiProblems.NotFound($"No event with ID {id}.");
        }

        if (ev.UserID != HttpContext.Session.GetUserId())
        {
            return ApiProblems.Forbidden("You can only delete events that you organise.");
        }

        if (await _db.Enrolments.AnyAsync(en => en.EventID == id))
        {
            return ApiProblems.Conflict("This event already has enrolments and cannot be deleted.");
        }

        _db.Categories.RemoveRange(ev.Categories);
        _db.Events.Remove(ev);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private bool IsValid(EventRequest request, bool requireFutureDate)
    {
        if (!EventTypes.IsValid(request.EventType))
        {
            ModelState.AddModelError(nameof(request.EventType), "Event type must be 'Run', 'Walk' or 'Cycle'.");
        }

        if (requireFutureDate && request.Date!.Value < DateOnly.FromDateTime(DateTime.UtcNow))
        {
            ModelState.AddModelError(nameof(request.Date), "The event date cannot be in the past.");
        }

        return ModelState.IsValid;
    }

    private static void Apply(Event ev, EventRequest request)
    {
        ev.Name = request.Name.Trim();
        ev.EventDate = request.Date!.Value;
        ev.Venue = request.Location.Trim();
        ev.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        ev.RouteInfo = string.IsNullOrWhiteSpace(request.RouteInfo) ? null : request.RouteInfo.Trim();
        ev.DistanceKm = request.DistanceKm;
        ev.EventType = request.EventType;
    }
}
