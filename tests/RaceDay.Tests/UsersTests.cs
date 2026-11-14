using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.Api.Data;
using RaceDay.Api.Dtos;
using RaceDay.Api.Models;

namespace RaceDay.Tests;

public class UsersTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public UsersTests(ApiFactory factory) => _factory = factory;

    private static UpdateProfileRequest Update(string name = "Updated Name", string? email = null, string? contact = "0821234567") => new()
    {
        Name = name,
        Email = email ?? TestHelpers.UniqueEmail("updated"),
        ContactNumber = contact
    };

    // ---------- GET /api/users/{id} ----------

    [Fact]
    public async Task GetUser_WithoutASession_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/users/1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetUser_OwnProfile_IncludesEmail()
    {
        var (client, user) = await TestHelpers.LoggedInClientAsync(_factory, RoleNames.Participant);

        var response = await client.GetAsync($"/api/users/{user.UserID}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<UserSummary>();
        Assert.Equal(user.Email, profile!.Email);
    }

    [Fact]
    public async Task GetUser_SomeoneElsesProfile_HidesEmail()
    {
        var other = await TestHelpers.RegisterAsync(_factory.CreateClient(), RoleNames.Organiser);
        var (client, _) = await TestHelpers.LoggedInClientAsync(_factory, RoleNames.Participant);

        var response = await client.GetAsync($"/api/users/{other.UserID}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("email", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetUser_UnknownId_Returns404()
    {
        var (client, _) = await TestHelpers.LoggedInClientAsync(_factory, RoleNames.Organiser);

        var response = await client.GetAsync("/api/users/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- PUT /api/users/{id} ----------

    [Fact]
    public async Task UpdateUser_WithoutASession_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync("/api/users/1", Update());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_OwnProfile_Returns200AndSavesChanges()
    {
        var (client, user) = await TestHelpers.LoggedInClientAsync(_factory, RoleNames.Organiser);
        var request = Update("Lindiwe Dlamini", contact: "0837654321");

        var response = await client.PutAsJsonAsync($"/api/users/{user.UserID}", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<UserSummary>();
        Assert.Equal("Lindiwe Dlamini", updated!.Name);
        Assert.Equal("0837654321", updated.ContactNumber);
        Assert.Equal(request.Email.ToLowerInvariant(), updated.Email);
    }

    [Fact]
    public async Task UpdateUser_ForAParticipant_AlsoUpdatesTheParticipantName()
    {
        var (client, user) = await TestHelpers.LoggedInClientAsync(_factory, RoleNames.Participant);

        await client.PutAsJsonAsync($"/api/users/{user.UserID}", Update("Sipho Khumalo"));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RaceDayDbContext>();
        var participantName = await db.Participants.Where(p => p.UserID == user.UserID).Select(p => p.Name).SingleAsync();
        Assert.Equal("Sipho Khumalo", participantName);
    }

    [Fact]
    public async Task UpdateUser_SomeoneElsesProfile_Returns403()
    {
        var other = await TestHelpers.RegisterAsync(_factory.CreateClient(), RoleNames.Participant);
        var (client, _) = await TestHelpers.LoggedInClientAsync(_factory, RoleNames.Participant);

        var response = await client.PutAsJsonAsync($"/api/users/{other.UserID}", Update());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_UnknownId_Returns404()
    {
        var (client, _) = await TestHelpers.LoggedInClientAsync(_factory, RoleNames.Organiser);

        var response = await client.PutAsJsonAsync("/api/users/999999", Update());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_EmailBelongingToAnotherUser_Returns409()
    {
        var other = await TestHelpers.RegisterAsync(_factory.CreateClient(), RoleNames.Organiser);
        var (client, user) = await TestHelpers.LoggedInClientAsync(_factory, RoleNames.Participant);

        var response = await client.PutAsJsonAsync($"/api/users/{user.UserID}", Update(email: other.Email));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_InvalidEmail_Returns400()
    {
        var (client, user) = await TestHelpers.LoggedInClientAsync(_factory, RoleNames.Participant);

        var response = await client.PutAsJsonAsync($"/api/users/{user.UserID}", Update(email: "not-an-email"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
