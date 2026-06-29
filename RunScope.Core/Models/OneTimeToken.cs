namespace RunScope.Core.Models;

public class OneTimeToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Token { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public bool Used { get; set; }

    public User User { get; set; } = null!;
}