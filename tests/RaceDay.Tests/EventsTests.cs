using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.Api.Data;
using RaceDay.Api.Dtos;
using RaceDay.Api.Models;
using Xunit;

namespace RaceDay.Tests;

public class EventsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public EventsTests(ApiFactory factory) => _factory = factory;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static EventRequest NewEvent(
        string? name = null, DateOnly? date = null, string location = "Pietermaritzburg, KZN",
        string eventType = "Run", decimal distanceKm = 21.1m) => new()
    {
        Name = name ?? $"Event-{Guid.NewGuid():N}",
        Date = date ?? Today.AddDays(30),
        Location = location,
        Description = "A test event.",
        RouteInfo = "Start at the city hall.",
        DistanceKm = distanceKm,
        EventType = eventType
    };

    private static async Task<EventResponse> CreateEventAsync(HttpClient organiser, EventRequest? request = null)
    {
        var response = await organiser.PostAsJsonAsync("/api/events", request ?? NewEvent());
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EventResponse>())!;
    }

    private Task<(HttpClient Client, UserSummary User)> LoggedInOrganiserAsync() =>
        TestHelpers.LoggedInClientAsync(_factory, RoleNames.Organiser);

    private Task<(HttpClient Client, UserSummary User)> LoggedInParticipantAsync() =>
        TestHelpers.LoggedInClientAsync(_factory, RoleNames.Participant);

    // ---------- POST /api/events ----------

    [Fact]
    public async Task CreateEvent_AsOrganiser_Returns201AndTheEvent()
    {
        var (client, organiser) = await LoggedInOrganiserAsync();
        var request = NewEvent(eventType: "Cycle", distanceKm: 109m);

        var response = await client.PostAsJsonAsync("/api/events", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<EventResponse>();
        Assert.Equal(request.Name, created!.Name);
        Assert.Equal(organiser.UserID, created.OrganiserID);
        Assert.Equal("Cycle", created.EventType);
        Assert.Equal(109m, created.DistanceKm);
        Assert.Equal(request.Location, created.Location);
    }

    [Fact]
    public async Task CreateEvent_WithoutASession_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/events", NewEvent());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateEvent_AsParticipant_Returns403()
    {
        var (client, _) = await LoggedInParticipantAsync();

        var response = await client.PostAsJsonAsync("/api/events", NewEvent());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateEvent_InvalidEventType_Returns400()
    {
        var (client, _) = await LoggedInOrganiserAsync();

        var response = await client.PostAsJsonAsync("/api/events", NewEvent(eventType: "Swim"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateEvent_DateInThePast_Returns400()
    {
        var (client, _) = await LoggedInOrganiserAsync();

        var response = await client.PostAsJsonAsync("/api/events", NewEvent(date: Today.AddDays(-1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateEvent_MissingName_Returns400()
    {
        var (client, _) = await LoggedInOrganiserAsync();
        var request = NewEvent();
        request.Name = "";

        var response = await client.PostAsJsonAsync("/api/events", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateEvent_ZeroDistance_Returns400()
    {
        var (client, _) = await LoggedInOrganiserAsync();

        var response = await client.PostAsJsonAsync("/api/events", NewEvent(distanceKm: 0m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- GET /api/events (public) ----------

    [Fact]
    public async Task GetEvents_WithoutASession_Returns200AndIncludesNewEvents()
    {
        var (organiser, _) = await LoggedInOrganiserAsync();
        var created = await CreateEventAsync(organiser);

        var response = await _factory.CreateClient().GetAsync("/api/events");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var events = await response.Content.ReadFromJsonAsync<List<EventResponse>>();
        Assert.Contains(events!, e => e.EventID == created.EventID);
    }

    [Fact]
    public async Task GetEvents_OnlyReturnsUpcomingEvents()
    {
        var (_, organiser) = await LoggedInOrganiserAsync();
        var pastName = $"Past-{Guid.NewGuid():N}";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RaceDayDbContext>();
            db.Events.Add(new Event
            {
                UserID = organiser.UserID,
                Name = pastName,
                EventDate = Today.AddDays(-10),
                Venue = "Old Venue",
                DistanceKm = 5m,
                EventType = EventTypes.Run
            });
            await db.SaveChangesAsync();
        }

        var events = await _factory.CreateClient().GetFromJsonAsync<List<EventResponse>>("/api/events");

        Assert.DoesNotContain(events!, e => e.Name == pastName);
    }

    [Fact]
    public async Task GetEvents_FilterByLocation_ReturnsOnlyMatchingEvents_IgnoringCase()
    {
        var (organiser, _) = await LoggedInOrganiserAsync();
        var token = Guid.NewGuid().ToString("N")[..8];
        var match = await CreateEventAsync(organiser, NewEvent(location: $"Venue-{token.ToUpperInvariant()}"));
        var other = await CreateEventAsync(organiser, NewEvent(location: "Somewhere else entirely"));

        var events = await _factory.CreateClient()
            .GetFromJsonAsync<List<EventResponse>>($"/api/events?location=venue-{token}");

        Assert.Contains(events!, e => e.EventID == match.EventID);
        Assert.DoesNotContain(events!, e => e.EventID == other.EventID);
    }

    [Fact]
    public async Task GetEvents_FilterByDate_ReturnsOnlyEventsOnThatDate()
    {
        var (organiser, _) = await LoggedInOrganiserAsync();
        var day = Today.AddDays(75);
        var match = await CreateEventAsync(organiser, NewEvent(date: day));
        var other = await CreateEventAsync(organiser, NewEvent(date: day.AddDays(1)));

        var events = await _factory.CreateClient()
            .GetFromJsonAsync<List<EventResponse>>($"/api/events?date={day:yyyy-MM-dd}");

        Assert.Contains(events!, e => e.EventID == match.EventID);
        Assert.DoesNotContain(events!, e => e.EventID == other.EventID);
        Assert.All(events!, e => Assert.Equal(day, e.Date));
    }

    [Fact]
    public async Task GetEvents_FilterByEventType_ReturnsOnlyThatType()
    {
        var (organiser, _) = await LoggedInOrganiserAsync();
        var walk = await CreateEventAsync(organiser, NewEvent(eventType: "Walk", distanceKm: 5m));
        var cycle = await CreateEventAsync(organiser, NewEvent(eventType: "Cycle", distanceKm: 50m));

        var events = await _factory.CreateClient()
            .GetFromJsonAsync<List<EventResponse>>("/api/events?eventType=Walk");

        Assert.Contains(events!, e => e.EventID == walk.EventID);
        Assert.DoesNotContain(events!, e => e.EventID == cycle.EventID);
        Assert.All(events!, e => Assert.Equal("Walk", e.EventType));
    }

    [Fact]
    public async Task GetEvents_InvalidEventTypeFilter_Returns400()
    {
        var response = await _factory.CreateClient().GetAsync("/api/events?eventType=Swim");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- GET /api/events/{id} (public) ----------

    [Fact]
    public async Task GetEvent_ById_WithoutASession_Returns200()
    {
        var (organiser, _) = await LoggedInOrganiserAsync();
        var created = await CreateEventAsync(organiser);

        var response = await _factory.CreateClient().GetAsync($"/api/events/{created.EventID}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ev = await response.Content.ReadFromJsonAsync<EventResponse>();
        Assert.Equal(created.Name, ev!.Name);
    }

    [Fact]
    public async Task GetEvent_UnknownId_Returns404()
    {
        var response = await _factory.CreateClient().GetAsync("/api/events/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- PUT /api/events/{id} ----------

    [Fact]
    public async Task UpdateEvent_AsOwner_Returns200AndSavesChanges()
    {
        var (client, _) = await LoggedInOrganiserAsync();
        var created = await CreateEventAsync(client);
        var changes = NewEvent(name: "Renamed Event", location: "Durban Beachfront", eventType: "Walk", distanceKm: 5m);

        var response = await client.PutAsJsonAsync($"/api/events/{created.EventID}", changes);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<EventResponse>();
        Assert.Equal("Renamed Event", updated!.Name);
        Assert.Equal("Walk", updated.EventType);

        var fetched = await _factory.CreateClient().GetFromJsonAsync<EventResponse>($"/api/events/{created.EventID}");
        Assert.Equal("Durban Beachfront", fetched!.Location);
    }

    [Fact]
    public async Task UpdateEvent_WithoutASession_Returns401()
    {
        var response = await _factory.CreateClient().PutAsJsonAsync("/api/events/1", NewEvent());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateEvent_AsParticipant_Returns403()
    {
        var (organiser, _) = await LoggedInOrganiserAsync();
        var created = await CreateEventAsync(organiser);
        var (participant, _) = await LoggedInParticipantAsync();

        var response = await participant.PutAsJsonAsync($"/api/events/{created.EventID}", NewEvent());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateEvent_OwnedByAnotherOrganiser_Returns403()
    {
        var (owner, _) = await LoggedInOrganiserAsync();
        var created = await CreateEventAsync(owner);
        var (otherOrganiser, _) = await LoggedInOrganiserAsync();

        var response = await otherOrganiser.PutAsJsonAsync($"/api/events/{created.EventID}", NewEvent());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateEvent_UnknownId_Returns404()
    {
        var (client, _) = await LoggedInOrganiserAsync();

        var response = await client.PutAsJsonAsync("/api/events/999999", NewEvent());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- DELETE /api/events/{id} ----------

    [Fact]
    public async Task DeleteEvent_AsOwner_Returns204AndTheEventIsGone()
    {
        var (client, _) = await LoggedInOrganiserAsync();
        var created = await CreateEventAsync(client);

        var response = await client.DeleteAsync($"/api/events/{created.EventID}");
        var afterwards = await _factory.CreateClient().GetAsync($"/api/events/{created.EventID}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, afterwards.StatusCode);
    }

    [Fact]
    public async Task DeleteEvent_WithoutASession_Returns401()
    {
        var response = await _factory.CreateClient().DeleteAsync("/api/events/1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteEvent_AsParticipant_Returns403()
    {
        var (organiser, _) = await LoggedInOrganiserAsync();
        var created = await CreateEventAsync(organiser);
        var (participant, _) = await LoggedInParticipantAsync();

        var response = await participant.DeleteAsync($"/api/events/{created.EventID}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteEvent_OwnedByAnotherOrganiser_Returns403()
    {
        var (owner, _) = await LoggedInOrganiserAsync();
        var created = await CreateEventAsync(owner);
        var (otherOrganiser, _) = await LoggedInOrganiserAsync();

        var response = await otherOrganiser.DeleteAsync($"/api/events/{created.EventID}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteEvent_UnknownId_Returns404()
    {
        var (client, _) = await LoggedInOrganiserAsync();

        var response = await client.DeleteAsync("/api/events/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteEvent_AlsoRemovesItsCategories()
    {
        var (client, organiser) = await LoggedInOrganiserAsync();
        var created = await CreateEventAsync(client);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RaceDayDbContext>();
            db.Categories.Add(new Category
            {
                UserID = organiser.UserID,
                EventID = created.EventID,
                Name = "10km",
                DistanceKm = 10m,
                MaxParticipants = 100
            });
            await db.SaveChangesAsync();
        }

        var response = await client.DeleteAsync($"/api/events/{created.EventID}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var checkScope = _factory.Services.CreateScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<RaceDayDbContext>();
        Assert.False(await checkDb.Categories.AnyAsync(c => c.EventID == created.EventID));
    }

    [Fact]
    public async Task DeleteEvent_WithEnrolments_Returns409()
    {
        var (client, organiser) = await LoggedInOrganiserAsync();
        var created = await CreateEventAsync(client);
        var participant = await TestHelpers.RegisterAsync(_factory.CreateClient(), RoleNames.Participant);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RaceDayDbContext>();
            var participantId = await db.Participants
                .Where(p => p.UserID == participant.UserID)
                .Select(p => p.ParticipantID)
                .SingleAsync();
            var category = new Category
            {
                UserID = organiser.UserID,
                EventID = created.EventID,
                Name = "10km",
                DistanceKm = 10m,
                MaxParticipants = 100
            };
            db.Categories.Add(category);
            await db.SaveChangesAsync();

            db.Enrolments.Add(new Enrolment
            {
                ParticipantID = participantId,
                EventID = created.EventID,
                CategoryID = category.CategoryID,
                EnrolDate = DateTime.UtcNow,
                Status = EnrolmentStatuses.Pending,
                EmergencyContactName = "Nomsa Khumalo",
                EmergencyContactNumber = "0781234567"
            });
            await db.SaveChangesAsync();
        }

        var response = await client.DeleteAsync($"/api/events/{created.EventID}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
