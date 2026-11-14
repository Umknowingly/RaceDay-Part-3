using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Data;
using RaceDay.Api.Dtos;
using RaceDay.Api.Models;
using RaceDay.Api.Services;

namespace RaceDay.Api.Controllers;

/// <summary>Enrolling in events, and viewing, cancelling and confirming enrolments.</summary>
[ApiController]
[Route("api")]
[Produces("application/json")]
public class EnrolmentsController : ControllerBase
{
    private readonly RaceDayDbContext _db;

    public EnrolmentsController(RaceDayDbContext db) => _db = db;

    /// <summary>Enrols the logged-in Participant in a category of an event.</summary>
    /// <remarks>
    /// Participant only. The enrolment records the participant, the event and the chosen category,
    /// and starts as Pending until the Organiser confirms it. A participant can enter an event once,
    /// and only events that have not yet taken place.
    /// </remarks>
    /// <param name="categoryId">The category to enter.</param>
    /// <param name="request">An emergency contact name and number.</param>
    /// <response code="201">The enrolment was recorded.</response>
    /// <response code="400">Validation failed, the category is full, or the event has already taken place.</response>
    /// <response code="401">Not logged in.</response>
    /// <response code="403">The logged-in user is not a Participant.</response>
    /// <response code="404">No category with that ID.</response>
    /// <response code="409">The participant is already enrolled in this event.</response>
    [HttpPost("categories/{categoryId:int}/enrolments")]
    [SessionAuthorize(Roles = RoleNames.Participant)]
    [ProducesResponseType(typeof(EnrolmentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Enrol(int categoryId, [FromBody] EnrolRequest request)
    {
        var userId = HttpContext.Session.GetUserId()!.Value;
        var participant = await _db.Participants.AsNoTracking().FirstOrDefaultAsync(p => p.UserID == userId);
        if (participant == null)
        {
            return ApiProblems.Forbidden("Your account has no participant profile.");
        }

        var category = await _db.Categories.Include(c => c.Event).FirstOrDefaultAsync(c => c.CategoryID == categoryId);
        if (category == null)
        {
            return ApiProblems.NotFound($"No category with ID {categoryId}.");
        }

        if (category.Event.EventDate < DateOnly.FromDateTime(DateTime.UtcNow))
        {
            return ApiProblems.BadRequest("This event has already taken place.");
        }

        if (await _db.Enrolments.AnyAsync(e => e.ParticipantID == participant.ParticipantID && e.EventID == category.EventID))
        {
            return ApiProblems.Conflict("You are already enrolled in this event.");
        }

        var taken = await _db.Enrolments.CountAsync(e => e.CategoryID == categoryId && e.Status != EnrolmentStatuses.Cancelled);
        if (taken >= category.MaxParticipants)
        {
            return ApiProblems.BadRequest("This category is full.");
        }

        var enrolment = new Enrolment
        {
            ParticipantID = participant.ParticipantID,
            EventID = category.EventID,
            CategoryID = categoryId,
            EnrolDate = DateTime.UtcNow,
            Status = EnrolmentStatuses.Pending,
            EmergencyContactName = request.EmergencyContactName.Trim(),
            EmergencyContactNumber = request.EmergencyContactNumber.Trim()
        };

        _db.Enrolments.Add(enrolment);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return ApiProblems.Conflict("You are already enrolled in this event.");
        }

        var response = await Project(_db.Enrolments.AsNoTracking().Where(e => e.EnrolmentID == enrolment.EnrolmentID)).FirstAsync();
        return CreatedAtAction(nameof(GetUserEnrolments), new { userId }, response);
    }

    /// <summary>Lists everyone enrolled in one of the Organiser's events.</summary>
    /// <remarks>Organiser only, and only for events they own. Includes each participant's category, status and emergency contact.</remarks>
    /// <param name="eventId">The event's ID.</param>
    /// <response code="200">The event's enrolments (possibly an empty list).</response>
    /// <response code="401">Not logged in.</response>
    /// <response code="403">Not an Organiser, or the event belongs to another Organiser.</response>
    /// <response code="404">No event with that ID.</response>
    [HttpGet("events/{eventId:int}/enrolments")]
    [SessionAuthorize(Roles = RoleNames.Organiser)]
    [ProducesResponseType(typeof(IEnumerable<EnrolmentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEventEnrolments(int eventId)
    {
        var ev = await _db.Events.AsNoTracking().FirstOrDefaultAsync(e => e.EventID == eventId);
        if (ev == null)
        {
            return ApiProblems.NotFound($"No event with ID {eventId}.");
        }

        if (ev.UserID != HttpContext.Session.GetUserId())
        {
            return ApiProblems.Forbidden("You can only view enrolments for events that you organise.");
        }

        var query = _db.Enrolments.AsNoTracking()
            .Where(e => e.EventID == eventId)
            .OrderBy(e => e.Category.Name).ThenBy(e => e.Participant.Name);

        return Ok(await Project(query).ToListAsync());
    }

    /// <summary>Lists a user's enrolments, newest event first.</summary>
    /// <remarks>
    /// A user can view their own enrolments. An Organiser can view a participant's enrolments, but only
    /// those in events the Organiser owns. Anyone else is refused.
    /// </remarks>
    /// <param name="userId">The user's ID.</param>
    /// <response code="200">The enrolments (possibly an empty list).</response>
    /// <response code="401">Not logged in.</response>
    /// <response code="403">Viewing another user's enrolments is not allowed for this account.</response>
    /// <response code="404">No user with that ID.</response>
    [HttpGet("users/{userId:int}/enrolments")]
    [SessionAuthorize]
    [ProducesResponseType(typeof(IEnumerable<EnrolmentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserEnrolments(int userId)
    {
        if (!await _db.Users.AnyAsync(u => u.UserID == userId))
        {
            return ApiProblems.NotFound($"No user with ID {userId}.");
        }

        var currentUserId = HttpContext.Session.GetUserId()!.Value;
        var role = HttpContext.Session.GetRole();

        var query = _db.Enrolments.AsNoTracking().Where(e => e.Participant.UserID == userId);
        if (currentUserId != userId)
        {
            if (role != RoleNames.Organiser)
            {
                return ApiProblems.Forbidden("You can only view your own enrolments.");
            }

            query = query.Where(e => e.Event.UserID == currentUserId);
        }

        query = query.OrderByDescending(e => e.Event.EventDate).ThenBy(e => e.Event.Name);
        return Ok(await Project(query).ToListAsync());
    }

    /// <summary>Cancels (removes) the logged-in Participant's own enrolment.</summary>
    /// <remarks>Participant only. An enrolment that already has a result cannot be cancelled.</remarks>
    /// <param name="id">The enrolment's ID.</param>
    /// <response code="204">The enrolment was removed.</response>
    /// <response code="401">Not logged in.</response>
    /// <response code="403">Not a Participant, or the enrolment belongs to someone else.</response>
    /// <response code="404">No enrolment with that ID.</response>
    /// <response code="409">A result has already been recorded for this enrolment.</response>
    [HttpDelete("enrolments/{id:int}")]
    [SessionAuthorize(Roles = RoleNames.Participant)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelEnrolment(int id)
    {
        var enrolment = await _db.Enrolments
            .Include(e => e.Participant)
            .Include(e => e.Result)
            .FirstOrDefaultAsync(e => e.EnrolmentID == id);
        if (enrolment == null)
        {
            return ApiProblems.NotFound($"No enrolment with ID {id}.");
        }

        if (enrolment.Participant.UserID != HttpContext.Session.GetUserId())
        {
            return ApiProblems.Forbidden("You can only cancel your own enrolments.");
        }

        if (enrolment.Result != null)
        {
            return ApiProblems.Conflict("A result has already been recorded, so this enrolment cannot be cancelled.");
        }

        _db.Enrolments.Remove(enrolment);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Changes an enrolment's status to Pending, Confirmed or Cancelled.</summary>
    /// <remarks>Organiser only, and only for events they own. New enrolments start as Pending.</remarks>
    /// <param name="id">The enrolment's ID.</param>
    /// <param name="request">The new status.</param>
    /// <response code="200">The updated enrolment.</response>
    /// <response code="400">The status is not Pending, Confirmed or Cancelled.</response>
    /// <response code="401">Not logged in.</response>
    /// <response code="403">Not an Organiser, or the event belongs to another Organiser.</response>
    /// <response code="404">No enrolment with that ID.</response>
    [HttpPut("enrolments/{id:int}/status")]
    [SessionAuthorize(Roles = RoleNames.Organiser)]
    [ProducesResponseType(typeof(EnrolmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateEnrolmentStatus(int id, [FromBody] UpdateEnrolmentStatusRequest request)
    {
        var enrolment = await _db.Enrolments.Include(e => e.Event).FirstOrDefaultAsync(e => e.EnrolmentID == id);
        if (enrolment == null)
        {
            return ApiProblems.NotFound($"No enrolment with ID {id}.");
        }

        if (enrolment.Event.UserID != HttpContext.Session.GetUserId())
        {
            return ApiProblems.Forbidden("You can only change enrolments for events that you organise.");
        }

        if (!EnrolmentStatuses.IsValid(request.Status))
        {
            ModelState.AddModelError(nameof(request.Status), "Status must be 'Pending', 'Confirmed' or 'Cancelled'.");
            return ValidationProblem(ModelState);
        }

        enrolment.Status = request.Status;
        await _db.SaveChangesAsync();

        var response = await Project(_db.Enrolments.AsNoTracking().Where(e => e.EnrolmentID == id)).FirstAsync();
        return Ok(response);
    }

    private static IQueryable<EnrolmentResponse> Project(IQueryable<Enrolment> query) =>
        query.Select(e => new EnrolmentResponse(
            e.EnrolmentID, e.EventID, e.Event.Name, e.Event.EventDate,
            e.CategoryID, e.Category.Name, e.ParticipantID, e.Participant.Name,
            e.EnrolDate, e.Status, e.EmergencyContactName, e.EmergencyContactNumber,
            e.Result != null));
}
