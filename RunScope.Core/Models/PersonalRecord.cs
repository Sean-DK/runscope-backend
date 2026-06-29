namespace RunScope.Core.Models;

public enum RaceDistance
{
    OneMile,
    FiveK,
    FiveMile,
    TenK,
    HalfMarathon,
    Marathon,
}

public class PersonalRecord
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public RaceDistance Distance { get; set; }

    public int TimeSeconds { get; set; }

    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
}