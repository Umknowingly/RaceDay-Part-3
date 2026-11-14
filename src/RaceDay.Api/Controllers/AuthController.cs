using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Data;
using RaceDay.Api.Dtos;
using RaceDay.Api.Models;
using RaceDay.Api.Services;

namespace RaceDay.Api.Controllers;

// the authentication endpoints for registering, 
//logging in and logging out. All other endpoints require a session cookie to be sent with the request.
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly RaceDayDbContext _db;
    private readonly IPasswordHasher _hasher;

    public AuthController(RaceDayDbContext db, IPasswordHasher hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    // this will be the first endpoint that a user will hit when they want to register, login or logout. and it will be the only
    // endpoint that does not require a session cookie to be sent with the request. All other endpoints will require a session
    // cookie to be sent with the request.
    [HttpPost("register")]
    [ProducesResponseType(typeof(UserSummary), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (!RoleNames.IsValid(request.Role))
        {
            ModelState.AddModelError(nameof(request.Role), "Role must be 'Organiser' or 'Participant'.");
            return ValidationProblem(ModelState);
        }

        var email = request.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Email == email))
        {
            return ApiProblems.Conflict("That email address is already registered.");
        }

        var user = new AppUser
        {
            Name = request.Name.Trim(),
            Email = email,
            PasswordHash = _hasher.Hash(request.Password),
            Role = request.Role
        };

        if (request.Role == RoleNames.Participant)
        {
            user.Participant = new Participant { Name = user.Name };
        }

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(UsersController.GetUser), "Users", new { id = user.UserID }, UserSummary.From(user));
    }

    // this logs in a user and starts a server-side session holding the user's ID and role. It is a public endpoint, and on success a session cookie is returned and
    // used automatically by Swagger UI and browsers for every later request. No token is returned in the response body.
    [HttpPost("login")]
    [ProducesResponseType(typeof(UserSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email);

        if (user == null || !_hasher.Verify(request.Password, user.PasswordHash))
        {
            return ApiProblems.Unauthorized("Invalid email or password.");
        }

        HttpContext.Session.SignIn(user.UserID, user.Role);
        return Ok(UserSummary.From(user));
    }

    // this will log out a user and clear the server-side session. It is a public endpoint, and on success the session cookie is
    // cleared and no longer sent automatically by Swagger UI and browsers for every later request. No token is returned in the
    // response body.
    [HttpPost("logout")]
    [SessionAuthorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return NoContent();
    }
}
