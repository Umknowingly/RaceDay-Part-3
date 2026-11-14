using System.Net;
using System.Net.Http.Json;
using RaceDay.Api.Dtos;
using RaceDay.Api.Models;
using Xunit;

namespace RaceDay.Tests;

public class ResultsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ResultsTests(ApiFactory factory) => _factory = factory;

    private sealed record Setup(
        HttpClient Organiser, UserSummary OrganiserUser,
        HttpClient Participant, UserSummary ParticipantUser,
        int EventId, int CategoryId, int EnrolmentId);

    /// <summary>A concluded event with one category and one enrolled participant.</summary>
    private async Task<Setup> ConcludedEventAsync(string enrolmentStatus = EnrolmentStatuses.Confirmed)
    {
        var (organiser, organiserUser) = await TestHelpers.LoggedInClientAsync(_factory, RoleNames.Organiser);
        var (participant, participantUser) = await TestHelpers.LoggedInClientAsync(_factory, RoleNames.Participant);
        var (eventId, categoryId) = await TestHelpers.SeedPastEventAsync(_factory, organiserUser.UserID);
        var enrolmentId = await TestHelpers.SeedEnrolmentAsync(
            _factory, eventId, categoryId, participantUser.UserID, enrolmentStatus);
        return new Setup(organiser, organiserUser, participant, participantUser, eventId, categoryId, enrolmentId);
    }

    private static ResultRequest FinishedResult(int position = 1, string time = "00:45:30") => new()
    {
        FinishTime = TimeSpan.Parse(time),
        Position = position,
        Status = ResultStatuses.Finished,
        Notes = "Strong finish"
    };

    private static Task<HttpResponseMessage> CaptureAsync(HttpClient client, int enrolmentId, ResultRequest request) =>
        client.PostAsJsonAsync($"/api/enrolments/{enrolmentId}/results", request);

    // ---------- POST /api/enrolments/{enrolmentId}/results ----------

    [Fact]
    public async Task CaptureResult_AsOwnerForAConcludedEvent_Returns201AndTheResult()
    {
        var s = await ConcludedEventAsync();

        var response = await CaptureAsync(s.Organiser, s.EnrolmentId, FinishedResult(position: 3, time: "00:45:30"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ResultResponse>();
        Assert.Equal(TimeSpan.Parse("00:45:30"), result!.FinishTime);
        Assert.Equal(3, result.Position);
        Assert.Equal(ResultStatuses.Finished, result.Status);
        Assert.Equal(s.EventId, result.EventID);
        Assert.Equal(1, result.TotalFinishers);
        Assert.StartsWith("Past-", result.EventName);
    }

    [Fact]
    public async Task CaptureResult_ForAnEventThatHasNotTakenPlace_Returns400()
    {
        var (organiser, _) = await TestHelpers.LoggedInClientAsync(_factory, RoleNames.Organiser);
        var (participant, _) = await TestHelpers.LoggedInClientAsync(_factory, RoleNames.Participant);
        var ev = await TestHelpers.CreateEventAsync(organiser);
        var category = await TestHelpers.CreateCategoryAsync(organiser, ev.EventID);
        var enrolment = await TestHelpers.EnrolAsync(participant, category.CategoryID);

        var response = await CaptureAsync(organiser, enrolment.EnrolmentID, FinishedResult());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CaptureResult_Twice_Returns409()
    {
        var s = await ConcludedEventAsync();
        await CaptureAsync(s.Organiser, s.EnrolmentId, FinishedResult());

        var response = await CaptureAsync(s.Organiser, s.EnrolmentId, FinishedResult(position: 2));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CaptureResult_ForACancelledEnrolment_Returns400()
    {
        var s = await ConcludedEventAsync(EnrolmentStatuses.Cancelled);

        var response = await CaptureAsync(s.Organiser, s.EnrolmentId, FinishedResult());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CaptureResult_FinishedWithoutAFinishTime_Returns400()
    {
        var s = await ConcludedEventAsync();
        var request = FinishedResult();
        request.FinishTime = null;

        var response = await CaptureAsync(s.Organiser, s.EnrolmentId, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CaptureResult_FinishedWithoutAPosition_Returns400()
    {
        var s = await ConcludedEventAsync();
        var request = FinishedResult();
        request.Position = null;

        var response = await CaptureAsync(s.Organiser, s.EnrolmentId, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CaptureResult_FinishTimeOf24HoursOrMore_Returns400()
    {
        var s = await ConcludedEventAsync();

        var response = await CaptureAsync(s.Organiser, s.EnrolmentId, FinishedResult(time: "1.00:00:00"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CaptureResult_DidNotFinishWithoutTimeOrPosition_Returns201()
    {
        var s = await ConcludedEventAsync();

        var response = await CaptureAsync(s.Organiser, s.EnrolmentId, new ResultRequest { Status = ResultStatuses.DNF });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ResultResponse>();
        Assert.Equal(ResultStatuses.DNF, result!.Status);
        Assert.Null(result.Position);
        Assert.Equal(0, result.TotalFinishers);
    }

    [Fact]
    public async Task CaptureResult_InvalidStatus_Returns400()
    {
        var s = await ConcludedEventAsync();
        var request = FinishedResult();
        request.Status = "Winner";

        var response = await CaptureAsync(s.Organiser, s.EnrolmentId, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CaptureResult_AsParticipant_Returns403()
    {
        var s = await ConcludedEventAsync();

        var response = await CaptureAsync(s.Participant, s.EnrolmentId, FinishedResult());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CaptureResult_AsAnotherOrganiser_Returns403()
    {
        var s = await ConcludedEventAsync();
        var (otherOrganiser, _) = await TestHelpers.LoggedInClientAsync(_factory, RoleNames.Organiser);

        var response = await CaptureAsync(otherOrganiser, s.EnrolmentId, FinishedResult());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CaptureResult_WithoutASession_Returns401()
    {
        var response = await CaptureAsync(_factory.CreateClient(), 1, FinishedResult());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CaptureResult_UnknownEnrolment_Returns404()
    {
        var (organiser, _) = await TestHelpers.LoggedInClientAsync(_factory, RoleNames.Organiser);

        var response = await CaptureAsync(organiser, 999999, FinishedResult());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CaptureResult_MarksTheEnrolmentAsHavingAResult()
    {
        var s = await ConcludedEventAsync();
        await CaptureAsync(s.Organiser, s.EnrolmentId, FinishedResult());

        var enrolments = await s.Organiser.GetFromJsonAsync<List<EnrolmentResponse>>(
            $"/api/events/{s.EventId}/enrolments");

        Assert.True(enrolments!.Single(e => e.EnrolmentID == s.EnrolmentId).HasResult);
    }

    // ---------- GET /api/events/{eventId}/results  ----------

    [Fact]
    public async Task GetEventResults_WithoutASession_ReturnsResultsInPositionOrder()
    {
        var s = await ConcludedEventAsync();
        var (_, secondUser) = await TestHelpers.LoggedInClientAsync(_factory, RoleNames.Participant);
        var secondEnrolmentId = await TestHelpers.SeedEnrolmentAsync(_factory, s.EventId, s.CategoryId, secondUser.UserID);
        await CaptureAsync(s.Organiser, s.EnrolmentId, FinishedResult(position: 2, time: "00:50:00"));
        await CaptureAsync(s.Organiser, secondEnrolmentId, FinishedResult(position: 1, time: "00:45:00"));

        var response = await _factory.CreateClient().GetAsync($"/api/events/{s.EventId}/results");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<List<ResultResponse>>();
        Assert.Equal(new int?[] { 1, 2 }, results!.Select(r => r.Position).ToArray());
    }

    [Fact]
    public async Task GetEventResults_UnknownEvent_Returns404()
    {
        var response = await _factory.CreateClient().GetAsync("/api/events/999999/results");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- GET /api/users/{userId}/results ----------

    [Fact]
    public async Task GetUserResults_OwnResults_ReturnsTheRaceHistoryWithEventDetails()
    {
        var s = await ConcludedEventAsync();
        await CaptureAsync(s.Organiser, s.EnrolmentId, FinishedResult(position: 7));

        var response = await s.Participant.GetAsync($"/api/users/{s.ParticipantUser.UserID}/results");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<List<ResultResponse>>();
        var result = Assert.Single(results!);
        Assert.Equal(s.EventId, result.EventID);
        Assert.Equal("10km", result.CategoryName);
        Assert.Equal(7, result.Position);
        Assert.Equal(TimeSpan.Parse("00:45:30"), result.FinishTime);
    }

    [Fact]
    public async Task GetUserResults_TotalFinishersCountsEveryFinisherInTheCategory()
    {
        var s = await ConcludedEventAsync();
        var (_, secondUser) = await TestHelpers.LoggedInClientAsync(_factory, RoleNames.Participant);
        var secondEnrolmentId = await TestHelpers.SeedEnrolmentAsync(_factory, s.EventId, s.CategoryId, secondUser.UserID);
        await CaptureAsync(s.Organiser, s.EnrolmentId, FinishedResult(position: 1, time: "00:45:00"));
        await CaptureAsync(s.Organiser, secondEnrolmentId, FinishedResult(position: 2, time: "00:50:00"));

        var results = await s.Participant.GetFromJsonAsync<List<ResultResponse>>(
            $"/api/users/{s.ParticipantUser.UserID}/results");

        Assert.Equal(2, Assert.Single(results!).TotalFinishers);
    }

    [Fact]
    public async Task GetUserResults_AnotherUsersResults_Returns403()
    {
        var s = await ConcludedEventAsync();
        var (otherParticipant, _) = await TestHelpers.LoggedInClientAsync(_factory, RoleNames.Participant);

        var response = await otherParticipant.GetAsync($"/api/users/{s.ParticipantUser.UserID}/results");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetUserResults_WithoutASession_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/users/1/results");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetUserResults_UnknownUser_Returns404()
    {
        var (participant, _) = await TestHelpers.LoggedInClientAsync(_factory, RoleNames.Participant);

        var response = await participant.GetAsync("/api/users/999999/results");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
