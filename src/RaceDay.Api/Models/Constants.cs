namespace RaceDay.Api.Models;

/// <summary>The two roles a user can register as.</summary>
public static class RoleNames
{
    public const string Organiser = "Organiser";
    public const string Participant = "Participant";

    public static bool IsValid(string? role) => role == Organiser || role == Participant;
}

public static class EventTypes
{
    public const string Run = "Run";
    public const string Walk = "Walk";
    public const string Cycle = "Cycle";

    public static readonly string[] All = { Run, Walk, Cycle };
    public static bool IsValid(string? type) => type != null && All.Contains(type);
}

public static class EnrolmentStatuses
{
    public const string Pending = "Pending";
    public const string Confirmed = "Confirmed";
    public const string Cancelled = "Cancelled";

    public static bool IsValid(string? status) => status == Pending || status == Confirmed || status == Cancelled;
}

public static class ResultStatuses
{
    public const string Finished = "Finished";
    public const string DNF = "DNF";
    public const string DSQ = "DSQ";

    public static bool IsValid(string? status) => status == Finished || status == DNF || status == DSQ;
}
