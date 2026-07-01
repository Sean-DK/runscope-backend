namespace RunScope.Core.Models;

public class Route
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Name { get; set; } = string.Empty;

    public double TotalDistance { get; set; }

    public double? ElevationGainMeters { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
    public ICollection<RouteWaypoint> Waypoints { get; set; } = [];

    public ICollection<RouteSegment> Segments { get; set; } = [];

    public ICollection<Event> Events { get; set; } = [];
}