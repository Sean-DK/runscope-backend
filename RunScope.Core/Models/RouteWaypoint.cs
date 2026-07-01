namespace RunScope.Core.Models;

public class RouteWaypoint
{
    public Guid Id { get; set; }

    public Guid RouteId { get; set; }

    public int Order { get; set; }

    public double Longitude { get; set; }

    public double Latitude { get; set; }

    public double? ElevationMeters { get; set; }

    public Route Route { get; set; } = null!;
}