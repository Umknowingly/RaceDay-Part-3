using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Data;
using RaceDay.Api.Dtos;
using RaceDay.Api.Services;

namespace RaceDay.Api.Controllers;

/// <summary>User profiles.</summary>
[ApiController]
[Route("api/users")]
[Produces("application/json")]
public class UsersController : ControllerBase
{
    private readonly RaceDayDbContext _db;

    public UsersController(RaceDayDbContext db) => _db = db;

    /// <summary>Retrieves a user's profile.</summary>
    /// <remarks>
    /// Any logged-in user. You see your own full profile (including email and contact number);
    /// for anyone else only the name and role are returned.
    /// </remarks>
    /// <param name="id">The user's ID.</param>
    /// <response code="200">The profile.</response>
    /// <response code="401">Not logged in.</response>
    /// <response code="404">No user with that ID.</response>
    [HttpGet("{id:int}")]
    [SessionAuthorize]
    [ProducesResponseType(typeof(UserSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUser(int id)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserID == id);
        if (user == null)
        {
            return ApiProblems.NotFound($"No user with ID {id}.");
        }

        return HttpContext.Session.GetUserId() == id
            ? Ok(UserSummary.From(user))
            : Ok(PublicProfile.From(user));
    }

    /// <summary>Updates the logged-in user's own profile.</summary>
    /// <remarks>Any logged-in user, but only for their own account.</remarks>
    /// <param name="id">The user's ID (must be the logged-in user).</param>
    /// <param name="request">name, email and optional contactNumber.</param>
    /// <response code="200">The updated profile.</response>
    /// <response code="400">Validation failed.</response>
    /// <response code="401">Not logged in.</response>
    /// <response code="403">The ID belongs to a different account.</response>
    /// <response code="404">No user with that ID.</response>
    /// <response code="409">That email address belongs to another user.</response>
    [HttpPut("{id:int}")]
    [SessionAuthorize]
    [ProducesResponseType(typeof(UserSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateProfileRequest request)
    {
        var user = await _db.Users.Include(u => u.Participant).FirstOrDefaultAsync(u => u.UserID == id);
        if (user == null)
        {
            return ApiProblems.NotFound($"No user with ID {id}.");
        }

        if (HttpContext.Session.GetUserId() != id)
        {
            return ApiProblems.Forbidden("You can only update your own profile.");
        }

        var email = request.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Email == email && u.UserID != id))
        {
            return ApiProblems.Conflict("That email address belongs to another user.");
        }

        user.Name = request.Name.Trim();
        user.Email = email;
        user.ContactNumber = string.IsNullOrWhiteSpace(request.ContactNumber) ? null : request.ContactNumber.Trim();
        if (user.Participant != null)
        {
            user.Participant.Name = user.Name;
        }

        await _db.SaveChangesAsync();
        return Ok(UserSummary.From(user));
    }
}
