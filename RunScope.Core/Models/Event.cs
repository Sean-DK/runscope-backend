namespace RunScope.Core.Models;

public enum EventStatus
{
    Pending,
    Active,
    Finished,
    Cancelled,
    Ended
}

public enum CancelReason
{
    DNF,
    Injury,
    Weather,
    Other
}

public class Event
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid RouteId { get; set; }

    public string EventCode { get; set; } = string.Empty;

    public EventStatus Status { get; set; } = EventStatus.Pending;

    public CancelReason? CancelReason { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public DateTime? EndedAt { get; set; }

    public User User { get; set; } = null!;

    public Route Route { get; set; } = null!;

    public ICollection<EventLocation> Locations { get; set; } = [];
}