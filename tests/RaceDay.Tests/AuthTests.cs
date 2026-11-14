using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.Api.Data;
using RaceDay.Api.Dtos;
using RaceDay.Api.Models;
using Xunit;

namespace RaceDay.Tests;

public class AuthTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AuthTests(ApiFactory factory) => _factory = factory;

    private static RegisterRequest ValidRegistration(string role = RoleNames.Organiser) => new()
    {
        Name = "Thabo Nkosi",
        Email = TestHelpers.UniqueEmail(),
        Password = TestHelpers.Password,
        Role = role
    };

    // ---------- Registration ----------

    [Fact]
    public async Task Register_ValidOrganiser_Returns201WithoutPassword()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", ValidRegistration());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_Participant_CreatesLinkedParticipantRecord()
    {
        var client = _factory.CreateClient();

        var user = await TestHelpers.RegisterAsync(client, RoleNames.Participant);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RaceDayDbContext>();
        Assert.True(await db.Participants.AnyAsync(p => p.UserID == user.UserID));
    }

    [Fact]
    public async Task Register_Organiser_DoesNotCreateParticipantRecord()
    {
        var client = _factory.CreateClient();

        var user = await TestHelpers.RegisterAsync(client, RoleNames.Organiser);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RaceDayDbContext>();
        Assert.False(await db.Participants.AnyAsync(p => p.UserID == user.UserID));
    }

    [Fact]
    public async Task Register_StoresAHashedPassword_NotThePlainText()
    {
        var client = _factory.CreateClient();

        var user = await TestHelpers.RegisterAsync(client, RoleNames.Participant);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RaceDayDbContext>();
        var stored = await db.Users.Where(u => u.UserID == user.UserID).Select(u => u.PasswordHash).SingleAsync();
        Assert.NotEqual(TestHelpers.Password, stored);
        Assert.StartsWith("v1.", stored);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409()
    {
        var client = _factory.CreateClient();
        var request = ValidRegistration();
        await client.PostAsJsonAsync("/api/auth/register", request);

        var response = await client.PostAsJsonAsync("/api/auth/register", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Register_InvalidRole_Returns400()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", ValidRegistration("Admin"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_ShortPassword_Returns400()
    {
        var client = _factory.CreateClient();
        var request = ValidRegistration();
        request.Password = "short";

        var response = await client.PostAsJsonAsync("/api/auth/register", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_MissingEmail_Returns400()
    {
        var client = _factory.CreateClient();
        var request = ValidRegistration();
        request.Email = "";

        var response = await client.PostAsJsonAsync("/api/auth/register", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Login ----------

    [Fact]
    public async Task Login_ValidCredentials_Returns200AndSetsSessionCookie()
    {
        var client = _factory.CreateClient();
        var user = await TestHelpers.RegisterAsync(client, RoleNames.Organiser);

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = user.Email, Password = TestHelpers.Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        Assert.Contains(cookies!, c => c.Contains(".RaceDay.Session"));
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsTheUsersRole()
    {
        var client = _factory.CreateClient();
        var user = await TestHelpers.RegisterAsync(client, RoleNames.Participant);

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = user.Email, Password = TestHelpers.Password });

        var summary = await response.Content.ReadFromJsonAsync<UserSummary>();
        Assert.Equal(RoleNames.Participant, summary!.Role);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var client = _factory.CreateClient();
        var user = await TestHelpers.RegisterAsync(client, RoleNames.Organiser);

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = user.Email, Password = "not-the-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_UnknownEmail_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = TestHelpers.UniqueEmail("nobody"), Password = TestHelpers.Password });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- Logout / session ----------

    [Fact]
    public async Task Logout_EndsTheSession()
    {
        var (client, user) = await TestHelpers.LoggedInClientAsync(_factory, RoleNames.Organiser);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/users/{user.UserID}")).StatusCode);

        var logout = await client.PostAsync("/api/auth/logout", null);
        var afterLogout = await client.GetAsync($"/api/users/{user.UserID}");

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task Logout_WithoutASession_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/auth/logout", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
