using System.Net;
using System.Net.Http.Json;
using RaceDay.Api.Dtos;
using RaceDay.Api.Models;
using Xunit;

namespace RaceDay.Tests;

public class CategoriesTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public CategoriesTests(ApiFactory factory) => _factory = factory;

    private Task<(HttpClient Client, UserSummary User)> OrganiserAsync() =>
        TestHelpers.LoggedInClientAsync(_factory, RoleNames.Organiser);

    private Task<(HttpClient Client, UserSummary User)> ParticipantAsync() =>
        TestHelpers.LoggedInClientAsync(_factory, RoleNames.Participant);

    private static CategoryRequest NewCategory(string? name = null, decimal? distanceKm = 10m, int? maxParticipants = 100) => new()
    {
        Name = name ?? $"Cat-{Guid.NewGuid():N}",
        Description = "A category.",
        DistanceKm = distanceKm,
        MaxParticipants = maxParticipants
    };

    // ---------- POST /api/events/{eventId}/categories ----------

    [Fact]
    public async Task CreateCategory_AsEventOwner_Returns201AndTheCategory()
    {
        var (client, _) = await OrganiserAsync();
        var ev = await TestHelpers.CreateEventAsync(client);
        var request = NewCategory("21km Half Marathon", 21.1m, 300);

        var response = await client.PostAsJsonAsync($"/api/events/{ev.EventID}/categories", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CategoryResponse>();
        Assert.Equal("21km Half Marathon", created!.Name);
        Assert.Equal(21.1m, created.DistanceKm);
        Assert.Equal(300, created.MaxParticipants);
        Assert.Equal(ev.EventID, created.EventID);
        Assert.Equal(0, created.EnrolmentCount);
    }

    [Fact]
    public async Task CreateCategory_AgeCategoryWithoutDistance_Returns201WithNoDistance()
    {
        var (client, _) = await OrganiserAsync();
        var ev = await TestHelpers.CreateEventAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/events/{ev.EventID}/categories", NewCategory("Under 20", distanceKm: null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CategoryResponse>();
        Assert.Null(created!.DistanceKm);
    }

    [Fact]
    public async Task CreateCategory_WithoutMaxParticipants_DefaultsTo100()
    {
        var (client, _) = await OrganiserAsync();
        var ev = await TestHelpers.CreateEventAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/events/{ev.EventID}/categories", NewCategory(maxParticipants: null));

        var created = await response.Content.ReadFromJsonAsync<CategoryResponse>();
        Assert.Equal(100, created!.MaxParticipants);
    }

    [Fact]
    public async Task CreateCategory_WithoutASession_Returns401()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/events/1/categories", NewCategory());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateCategory_AsParticipant_Returns403()
    {
        var (organiser, _) = await OrganiserAsync();
        var ev = await TestHelpers.CreateEventAsync(organiser);
        var (participant, _) = await ParticipantAsync();

        var response = await participant.PostAsJsonAsync($"/api/events/{ev.EventID}/categories", NewCategory());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateCategory_ForAnotherOrganisersEvent_Returns403()
    {
        var (owner, _) = await OrganiserAsync();
        var ev = await TestHelpers.CreateEventAsync(owner);
        var (otherOrganiser, _) = await OrganiserAsync();

        var response = await otherOrganiser.PostAsJsonAsync($"/api/events/{ev.EventID}/categories", NewCategory());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateCategory_UnknownEvent_Returns404()
    {
        var (client, _) = await OrganiserAsync();

        var response = await client.PostAsJsonAsync("/api/events/999999/categories", NewCategory());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateCategory_DuplicateNameInSameEvent_Returns409()
    {
        var (client, _) = await OrganiserAsync();
        var ev = await TestHelpers.CreateEventAsync(client);
        await TestHelpers.CreateCategoryAsync(client, ev.EventID, name: "10km");

        var response = await client.PostAsJsonAsync($"/api/events/{ev.EventID}/categories", NewCategory("10km"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateCategory_SameNameInDifferentEvents_Returns201()
    {
        var (client, _) = await OrganiserAsync();
        var first = await TestHelpers.CreateEventAsync(client);
        var second = await TestHelpers.CreateEventAsync(client);
        await TestHelpers.CreateCategoryAsync(client, first.EventID, name: "10km");

        var response = await client.PostAsJsonAsync($"/api/events/{second.EventID}/categories", NewCategory("10km"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateCategory_MissingName_Returns400()
    {
        var (client, _) = await OrganiserAsync();
        var ev = await TestHelpers.CreateEventAsync(client);
        var request = NewCategory();
        request.Name = "";

        var response = await client.PostAsJsonAsync($"/api/events/{ev.EventID}/categories", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateCategory_NegativeDistance_Returns400()
    {
        var (client, _) = await OrganiserAsync();
        var ev = await TestHelpers.CreateEventAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/events/{ev.EventID}/categories", NewCategory(distanceKm: -5m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- GET /api/events/{eventId}/categories (public) ----------

    [Fact]
    public async Task GetCategories_WithoutASession_Returns200AndTheEventsCategories()
    {
        var (organiser, _) = await OrganiserAsync();
        var ev = await TestHelpers.CreateEventAsync(organiser);
        var category = await TestHelpers.CreateCategoryAsync(organiser, ev.EventID);
        var otherEvent = await TestHelpers.CreateEventAsync(organiser);
        var otherCategory = await TestHelpers.CreateCategoryAsync(organiser, otherEvent.EventID);

        var response = await _factory.CreateClient().GetAsync($"/api/events/{ev.EventID}/categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var categories = await response.Content.ReadFromJsonAsync<List<CategoryResponse>>();
        Assert.Contains(categories!, c => c.CategoryID == category.CategoryID);
        Assert.DoesNotContain(categories!, c => c.CategoryID == otherCategory.CategoryID);
    }

    [Fact]
    public async Task GetCategories_UnknownEvent_Returns404()
    {
        var response = await _factory.CreateClient().GetAsync("/api/events/999999/categories");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- DELETE /api/categories/{id} ----------

    [Fact]
    public async Task DeleteCategory_AsEventOwner_Returns204AndTheCategoryIsGone()
    {
        var (client, _) = await OrganiserAsync();
        var ev = await TestHelpers.CreateEventAsync(client);
        var category = await TestHelpers.CreateCategoryAsync(client, ev.EventID);

        var response = await client.DeleteAsync($"/api/categories/{category.CategoryID}");
        var remaining = await _factory.CreateClient()
            .GetFromJsonAsync<List<CategoryResponse>>($"/api/events/{ev.EventID}/categories");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.DoesNotContain(remaining!, c => c.CategoryID == category.CategoryID);
    }

    [Fact]
    public async Task DeleteCategory_WithoutASession_Returns401()
    {
        var response = await _factory.CreateClient().DeleteAsync("/api/categories/1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteCategory_AsParticipant_Returns403()
    {
        var (organiser, _) = await OrganiserAsync();
        var ev = await TestHelpers.CreateEventAsync(organiser);
        var category = await TestHelpers.CreateCategoryAsync(organiser, ev.EventID);
        var (participant, _) = await ParticipantAsync();

        var response = await participant.DeleteAsync($"/api/categories/{category.CategoryID}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteCategory_FromAnotherOrganisersEvent_Returns403()
    {
        var (owner, _) = await OrganiserAsync();
        var ev = await TestHelpers.CreateEventAsync(owner);
        var category = await TestHelpers.CreateCategoryAsync(owner, ev.EventID);
        var (otherOrganiser, _) = await OrganiserAsync();

        var response = await otherOrganiser.DeleteAsync($"/api/categories/{category.CategoryID}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteCategory_UnknownId_Returns404()
    {
        var (client, _) = await OrganiserAsync();

        var response = await client.DeleteAsync("/api/categories/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
