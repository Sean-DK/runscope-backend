namespace RunScope.Core.Models;

public enum UnitPreference
{
    Miles,
    Kilometers
}

public class User
{
    public Guid Id { get; set; }

    public string GoogleId { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? AvatarUrl { get; set; }

    public UnitPreference UnitPreference { get; set; } = UnitPreference.Miles;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ICollection<Route> Routes { get; set; } = [];

    public ICollection<Event> Events { get; set; } = [];
}