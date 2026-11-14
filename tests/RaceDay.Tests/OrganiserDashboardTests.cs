using System.Net;
using System.Net.Http.Json;
using RaceDay.Api.Dtos;
using RaceDay.Api.Models;
using Xunit;

namespace RaceDay.Tests;

public class OrganiserDashboardTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public OrganiserDashboardTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task GetMyEvents_AsOrganiser_ReturnsOnlyTheirOwnEventsIncludingPastOnes()
    {
        var (client, organiser) = await TestHelpers.LoggedInClientAsync(_factory, RoleNames.Organiser);
        var upcoming = await TestHelpers.CreateEventAsync(client);
        var (pastEventId, _) = await TestHelpers.SeedPastEventAsync(_factory, organiser.UserID);
        var (otherOrganiser, _) = await TestHelpers.LoggedInClientAsync(_factory, RoleNames.Organiser);
        var othersEvent = await TestHelpers.CreateEventAsync(otherOrganiser);

        var response = await client.GetAsync("/api/events/mine");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var events = await response.Content.ReadFromJsonAsync<List<OrganiserEventSummary>>();
        Assert.Contains(events!, e => e.EventID == upcoming.EventID);
        Assert.Contains(events!, e => e.EventID == pastEventId);
        Assert.DoesNotContain(events!, e => e.EventID == othersEvent.EventID);
    }

    [Fact]
    public async Task GetMyEvents_CountsCategoriesAndEnrolments()
    {
        var (client, _) = await TestHelpers.LoggedInClientAsync(_factory, RoleNames.Organiser);
        var ev = await TestHelpers.CreateEventAsync(client);
        var category = await TestHelpers.CreateCategoryAsync(client, ev.EventID);
        await TestHelpers.CreateCategoryAsync(client, ev.EventID);
        var (participant, _) = await TestHelpers.LoggedInClientAsync(_factory, RoleNames.Participant);
        await TestHelpers.EnrolAsync(participant, category.CategoryID);

        var events = await client.GetFromJsonAsync<List<OrganiserEventSummary>>("/api/events/mine");

        var summary = events!.Single(e => e.EventID == ev.EventID);
        Assert.Equal(2, summary.CategoryCount);
        Assert.Equal(1, summary.EnrolmentCount);
        Assert.Equal(0, summary.ResultCount);
    }

    [Fact]
    public async Task GetMyEvents_AsParticipant_Returns403()
    {
        var (client, _) = await TestHelpers.LoggedInClientAsync(_factory, RoleNames.Participant);

        var response = await client.GetAsync("/api/events/mine");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetMyEvents_WithoutASession_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/events/mine");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
