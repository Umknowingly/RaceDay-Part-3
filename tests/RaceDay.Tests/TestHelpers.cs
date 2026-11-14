using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.Api.Data;
using RaceDay.Api.Dtos;
using RaceDay.Api.Models;

namespace RaceDay.Tests;

internal static class TestHelpers
{
    public const string Password = "Passw0rd!23";

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public static string UniqueEmail(string prefix = "user") => $"{prefix}-{Guid.NewGuid():N}@example.com";

    // ---------- users and sessions ----------

    public static async Task<UserSummary> RegisterAsync(HttpClient client, string role, string? email = null, string name = "Test User")
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Name = name,
            Email = email ?? UniqueEmail(),
            Password = Password,
            Role = role
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UserSummary>())!;
    }

    public static async Task LoginAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = Password
        });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Registers a new user with the given role and returns a client that is logged in as them.</summary>
    public static async Task<(HttpClient Client, UserSummary User)> LoggedInClientAsync(ApiFactory factory, string role)
    {
        var client = factory.CreateClient();
        var user = await RegisterAsync(client, role);
        await LoginAsync(client, user.Email);
        return (client, user);
    }

    // ---------- events, categories and enrolments created through the API ----------

    public static EventRequest NewEventRequest(string? name = null, DateOnly? date = null) => new()
    {
        Name = name ?? $"Event-{Guid.NewGuid():N}",
        Date = date ?? Today.AddDays(30),
        Location = "Pietermaritzburg, KZN",
        Description = "A test event.",
        RouteInfo = "Start at the city hall.",
        DistanceKm = 21.1m,
        EventType = EventTypes.Run
    };

    public static async Task<EventResponse> CreateEventAsync(HttpClient organiser, DateOnly? date = null)
    {
        var response = await organiser.PostAsJsonAsync("/api/events", NewEventRequest(date: date));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EventResponse>())!;
    }

    public static async Task<CategoryResponse> CreateCategoryAsync(
        HttpClient organiser, int eventId, string? name = null, decimal? distanceKm = 10m, int? maxParticipants = 100)
    {
        var response = await organiser.PostAsJsonAsync($"/api/events/{eventId}/categories", new CategoryRequest
        {
            Name = name ?? $"Cat-{Guid.NewGuid():N}",
            DistanceKm = distanceKm,
            MaxParticipants = maxParticipants
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;
    }

    public static EnrolRequest NewEnrolRequest() => new()
    {
        EmergencyContactName = "Nomsa Khumalo",
        EmergencyContactNumber = "0781234567"
    };

    public static async Task<EnrolmentResponse> EnrolAsync(HttpClient participant, int categoryId)
    {
        var response = await participant.PostAsJsonAsync($"/api/categories/{categoryId}/enrolments", NewEnrolRequest());
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EnrolmentResponse>())!;
    }

    // ---------- data written straight to the database ----------
    // The API refuses past events and enrolments in them, so tests that need a concluded event seed it directly.

    public static async Task<(int EventId, int CategoryId)> SeedPastEventAsync(ApiFactory factory, int organiserUserId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RaceDayDbContext>();

        var ev = new Event
        {
            UserID = organiserUserId,
            Name = $"Past-{Guid.NewGuid():N}",
            EventDate = Today.AddDays(-7),
            Venue = "Old Venue",
            DistanceKm = 10m,
            EventType = EventTypes.Run
        };
        db.Events.Add(ev);
        await db.SaveChangesAsync();

        var category = new Category
        {
            UserID = organiserUserId,
            EventID = ev.EventID,
            Name = "10km",
            DistanceKm = 10m,
            MaxParticipants = 100
        };
        db.Categories.Add(category);
        await db.SaveChangesAsync();

        return (ev.EventID, category.CategoryID);
    }

    public static async Task<int> SeedEnrolmentAsync(
        ApiFactory factory, int eventId, int categoryId, int participantUserId, string status = EnrolmentStatuses.Confirmed)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RaceDayDbContext>();

        var participantId = await db.Participants
            .Where(p => p.UserID == participantUserId)
            .Select(p => p.ParticipantID)
            .SingleAsync();

        var enrolment = new Enrolment
        {
            ParticipantID = participantId,
            EventID = eventId,
            CategoryID = categoryId,
            EnrolDate = DateTime.UtcNow,
            Status = status,
            EmergencyContactName = "Nomsa Khumalo",
            EmergencyContactNumber = "0781234567"
        };
        db.Enrolments.Add(enrolment);
        await db.SaveChangesAsync();

        return enrolment.EnrolmentID;
    }

    public static async Task SeedResultAsync(ApiFactory factory, int enrolmentId, int position = 1)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RaceDayDbContext>();

        db.Results.Add(new Result
        {
            EnrolmentID = enrolmentId,
            FinishTime = TimeSpan.FromMinutes(50),
            Position = position,
            Status = ResultStatuses.Finished
        });
        await db.SaveChangesAsync();
    }
}
