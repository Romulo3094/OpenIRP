namespace OpenIRP.Domain.Entities;

public enum IncidentState { Registered, UnderEvaluation, InResponse, Resolved, Closed, Discarded }
public enum ClosureReviewStatus { Pending, Approved, Rejected, Invalidated }

public static class IncidentWorkflow
{
    public static string Store(IncidentState state) => state switch
    {
        IncidentState.Registered => "REGISTERED", IncidentState.UnderEvaluation => "UNDER_EVALUATION",
        IncidentState.InResponse => "IN_RESPONSE", IncidentState.Resolved => "RESOLVED",
        IncidentState.Closed => "CLOSED", IncidentState.Discarded => "DISCARDED",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    public static bool CanTransition(string current, IncidentState target) => current switch
    {
        "REGISTERED" => target == IncidentState.UnderEvaluation,
        "UNDER_EVALUATION" => target is IncidentState.InResponse or IncidentState.Discarded,
        "IN_RESPONSE" => target is IncidentState.UnderEvaluation or IncidentState.Resolved,
        "RESOLVED" => target is IncidentState.InResponse or IncidentState.Closed,
        "CLOSED" or "DISCARDED" => target == IncidentState.UnderEvaluation,
        _ => false
    };
}
