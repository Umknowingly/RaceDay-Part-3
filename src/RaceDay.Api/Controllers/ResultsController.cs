using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Data;
using RaceDay.Api.Dtos;
using RaceDay.Api.Models;
using RaceDay.Api.Services;

namespace RaceDay.Api.Controllers;

/// <summary>Race results. Organisers capture them after an event; viewing an event's results is public.</summary>
[ApiController]
[Route("api")]
[Produces("application/json")]
public class ResultsController : ControllerBase
{
    private readonly RaceDayDbContext _db;

    public ResultsController(RaceDayDbContext db) => _db = db;

    /// <summary>Captures a finish time and finishing position for an enrolment.</summary>
    /// <remarks>
    /// Organiser only, and only for events they own. Results can only be captured once the event has
    /// taken place, and only once per enrolment. A Finished result needs a finish time and a position;
    /// DNF and DSQ results do not.
    /// </remarks>
    /// <param name="enrolmentId">The enrolment the result belongs to.</param>
    /// <param name="request">finishTime (hh:mm:ss), position, status and optional notes.</param>
    /// <response code="201">The result was recorded.</response>
    /// <response code="400">Validation failed, the event has not taken place yet, or the enrolment is cancelled.</response>
    /// <response code="401">Not logged in.</response>
    /// <response code="403">Not an Organiser, or the event belongs to another Organiser.</response>
    /// <response code="404">No enrolment with that ID.</response>
    /// <response code="409">A result has already been captured for this enrolment.</response>
    [HttpPost("enrolments/{enrolmentId:int}/results")]
    [SessionAuthorize(Roles = RoleNames.Organiser)]
    [ProducesResponseType(typeof(ResultResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CaptureResult(int enrolmentId, [FromBody] ResultRequest request)
    {
        var enrolment = await _db.Enrolments
            .Include(e => e.Event)
            .Include(e => e.Result)
            .FirstOrDefaultAsync(e => e.EnrolmentID == enrolmentId);
        if (enrolment == null)
        {
            return ApiProblems.NotFound($"No enrolment with ID {enrolmentId}.");
        }

        if (enrolment.Event.UserID != HttpContext.Session.GetUserId())
        {
            return ApiProblems.Forbidden("You can only capture results for events that you organise.");
        }

        if (enrolment.Result != null)
        {
            return ApiProblems.Conflict("A result has already been captured for this enrolment.");
        }

        if (enrolment.Status == EnrolmentStatuses.Cancelled)
        {
            return ApiProblems.BadRequest("A cancelled enrolment cannot have a result.");
        }

        if (enrolment.Event.EventDate > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            return ApiProblems.BadRequest("Results can only be captured once the event has taken place.");
        }

        if (!ResultStatuses.IsValid(request.Status))
        {
            ModelState.AddModelError(nameof(request.Status), "Status must be 'Finished', 'DNF' or 'DSQ'.");
        }

        if (request.FinishTime >= TimeSpan.FromHours(24))
        {
            ModelState.AddModelError(nameof(request.FinishTime), "The finish time must be under 24 hours.");
        }

        if (request.Status == ResultStatuses.Finished)
        {
            if (request.FinishTime == null || request.FinishTime <= TimeSpan.Zero)
            {
                ModelState.AddModelError(nameof(request.FinishTime), "A finish time greater than zero is required for a finished result.");
            }

            if (request.Position == null)
            {
                ModelState.AddModelError(nameof(request.Position), "A finishing position is required for a finished result.");
            }
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = new Result
        {
            EnrolmentID = enrolmentId,
            FinishTime = request.FinishTime,
            Position = request.Position,
            Status = request.Status,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
        };

        _db.Results.Add(result);
        await _db.SaveChangesAsync();

        var response = await Project(_db.Results.AsNoTracking().Where(r => r.ResultID == result.ResultID)).FirstAsync();
        return CreatedAtAction(nameof(GetEventResults), new { eventId = enrolment.EventID }, response);
    }

    /// <summary>Lists the results for an event, grouped by category and ordered by finishing position.</summary>
    /// <remarks>Public endpoint.</remarks>
    /// <param name="eventId">The event's ID.</param>
    /// <response code="200">The event's results (possibly an empty list).</response>
    /// <response code="404">No event with that ID.</response>
    [HttpGet("events/{eventId:int}/results")]
    [ProducesResponseType(typeof(IEnumerable<ResultResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEventResults(int eventId)
    {
        if (!await _db.Events.AnyAsync(e => e.EventID == eventId))
        {
            return ApiProblems.NotFound($"No event with ID {eventId}.");
        }

        var query = _db.Results.AsNoTracking()
            .Where(r => r.Enrolment.EventID == eventId)
            .OrderBy(r => r.Enrolment.Category.Name)
            .ThenBy(r => r.Position == null)
            .ThenBy(r => r.Position)
            .ThenBy(r => r.FinishTime);

        return Ok(await Project(query).ToListAsync());
    }

    /// <summary>Lists a participant's own race history, most recent first.</summary>
    /// <remarks>
    /// Logged-in users can only view their own results. Each result includes the event, date, category,
    /// finish time, finishing position and the number of finishers in that category.
    /// </remarks>
    /// <param name="userId">The user's ID (must be the logged-in user).</param>
    /// <response code="200">The user's results (possibly an empty list).</response>
    /// <response code="401">Not logged in.</response>
    /// <response code="403">The ID belongs to a different account.</response>
    /// <response code="404">No user with that ID.</response>
    [HttpGet("users/{userId:int}/results")]
    [SessionAuthorize]
    [ProducesResponseType(typeof(IEnumerable<ResultResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserResults(int userId)
    {
        if (!await _db.Users.AnyAsync(u => u.UserID == userId))
        {
            return ApiProblems.NotFound($"No user with ID {userId}.");
        }

        if (HttpContext.Session.GetUserId() != userId)
        {
            return ApiProblems.Forbidden("You can only view your own results.");
        }

        var query = _db.Results.AsNoTracking()
            .Where(r => r.Enrolment.Participant.UserID == userId)
            .OrderByDescending(r => r.Enrolment.Event.EventDate);

        return Ok(await Project(query).ToListAsync());
    }

    private IQueryable<ResultResponse> Project(IQueryable<Result> query) =>
        query.Select(r => new ResultResponse(
            r.ResultID, r.EnrolmentID, r.Enrolment.EventID, r.Enrolment.Event.Name, r.Enrolment.Event.EventDate,
            r.Enrolment.CategoryID, r.Enrolment.Category.Name, r.Enrolment.ParticipantID, r.Enrolment.Participant.Name,
            r.FinishTime, r.Position,
            _db.Results.Count(x => x.Enrolment.CategoryID == r.Enrolment.CategoryID && x.Status == ResultStatuses.Finished),
            r.Status, r.Notes));
}
