namespace RunScope.Core.Models;

public class RouteSegment
{
    public Guid Id { get; set; }

    public Guid RouteId { get; set; }

    public Guid FromWaypointId { get; set; }

    public Guid ToWaypointId { get; set; }

    public double Distance { get; set; }

    // Stored as JSON — array of [lng, lat] coordinate pairs
    public string PathJson { get; set; } = "[]";

    public Route Route { get; set; } = null!;
}