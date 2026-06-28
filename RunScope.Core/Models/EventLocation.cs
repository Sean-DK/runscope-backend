namespace RunScope.Core.Models;

public class EventLocation
{
    public Guid Id { get; set; }

    public Guid EventId { get; set; }

    public double Longitude { get; set; }

    public double Latitude { get; set; }

    public double DistanceFromStart { get; set; }

    public double? CurrentPaceSecondsPerMile { get; set; }

    public double? AveragePaceSecondsPerMile { get; set; }

    public DateTime Timestamp { get; set; }

    public Event Event { get; set; } = null!;
}