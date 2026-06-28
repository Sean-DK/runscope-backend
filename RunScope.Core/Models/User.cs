namespace RunScope.Core.Models;

public class User
{
    public Guid Id { get; set; }

    public string GoogleId { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? AvatarUrl { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ICollection<Route> Routes { get; set; } = [];

    public ICollection<Event> Events { get; set; } = [];
}