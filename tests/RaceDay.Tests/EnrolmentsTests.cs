using System.Net;
using System.Net.Http.Json;
using RaceDay.Api.Dtos;
using RaceDay.Api.Models;
using Xunit;

namespace RaceDay.Tests;
// these tests use the real API in memory, but with an in-memory database so they don't touch raceday.db and need no setup
public class EnrolmentsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public EnrolmentsTests(ApiFactory factory) => _factory = factory;

    private Task<(HttpClient Client, UserSummary User)> OrganiserAsync() =>
        TestHelpers.LoggedInClientAsync(_factory, RoleNames.Organiser);

    private Task<(HttpClient Client, UserSummary User)> ParticipantAsync() =>
        TestHelpers.LoggedInClientAsync(_factory, RoleNames.Participant);

    /// <summary>An organiser with an upcoming event that has one category.</summary>
    private async Task<(HttpClient Organiser, UserSummary OrganiserUser, EventResponse Event, CategoryResponse Category)> EventWithCategoryAsync(
        int? maxParticipants = 100)
    {
        var (organiser, organiserUser) = await OrganiserAsync();
        var ev = await TestHelpers.CreateEventAsync(organiser);
        var category = await TestHelpers.CreateCategoryAsync(organiser, ev.EventID, maxParticipants: maxParticipants);
        return (organiser, organiserUser, ev, category);
    }

    // ---------- POST /api/categories/{categoryId}/enrolments ----------

    [Fact]
    public async Task Enrol_AsParticipant_Returns201AndAPendingEnrolment()
    {
        var (_, _, ev, category) = await EventWithCategoryAsync();
        var (participant, _) = await ParticipantAsync();

        var response = await participant.PostAsJsonAsync(
            $"/api/categories/{category.CategoryID}/enrolments", TestHelpers.NewEnrolRequest());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var enrolment = await response.Content.ReadFromJsonAsync<EnrolmentResponse>();
        Assert.Equal(EnrolmentStatuses.Pending, enrolment!.Status);
        Assert.Equal(ev.EventID, enrolment.EventID);
        Assert.Equal(ev.Name, enrolment.EventName);
        Assert.Equal(category.CategoryID, enrolment.CategoryID);
        Assert.Equal(category.Name, enrolment.CategoryName);
        Assert.False(enrolment.HasResult);
    }

    [Fact]
    public async Task Enrol_WithoutASession_Returns401()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/api/categories/1/enrolments", TestHelpers.NewEnrolRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Enrol_AsOrganiser_Returns403()
    {
        var (organiser, _, _, category) = await EventWithCategoryAsync();

        var response = await organiser.PostAsJsonAsync(
            $"/api/categories/{category.CategoryID}/enrolments", TestHelpers.NewEnrolRequest());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Enrol_UnknownCategory_Returns404()
    {
        var (participant, _) = await ParticipantAsync();

        var response = await participant.PostAsJsonAsync(
            "/api/categories/999999/enrolments", TestHelpers.NewEnrolRequest());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Enrol_TwiceInTheSameCategory_Returns409()
    {
        var (_, _, _, category) = await EventWithCategoryAsync();
        var (participant, _) = await ParticipantAsync();
        await TestHelpers.EnrolAsync(participant, category.CategoryID);

        var response = await participant.PostAsJsonAsync(
            $"/api/categories/{category.CategoryID}/enrolments", TestHelpers.NewEnrolRequest());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Enrol_InASecondCategoryOfTheSameEvent_Returns409()
    {
        var (organiser, _, ev, firstCategory) = await EventWithCategoryAsync();
        var secondCategory = await TestHelpers.CreateCategoryAsync(organiser, ev.EventID);
        var (participant, _) = await ParticipantAsync();
        await TestHelpers.EnrolAsync(participant, firstCategory.CategoryID);

        var response = await participant.PostAsJsonAsync(
            $"/api/categories/{secondCategory.CategoryID}/enrolments", TestHelpers.NewEnrolRequest());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Enrol_WhenTheCategoryIsFull_Returns400()
    {
        var (_, _, _, category) = await EventWithCategoryAsync(maxParticipants: 1);
        var (first, _) = await ParticipantAsync();
        var (second, _) = await ParticipantAsync();
        await TestHelpers.EnrolAsync(first, category.CategoryID);

        var response = await second.PostAsJsonAsync(
            $"/api/categories/{category.CategoryID}/enrolments", TestHelpers.NewEnrolRequest());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Enrol_MissingEmergencyContact_Returns400()
    {
        var (_, _, _, category) = await EventWithCategoryAsync();
        var (participant, _) = await ParticipantAsync();
        var request = TestHelpers.NewEnrolRequest();
        request.EmergencyContactName = "";

        var response = await participant.PostAsJsonAsync($"/api/categories/{category.CategoryID}/enrolments", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Enrol_InAnEventThatAlreadyTookPlace_Returns400()
    {
        var (_, organiserUser, _, _) = await EventWithCategoryAsync();
        var (_, categoryId) = await TestHelpers.SeedPastEventAsync(_factory, organiserUser.UserID);
        var (participant, _) = await ParticipantAsync();

        var response = await participant.PostAsJsonAsync(
            $"/api/categories/{categoryId}/enrolments", TestHelpers.NewEnrolRequest());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- GET /api/events/{eventId}/enrolments (organiser) ----------

    [Fact]
    public async Task GetEventEnrolments_AsEventOwner_ReturnsTheEnrolments()
    {
        var (organiser, _, ev, category) = await EventWithCategoryAsync();
        var (participant, _) = await ParticipantAsync();
        var enrolment = await TestHelpers.EnrolAsync(participant, category.CategoryID);

        var response = await organiser.GetAsync($"/api/events/{ev.EventID}/enrolments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<List<EnrolmentResponse>>();
        var found = Assert.Single(list!);
        Assert.Equal(enrolment.EnrolmentID, found.EnrolmentID);
        Assert.Equal(category.Name, found.CategoryName);
        Assert.Equal(EnrolmentStatuses.Pending, found.Status);
    }

    [Fact]
    public async Task GetEventEnrolments_AsAnotherOrganiser_Returns403()
    {
        var (_, _, ev, _) = await EventWithCategoryAsync();
        var (otherOrganiser, _) = await OrganiserAsync();

        var response = await otherOrganiser.GetAsync($"/api/events/{ev.EventID}/enrolments");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetEventEnrolments_AsParticipant_Returns403()
    {
        var (_, _, ev, _) = await EventWithCategoryAsync();
        var (participant, _) = await ParticipantAsync();

        var response = await participant.GetAsync($"/api/events/{ev.EventID}/enrolments");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetEventEnrolments_WithoutASession_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/events/1/enrolments");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetEventEnrolments_UnknownEvent_Returns404()
    {
        var (organiser, _) = await OrganiserAsync();

        var response = await organiser.GetAsync("/api/events/999999/enrolments");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- GET /api/users/{userId}/enrolments ----------

    [Fact]
    public async Task GetUserEnrolments_OwnEnrolments_ReturnsThem()
    {
        var (_, _, _, category) = await EventWithCategoryAsync();
        var (participant, user) = await ParticipantAsync();
        var enrolment = await TestHelpers.EnrolAsync(participant, category.CategoryID);

        var response = await participant.GetAsync($"/api/users/{user.UserID}/enrolments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<List<EnrolmentResponse>>();
        Assert.Contains(list!, e => e.EnrolmentID == enrolment.EnrolmentID);
    }

    [Fact]
    public async Task GetUserEnrolments_AnotherParticipantsEnrolments_Returns403()
    {
        var (participant, _) = await ParticipantAsync();
        var (_, otherUser) = await ParticipantAsync();

        var response = await participant.GetAsync($"/api/users/{otherUser.UserID}/enrolments");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetUserEnrolments_AsOrganiser_OnlyShowsEnrolmentsInTheirOwnEvents()
    {
        var (organiserA, _, _, categoryA) = await EventWithCategoryAsync();
        var (_, _, _, categoryB) = await EventWithCategoryAsync();
        var (participant, participantUser) = await ParticipantAsync();
        var inA = await TestHelpers.EnrolAsync(participant, categoryA.CategoryID);
        var inB = await TestHelpers.EnrolAsync(participant, categoryB.CategoryID);

        var list = await organiserA.GetFromJsonAsync<List<EnrolmentResponse>>(
            $"/api/users/{participantUser.UserID}/enrolments");

        Assert.Contains(list!, e => e.EnrolmentID == inA.EnrolmentID);
        Assert.DoesNotContain(list!, e => e.EnrolmentID == inB.EnrolmentID);
    }

    [Fact]
    public async Task GetUserEnrolments_WithoutASession_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/users/1/enrolments");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetUserEnrolments_UnknownUser_Returns404()
    {
        var (participant, _) = await ParticipantAsync();

        var response = await participant.GetAsync("/api/users/999999/enrolments");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- DELETE /api/enrolments/{id} ----------

    [Fact]
    public async Task CancelEnrolment_AsOwner_Returns204AndTheEnrolmentIsGone()
    {
        var (_, _, _, category) = await EventWithCategoryAsync();
        var (participant, user) = await ParticipantAsync();
        var enrolment = await TestHelpers.EnrolAsync(participant, category.CategoryID);

        var response = await participant.DeleteAsync($"/api/enrolments/{enrolment.EnrolmentID}");
        var remaining = await participant.GetFromJsonAsync<List<EnrolmentResponse>>($"/api/users/{user.UserID}/enrolments");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.DoesNotContain(remaining!, e => e.EnrolmentID == enrolment.EnrolmentID);
    }

    [Fact]
    public async Task CancelEnrolment_WithoutASession_Returns401()
    {
        var response = await _factory.CreateClient().DeleteAsync("/api/enrolments/1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CancelEnrolment_BelongingToAnotherParticipant_Returns403()
    {
        var (_, _, _, category) = await EventWithCategoryAsync();
        var (owner, _) = await ParticipantAsync();
        var enrolment = await TestHelpers.EnrolAsync(owner, category.CategoryID);
        var (otherParticipant, _) = await ParticipantAsync();

        var response = await otherParticipant.DeleteAsync($"/api/enrolments/{enrolment.EnrolmentID}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CancelEnrolment_AsOrganiser_Returns403()
    {
        var (organiser, _, _, category) = await EventWithCategoryAsync();
        var (participant, _) = await ParticipantAsync();
        var enrolment = await TestHelpers.EnrolAsync(participant, category.CategoryID);

        var response = await organiser.DeleteAsync($"/api/enrolments/{enrolment.EnrolmentID}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CancelEnrolment_UnknownId_Returns404()
    {
        var (participant, _) = await ParticipantAsync();

        var response = await participant.DeleteAsync("/api/enrolments/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CancelEnrolment_WithARecordedResult_Returns409()
    {
        var (_, organiserUser) = await OrganiserAsync();
        var (participant, participantUser) = await ParticipantAsync();
        var (eventId, categoryId) = await TestHelpers.SeedPastEventAsync(_factory, organiserUser.UserID);
        var enrolmentId = await TestHelpers.SeedEnrolmentAsync(_factory, eventId, categoryId, participantUser.UserID);
        await TestHelpers.SeedResultAsync(_factory, enrolmentId);

        var response = await participant.DeleteAsync($"/api/enrolments/{enrolmentId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ---------- PUT /api/enrolments/{id}/status ----------

    [Fact]
    public async Task UpdateStatus_AsEventOwner_Returns200AndTheNewStatus()
    {
        var (organiser, _, _, category) = await EventWithCategoryAsync();
        var (participant, _) = await ParticipantAsync();
        var enrolment = await TestHelpers.EnrolAsync(participant, category.CategoryID);

        var response = await organiser.PutAsJsonAsync(
            $"/api/enrolments/{enrolment.EnrolmentID}/status",
            new UpdateEnrolmentStatusRequest { Status = EnrolmentStatuses.Confirmed });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<EnrolmentResponse>();
        Assert.Equal(EnrolmentStatuses.Confirmed, updated!.Status);
    }

    [Fact]
    public async Task UpdateStatus_InvalidStatus_Returns400()
    {
        var (organiser, _, _, category) = await EventWithCategoryAsync();
        var (participant, _) = await ParticipantAsync();
        var enrolment = await TestHelpers.EnrolAsync(participant, category.CategoryID);

        var response = await organiser.PutAsJsonAsync(
            $"/api/enrolments/{enrolment.EnrolmentID}/status",
            new UpdateEnrolmentStatusRequest { Status = "Maybe" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_AsAnotherOrganiser_Returns403()
    {
        var (_, _, _, category) = await EventWithCategoryAsync();
        var (participant, _) = await ParticipantAsync();
        var enrolment = await TestHelpers.EnrolAsync(participant, category.CategoryID);
        var (otherOrganiser, _) = await OrganiserAsync();

        var response = await otherOrganiser.PutAsJsonAsync(
            $"/api/enrolments/{enrolment.EnrolmentID}/status",
            new UpdateEnrolmentStatusRequest { Status = EnrolmentStatuses.Confirmed });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_AsParticipant_Returns403()
    {
        var (_, _, _, category) = await EventWithCategoryAsync();
        var (participant, _) = await ParticipantAsync();
        var enrolment = await TestHelpers.EnrolAsync(participant, category.CategoryID);

        var response = await participant.PutAsJsonAsync(
            $"/api/enrolments/{enrolment.EnrolmentID}/status",
            new UpdateEnrolmentStatusRequest { Status = EnrolmentStatuses.Confirmed });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_WithoutASession_Returns401()
    {
        var response = await _factory.CreateClient().PutAsJsonAsync(
            "/api/enrolments/1/status", new UpdateEnrolmentStatusRequest { Status = EnrolmentStatuses.Confirmed });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_UnknownEnrolment_Returns404()
    {
        var (organiser, _) = await OrganiserAsync();

        var response = await organiser.PutAsJsonAsync(
            "/api/enrolments/999999/status", new UpdateEnrolmentStatusRequest { Status = EnrolmentStatuses.Confirmed });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- categories and enrolments ----------

    [Fact]
    public async Task GetCategories_CountsEnrolmentsInEachCategory()
    {
        var (organiser, _) = await OrganiserAsync();
        var ev = await TestHelpers.CreateEventAsync(organiser);
        var category = await TestHelpers.CreateCategoryAsync(organiser, ev.EventID);
        var (participant, _) = await ParticipantAsync();
        await TestHelpers.EnrolAsync(participant, category.CategoryID);

        var categories = await _factory.CreateClient()
            .GetFromJsonAsync<List<CategoryResponse>>($"/api/events/{ev.EventID}/categories");

        Assert.Equal(1, categories!.Single(c => c.CategoryID == category.CategoryID).EnrolmentCount);
    }

    [Fact]
    public async Task DeleteCategory_WithEnrolments_Returns409()
    {
        var (organiser, _) = await OrganiserAsync();
        var ev = await TestHelpers.CreateEventAsync(organiser);
        var category = await TestHelpers.CreateCategoryAsync(organiser, ev.EventID);
        var (participant, _) = await ParticipantAsync();
        await TestHelpers.EnrolAsync(participant, category.CategoryID);

        var response = await organiser.DeleteAsync($"/api/categories/{category.CategoryID}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
